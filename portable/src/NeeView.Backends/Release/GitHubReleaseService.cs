using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using NeeView;
namespace NeeView.Backends;

/// <summary>原检查器的独立fork适配；仅查询公开发布，不使用令牌、不下载或替换应用。</summary>
public sealed class GitHubReleaseService(HttpClient? client = null) : IApplicationReleaseService
{
    private static readonly HttpClient Shared = new(new HttpClientHandler { AllowAutoRedirect = false });
    private const string Repository = "https://github.com/ump45nose/NeeView";
    private const int MaximumResponse = 1024 * 1024;

    /// <summary>请求限制十秒及1MiB；404表示尚无发布，其他失败允许用户重试。</summary>
    public async Task<ApplicationRelease> CheckAsync(string currentVersion, CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/repos/ump45nose/NeeView/releases/latest");
        request.Headers.UserAgent.ParseAdd("NeeView-Mac/" + currentVersion);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        try
        {
            using var response = await (client ?? Shared).SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.NotFound) return new(ApplicationReleaseStatus.NoRelease);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is > MaximumResponse) throw new IOException("发布响应超过大小限制。");
            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
            using var data = new MemoryStream(); var buffer = new byte[16 * 1024]; int count;
            while ((count = await stream.ReadAsync(buffer, timeout.Token).ConfigureAwait(false)) > 0)
            { if (data.Length + count > MaximumResponse) throw new IOException("发布响应超过大小限制。"); await data.WriteAsync(buffer.AsMemory(0, count), timeout.Token).ConfigureAwait(false); }
            using var document = JsonDocument.Parse(data.ToArray());
            return Parse(document.RootElement, currentVersion);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        { throw new TimeoutException("发布检查超过十秒，请稍后重试。"); }
    }

    /// <summary>仅接受同一fork、稳定版本和唯一ARM64包；Windows资产与模糊命名不提示安装。</summary>
    internal static ApplicationRelease Parse(JsonElement release, string currentVersion)
    {
        if (release.ValueKind != JsonValueKind.Object) throw new IOException("发布信息不是对象。");
        if (Flag(release, "draft") || Flag(release, "prerelease")) return new(ApplicationReleaseStatus.NoMacPackage);
        var tag = Text(release, "tag_name");
        var versionText = tag.StartsWith("mac-", StringComparison.OrdinalIgnoreCase) ? tag[4..] : tag;
        if (versionText.StartsWith('v')) versionText = versionText[1..];
        if (!Version.TryParse(versionText, out var version) || !Version.TryParse(currentVersion.Split('+')[0], out var current))
            return new(ApplicationReleaseStatus.NoMacPackage);
        var page = new Uri(Repository + "/releases/tag/" + Uri.EscapeDataString(tag));
        if (Text(release, "html_url") != page.AbsoluteUri) throw new IOException("发布页面不属于当前Mac仓库。");
        var candidates = new List<Uri>();
        if (release.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
            foreach (var asset in assets.EnumerateArray())
            {
                var name = Text(asset, "name");
                bool namedMac = name is "NeeView-Mac-arm64.zip" or "NeeView-Mac-osx-arm64.zip";
                if (!namedMac && !(name == "NeeView.zip" && tag.StartsWith("mac-", StringComparison.OrdinalIgnoreCase))) continue;
                var expected = Repository + "/releases/download/" + Uri.EscapeDataString(tag) + "/" + Uri.EscapeDataString(name);
                if (Text(asset, "browser_download_url") != expected) throw new IOException("Mac资产地址不属于当前发布。");
                if (!asset.TryGetProperty("size", out var size) || !size.TryGetInt64(out var bytes) || bytes <= 0) continue;
                candidates.Add(new Uri(expected));
            }
        if (candidates.Count != 1) return new(ApplicationReleaseStatus.NoMacPackage, version.ToString(), page);
        return new(version > current ? ApplicationReleaseStatus.Available : ApplicationReleaseStatus.Current, version.ToString(), page, candidates[0]);
    }
    private static bool Flag(JsonElement element, string name) => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;
    private static string Text(JsonElement element, string name) => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";
}
