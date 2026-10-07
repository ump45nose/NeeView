using System.Security.Cryptography;
using System.Text;
using NeeView;
namespace NeeView.Backends;

public sealed partial class FileOperationBackend : IBookTransferBackend
{
    /// <summary>共用原重命名路径联动记录的移动指纹验证，不增加第二恢复存储。</summary>
    public Task<bool?> WasBookMovedAsync(BookRenamePlan plan, CancellationToken token) => WasRenamedAsync(plan, token);
    /// <summary>文件恢复先于JSON联动；未完成的实体日志仍可能回滚，禁止凭新落点猜测已提交。</summary>
    private async Task<bool?> VerifyMovedBookAsync(BookRenamePlan plan, CancellationToken token)
    {
        await _slot.WaitAsync(token);
        try
        {
            return await SourceIo.RunReadAsync<bool?>(async token =>
            {
                if (!Path.IsPathFullyQualified(plan.Target.Path) || !Path.IsPathFullyQualified(plan.Destination) || string.IsNullOrEmpty(plan.ContentHash))
                    throw new IOException("移动恢复路径或完整性指纹无效。");
                var source = CanonicalFile(plan.Target.Path); var destination = CanonicalFile(plan.Destination); ValidateBookPaths(source, destination);
                if (await MatchesAsync(source, plan.ContentHash) && await MatchesAsync(destination, plan.PreviousDestinationHash)) return false;
                if (Exists(source) || !await MatchesAsync(destination, plan.ContentHash)) return null;
                if (Directory.Exists(recoveryDirectory))
                    foreach (var path in Directory.EnumerateFiles(recoveryDirectory, "*.json"))
                    {
                        token.ThrowIfCancellationRequested();
                        try
                        {
                            var journal = await ReadJournalAsync(path, token);
                            if (journal.Source == source && journal.Destination == destination && !journal.Completed) return null;
                        }
                        catch (OperationCanceledException) { throw; }
                        catch (Exception) { return null; }
                    }
                return true;
            }, token);
        }
        finally { _slot.Release(); }
    }
    /// <summary>与分类/改名共享后台单槽；确认快照不持有书籍流或窗口。</summary>
    public Task<BookTransferPlan> PlanBookTransferAsync(string source, string folder, CancellationToken token)
        => PlanPathCoreAsync(source, folder, null, token);
    /// <summary>原脚本SHCopy/SHMove的明确目录落点，使用与整书完全相同的快照校验。</summary>
    public Task<BookTransferPlan> PlanPathTransferAsync(string source, string destination, CancellationToken token)
        => PlanPathCoreAsync(source, null, destination, token);
    private async Task<BookTransferPlan> PlanPathCoreAsync(string source, string? folder, string? path, CancellationToken token)
    {
        await _slot.WaitAsync(token);
        try
        {
            var progress = new SourceIo.ReadProgress();
            return await SourceIo.RunReadAsync(async token =>
            {
                var target = ReadRenameTarget(source, true);
                var destination = path is null ? Path.Combine(Path.GetFullPath(folder!), Path.GetFileName(target.Path)) : Path.GetFullPath(path);
                if (!Directory.Exists(Path.GetDirectoryName(destination))) throw new DirectoryNotFoundException("目标目录已不存在。");
                ValidateBookPaths(CanonicalFile(target.Path), CanonicalFile(destination));

                if (Exists(destination) && (Directory.Exists(destination) && new FileInfo(destination).LinkTarget is null) != target.IsDirectory) throw new IOException("源和目标类型不同，不能覆盖。");
                return new BookTransferPlan(target, destination, await HashAsync(target.Path, token, progress.Mark), Exists(destination) ? await HashAsync(destination, token, progress.Mark) : null);
            }, token, progress: progress);
        }
        finally { _slot.Release(); }
    }

    /// <summary>完整快照复核后复用既有安装/回滚协议；返回结果不登记分类历史。</summary>
    public async Task<FileTransferResult> TransferBookAsync(BookTransferPlan plan, bool move, CancellationToken token)
    {
        await _slot.WaitAsync(token);
        try
        {
            return await Task.Run(() => TransferCoreAsync(new(plan.Target.Path, plan.Destination, move,
                plan.DestinationHash is not null, ExpectedSourceHash: plan.ContentHash, PreserveSourceLink: true), token, plan), token);
        }
        finally { _slot.Release(); }
    }

