using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using NeeView;
namespace NeeView.Backends;

/// <summary>有界文件操作后端，临时目标和SHA256校验使跨卷/NAS与普通卷共用恢复协议。</summary>
public sealed partial class FileOperationBackend(string recoveryDirectory) : IFileOperationBackend
{
    private readonly SemaphoreSlim _slot = new(1);
    private sealed class Journal
    {
        public string Source { get; set; } = "";
        public string Destination { get; set; } = "";
        public string Temporary { get; set; } = "";
        public string Backup { get; set; } = "";
        public string SourceStage { get; set; } = "";
        public string DestinationStage { get; set; } = "";
        public string RestoreTemporary { get; set; } = "";
        public string? RestoreBackup { get; set; }
        public string? RestoreHash { get; set; }
        public string SourceHash { get; set; } = "";
        public string? DestinationHash { get; set; }
        public bool Move { get; set; }
        public bool IsDirectory { get; set; }
        public bool PreserveLinks { get; set; }
        public bool Completed { get; set; }
    }
    [JsonSerializable(typeof(Journal))]
    private partial class RecoveryJsonContext : JsonSerializerContext { }
    /// <summary>文件存在检查进入后台；UI不直接阻塞挂载目录。</summary>
    public Task<bool> FileExistsAsync(string path, CancellationToken token) => Task.Run(() => { token.ThrowIfCancellationRequested(); return Exists(path); }, token);

