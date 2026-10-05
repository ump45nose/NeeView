using Foundation;
namespace NeeView.Backends;

/// <summary>Finder别名打开适配。只解析系统确认的alias文件，不挂载卷或弹窗，不用于删除目标。</summary>
public static class MacFileAliases
{
    /// <summary>返回Finder别名目标；普通文件返回null，无法解析的别名回报真实系统错误。</summary>
    /// <param name="path">本机文件路径。</param><returns>别名目标的本机路径，或普通文件的null。</returns>
    public static string? Resolve(string path)
    {
        using var url = NSUrl.FromFilename(path);
        if (!url.TryGetResource(NSUrl.IsAliasFileKey, out var value, out var error))
        { try { throw new IOException(error?.LocalizedDescription ?? "无法检查Finder别名。"); } finally { value?.Dispose(); error?.Dispose(); } }
        bool alias;
        try { alias = value is NSNumber number && number.BoolValue; } finally { value?.Dispose(); error?.Dispose(); }
        if (!alias) return null;
        using var resolved = NSUrl.ResolveAlias(url, NSUrlBookmarkResolutionOptions.WithoutUI | NSUrlBookmarkResolutionOptions.WithoutMounting, out var resolutionError);
        try { return resolved?.IsFileUrl == true && resolved.Path is { } target ? target : throw new IOException(resolutionError?.LocalizedDescription ?? "Finder别名目标暂不可访问。"); }
        finally { resolutionError?.Dispose(); }
    }
}
