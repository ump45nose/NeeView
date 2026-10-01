using ImageMagick;
using NeeView.Application;
using NeeView.Core;
using NeeView.Imaging;
using Xunit;

#pragma warning disable xUnit1051
namespace NeeView.Portable.Tests;

public sealed class NativeAndSchedulerTests
{
    /// <summary>不可立即取消的旧解码晚到时，新同键请求仍成功且缓存不被旧像素覆盖。</summary>
    [Fact]
    public async Task CancelledNativeWorkCannotCaptureReissuedRequest()
    {
        var decoder = new LateDecoder(); await using var scheduler = new ImageScheduler(decoder); var page = FakeSource.SamplePage();
        using var cancellation = new CancellationTokenSource();
        var first = scheduler.RequestAsync(new FakeSource(), new(page), ImagePriority.Current, cancellation.Token);
        try
        {
            await decoder.Started.Task.WaitAsync(TimeSpan.FromSeconds(5)); cancellation.Cancel(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
            using var second = await scheduler.RequestAsync(new FakeSource(), new(page), ImagePriority.Current, default).WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(2, second.Pixels.Span[0]); decoder.Release.TrySetResult(); await decoder.Returned.Task;
            using var cached = await scheduler.RequestAsync(new FakeSource(), new(page), ImagePriority.Current, default);
            Assert.Equal(2, cached.Pixels.Span[0]); Assert.Equal(256, scheduler.CachedBytes);
        }
        finally { decoder.Release.TrySetResult(); } // 断言失败也释放原生替身，避免异步 Dispose 永久等待。
    }
    /// <summary>EXIF 转向后的探测尺寸与解码尺寸一致，GIF 只返回首帧。</summary>
    [Fact]
    public async Task ExifOrientationAndGifFirstFrameRespectPixelContract()
    {
        var decoder = new MagickImageDecoder();
        using var original = new MagickImage(MagickColors.Red, 80, 120); var exif = new ExifProfile(); exif.SetValue(ExifTag.Orientation, (ushort)6); original.SetProfile(exif);
        original.Orientation = OrientationType.RightTop; // 编码器以图像属性写回 EXIF，夹具必须同步声明旋转方向。
        await using var stream = new MemoryStream(original.ToByteArray(MagickFormat.Jpeg));
        var probe = await decoder.ProbeAsync(stream, default); Assert.Equal(new(120, 80), probe.Size); stream.Position = 0;
        using var oriented = await decoder.DecodeAsync(stream, new(FakeSource.SamplePage()), default); Assert.Equal(probe.Size, oriented.Size);
        using var frames = new MagickImageCollection(); frames.Add(new MagickImage(MagickColors.Red, 8, 8)); frames.Add(new MagickImage(MagickColors.Blue, 8, 8));
        await using var gif = new MemoryStream(frames.ToByteArray(MagickFormat.Gif));
        using var first = await decoder.DecodeAsync(gif, new(FakeSource.SamplePage()), default); Assert.Equal(255, first.Pixels.Span[2]); Assert.Equal(0, first.Pixels.Span[0]);
    }
    /// <summary>模拟原生调用忽略取消，第一份像素只能由调度器在晚到时释放。</summary>
    private sealed class LateDecoder : IImageDecoder
    {
        public TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Returned = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _calls;
        public Task<ImageInfo> ProbeAsync(Stream stream, CancellationToken token) => Task.FromResult(new ImageInfo(new(8, 8), "test"));
        public async Task<DecodedImageLease> DecodeAsync(Stream stream, DecodeRequest request, CancellationToken token)
        {
            var call = Interlocked.Increment(ref _calls);
            if (call == 1) { Started.SetResult(); await Release.Task; Returned.SetResult(); }
            var pixels = new byte[256]; pixels[0] = (byte)call; return new(new(8, 8), pixels);
        }
    }
}