    /// <summary>安装前检查原图与目标指纹；取消/错误保守回滚，无法回滚时保留日志。</summary>
    public async Task<FileTransferResult> TransferAsync(FileTransferRequest request, CancellationToken token)
    {
        await _slot.WaitAsync(token);
        try { return await Task.Run(() => TransferCoreAsync(request, token), token); }
        finally { _slot.Release(); }
    }
    private async Task<FileTransferResult> TransferCoreAsync(FileTransferRequest request, CancellationToken token, BookTransferPlan? bookPlan = null)
    {
        string source = CanonicalFile(Path.GetFullPath(request.Source)), destination = CanonicalFile(Path.GetFullPath(request.Destination));
        if (!Exists(source)) throw new FileNotFoundException("源项目已不存在。", source);
        bool preserveLinks = request.PreserveSourceLink || request.ExpectedSourceHash?.StartsWith("L:", StringComparison.Ordinal) == true || bookPlan is not null;
        bool directory = Directory.Exists(source) && new FileInfo(source).LinkTarget is null;
        if (directory && bookPlan is null) throw new IOException("图片传输不能处理目录。 ");
        if (bookPlan is not null)
        {
            ValidateBookPaths(source, destination);
            if (ReadRenameTarget(request.Source, true) != bookPlan.Target) throw new IOException("书籍在确认期间已变化，请重新确认。");
        }
        if (!Directory.Exists(Path.GetDirectoryName(destination))) throw new DirectoryNotFoundException("目标目录已不存在。");
        // 不跟随符号链接分类；别名/大小写解析后同一目录项不能覆盖自身。
        RejectLinkIfPresent(source, preserveLinks); RejectLinkIfPresent(destination, preserveLinks);
        if (CanonicalFile(source) == CanonicalFile(destination)) throw new IOException("源和目标是同一个文件。");
        if (Exists(destination) && (Directory.Exists(destination) && new FileInfo(destination).LinkTarget is null) != directory) throw new IOException("源和目标类型不同，不能覆盖。");
        if (Exists(destination) && !request.Overwrite) throw new IOException("目标存在，尚未确认覆盖。");
        if (request.RestoreBackup is not null)
        {
            if (!IsOwned(request.RestoreBackup, ".backup") || !Exists(request.RestoreBackup) || !request.Move) throw new IOException("覆盖恢复副本已不存在。");
            RejectLinkIfPresent(request.RestoreBackup, preserveLinks);
        }
        Directory.CreateDirectory(recoveryDirectory);
        string id = Guid.NewGuid().ToString("N"), journalPath = Path.Combine(recoveryDirectory, id + ".json");
        var journal = new Journal { Source = source, Destination = destination, Move = request.Move, IsDirectory = directory, PreserveLinks = preserveLinks,
            Temporary = Sibling(destination, id, ".tmp"), Backup = Path.Combine(Path.GetFullPath(recoveryDirectory), id + ".backup"), RestoreBackup = request.RestoreBackup,
            SourceStage = Sibling(source, id, ".source"), DestinationStage = Sibling(destination, id, ".destination"), RestoreTemporary = Sibling(source, id, ".restore") };
        journal.SourceHash = await HashAsync(source, token);
        if (request.ExpectedSourceHash is not null && journal.SourceHash != request.ExpectedSourceHash) throw new IOException("移动后的图片已被外部替换，撤销记录保留。");
        journal.DestinationHash = Exists(destination) ? await HashAsync(destination, token) : null;
        if (bookPlan is not null && journal.DestinationHash != bookPlan.DestinationHash) throw new IOException("目标在确认期间已变化，请重新确认。");
        if (request.RestoreBackup is not null) journal.RestoreHash = await HashAsync(request.RestoreBackup, token);
        if (request.ExpectedRestoreHash is not null && journal.RestoreHash != request.ExpectedRestoreHash) throw new IOException("覆盖恢复副本已变化，撤销记录保留。");
        // 日志先于任何写入用户目录；副本只使用本应用随机文件名。
        await WriteJournalAsync(journalPath, journal, token);
        try
        {
            await CopyVerifiedAsync(source, journal.Temporary, journal.SourceHash, token);
            if (request.RestoreBackup is not null) await CopyVerifiedAsync(request.RestoreBackup, journal.RestoreTemporary, journal.RestoreHash!, token);
            token.ThrowIfCancellationRequested();
            if (!await MatchesAsync(source, journal.SourceHash) || !await MatchesAsync(destination, journal.DestinationHash)) throw new IOException("源或目标在操作期间已变化，保留原文件。");
            // 同目录原子暂存后再校验取得的文件，不对用户路径执行覆盖或检查后删除。
            // 外部此时创建的新目录项会使无覆盖安装失败，原件及恢复材料仍保留。
            if (journal.DestinationHash is not null)
            {
                await ClaimAsync(destination, journal.DestinationStage, journal.DestinationHash);
                await CopyVerifiedAsync(journal.DestinationStage, journal.Backup, journal.DestinationHash, token);
            }
            if (request.Move)
                await ClaimAsync(source, journal.SourceStage, journal.SourceHash);
            token.ThrowIfCancellationRequested();
            MoveItem(journal.Temporary, destination);
            // 安装是提交点。晚取消不能隐藏已经落盘的移动；必要记录及原书协调继续完成。
            if (request.RestoreBackup is not null) MoveItem(journal.RestoreTemporary, source);
            journal.Completed = true;
            await WriteJournalAsync(journalPath, journal, CancellationToken.None);
            // 完成记录已持久化，暂存清理失败留待启动重试，不能把成功移动误报为失败。
            try { await CleanStagesAsync(journal); } catch (IOException) { }
            // 不发生Shell自动改名，回传调用方已规范的真实定位；父级别名仍是合法可访问地址。
            return new(Path.GetFullPath(request.Source), Path.GetFullPath(request.Destination), request.Move, journal.DestinationHash is null ? null : journal.Backup, journalPath, journal.SourceHash, journal.DestinationHash);
        }
        catch (Exception ex)
        {
            try { await RollbackAsync(journal); DeleteOwned(journal.Backup, ".backup", journal.PreserveLinks); File.Delete(journalPath); }
            catch (Exception rollback) { throw new IOException("操作未完成，恢复记录已保留。" + rollback.Message, ex); }
            throw;
        }
    }
    /// <summary>只接受应用恢复根内的生成材料，禁止任意路径清理。</summary>
    public Task ReleaseAsync(FileTransferResult result) => Task.Run(async () =>
    {
        if (!IsOwned(result.Journal, ".json")) throw new IOException("非应用恢复材料。");
        var journal = await ReadJournalAsync(result.Journal, CancellationToken.None);
        if (!journal.Completed) throw new IOException("未完成的恢复材料不能清理。");
        await CleanStagesAsync(journal);
        DeleteOwned(journal.Backup, ".backup", journal.PreserveLinks); DeleteOwned(result.Journal, ".json");
    });

