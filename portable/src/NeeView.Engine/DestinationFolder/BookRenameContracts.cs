namespace NeeView;

/// <summary>真实书籍项快照；链接捕获目标文字并只操作链接本身，归档内部目录不是实体。</summary>
public sealed record BookRenameTarget(string Path, bool IsDirectory, DateTime CreationTimeUtc, DateTime LastWriteTimeUtc, long Length, string? LinkTarget = null);
/// <summary>原路径联动记录；改名限同父目录，整书移动扩展带完整指纹及原目标指纹。</summary>
public sealed record BookRenamePlan(BookRenameTarget Target, string Destination, bool ExtensionChanged, bool Conflict,
    bool IsMove = false, string? ContentHash = null, string? PreviousDestinationHash = null);
/// <summary>重命名仅替换一个同目录项，独立于分类移动历史和跨卷协议。</summary>
public interface IBookRenameBackend
{
    /// <summary>后台捕获实体或链接目录项；不存在及逻辑归档地址明确拒绝。</summary>
    /// <param name="path">当前实体书籍的绝对路径。</param><param name="token">排队及探测的取消令牌。</param>
    /// <returns>用于后续规划和提交核对的来源快照。</returns>
    Task<BookRenameTarget> GetRenameTargetAsync(string path, CancellationToken token);
    /// <summary>沿原同名编号规则建议未占用名称，保留实际文件系统大小写。</summary>
    /// <param name="target">此前捕获的来源快照。</param><param name="name">用户输入的单个文件名，包含需要保留的扩展名。</param>
    /// <param name="token">排队和名称规划的取消令牌。</param><returns>实际建议路径以及需确认的扩展名变化/冲突。</returns>
    Task<BookRenamePlan> PlanRenameAsync(BookRenameTarget target, string name, CancellationToken token);
    /// <summary>再次检查来源快照和目标，不覆盖；成功后取消不能改变实际返回结果。</summary>
    /// <param name="plan">已经过用户确认的同目录改名计划。</param><param name="token">实体提交前的取消令牌。</param>
    /// <returns>实体提交完成的任务；失败通过异常回报。</returns>
    Task RenameAsync(BookRenamePlan plan, CancellationToken token);
    /// <summary>启动恢复仅在源/目标身份明确时返回结果；null保留恢复材料并提示。</summary>
    /// <param name="plan">从恢复记录读取的计划。</param><param name="token">排队和恢复探测的取消令牌。</param>
    /// <returns>true表示已改名，false表示未提交，null表示不能可靠确认。</returns>
    Task<bool?> WasRenamedAsync(BookRenamePlan plan, CancellationToken token);
}
