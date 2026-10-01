using NeeView.Application;
using NeeView.Core;
using NeeView.Content;
using NeeView.Persistence;

namespace NeeView.Portable.Tests;

/// <summary>测试拥有独立目录和数据库，绝不操作用户图片或配置。</summary>
public sealed class TestWorkspace : IAsyncDisposable
{
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "neeview-test-" + Guid.NewGuid().ToString("N"));
    public SqliteStateStore States { get; }
    public JsonSettingsStore Settings { get; }
    public TestWorkspace()
    {
        Directory.CreateDirectory(Root); States = new(Path.Combine(Root, "state.sqlite")); Settings = new(Path.Combine(Root, "settings.json"));
    }
    public ReaderSession Session() => new(new ContentSourceFactory(States, Path.Combine(Root, "cache")), States, Settings);
    public FileActionService FileService() => new(States, States, new TestPlatform(), Path.Combine(Root, "backups"));
    /// <summary>生成普通文件元数据并登记真实身份。</summary>
    public async Task<PageDescriptor> PageAsync(string path)
    { var file = new FileInfo(path); return new(await States.GetContentAsync(new(path), default), file.Name, new(path), new(file.Length, file.LastWriteTimeUtc.Ticks), 0); }
    public async ValueTask DisposeAsync() { await States.DisposeAsync(); Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); Directory.Delete(Root, true); }
}
public sealed class TestPlatform : IPlatformService
{
    public Task RevealAsync(string path, CancellationToken token = default) => Task.CompletedTask;
    public Task TrashAsync(string path, CancellationToken token = default) { File.Move(path, path + ".trash"); return Task.CompletedTask; }
}
public sealed class FakeDecoder : IImageDecoder
{
    public int Calls;
    public TaskCompletionSource? Gate;
    public Task<ImageInfo> ProbeAsync(Stream stream, CancellationToken token) => Task.FromResult(new ImageInfo(new(8, 8), "test"));
    public async Task<DecodedImageLease> DecodeAsync(Stream stream, DecodeRequest request, CancellationToken token)
    { Interlocked.Increment(ref Calls); if (Gate is not null) await Gate.Task.WaitAsync(token); token.ThrowIfCancellationRequested(); return new(new(8, 8), new byte[256]); }
}
public sealed class FakeSource : IContentSource
{
    public SourceLocator Locator => new("fake");
    public SourceCapabilities Capabilities => new(false, AccessCost.Random, false);
    public Task<Stream> OpenReadAsync(PageDescriptor page, CancellationToken cancellationToken) => Task.FromResult<Stream>(new MemoryStream([1, 2, 3]));
    public Task<SourceIndex> IndexAsync(CancellationToken cancellationToken) => Task.FromResult(new SourceIndex(new("book"), Locator, [SamplePage()], null, Capabilities));
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    public static PageDescriptor SamplePage(string id = "image", int width = 600, int height = 900) => new(new(id), id + ".jpg", new(id + ".jpg"), new(100, 1), 0, new(width, height));
}
