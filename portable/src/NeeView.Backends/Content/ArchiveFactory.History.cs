using NeeView;
namespace NeeView.Backends;

public sealed partial class ArchiveFactory
{
    /// <summary>原ArchiveEntryUtility.Exists替换点：精确定位，检查不解压或解码。</summary>
    /// <param name="path">真实历史目标，包括归档逻辑目录；未映射旧路径不参与删除。</param>
    /// <param name="token">有界I/O与遍历取消。</param><returns>可靠确定是否存在。</returns>
    public Task<bool> ExistsAsync(string path, CancellationToken token) => SourceIo.RunAsync(() =>
    {
        token.ThrowIfCancellationRequested();
        if (!System.IO.Path.IsPathFullyQualified(path) || path.StartsWith(@"\\", StringComparison.Ordinal))
            throw new NotSupportedException("历史路径需要重新定位，暂不执行无效清理：" + path);
        path = System.IO.Path.GetFullPath(path);
        // 卷离线不是卷内文件删除。只探测该挂载根，不枚举NAS或改变系统挂载。
        if (path.StartsWith("/Volumes/", StringComparison.Ordinal))
        {
            var end = path.IndexOf('/', 9); var volume = end < 0 ? path : path[..end];
            try { _ = File.GetAttributes(volume); }
            catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
            { throw new IOException("历史来源卷暂不可访问：" + volume, ex); }
        }
        var candidate = path;
        while (!string.IsNullOrEmpty(candidate))
        {
            token.ThrowIfCancellationRequested(); FileAttributes attributes;
            try { attributes = File.GetAttributes(candidate); }
            catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
            { candidate = System.IO.Path.GetDirectoryName(candidate); continue; }
            if (candidate == path) return true;
            if ((attributes & FileAttributes.Directory) != 0) return false;
            if (!ArchiveFormats.IsCompressedArchive(candidate)) return false;
            using var archive = SharpCompress.Archives.ArchiveFactory.OpenArchive(candidate);
            var relative = System.IO.Path.GetRelativePath(candidate, path).Replace('\\', '/').TrimEnd('/');
            // 嵌套归档无法可靠判断其内部；明确传播能力错误，整批不误删。
            var entries = archive.Entries.ToArray();
            if (entries.Any(e => !e.IsDirectory && ArchiveFormats.IsArchive(e.Key ?? "") && relative.StartsWith((e.Key ?? "").Replace('\\', '/').TrimEnd('/') + "/", StringComparison.Ordinal)))
                throw new NotSupportedException("嵌套归档的历史检查尚未迁移。");
            return entries.Any(e => (e.Key ?? "").Replace('\\', '/').TrimEnd('/') == relative
                || (e.Key ?? "").Replace('\\', '/').StartsWith(relative + "/", StringComparison.Ordinal));
        }
        return false;
    }, token);
}
