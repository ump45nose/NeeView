using System.Security.Cryptography;
using NeeView.Content;
using Xunit;

namespace NeeView.Portable.Tests;

public sealed class ArchiveTests
{
    /// <summary>仅嵌套归档不能被伪装成有效空书籍，返回可识别的能力提示。</summary>
    [Fact]
    public async Task NestedArchiveOnlyReturnsUnsupported()
    {
        await using var workspace = new TestWorkspace(); var path = Path.Combine(workspace.Root, "nested.cbz");
        using (var archive = System.IO.Compression.ZipFile.Open(path, System.IO.Compression.ZipArchiveMode.Create)) archive.CreateEntry("inner.cbz");
        var factory = new ContentSourceFactory(workspace.States, Path.Combine(workspace.Root, "cache"));
        await using var source = await factory.OpenAsync(new(path), TestContext.Current.CancellationToken);
        var error = await Assert.ThrowsAsync<NeeView.Application.ReaderException>(() => source.IndexAsync(TestContext.Current.CancellationToken));
        Assert.Equal(NeeView.Application.FailureKind.Unsupported, error.Kind);
    }
    /// <summary>使用 SharpCompress MIT 测试归档验证普通和固实来源的真实读取。</summary>
    [Theory]
    [InlineData("Rar.rar")]
    [InlineData("Rar.solid.rar")]
    [InlineData("Rar5.solid.rar")]
    [InlineData("7Zip.LZMA.7z")]
    [InlineData("7Zip.solid.7z")]
    public async Task RarAndSevenZipReadImagesInBothDirections(string name)
    {
        await using var workspace = new TestWorkspace();
        var factory = new ContentSourceFactory(workspace.States, Path.Combine(workspace.Root, "cache"));
        await using var source = await factory.OpenAsync(new(Path.Combine(AppContext.BaseDirectory, "Fixtures", name)), TestContext.Current.CancellationToken);
        var index = await source.IndexAsync(TestContext.Current.CancellationToken); Assert.NotEmpty(index.Pages);
        var hashes = new Dictionary<string, byte[]>();
        foreach (var page in index.Pages)
        { await using var stream = await source.OpenReadAsync(page, TestContext.Current.CancellationToken); hashes[page.Name] = await SHA256.HashDataAsync(stream, TestContext.Current.CancellationToken); }
        foreach (var page in index.Pages.Reverse())
        { await using var stream = await source.OpenReadAsync(page, TestContext.Current.CancellationToken); Assert.Equal(hashes[page.Name], await SHA256.HashDataAsync(stream, TestContext.Current.CancellationToken)); }
    }
}
