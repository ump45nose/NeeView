using NeeView;
namespace NeeView.Backends;

public sealed partial class FileOperationBackend : IBookRenameBackend
{
    /// <summary>与分类共用单槽，文件系统检查不阻塞UI。</summary>
    public Task<BookRenameTarget> GetRenameTargetAsync(string path, CancellationToken token) => RenameReadAsync(() => ReadRenameTarget(path, true), token);
    /// <summary>名称只占一个目录项；允许Mac合法字符，不延续Windows设备名限制。</summary>
    public Task<BookRenamePlan> PlanRenameAsync(BookRenameTarget target, string name, CancellationToken token) => RenameReadAsync<BookRenamePlan>(() =>
    {
        name = name.Trim();
        if (string.IsNullOrWhiteSpace(name) || name is "." or ".." || name.IndexOfAny(['/', '\0']) >= 0)
            throw new ArgumentException("请输入有效的文件名，不能包含 / 或空字符。", nameof(name));
        var current = ReadRenameTarget(target.Path, true);
        if (current != target) throw new IOException("书籍已被外部修改，请重新发起重命名。");
        var parent = Path.GetDirectoryName(target.Path)!;
        var destination = Path.Combine(parent, name);
        bool conflict = Exists(destination) && ActualRenamePath(destination) != ActualRenamePath(target.Path);
        if (conflict)
        {
            string stem = Path.GetFileNameWithoutExtension(name), extension = Path.GetExtension(name);
            for (int count = 2; Exists(destination); count++)
            { token.ThrowIfCancellationRequested(); destination = Path.Combine(parent, $"{stem} ({count}){extension}"); }
        }
        return new(target, destination, !target.IsDirectory && !string.Equals(Path.GetExtension(target.Path), Path.GetExtension(destination), StringComparison.OrdinalIgnoreCase), conflict);
    }, token);
    /// <summary>关闭来源后执行同目录无覆盖改名；提交点后不再检查取消。</summary>
    public async Task RenameAsync(BookRenamePlan plan, CancellationToken token)
    {
        await _slot.WaitAsync(token);
        try
        {
            var target = await SourceIo.RunReadAsync(t =>
            {
            var target = ReadRenameTarget(plan.Target.Path, true);
            if (target != plan.Target) throw new IOException("书籍已被外部修改，请重新发起重命名。");
            if (Path.GetDirectoryName(plan.Destination) != Path.GetDirectoryName(target.Path)) throw new IOException("重命名不能移出原目录。");
            if (Exists(plan.Destination) && ActualRenamePath(plan.Destination) != ActualRenamePath(target.Path)) throw new IOException("目标名称已被占用，请重新发起重命名。");
                t.ThrowIfCancellationRequested();
                return Task.FromResult(target);
            }, token);
            // 只有及时完成的只读结果才能授权写入；改名开始后等真实系统结果。
            await Task.Run(() =>
            {
                token.ThrowIfCancellationRequested();
                if (target.Path != plan.Destination) MoveItem(target.Path, plan.Destination);
            }, token);
        }
        finally { _slot.Release(); }
    }
    /// <summary>仅确认和查询使用可超时准备；写入不能进入此入口。</summary>
    private async Task<T> RenameReadAsync<T>(Func<T> work, CancellationToken token)
    {
        await _slot.WaitAsync(token);
        try { return await SourceIo.RunReadAsync(t => { t.ThrowIfCancellationRequested(); return Task.FromResult(work()); }, token); }
        finally { _slot.Release(); }
    }
    public Task<bool?> WasRenamedAsync(BookRenamePlan plan, CancellationToken token) => plan.IsMove ? VerifyMovedBookAsync(plan, token) : RenameReadAsync<bool?>(() =>
    {
        if (!Path.IsPathFullyQualified(plan.Target.Path) || !Path.IsPathFullyQualified(plan.Destination)
            || Path.GetDirectoryName(plan.Target.Path) != Path.GetDirectoryName(plan.Destination)
            || plan.Target.Path == plan.Destination) throw new IOException("重命名恢复记录的路径关系无效。");
        bool source = Exists(plan.Target.Path) && ActualRenamePath(plan.Target.Path) == plan.Target.Path;
        bool destination = Exists(plan.Destination) && ActualRenamePath(plan.Destination) == plan.Destination;
        if (source && !destination && ReadRenameTarget(plan.Target.Path, true) == plan.Target) return false;
        if (!source && destination)
        {
            var found = ReadRenameTarget(plan.Destination, true) with { Path = plan.Target.Path };
            // 路径联动可能在该目录内原子保存.nvpls，目录mtime会变化；创建时间/类型仍须匹配。
            if (found.IsDirectory) found = found with { LastWriteTimeUtc = plan.Target.LastWriteTimeUtc };
            if (found == plan.Target) return true;
        }
        return null;
    }, token);
    private static BookRenameTarget ReadRenameTarget(string path, bool preserveLink = false)
    {
        path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        if (!Exists(path)) throw new FileNotFoundException("书籍实体已不存在。", path);
        var link = new FileInfo(path).LinkTarget;
        if (!preserveLink) RejectLink(path);
        if (Path.GetDirectoryName(path) is null) throw new IOException("不能重命名卷根目录。");
        bool directory = Directory.Exists(path) && link is null;
        FileSystemInfo info = directory ? new DirectoryInfo(path) : new FileInfo(path);
        return new(path, directory, info.CreationTimeUtc, info.LastWriteTimeUtc, directory ? 0 : link is not null ? System.Text.Encoding.UTF8.GetByteCount(link) : ((FileInfo)info).Length, link);
    }
    /// <summary>保留请求父路径，只有最后名称按系统解析；改名恢复不能把/var等别名改写。</summary>
    private static string ActualRenamePath(string path)
    {
        return Path.Combine(Path.GetDirectoryName(path)!, Path.GetFileName(CanonicalFile(path)));
    }
}