    /// <summary>拒绝自身、祖先覆盖、源内目标及含恢复目录的对象；路径已解析父级别名。</summary>
    private void ValidateBookPaths(string source, string destination)
    {
        static bool Within(string path, string root) => path == root || path.StartsWith(root.TrimEnd('/') + "/", StringComparison.Ordinal);
        if (Within(destination, source) || Within(source, destination)) throw new IOException("不能复制或移动到自身、子目录或祖先目录。");
        var recovery = CanonicalFile(Path.GetFullPath(recoveryDirectory));
        if (Within(recovery, source) || Within(recovery, destination)) throw new IOException("书籍或目标包含应用恢复目录。");
    }

    /// <summary>按名称排序逐级计算结构/内容指纹，包含空目录；不跟随任何链接。</summary>
    private static async Task<string> HashDirectoryAsync(string path, CancellationToken token, Action? progress = null)
    {
        RejectLink(path);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData("NeeView.Directory.v1"u8);
        foreach (var child in Directory.EnumerateFileSystemEntries(path).Order(StringComparer.Ordinal))
        {
            token.ThrowIfCancellationRequested();
            var name = Encoding.UTF8.GetBytes(Path.GetFileName(child));
            hash.AppendData(Encoding.UTF8.GetBytes(name.Length.ToString(System.Globalization.CultureInfo.InvariantCulture) + ":"));
            var childHash = await HashAsync(child, token, progress); bool link = childHash.StartsWith("L:", StringComparison.Ordinal);
            hash.AppendData(name); hash.AppendData(link ? "L"u8 : Directory.Exists(child) ? "D"u8 : "F"u8);
            hash.AppendData(Convert.FromHexString(link ? childHash[2..] : childHash));
        }
        progress?.Invoke(); FileOperationProgress.Report(); return Convert.ToHexString(hash.GetHashAndReset());
    }

    /// <summary>创建独立目录树并逐文件验证，最后核对整个树；任何源变化均拒绝安装。</summary>
    private static async Task CopyDirectoryVerifiedAsync(string source, string destination, string hash, CancellationToken token)
    {
        if (Exists(destination)) throw new IOException("应用临时目录名称已被占用。");
        Directory.CreateDirectory(destination); FileOperationProgress.Report();
        var copied = new List<(string Path, string Hash)>();
        try
        {
            foreach (var child in Directory.EnumerateFileSystemEntries(source).Order(StringComparer.Ordinal))
            {
                token.ThrowIfCancellationRequested();
                var output = Path.Combine(destination, Path.GetFileName(child)); var childHash = await HashAsync(child, token);
                await CopyVerifiedAsync(child, output, childHash, token); copied.Add((output, childHash));
            }
            if (!await MatchesAsync(destination, hash)) throw new IOException("目录完整性校验失败。");
            Directory.SetLastWriteTimeUtc(destination, Directory.GetLastWriteTimeUtc(source));
        }
        catch
        {
            foreach (var item in copied) if (await MatchesAsync(item.Path, item.Hash)) DeleteItem(item.Path);
            if (!Directory.EnumerateFileSystemEntries(destination).Any()) Directory.Delete(destination);
            throw;
        }
    }

    /// <summary>同父级暂存和安装都是无覆盖改名；跨卷只发生在验证复制阶段。</summary>
    private static void MoveItem(string source, string destination)
    {
        // .NET File.Move拒绝指向目录的链接；Directory.Move仍是目录项改名，不递归搬动目标。
        if (Directory.Exists(source)) Directory.Move(source, destination);
        else File.Move(source, destination, false);
        FileOperationProgress.Report();
    }

    /// <summary>只清理已验证的应用材料；删除链接对象，目录递归删除不跟随链接。</summary>
    private static void DeleteItem(string path)
    {
        if (!Exists(path)) return;
        if (new FileInfo(path).LinkTarget is not null) { File.Delete(path); return; }
        if (Directory.Exists(path))
        {
            Directory.Delete(path, true);
        }
        else File.Delete(path);
        FileOperationProgress.Report();
    }
}