    /// <summary>退出不持久化历史；启动仅自动恢复可由内容指纹确认的中断。</summary>
    public async Task<IReadOnlyList<string>> RecoverAsync(CancellationToken token = default)
    {
        await _slot.WaitAsync(token);
        try
        {
            return await Task.Run(async () =>
            {
                var warnings = new List<string>();
                if (!Directory.Exists(recoveryDirectory)) return (IReadOnlyList<string>)warnings;
                foreach (var path in Directory.EnumerateFiles(recoveryDirectory, "*.json"))
                {
                    token.ThrowIfCancellationRequested();
                    try
                    {
                        var journal = await ReadJournalAsync(path, token);
                        if (!journal.Completed) await RollbackAsync(journal);
                        else await CleanStagesAsync(journal);
                        DeleteOwned(journal.Backup, ".backup", journal.PreserveLinks); File.Delete(path);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException) { warnings.Add("文件恢复需要检查：" + Path.GetFileName(path) + "：" + ex.Message); }
                }
                return (IReadOnlyList<string>)warnings;
            }, token);
        }
        finally { _slot.Release(); }
    }
    private async Task RollbackAsync(Journal journal)
    {
        // 先核对全部落点和暂存，模糊情况保留日志。恢复安装仍不覆盖新目录项。
        RejectLinkIfPresent(journal.Source, journal.PreserveLinks); RejectLinkIfPresent(journal.Destination, journal.PreserveLinks);
        bool sourceOriginal = await MatchesAsync(journal.Source, journal.SourceHash);
        bool sourceEmpty = !Exists(journal.Source);
        bool sourceRestored = journal.RestoreHash is not null && await MatchesAsync(journal.Source, journal.RestoreHash);
        bool targetOriginal = await MatchesAsync(journal.Destination, journal.DestinationHash);
        bool targetInstalled = await MatchesAsync(journal.Destination, journal.SourceHash);
        bool targetEmpty = !Exists(journal.Destination);
        foreach (var (path, hash) in new[] { (journal.SourceStage, (string?)journal.SourceHash), (journal.DestinationStage, journal.DestinationHash), (journal.Temporary, journal.SourceHash), (journal.RestoreTemporary, journal.RestoreHash) })
            if (Exists(path) && !await MatchesAsync(path, hash)) throw new IOException("暂存文件已变化，恢复材料已保留。");
        if (!(sourceOriginal || sourceEmpty || sourceRestored) || !(targetOriginal || targetInstalled || targetEmpty && Exists(journal.DestinationStage))) throw new IOException("文件已被外部更改，请手动检查，未覆盖。");
        if (journal.DestinationHash is not null && !targetOriginal && !await MatchesAsync(journal.DestinationStage, journal.DestinationHash) && !await MatchesAsync(journal.Backup, journal.DestinationHash)) throw new IOException("覆盖副本无法验证。");
        if (!sourceOriginal && !await MatchesAsync(journal.SourceStage, journal.SourceHash) && !targetInstalled && !await MatchesAsync(journal.Temporary, journal.SourceHash)) throw new IOException("没有可验证的原图副本。");
        if (targetInstalled && !targetOriginal)
            await ClaimAsync(journal.Destination, journal.Temporary, journal.SourceHash);
        if (sourceRestored && !sourceOriginal)
            await ClaimAsync(journal.Source, journal.RestoreTemporary, journal.RestoreHash!);
        if (!sourceOriginal)
        {
            if (!Exists(journal.SourceStage)) await CopyVerifiedAsync(targetOriginal && targetInstalled ? journal.Destination : journal.Temporary, journal.SourceStage, journal.SourceHash, CancellationToken.None);
            MoveItem(journal.SourceStage, journal.Source);
        }
        if (!targetOriginal && journal.DestinationHash is not null)
        {
            if (!Exists(journal.DestinationStage)) await CopyVerifiedAsync(journal.Backup, journal.DestinationStage, journal.DestinationHash, CancellationToken.None);
            MoveItem(journal.DestinationStage, journal.Destination);
        }
        await CleanStagesAsync(journal);
    }
    public Task CreateDirectoryAsync(string parent, string name, CancellationToken token) => Task.Run(() =>
    {
        token.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(name) || name != Path.GetFileName(name) || name is "." or "..") throw new ArgumentException("请输入直接子目录名称。");
        string path = Path.Combine(parent, name);
        if (!Directory.Exists(parent)) throw new DirectoryNotFoundException();
        if (File.Exists(path) || Directory.Exists(path)) throw new IOException("同名项目已存在。");
        Directory.CreateDirectory(path);
    }, token);

    private static string Sibling(string path, string id, string suffix) => Path.Combine(Path.GetDirectoryName(path)!, ".neeview-" + id + suffix);
    private bool IsOwned(string path, string extension) => Path.GetDirectoryName(Path.GetFullPath(path)) == Path.GetFullPath(recoveryDirectory)
        && Path.GetExtension(path) == extension && Guid.TryParseExact(Path.GetFileNameWithoutExtension(path), "N", out _);
    private void DeleteOwned(string? path, string extension, bool preserveLinks = false) { if (path is not null) { if (!IsOwned(path, extension)) throw new IOException("非应用恢复材料。"); RejectLinkIfPresent(path, preserveLinks); DeleteItem(path); } }
    private static bool Exists(string path) => File.Exists(path) || Directory.Exists(path) || new FileInfo(path).LinkTarget is not null;
    private static void RejectLinkIfPresent(string path, bool preserveLinks = false) { if (Exists(path) && !preserveLinks) RejectLink(path); }
    private static void RejectLink(string path) { if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new IOException("符号链接不参与分类。"); }
    /// <summary>恢复材料必须与日志随机ID及两端同目录暂存路径完全相符，禁止跟随链接。</summary>
    private async Task<Journal> ReadJournalAsync(string path, CancellationToken token)
    {
        if (!IsOwned(path, ".json")) throw new IOException("恢复记录名称无效。");
        RejectLink(path);
        var journal = JsonSerializer.Deserialize(await File.ReadAllTextAsync(path, token), RecoveryJsonContext.Default.Journal) ?? throw new IOException("无效记录。");
        var id = Path.GetFileNameWithoutExtension(path);
        if (!Path.IsPathFullyQualified(journal.Source) || !Path.IsPathFullyQualified(journal.Destination)
            || journal.Source != CanonicalFile(journal.Source) || journal.Destination != CanonicalFile(journal.Destination)
            || journal.Source == journal.Destination || journal.Backup != Path.Combine(Path.GetFullPath(recoveryDirectory), id + ".backup")
            || journal.Temporary != Sibling(journal.Destination, id, ".tmp") || journal.SourceStage != Sibling(journal.Source, id, ".source")
            || journal.DestinationStage != Sibling(journal.Destination, id, ".destination") || journal.RestoreTemporary != Sibling(journal.Source, id, ".restore")) throw new IOException("恢复材料不匹配。");
        if (journal.IsDirectory)
        {
            if (journal.RestoreBackup is not null || journal.RestoreHash is not null) throw new IOException("目录记录不能用于图片撤销。");
            ValidateBookPaths(journal.Source, journal.Destination);
        }
        foreach (var material in new[] { journal.Backup, journal.Temporary, journal.SourceStage, journal.DestinationStage, journal.RestoreTemporary }) RejectLinkIfPresent(material, journal.PreserveLinks);
        if (journal.RestoreBackup is not null) { if (!IsOwned(journal.RestoreBackup, ".backup")) throw new IOException("恢复副本名称无效。"); RejectLinkIfPresent(journal.RestoreBackup, journal.PreserveLinks); }
        if (journal.Completed && Exists(journal.Backup) && !await MatchesAsync(journal.Backup, journal.DestinationHash)) throw new IOException("覆盖副本已被外部替换，保留记录。");
        return journal;
    }
    /// <summary>原子取走后验证，变化的对象原路无覆盖退还；退还失败保留暂存供检查。</summary>
    private static async Task ClaimAsync(string path, string stage, string hash)
    {
        RejectLinkIfPresent(path, hash.StartsWith("L:", StringComparison.Ordinal));
        MoveItem(path, stage);
        if (await MatchesAsync(stage, hash)) return;
        if (!Exists(path)) MoveItem(stage, path);
        throw new IOException("文件在操作期间被外部替换，未覆盖或删除。");
    }
    /// <summary>只清理内容仍符合记录的应用暂存；变更材料保留，等待显式检查。</summary>
    private static async Task CleanStagesAsync(Journal journal)
    {
        foreach (var (path, hash) in new[] { (journal.SourceStage, (string?)journal.SourceHash), (journal.DestinationStage, journal.DestinationHash), (journal.Temporary, journal.SourceHash), (journal.RestoreTemporary, journal.RestoreHash) })
        {
            if (!Exists(path)) continue;
            if (!await MatchesAsync(path, hash)) throw new IOException("暂存文件已变化，未清理。");
            DeleteItem(path);
        }
    }
    private static string CanonicalFile(string path)
    {
        path = Path.Combine(CanonicalDirectory(Path.GetDirectoryName(path)!), Path.GetFileName(path));
        if (!Exists(path)) return path;
        var names = Directory.EnumerateFileSystemEntries(Path.GetDirectoryName(path)!).Select(Path.GetFullPath).ToArray();
        return names.FirstOrDefault(p => p == path) ?? names.FirstOrDefault(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase)) ?? path;
    }
    /// <summary>解析父级别名，按实际文件系统目录项比较；Mac合法大小写文件仍区分。</summary>
    private static string CanonicalDirectory(string path)
    {
        var info = new DirectoryInfo(path);
        if (info.Parent is null) return info.FullName;
        var actual = new DirectoryInfo(Path.Combine(CanonicalDirectory(info.Parent.FullName), info.Name));
        if (!actual.Exists && actual.LinkTarget is null) return actual.FullName;
        var link = actual.ResolveLinkTarget(true);
        if (link is not null) return CanonicalDirectory(link.FullName);
        var resolved = actual.FullName;
        if (!Directory.Exists(resolved) || actual.Parent is null) return resolved;
        var entries = Directory.EnumerateDirectories(Path.GetDirectoryName(resolved)!);
        return entries.FirstOrDefault(p => p == resolved) ?? Directory.EnumerateDirectories(Path.GetDirectoryName(resolved)!).FirstOrDefault(p => string.Equals(p, resolved, StringComparison.OrdinalIgnoreCase)) ?? resolved;
    }
    private static async Task CopyVerifiedAsync(string source, string destination, string hash, CancellationToken token)
    {
        if (new FileInfo(source).LinkTarget is { } target)
        {
            token.ThrowIfCancellationRequested();
            if (Exists(destination)) throw new IOException("临时链接落点已被占用。");
            File.CreateSymbolicLink(destination, target);
            if (!await MatchesAsync(destination, hash)) { File.Delete(destination); throw new IOException("链接完整性校验失败。"); }
            return;
        }
        if (Directory.Exists(source)) { await CopyDirectoryVerifiedAsync(source, destination, hash, token); return; }
        using var written = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        bool created = false;
        try
        {
            await using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 1024, true))
            await using (var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 128 * 1024, true))
            {
                created = true; var buffer = new byte[128 * 1024]; int count;
                while ((count = await input.ReadAsync(buffer, token)) > 0)
                { await output.WriteAsync(buffer.AsMemory(0, count), token); written.AppendData(buffer, 0, count); }
                await output.FlushAsync(token); output.Flush(true);
            }
            if (!await MatchesAsync(destination, hash)) throw new IOException("完整性校验失败。");
            File.SetLastWriteTimeUtc(destination, File.GetLastWriteTimeUtc(source));
        }
        catch
        {
            // 只删除仍符合本次已写字节的部分副本；中断/外部替换无法证明时留给恢复记录。
            if (created && await MatchesAsync(destination, Convert.ToHexString(written.GetHashAndReset()))) File.Delete(destination);
            throw;
        }
    }
    private static async Task<string> HashAsync(string path, CancellationToken token)
    { if (new FileInfo(path).LinkTarget is { } target) { token.ThrowIfCancellationRequested(); return "L:" + Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(target))); } if (Directory.Exists(path)) return await HashDirectoryAsync(path, token); await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 1024, true); return Convert.ToHexString(await SHA256.HashDataAsync(stream, token)); }
    private static async Task<bool> MatchesAsync(string path, string? hash) => hash is null
        ? !Exists(path) : Exists(path) && await HashAsync(path, CancellationToken.None) == hash;
    private static async Task WriteJournalAsync(string path, Journal journal, CancellationToken token)
    {
        var temporary = path + ".tmp";
        await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(journal, RecoveryJsonContext.Default.Journal), token);
        using (var stream = new FileStream(temporary, FileMode.Open, FileAccess.Write)) stream.Flush(true);
        File.Move(temporary, path, true);
    }
}
