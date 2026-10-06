using CoreGraphics;
using Foundation;
using ImageIO;
using System.Runtime.InteropServices;
using NeeView;
namespace NeeView.Backends;

/// <summary>ImageIO替换原APNG后端，使用正式macOS官方绑定，不增加外部编码器。</summary>
public sealed class MacAnimatedPngDecoder : IAnimatedImageDecoder
{
    public Task<IAnimatedImageSource?> OpenAnimationAsync(Stream stream, DecodeRequest request, long workingBudget, CancellationToken token) => Task.Run(() =>
    {
        using var pool = new NSAutoreleasePool(); token.ThrowIfCancellationRequested(); stream.Position = 0;
        Span<byte> signature = stackalloc byte[8];
        if (stream.ReadAtLeast(signature,8,false) != 8 || !signature.SequenceEqual(new byte[] {137,80,78,71,13,10,26,10})) return null;
        if (!HasAnimationControl(stream,token)) return null;
        stream.Position = 0;
        // NSData持有输入，限制编码数据且纳入动画计费；调用方可以关闭原归档流。
        var inputLimit = Math.Min(64L * 1024 * 1024, workingBudget / 8);
        if (stream.Length > inputLimit) throw new NotSupportedException("PNG动画编码数据超过输入预算。");
        using var copy = new MemoryStream(); var buffer = new byte[81920]; int read;
        while ((read = stream.Read(buffer,0,buffer.Length)) > 0)
        { token.ThrowIfCancellationRequested(); if(copy.Length+read>inputLimit)throw new NotSupportedException("PNG动画编码数据超过输入预算。");copy.Write(buffer,0,read); }
        NSData? data = NSData.FromArray(copy.ToArray()); CGImageSource? images = null;
        try
        {
            images = CGImageSource.FromData(data) ?? throw new InvalidDataException("PNG来源无效。");
            var count = checked((int)images.ImageCount); if(count<=1)return null;
            if(count>4096)throw new NotSupportedException("PNG动画帧数超过安全上限4096。");
            // 来源级属性只描述容器，画布尺寸从首帧属性读取。
            var properties=images.GetProperties(0); using var root=properties.Dictionary;
            var width=checked((int)Number(root,"PixelWidth")); var height=checked((int)Number(root,"PixelHeight"));
            var frameBytes=checked((long)width*height*8);
            if(width<=0 || height<=0 || checked(frameBytes*(count*2L+2)+copy.Length*3)>workingBudget)throw new NotSupportedException("PNG动画超过解码工作预算。");
            var delays=new TimeSpan[count];
            for(int i=0;i<count;i++)
            {
                var item=images.GetProperties(i); using var dictionary=item.Dictionary; using var key=new NSString("{PNG}"); using var png=dictionary[key] as NSDictionary;
                var seconds=png is null?0:Number(png,"UnclampedDelayTime"); if(seconds<=0&&png is not null)seconds=Number(png,"DelayTime");
                delays[i]=TimeSpan.FromSeconds(seconds>0?seconds:.1);token.ThrowIfCancellationRequested();
            }
            var ratio=Math.Min(1,Math.Min((double)Math.Max(1,request.TargetWidth)/width,(double)Math.Max(1,request.TargetHeight)/height));
            var targetWidth=Math.Max(1,(int)(width*ratio));var targetHeight=Math.Max(1,(int)(height*ratio));
            var info=new AnimatedImageInfo(new(targetWidth,targetHeight),AnimatedImageType.Png,delays,checked(copy.Length+frameBytes*count));
            var source=new Source(data,images,info,width,height); data=null;images=null;return (IAnimatedImageSource?)source;
        }
        finally { images?.Dispose();data?.Dispose(); }
    },token);
    /// <summary>原APNG检测规则：acTL必须先于首个IDAT，普通PNG不复制输入或打开动画来源。</summary>
    private static bool HasAnimationControl(Stream stream,CancellationToken token)
    {
        Span<byte> header=stackalloc byte[8];
        while(stream.ReadAtLeast(header,8,false)==8)
        {
            token.ThrowIfCancellationRequested();
            var length=System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(header);
            if(header[4..].SequenceEqual("acTL"u8))return length==8;
            if(header[4..].SequenceEqual("IDAT"u8)||header[4..].SequenceEqual("IEND"u8))return false;
            if(length>stream.Length-stream.Position-4)return false;
            stream.Seek(length+4L,SeekOrigin.Current);
        }
        return false;
    }
    private static double Number(NSDictionary properties,string name)
    {using var key=new NSString(name);return (properties[key] as NSNumber)?.DoubleValue??0;}
    private sealed class Source(NSData data,CGImageSource images,AnimatedImageInfo info,int canvasWidth,int canvasHeight):IAnimatedImageSource
    {
        private readonly object _gate=new();private bool _disposed;
        public AnimatedImageInfo Info=>info;
        /// <summary>系统APNG完整合成帧按需转换为sRGB/BGRA8预乘输出。</summary>
        public Task<DecodedImageLease> ReadFrameAsync(int index,CancellationToken token)=>Task.Run(()=>
        {
            lock(_gate)
            {
                using var pool=new NSAutoreleasePool();ObjectDisposedException.ThrowIf(_disposed,this);token.ThrowIfCancellationRequested();
                if((uint)index>=info.FrameCount)throw new ArgumentOutOfRangeException(nameof(index));
                using var image=images.CreateImage(index,new CGImageOptions())??throw new InvalidDataException("PNG动画帧损坏。");
                if(image.Width!=canvasWidth || image.Height!=canvasHeight)throw new NotSupportedException("系统PNG动画未返回完整画布。");
                var width=(int)info.Size.Width;var height=(int)info.Size.Height;var pixels=new byte[checked(width*height*4)];var handle=GCHandle.Alloc(pixels,GCHandleType.Pinned);
                try
                {
                    using var color=CGColorSpace.CreateSrgb();using var context=new CGBitmapContext(handle.AddrOfPinnedObject(),width,height,8,checked(width*4),color,CGBitmapFlags.PremultipliedFirst|CGBitmapFlags.ByteOrder32Little);
                    context.DrawImage(new CGRect(0,0,width,height),image);token.ThrowIfCancellationRequested();return new DecodedImageLease(info.Size,pixels);
                }
                finally {handle.Free();}
            }
        },token);
        public void Dispose(){lock(_gate){if(_disposed)return;_disposed=true;images.Dispose();data.Dispose();}}
    }
}
