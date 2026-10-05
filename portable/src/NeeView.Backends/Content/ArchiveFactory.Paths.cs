namespace NeeView.Backends;

public sealed partial class ArchiveFactory
{
    /// <summary>实体动作才调用系统realpath；保留文件系统大小写语义，避免Profile保护被路径别名绕过。</summary>
    /// <param name="path">完整目标或受保护目录；不存在的末段保留原名。</param>
    /// <param name="token">后台排队、系统查询和返回前的取消。</param>
    /// <returns>实际大小写及系统链接解析后的绝对路径，不参与书籍身份或JSON改写。</returns>
    public Task<string> GetPhysicalPathAsync(string path, CancellationToken token) => SourceIo.RunAsync(() => UnixPhysicalPath.Resolve(path, token), token);
}
