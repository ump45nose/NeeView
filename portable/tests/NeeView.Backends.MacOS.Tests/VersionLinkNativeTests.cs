using NeeView.Backends;
namespace NeeView.Backends.MacOS.Tests;

/// <summary>真实平台链接入口的提交前拒绝/取消；不打开网页、系统文件或产品窗口。</summary>
public sealed class VersionLinkNativeTests
{
    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("file://remote-host/README.md")]
    public async Task UnsupportedLinksCannotEnterSystemLauncher(string uri)
    { await Assert.ThrowsAsync<NotSupportedException>(() => new MacPlatformService().OpenUriAsync(new Uri(uri), TestContext.Current.CancellationToken)); }
    [Fact]
    public async Task CanceledLinkDoesNotEnterSystemLauncher()
    {
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new MacPlatformService().OpenUriAsync(new("https://github.com/ump45nose/NeeView"), cancel.Token));
    }
    [Fact]
    public async Task MissingBundledLicenseReturnsRealFailure()
    {
        var path = Path.Combine(Path.GetTempPath(), "NeeView-P5-absent-" + Guid.NewGuid().ToString("N") + ".md");
        await Assert.ThrowsAsync<FileNotFoundException>(() => new MacPlatformService().OpenUriAsync(new Uri(path), TestContext.Current.CancellationToken));
    }
}
