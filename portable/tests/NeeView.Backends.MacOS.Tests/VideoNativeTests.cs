using AVFoundation;
using CoreMedia;
using CoreVideo;
using Foundation;
using System.Runtime.InteropServices;
using NeeView;
namespace NeeView.Backends.MacOS.Tests;
/// <summary>系统编码合成H264，再经真正AVPlayer播放/seek/EOS；始终静音且不创建窗口。</summary>
public sealed class VideoNativeTests
{
    private static CancellationToken Token=>TestContext.Current.CancellationToken;
    private sealed class Fixture : IAsyncDisposable
    {
        public string Root {get;}=System.IO.Path.Combine(System.IO.Path.GetTempPath(),"NeeView-video-tests-"+Guid.NewGuid().ToString("N"));
        public string Path=>System.IO.Path.Combine(Root,"synthetic.mp4");
        public ArchiveEntryRealizer Realizer {get;}
        public Fixture(){Directory.CreateDirectory(Root);Realizer=new(System.IO.Path.Combine(Root,"Realized"));}
        public async Task GenerateAsync(bool rotated=false)
        {
            using var url=NSUrl.FromFilename(Path);using var writer=new AVAssetWriter(url,AVFileTypes.Mpeg4.GetConstant()!,out var error);error?.Dispose();
            // 明确合成像素的sRGB/709矩阵，避免未标记小尺寸H264被系统按601/gamma解释。
            var colors=new AVColorProperties{AVVideoColorPrimaries=AVVideoColorPrimaries.Itu_R_709_2,AVVideoTransferFunction=AVVideoTransferFunction.Iec_sRgb};
            var compression=new AVVideoSettingsCompressed{CodecType=AVVideoCodecType.H264,Width=64,Height=32};
            var library=ObjCRuntime.Dlfcn.dlopen("/System/Library/Frameworks/AVFoundation.framework/AVFoundation",0);
            try {var key=ObjCRuntime.Dlfcn.GetStringConstant(library,"AVVideoYCbCrMatrixKey");Assert.NotNull(key);colors.Dictionary.SetValueForKey(AVVideoYCbCrMatrix.Itu_R_709_2,key);
                var colorKey=ObjCRuntime.Dlfcn.GetStringConstant(library,"AVVideoColorPropertiesKey");Assert.NotNull(colorKey);compression.Dictionary.SetValueForKey(colors.Dictionary,colorKey);}
            finally{ObjCRuntime.Dlfcn.dlclose(library);}
            using var input=new AVAssetWriterInput(AVMediaTypes.Video.GetConstant()!,compression);colors.Dictionary.Dispose();compression.Dictionary.Dispose();
            if(rotated)input.Transform=new CoreGraphics.CGAffineTransform(0,1,-1,0,32,0);
            var attributes=new CVPixelBufferAttributes{PixelFormatType=CVPixelFormatType.CV32BGRA,Width=64,Height=32};
            using var adaptor=new AVAssetWriterInputPixelBufferAdaptor(input,attributes);attributes.Dictionary.Dispose();
            writer.AddInput(input);Assert.True(writer.StartWriting());writer.StartSessionAtSourceTime(CMTime.Zero);
            for(int i=0;i<20;i++)
            {
                for(int wait=0;!input.ReadyForMoreMediaData;wait++){if(wait>1000)throw new TimeoutException("系统视频编码等待超时。");await Task.Delay(5,Token);}
                using var pixel=new CVPixelBuffer(64,32,CVPixelFormatType.CV32BGRA);
                Assert.Equal(CVReturn.Success,pixel.Lock(CVPixelBufferLock.None));
                try
                {
                    var row=Enumerable.Range(0,64).SelectMany(_=>i<10?new byte[]{0,0,255,255}:new byte[]{0,255,0,255}).ToArray();
                    for(int y=0;y<32;y++)Marshal.Copy(row,0,pixel.BaseAddress+y*(int)pixel.BytesPerRow,row.Length);
                }
                finally{pixel.Unlock(CVPixelBufferLock.None);}
                Assert.True(adaptor.AppendPixelBufferWithPresentationTime(pixel,new CMTime(i,10)));
            }
            input.MarkAsFinished();await writer.FinishWritingAsync();Assert.Equal(AVAssetWriterStatus.Completed,writer.Status);
        }
        public async ValueTask DisposeAsync(){await Realizer.DisposeAsync();Directory.Delete(Root,true);}
    }
    [Fact]
    public async Task ActualH264ProbePlaybackSeekPauseAndDisabledIntentUseSystemClock()
    {
        await using var f=new Fixture();await f.GenerateAsync();var factory=new MacVideoPlayerFactory(f.Realizer);
        await using var archive=new MediaArchiveSource(f.Path);var entry=(await archive.GetEntriesAsync(Token)).Single();
        var info=await factory.ProbeAsync(entry,Token);Assert.True(info.HasVideo);Assert.False(info.HasAudio);Assert.Equal(new Size(64,32),info.Size);Assert.InRange(info.Duration.TotalSeconds,1.8,2.1);
        await using var player=await factory.OpenAsync(entry,new(64,32),1024*1024,Token);player.IsMuted=true;
        async Task<DecodedImageLease> Frame(){for(int i=0;i<200;i++){var image=await player.ReadFrameAsync(new(64,32),Token);if(image is not null)return image;await Task.Delay(10,Token);}throw new TimeoutException("实际视频帧未输出。");}
        using(var first=await Frame()){Assert.InRange(first.Pixels[2],230,255);Assert.InRange(first.Pixels[1],0,20);}
        player.Position=.65;using(var seek=await Frame()){Assert.InRange(seek.Pixels[1],230,255);Assert.InRange(seek.Pixels[2],0,20);}
        using(var resized=await player.ReadFrameAsync(new(32,16),Token)){Assert.NotNull(resized);Assert.Equal(new Size(32,16),resized.Size);Assert.InRange(resized.Pixels[1],230,255);Assert.InRange(resized.Pixels[2],0,20);}
        Assert.InRange(player.Position,.63,.67);player.Play();await player.SynchronizeAsync(Token);await Task.Delay(120,Token);await player.SynchronizeAsync(Token);Assert.True(player.Position>.67);
        player.IsEnabled=false;await player.SynchronizeAsync(Token);double frozen=player.Position;Assert.True(player.IsPlaying);await Task.Delay(100,Token);await player.SynchronizeAsync(Token);Assert.InRange(player.Position,frozen-.01,frozen+.01);
        player.IsEnabled=true;await player.SynchronizeAsync(Token);await Task.Delay(100,Token);await player.SynchronizeAsync(Token);Assert.True(player.Position>frozen);player.Pause();await player.SynchronizeAsync(Token);Assert.False(player.IsPlaying);
        Assert.False(player.AddPosition(TimeSpan.FromSeconds(-.1)));Assert.True(player.AddPosition(TimeSpan.FromSeconds(20)));
    }
    [Fact]
    public async Task ActualEosCounterPrecedesEventAndRepeatReplaysWithoutNewPlayer()
    {
        await using var f=new Fixture();await f.GenerateAsync();await using var archive=new MediaArchiveSource(f.Path);var entry=(await archive.GetEntriesAsync(Token)).Single();
        await using var player=await new MacVideoPlayerFactory(f.Realizer).OpenAsync(entry,new(32,16),1024*1024,Token);player.IsMuted=true;player.IsRepeat=true;
        int observed=0;player.MediaEndOfStreamReached+=(_,_)=>observed=player.EndOfStreamCount;
        player.Position=.95;player.Play();await player.SynchronizeAsync(Token);
        for(int i=0;i<200&&observed==0;i++){await Task.Delay(10,Token);await player.SynchronizeAsync(Token);}
        Assert.Equal(1,observed);Assert.True(player.IsPlaying);await player.SynchronizeAsync(Token);Assert.True(player.Position<.2);
        await player.DisposeAsync();Assert.True(player.IsDisposed);await Assert.ThrowsAsync<ObjectDisposedException>(()=>player.ReadFrameAsync(new(32,16),Token));
    }
    [Fact]
    public async Task BudgetAndCancellationRejectWorkAndArchiveMaterialsAreOwnedByPlayer()
    {
        await using var f=new Fixture();await f.GenerateAsync();var factory=new MacVideoPlayerFactory(f.Realizer);
        await using var archive=new MediaArchiveSource(f.Path);var entry=(await archive.GetEntriesAsync(Token)).Single();
        await Assert.ThrowsAsync<NotSupportedException>(()=>factory.OpenAsync(entry,new(64,32),100,Token));
        await Assert.ThrowsAsync<NotSupportedException>(()=>factory.OpenAsync(entry,new(int.MaxValue,int.MaxValue),long.MaxValue,Token));
        using var cancel=new CancellationTokenSource();cancel.Cancel();await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>factory.ProbeAsync(entry,cancel.Token));
        var zip=System.IO.Path.Combine(f.Root,"video.zip");using(var package=System.IO.Compression.ZipFile.Open(zip,System.IO.Compression.ZipArchiveMode.Create))System.IO.Compression.ZipFileExtensions.CreateEntryFromFile(package,f.Path,"movie.mp4");
        await using var source=await new ArchiveFactory().OpenAsync(zip,Token);var internalEntry=(await source.GetEntriesAsync(Token)).Single(e=>!e.IsDirectory);
        var player=await factory.OpenAsync(internalEntry,new(32,16),1024*1024,Token);Assert.Single(Directory.GetFiles(System.IO.Path.Combine(f.Root,"Realized"),"*",SearchOption.AllDirectories));
        await player.DisposeAsync();Assert.Empty(Directory.GetFiles(System.IO.Path.Combine(f.Root,"Realized"),"*",SearchOption.AllDirectories));
    }
    [Fact]
    public async Task SharedNativeBudgetIsReturnedOnlyAfterRealCloseAndDirectionIsApplied()
    {
        await using var f=new Fixture();await f.GenerateAsync(true);var factory=new MacVideoPlayerFactory(f.Realizer);
        await using var source=new MediaArchiveSource(f.Path);var entry=(await source.GetEntriesAsync(Token)).Single();
        var info=await factory.ProbeAsync(entry,Token);Assert.Equal(new Size(32,64),info.Size);Assert.Equal(info.Size,info.AspectSize);
        var first=await factory.OpenAsync(entry,new(32,64),41000,Token);first.IsMuted=true;
        await Assert.ThrowsAsync<NotSupportedException>(()=>factory.OpenAsync(entry,new(32,64),41000,Token));
        await first.DisposeAsync();await using var second=await factory.OpenAsync(entry,new(32,64),41000,Token);second.IsMuted=true;
        DecodedImageLease? frame=null;for(int i=0;i<200&&frame is null;i++){frame=await second.ReadFrameAsync(new(32,64),Token);if(frame is null)await Task.Delay(10,Token);}
        Assert.NotNull(frame);using(frame){Assert.Equal(new Size(32,64),frame.Size);Assert.InRange(frame.Pixels[2],230,255);Assert.InRange(frame.Pixels[1],0,20);}
    }
    [Fact]
    public async Task CloseRequestedInsideActualEosEventPreventsLaterEventsAndDoesNotDeadlockSeek()
    {
        await using var f=new Fixture();await f.GenerateAsync();await using var source=new MediaArchiveSource(f.Path);var entry=(await source.GetEntriesAsync(Token)).Single();
        var player=await new MacVideoPlayerFactory(f.Realizer).OpenAsync(entry,new(32,16),1024*1024,Token);player.IsMuted=true;
        var closing=new TaskCompletionSource<Task>(TaskCreationOptions.RunContinuationsAsynchronously);int ended=0;
        player.MediaEndOfStreamReached+=(_,_)=>closing.TrySetResult(player.DisposeAsync().AsTask());player.MediaEnded+=(_,_)=>ended++;
        try
        {
            player.Position=.95;player.Play();await player.SynchronizeAsync(Token);
            var close=await closing.Task.WaitAsync(TimeSpan.FromSeconds(5),Token);await close.WaitAsync(TimeSpan.FromSeconds(5),Token);
            Assert.True(player.IsDisposed);Assert.Equal(1,player.EndOfStreamCount);Assert.Equal(0,ended);
        }
        finally{await player.DisposeAsync();}
    }
}
