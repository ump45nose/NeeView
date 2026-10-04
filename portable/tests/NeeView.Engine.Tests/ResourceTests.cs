using ImageMagick;
using NeeView;
using NeeView.Backends;
namespace NeeView.Engine.Tests;

public sealed class ResourceTests
{
    /// <summary>一个共享请求取消不会释放其他等待者或显示中的像素，超预算只回收无租约条目。</summary>
    [Fact]
    public async Task SharedDecodeCancellationAndPinnedBudget()
    {
        using var fixture = new Fixture(); await using var archive = await new ArchiveFactory().OpenAsync(fixture.Images, TestContext.Current.CancellationToken);
        var page = new Page((await archive.GetEntriesAsync(TestContext.Current.CancellationToken))[0]);
        var decoder = new ControlledDecoder(); using var cache = new BitmapFactory(decoder) { Budget = 1 };
        using var cancelled = new CancellationTokenSource();
        var first = cache.GetAsync(page, new(8, 8), cancelled.Token);
        var second = cache.GetAsync(page, new(8, 8), TestContext.Current.CancellationToken);
        await decoder.Started.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        cancelled.Cancel(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        decoder.Release.TrySetResult();
        using (var lease = await second)
        {
            lease.RegisterDisplayBytes(256);
            using var shared = await cache.GetAsync(page, new(8, 8), TestContext.Current.CancellationToken);
            Assert.Same(lease.Image, shared.Image); Assert.Equal(1, decoder.Calls); Assert.Equal(512, cache.ByteCount);
            Assert.Equal(256, lease.Image.Pixels.Length);
        }
        Assert.Equal(0, cache.ByteCount); Assert.Empty(decoder.LastImage!.Pixels);
    }
    /// <summary>工厂关闭后不可中断的原生结果只能清理，不能返回新显示租约。</summary>
    [Fact]
    public async Task LateNativeResultAfterCloseIsReleased()
    {
        using var fixture = new Fixture(); await using var archive = await new ArchiveFactory().OpenAsync(fixture.Images, TestContext.Current.CancellationToken);
        var page = new Page((await archive.GetEntriesAsync(TestContext.Current.CancellationToken))[0]);
        var decoder = new ControlledDecoder(); using var cache = new BitmapFactory(decoder);
        var work = cache.GetAsync(page, new(8, 8), TestContext.Current.CancellationToken);
        await decoder.Started.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        cache.Dispose(); decoder.Release.TrySetResult();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => work);
        Assert.Empty(decoder.LastImage!.Pixels);
    }
    /// <summary>旧原生工作忽略取消时，同键新请求独立排队，不能收到旧结果或丢失新登记。</summary>
    [Fact]
    public async Task CancelledNativeWorkDoesNotCaptureReissuedRequest()
    {
        using var fixture = new Fixture(); await using var archive = await new ArchiveFactory().OpenAsync(fixture.Images, TestContext.Current.CancellationToken);
        var page = new Page((await archive.GetEntriesAsync(TestContext.Current.CancellationToken))[0]);
        var decoder = new ControlledDecoder(); using var cache = new BitmapFactory(decoder); using var cancellation = new CancellationTokenSource();
        var old = cache.GetAsync(page, new(8, 8), cancellation.Token);
        await decoder.Started.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        cancellation.Cancel(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => old);
        var current = cache.GetAsync(page, new(8, 8), TestContext.Current.CancellationToken);
        for (int i = 0; i < 100 && Volatile.Read(ref decoder.Calls) < 2; i++) await Task.Delay(10, TestContext.Current.CancellationToken);
        decoder.Release.TrySetResult();
        using var lease = await current; Assert.Equal(2, decoder.Calls); Assert.Equal(2, lease.Image.Pixels[0]);
        using var repeated = await cache.GetAsync(page, new(8, 8), TestContext.Current.CancellationToken); Assert.Same(lease.Image, repeated.Image);
    }
    /// <summary>保留已验证解码契约：EXIF、首帧、透明预乘与 JPEG 不放大。</summary>
    [Fact]
    public async Task DecoderPreservesOrientationFirstFrameAndAlpha()
    {
        var decoder = new MagickImageDecoder(); var token = TestContext.Current.CancellationToken;
        using var original = new MagickImage(MagickColors.Red, 80, 120);
        var exif = new ExifProfile(); exif.SetValue(ExifTag.Orientation, (ushort)6); original.SetProfile(exif); original.Orientation = OrientationType.RightTop;
        await using var jpeg = new MemoryStream(original.ToByteArray(MagickFormat.Jpeg));
        var probe = await decoder.ProbeAsync(jpeg, token); Assert.Equal(new(120, 80), probe.Size); jpeg.Position = 0;
        using var oriented = await decoder.DecodeAsync(jpeg, new(2048, 4096), token); Assert.Equal(probe.Size, oriented.Size);
        using var frames = new MagickImageCollection(); frames.Add(new MagickImage(MagickColors.Red, 8, 8)); frames.Add(new MagickImage(MagickColors.Blue, 8, 8));
        await using var gif = new MemoryStream(frames.ToByteArray(MagickFormat.Gif));
        using var first = await decoder.DecodeAsync(gif, new(8, 8), token); Assert.Equal(255, first.Pixels[2]); Assert.Equal(0, first.Pixels[0]);
        using var transparent = new MagickImage(new MagickColor(255, 0, 0, 128), 8, 8);
        await using var png = new MemoryStream(transparent.ToByteArray(MagickFormat.Png));
        using var alpha = await decoder.DecodeAsync(png, new(8, 8), token); Assert.InRange(alpha.Pixels[2], 127, 129); Assert.InRange(alpha.Pixels[3], 127, 129);
    }
    /// <summary>两文件准备失败不改旧 History，关闭失败后当前书仍能翻页并重试。</summary>
    [Fact]
    public async Task FailedSaveDoesNotDestroyReaderOrPreviousHistory()
    {
        using var fixture = new Fixture(); var state = new SaveData(fixture.State); await state.LoadAsync(TestContext.Current.CancellationToken);
        var operation = fixture.Operation(state); await operation.OpenAsync(fixture.Images, TestContext.Current.CancellationToken); await operation.SaveAsync();
        var historyPath = Path.Combine(fixture.State, "History.json"); var before = await File.ReadAllTextAsync(historyPath, TestContext.Current.CancellationToken);
        var settingPath = Path.Combine(fixture.State, "UserSetting.json"); File.Delete(settingPath); Directory.CreateDirectory(settingPath);
        await Assert.ThrowsAnyAsync<IOException>(() => operation.DisposeAsync().AsTask());
        Assert.Equal(before, await File.ReadAllTextAsync(historyPath, TestContext.Current.CancellationToken));
        Assert.NotNull(operation.Book); await operation.MoveAsync(1); Assert.Equal(1, operation.Book.CurrentPage!.Index);
        Directory.Delete(settingPath); await operation.DisposeAsync(); Assert.Null(operation.Book);
    }
    /// <summary>中断于双文件提交中间时，加载先恢复原完整文件再解释 JSON。</summary>
    [Fact]
    public async Task InterruptedPairSaveRestoresBothFiles()
    {
        using var fixture = new Fixture(); var state = new SaveData(fixture.State); await state.LoadAsync(TestContext.Current.CancellationToken); await state.SaveAsync(null, TestContext.Current.CancellationToken);
        var names = new[] { "History.json", "UserSetting.json" }; var originals = names.Select(name => File.ReadAllText(Path.Combine(fixture.State, name))).ToArray();
        foreach (var name in names) File.Copy(Path.Combine(fixture.State, name), Path.Combine(fixture.State, name + ".save-backup"));
        await File.WriteAllTextAsync(Path.Combine(fixture.State, ".save-pending.json"), """{"History.json":true,"UserSetting.json":true}""", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(fixture.State, "History.json"), "{}", TestContext.Current.CancellationToken);
        await new SaveData(fixture.State).LoadAsync(TestContext.Current.CancellationToken);
        for (int i = 0; i < names.Length; i++) Assert.Equal(originals[i], File.ReadAllText(Path.Combine(fixture.State, names[i])));
        Assert.False(File.Exists(Path.Combine(fixture.State, ".save-pending.json")));
    }
    /// <summary>控制不可中断解码返回时机，不模拟真实格式解码成功。</summary>
    private sealed class ControlledDecoder : IImageDecoder
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Calls;
        public DecodedImageLease? LastImage;
        /// <summary>缓存测试不调用探测，意外调用必须失败。</summary>
        public Task<ImageInfo> ProbeAsync(Stream stream, CancellationToken token) => throw new NotSupportedException();
        /// <summary>忽略取消以模拟原生晚到结果，所有者由缓存测试核验。</summary>
        public async Task<DecodedImageLease> DecodeAsync(Stream stream, DecodeRequest request, CancellationToken token)
        {
            var call = Interlocked.Increment(ref Calls); Started.TrySetResult(); await Release.Task;
            var pixels = new byte[256]; pixels[0] = (byte)call;
            return LastImage = new(new(8, 8), pixels);
        }
    }
}
