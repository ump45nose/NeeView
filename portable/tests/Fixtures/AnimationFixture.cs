using System.Buffers.Binary;
using System.IO.Compression;
using ImageMagick;
using NeeView;
namespace NeeView.Tests;
internal static class AnimationFixture
{
    /// <summary>两个明确颜色和不等时长；PNG直接生成标准APNG块，不调用外部编码器。</summary>
    public static byte[] Create(AnimatedImageType type)
    {
        if(type==AnimatedImageType.Png)return Apng();
        using var frames = new MagickImageCollection();
        frames.Add(new MagickImage(MagickColors.Red,2,2) { AnimationDelay=10, AnimationTicksPerSecond=100 });
        frames.Add(new MagickImage(MagickColors.Lime,2,2) { AnimationDelay=30, AnimationTicksPerSecond=100 });
        return frames.ToByteArray(type==AnimatedImageType.Gif?MagickFormat.Gif:MagickFormat.WebP);
    }
    /// <summary>GIF局部帧和Previous销毁：绿色临时覆盖后，蓝色落到右下；左上应恢复红色。</summary>
    public static byte[] CreatePreviousGif()
    {
        using var frames=new MagickImageCollection();
        frames.Add(new MagickImage(MagickColors.Red,2,2){AnimationDelay=10,Page=new MagickGeometry(0,0,2,2),GifDisposeMethod=GifDisposeMethod.None});
        frames.Add(new MagickImage(MagickColors.Lime,1,1){AnimationDelay=30,Page=new MagickGeometry(0,0,2,2),GifDisposeMethod=GifDisposeMethod.Previous});
        frames.Add(new MagickImage(MagickColors.Blue,1,1){AnimationDelay=20,Page=new MagickGeometry(1,1,2,2),GifDisposeMethod=GifDisposeMethod.None});
        return frames.ToByteArray(MagickFormat.Gif);
    }
    private static byte[] Apng()
    {
        using var result = new MemoryStream(); result.Write(new byte[]{137,80,78,71,13,10,26,10});
        var header=new byte[13]; Put(header,0,2);Put(header,4,2);header[8]=8;header[9]=6; Chunk(result,"IHDR",header);
        var actl=new byte[8];Put(actl,0,2); Chunk(result,"acTL",actl);
        Chunk(result,"fcTL",Control(0,2,2,10)); Chunk(result,"IDAT",Pixels(2,2,255,0,0));
        Chunk(result,"fcTL",Control(1,1,1,30)); var compressed=Pixels(1,1,0,255,0); var fdat=new byte[compressed.Length+4];Put(fdat,0,2);compressed.CopyTo(fdat,4);Chunk(result,"fdAT",fdat);
        Chunk(result,"IEND",[]); return result.ToArray();
    }
    /// <summary>第三帧带半透明蓝色和偏移；第二帧按背景/前一画布规则销毁。</summary>
    public static byte[] CreateDisposalApng(byte dispose)
    {
        using var result = new MemoryStream(); result.Write(new byte[]{137,80,78,71,13,10,26,10});
        var header=new byte[13];Put(header,0,2);Put(header,4,2);header[8]=8;header[9]=6;Chunk(result,"IHDR",header);
        var actl=new byte[8];Put(actl,0,3);Chunk(result,"acTL",actl);
        Chunk(result,"fcTL",Control(0,2,2,10));Chunk(result,"IDAT",Pixels(2,2,255,0,0));
        Chunk(result,"fcTL",Control(1,1,1,30,dispose:dispose));FrameData(2,Pixels(1,1,0,255,0));
        Chunk(result,"fcTL",Control(3,1,1,20,x:1,y:1));FrameData(4,Pixels(1,1,0,0,255,128));
        Chunk(result,"IEND",[]);return result.ToArray();
        void FrameData(uint seq,byte[] pixels){var data=new byte[pixels.Length+4];Put(data,0,seq);pixels.CopyTo(data,4);Chunk(result,"fdAT",data);}
    }
    private static byte[] Control(uint sequence,uint width,uint height,ushort delay,uint x=0,uint y=0,byte dispose=0)
    {var b=new byte[26];Put(b,0,sequence);Put(b,4,width);Put(b,8,height);Put(b,12,x);Put(b,16,y); BinaryPrimitives.WriteUInt16BigEndian(b.AsSpan(20),delay);BinaryPrimitives.WriteUInt16BigEndian(b.AsSpan(22),100);b[24]=dispose;return b;}
    private static byte[] Pixels(int width,int height,byte red,byte green,byte blue,byte alpha=255)
    {
        using var output=new MemoryStream();using(var z=new ZLibStream(output,CompressionLevel.Optimal,true))
        {for(int y=0;y<height;y++){z.WriteByte(0);for(int x=0;x<width;x++)z.Write(new byte[]{red,green,blue,alpha});}}
        return output.ToArray();
    }
    private static void Put(byte[] b,int offset,uint value)=>BinaryPrimitives.WriteUInt32BigEndian(b.AsSpan(offset),value);
    private static void Chunk(Stream s,string kind,byte[] data)
    {
        var b=new byte[4];Put(b,0,(uint)data.Length);s.Write(b);var name=System.Text.Encoding.ASCII.GetBytes(kind);s.Write(name);s.Write(data);
        using var crc=new SharpCompress.Crypto.Crc32Stream(Stream.Null);crc.Write(name);crc.Write(data);Put(b,0,crc.Crc);s.Write(b);
    }
}
