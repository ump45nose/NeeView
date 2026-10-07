using System.Net;
using System.Text;
using System.Text.Json;
using NeeView.Backends;
using NeeView.MacOS.ViewModels;
namespace NeeView.Engine.Tests;

/// <summary>隔离网络响应和系统打开；Windows包、歧义及关闭晚到均不得提示可安装更新。</summary>
public sealed class MacReleaseTests
{
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handle) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => handle(request, token); }
    private sealed class Platform : IPlatformService
    {
        public List<Uri> Opened { get; } = [];
        public Task OpenUriAsync(Uri uri, CancellationToken token = default) { token.ThrowIfCancellationRequested(); Opened.Add(uri); return Task.CompletedTask; }
        public Task RevealAsync(string path, CancellationToken token = default) => throw new NotSupportedException();
        public Task TrashAsync(string path, CancellationToken token = default) => throw new NotSupportedException();
    }
    private sealed class PendingService(Task<ApplicationRelease> pending) : IApplicationReleaseService
    { public Task<ApplicationRelease> CheckAsync(string version, CancellationToken token) => pending; }
    private static string Payload(string tag = "mac-v1.2.3", string[]? names = null, string? url = null, bool prerelease = false)
    {
        const string root = "https://github.com/ump45nose/NeeView/releases/";
        return JsonSerializer.Serialize(new { tag_name = tag, html_url = root + "tag/" + tag, draft = false, prerelease,
            assets = (names ?? ["NeeView-Mac-arm64.zip"]).Select(name => new { name, size = 100, browser_download_url = url ?? root + "download/" + tag + "/" + name }) });
    }
    private static async Task<ApplicationRelease> CheckAsync(string payload, string current = "0.1.0", HttpStatusCode status = HttpStatusCode.OK)
    {
        using var handler = new Handler((request, _) =>
        {
            Assert.Equal("https://api.github.com/repos/ump45nose/NeeView/releases/latest", request.RequestUri!.AbsoluteUri);
            Assert.Null(request.Headers.Authorization); Assert.Contains("NeeView-Mac", request.Headers.UserAgent.ToString());
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(payload, Encoding.UTF8, "application/json") });
        });
        using var client = new HttpClient(handler); return await new GitHubReleaseService(client).CheckAsync(current, TestContext.Current.CancellationToken);
    }
    [Fact]
    public async Task MissingReleaseAndWindowsAssetsDoNotOfferAnInstall()
    {
        Assert.Equal(ApplicationReleaseStatus.NoRelease, (await CheckAsync("{}", status: HttpStatusCode.NotFound)).Status);
        var windows = await CheckAsync(Payload("46.3", ["NeeView46.3.zip", "NeeView46.3.msi"]));
        Assert.Equal(ApplicationReleaseStatus.NoMacPackage, windows.Status); Assert.Null(windows.Download);
        var ambiguousGeneric = await CheckAsync(Payload("v1.2.3", ["NeeView.zip"]));
        Assert.Equal(ApplicationReleaseStatus.NoMacPackage, ambiguousGeneric.Status);
    }
    [Theory]
    [InlineData("0.1.0", ApplicationReleaseStatus.Available)]
    [InlineData("1.2.3", ApplicationReleaseStatus.Current)]
    [InlineData("2.0.0", ApplicationReleaseStatus.Current)]
    public async Task TrustedUniqueMacAssetUsesTheMacProductVersion(string current, ApplicationReleaseStatus expected)
    {
        var result = await CheckAsync(Payload(), current);
        Assert.Equal(expected, result.Status); Assert.Equal("1.2.3", result.Version); Assert.NotNull(result.Download);
        Assert.Equal("https://github.com/ump45nose/NeeView/releases/tag/mac-v1.2.3", result.Page!.AbsoluteUri);
        Assert.Equal(ApplicationReleaseStatus.Available, (await CheckAsync(Payload(names: ["NeeView.zip"]))).Status);
    }
    [Fact]
    public async Task AmbiguousPrereleaseAndUntrustedAddressesAreRejected()
    {
        Assert.Equal(ApplicationReleaseStatus.NoMacPackage, (await CheckAsync(Payload(names: ["NeeView-Mac-arm64.zip", "NeeView-Mac-osx-arm64.zip"]))).Status);
        Assert.Equal(ApplicationReleaseStatus.NoMacPackage, (await CheckAsync(Payload(prerelease: true))).Status);
        await Assert.ThrowsAsync<IOException>(() => CheckAsync(Payload(url: "https://example.invalid/NeeView.zip")));
        await Assert.ThrowsAsync<IOException>(() => CheckAsync(Payload().Replace("releases/tag/mac-v1.2.3", "releases/tag/other")));
    }
    [Fact]
    public async Task HttpErrorsOversizeAndCancellationRemainFailures()
    {
        await Assert.ThrowsAsync<HttpRequestException>(() => CheckAsync("{}", status: HttpStatusCode.Forbidden));
        await Assert.ThrowsAsync<IOException>(() => CheckAsync(new string(' ', 1024 * 1024 + 1)));
        using var handler = new Handler(async (_, token) => { await Task.Delay(Timeout.Infinite, token); return new(HttpStatusCode.OK); });
        using var client = new HttpClient(handler); using var canceled = new CancellationTokenSource(); canceled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new GitHubReleaseService(client).CheckAsync("0.1.0", canceled.Token));
    }
    [Fact]
    public async Task ClosingVersionWindowRejectsLateResultsAndEveryNewAction()
    {
        var result = new TaskCompletionSource<ApplicationRelease>(TaskCreationOptions.RunContinuationsAsynchronously);
        var platform = new Platform(); var model = new VersionWindowViewModel(platform, (_, _) => Task.CompletedTask, releases: new PendingService(result.Task));
        var check = model.CheckReleaseAsync(); Assert.True(model.IsBusy); model.Dispose();
        result.SetResult(new(ApplicationReleaseStatus.Available, "9.0", new("https://github.com/ump45nose/NeeView/releases/tag/mac-v9.0"), new("https://github.com/ump45nose/NeeView/releases/download/mac-v9.0/NeeView.zip")));
        await check; Assert.Equal("尚未检查Mac发布", model.UpdateStatus); Assert.False(model.CanCheck); Assert.False(model.CanDownload);
        await model.OpenReleaseAsync(); await model.OpenDownloadAsync(); Assert.Empty(platform.Opened);
    }
    [Fact]
    public async Task OriginalDisabledNetworkSettingPreventsReleaseQueries()
    {
        bool previous = Config.Current.System.IsNetworkEnabled;
        Config.Current.System.IsNetworkEnabled = false;
        try
        {
            var pending = new TaskCompletionSource<ApplicationRelease>();
            using var model = new VersionWindowViewModel(new Platform(), (_, _) => Task.CompletedTask, releases: new PendingService(pending.Task));
            Assert.False(model.CanCheck); await model.CheckReleaseAsync();
            Assert.Equal("网络访问已关闭", model.UpdateStatus); Assert.False(model.IsBusy);
        }
        finally { Config.Current.System.IsNetworkEnabled = previous; }
    }
    [Fact]
    public async Task AvailableReleaseOnlyTransfersExplicitLinksToThePlatform()
    {
        var release = await CheckAsync(Payload()); var platform = new Platform();
        using var model = new VersionWindowViewModel(platform, (_, _) => Task.CompletedTask, releases: new PendingService(Task.FromResult(release)));
        Assert.True(model.CanCheck); await model.CheckReleaseAsync(); Assert.True(model.CanDownload); Assert.Empty(platform.Opened);
        await model.OpenReleaseAsync(); await model.OpenDownloadAsync(); Assert.Equal([release.Page!, release.Download!], platform.Opened);
    }
}
