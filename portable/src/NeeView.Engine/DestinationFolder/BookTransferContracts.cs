namespace NeeView;

/// <summary>整书实体传输的确认快照；指纹包含目录名称、空目录和全部文件内容。</summary>
public sealed record BookTransferPlan(BookRenameTarget Target, string Destination, string ContentHash, string? DestinationHash);

/// <summary>在既有文件后端下扩展真实根文件/目录传输，不进入分类移动历史。</summary>
public interface IBookTransferBackend
{
    /// <summary>后台规划固定名称的落点，拒绝链接、自包含目标及类型冲突。</summary>
    /// <param name="source">真实书籍根路径。</param><param name="folder">已经存在的目标目录。</param>
    /// <param name="token">规划和指纹计算取消。</param><returns>提交前再次核对的完整快照。</returns>
    Task<BookTransferPlan> PlanBookTransferAsync(string source, string folder, CancellationToken token);
    /// <summary>脚本文件入口的明确落点，复用同一指纹/恢复协议并允许改名。</summary>
    Task<BookTransferPlan> PlanPathTransferAsync(string source, string destination, CancellationToken token)
        => throw new NotSupportedException("当前后端不支持明确目录落点。");
    /// <summary>共用临时目标、指纹校验和恢复日志；成功返回后调用既有ReleaseAsync。</summary>
    /// <param name="plan">用户确认的来源和目标指纹。</param><param name="move">true移动，false复制。</param>
    /// <param name="token">安装提交点前可取消。</param><returns>真实成功的落点及恢复材料。</returns>
    Task<FileTransferResult> TransferBookAsync(BookTransferPlan plan, bool move, CancellationToken token);
    /// <summary>失败后仅在来源/目标指纹均明确时判断未提交；null保留路径恢复记录。</summary>
    /// <param name="plan">已经准备的原书路径联动记录。</param><param name="token">恢复探测取消。</param>
    /// <returns>true已移动，false已回滚或未移动，null需检查恢复材料。</returns>
    Task<bool?> WasBookMovedAsync(BookRenamePlan plan, CancellationToken token);
}
