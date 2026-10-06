using NeeView;
namespace NeeView.Backends;

public sealed partial class ArchiveFactory
{
    /// <summary>原ArchiveEntryUtility.Exists替换点：精确定位，普通根只探测；嵌套内部沿原抽取链，只读且不解码图片。</summary>
    /// <param name="path">真实历史目标，包括归档逻辑目录；未映射旧路径不参与删除。</param>
    /// <param name="token">有界I/O与遍历取消。</param><returns>可靠确定是否存在。</returns>
    public async Task<bool> ExistsAsync(string path, CancellationToken token)
    {
        var physical = await SourceIo.RunAsync<bool?>(() =>
        {
            token.ThrowIfCancellationRequested();
            RejectInnerParentTraversal(path);
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
                return null; // 逻辑内部定位交给同一工厂，不能将能力/读取失败当作缺失。

            }
            return false;
        }, token).ConfigureAwait(false);
        if (physical is { } exists) return exists;
        try { await using var source = await OpenAsync(path, token).ConfigureAwait(false); return true; }
        catch (FileNotFoundException) { return false; }
    }
}
