using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
namespace NeeView;

/// <summary>导入结果包含持久备份，失败重建时仍能恢复导入前五文件及缺失状态。</summary>
public sealed record ProfileImportResult(string BackupDirectory, IReadOnlyList<string> AppliedFiles);

public sealed partial class SaveData
{
    /// <summary>在旧窗口关闭前只验证候选；不切换 Config.Current、不写磁盘。</summary>
    /// <param name="request">预览确认的独立快照。</param><param name="token">排队和读取取消。</param>
    public async Task ValidateProfileImportAsync(ProfileImportRequest request, CancellationToken token = default)
    {
        await _gate.WaitAsync(token);
        // JSON 合并/验证和哈希在后台执行，gate 仍覆盖整个离线事务。
        try { await Task.Run(async () => ValidateImportDocuments(await PrepareImportAsync(request, token)), token); }
        finally { _gate.Release(); }
    }
    /// <summary>离线应用选项；调用方必须先完成唯一阅读窗口的保存与释放，再用新实例重建。</summary>
    /// <param name="request">经确认的候选，不重新读来源。</param><param name="token">提交开始前可取消，之后完成或回滚。</param>
    /// <returns>实际应用文件及持久备份目录。</returns>
    public async Task<ProfileImportResult> ApplyProfileImportAsync(ProfileImportRequest request, CancellationToken token = default)
    {
        await _gate.WaitAsync(token);
        try
        {
            return await Task.Run(async () =>
            {
                RecoverInterruptedSave();
                var documents = await PrepareImportAsync(request, token); ValidateImportDocuments(documents);
                var names = request.Selection.Files.Where(name => request.GetEffectiveDocument(name) is not null).ToArray();
                var backup = await CreateImportBackupAsync(token);
                await WriteImportDocumentsAsync(names.ToDictionary(name => name, name => documents[name]), token);
                return new ProfileImportResult(backup, names);
            }, token);
        }
        finally { _gate.Release(); }
    }
    /// <summary>验证持久备份哈希后通过同一事务恢复；不加载配置，失败保留唯一恢复材料。</summary>
    /// <param name="backupDirectory">由实际应用结果返回的应用备份目录。</param><param name="token">准备阶段取消。</param>
    public async Task RestoreProfileImportAsync(string backupDirectory, CancellationToken token = default)
    {
        await _gate.WaitAsync(token);
        try
        {
            await Task.Run(async () =>
            {
                var root = System.IO.Path.GetFullPath(System.IO.Path.Combine(DirectoryPath, "ImportBackups")) + System.IO.Path.DirectorySeparatorChar;
                var full = System.IO.Path.GetFullPath(backupDirectory);
                if (!full.StartsWith(root, StringComparison.Ordinal)) throw new InvalidDataException("备份不属于当前 Profile。");
                var manifest = JsonNode.Parse(await File.ReadAllTextAsync(System.IO.Path.Combine(full, "manifest.json"), token))!.AsObject();
                var values = new Dictionary<string, byte[]?>();
                foreach (var name in ProfileImportFiles.Names)
                {
                    if (!manifest.ContainsKey(name)) throw new InvalidDataException("备份清单缺失：" + name);
                    if (manifest[name] is null) { values[name] = null; continue; }
                    var bytes = await File.ReadAllBytesAsync(System.IO.Path.Combine(full, name), token);
                    if (Convert.ToHexString(SHA256.HashData(bytes)) != manifest[name]!.GetValue<string>()) throw new InvalidDataException("备份校验失败：" + name);
                    values[name] = bytes;
                }
                // 全部验证后才准备目标，恢复保持原文件字节及原缺失状态。
                await WriteImportBytesAsync(values, token);
            }, token);
        }
        finally { _gate.Release(); }
    }
    /// <summary>重读刚保存的当前文件，仅将实际选中的来源恢复为候选，不改运行配置。</summary>
    /// <param name="request">独立导入快照。</param><param name="token">读取取消。</param><returns>包含未选择文件原节点的完整五文件候选。</returns>
    private async Task<Dictionary<string, JsonObject?>> PrepareImportAsync(ProfileImportRequest request, CancellationToken token)
    {
        var documents = new Dictionary<string, JsonObject?>();
        foreach (var name in ProfileImportFiles.Names)
            documents[name] = File.Exists(System.IO.Path.Combine(DirectoryPath, name)) ? await ReadAsync(name, token) : null;
        foreach (var name in request.Selection.Files)
        {
            if (request.GetEffectiveDocument(name) is not { } imported) continue;
            imported = ProfileImportCompatibility.Upgrade(name, imported);
            if (name == "UserSetting.json")
            {
                var result = documents[name]?.DeepClone().AsObject() ?? new JsonObject();
                // 原 ObjectMerge 使用反序列化后的默认实例；差分缺省必须回到来源方案默认值。
                var projected = JsonSerializer.SerializeToNode(ReadProfileConfig(imported), Options)!.AsObject();
                if (imported["Config"] is JsonObject config) Merge(projected, config);
                Merge(Object(result, "Config"), projected);
                foreach (var pair in imported.Where(p => p.Key != "Config")) result[pair.Key] = pair.Value?.DeepClone();
                // 原 RestoreCommandCollection(reset:true)：来源没记录的键位回到默认，不继承旧差分。
                result["Commands"] = imported["Commands"]?.DeepClone();
                documents[name] = result;
            }
            else documents[name] = imported;
        }
        return documents;
    }
    /// <summary>检查已迁字段、集合和已知命令参数；未迁未知字段继续保留。</summary>
    /// <param name="docs">完整五文件候选；缺省节点使用原默认值。</param>
    private static void ValidateImportDocuments(IReadOnlyDictionary<string, JsonObject?> docs)
    {
        _ = ReadProfileConfig(docs["UserSetting.json"] ?? new());
        var bookmark = docs["Bookmark.json"];
        if (bookmark?["Nodes"] is { } nodes)
        {
            var tree = nodes.Deserialize<BookmarkNode>(Options) ?? throw new InvalidDataException("书签根无效。");
            if (!tree.IsFolder) throw new InvalidDataException("Bookmark.Nodes 必须是根文件夹。");
            foreach (var node in ProfileImportCompatibility.Walk(nodes.AsObject()))
                if (node["Children"] is null && node["Path"]?.GetValue<string>() is { } path) _ = ReadMemento(path, node);
        }
        var history = docs["History.json"];
        if (history?["Items"] is JsonArray items)
        {
            var paths = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in items)
            {
                var item = entry as JsonObject ?? throw new InvalidDataException("历史记录无效。");
                var path = item["Path"]?.GetValue<string>() ?? throw new InvalidDataException("历史 Path 缺失。");
                if (!paths.Add(path)) throw new InvalidDataException("历史映射后重复路径：" + path);
                _ = item["LastAccessTime"]?.GetValue<DateTime>(); _ = ReadMemento(path, item);
            }
        }
        foreach (var field in new[] { "BookmarkSearchHistory", "BookHistorySearchHistory", "PageListSearchHistory", "BookshelfSearchHistory" })
            _ = history?[field]?.Deserialize<string[]>(Options);
        if (docs["UserSetting.json"]?["Config"]?["StartUp"]?["LastBookV2"] is JsonObject last && last["Path"]?.GetValue<string>() is { } lastPath) _ = ReadMemento(lastPath, last);
        new FolderConfigCollection().Restore(docs["Foldres.json"] ?? new());
        new QuickAccessCollection().Restore(docs["QuicAccess.json"] ?? new());
        // 参数由原类型解析，不启用未知命令或脚本；应用后设置窗口不能因已知坏参数崩溃。
        if (docs["UserSetting.json"]?["Commands"] is JsonObject commands)
            foreach (var pair in commands)
            {
                if (pair.Value is not JsonObject command) throw new InvalidDataException("命令差分必须是对象：" + pair.Key);
                _ = command["ShortCutKey"]?.GetValue<string>(); _ = command["MouseGesture"]?.GetValue<string>();
                if (command["Parameter"] is { } parameter && CommandParameterTypes.Get(pair.Key) is { } type)
                {
                    var discriminator = parameter["$type"]?.GetValue<string>();
                    var expected = type.Name[..^"CommandParameter".Length];
                    if (discriminator is not null && discriminator != expected)
                        throw new InvalidDataException($"命令 {pair.Key} 的参数类型 {discriminator} 与原 {expected} 不匹配。");
                    var options = new JsonSerializerOptions(Options);
                    options.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
                    _ = parameter.Deserialize(type, options);
                }
            }
    }
    /// <summary>在权威文件改写前保存原字节和缺失清单，不替代短期事务副本。</summary>
    /// <param name="token">备份准备取消。</param><returns>当前 Profile 下带哈希清单的备份目录。</returns>
    private async Task<string> CreateImportBackupAsync(CancellationToken token)
    {
        var path = System.IO.Path.Combine(DirectoryPath, "ImportBackups", DateTime.UtcNow.ToString("yyyyMMddTHHmmssfff") + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path); var manifest = new JsonObject();
        foreach (var name in ProfileImportFiles.Names)
        {
            var source = System.IO.Path.Combine(DirectoryPath, name);
            if (!File.Exists(source)) { manifest[name] = null; continue; }
            var bytes = await File.ReadAllBytesAsync(source, token);
            await WriteDurableBytesAsync(System.IO.Path.Combine(path, name), bytes, token);
            manifest[name] = Convert.ToHexString(SHA256.HashData(bytes));
        }
        await WriteDurableBytesAsync(System.IO.Path.Combine(path, "manifest.json"), JsonSerializer.SerializeToUtf8Bytes(manifest, Options), token);
        return path;
    }
    /// <summary>将选定 JSON 序列化到后台事务；未包含的文件不触碰。</summary>
    /// <param name="docs">实际提交文件。</param><param name="token">提交点之前取消。</param>
    private Task WriteImportDocumentsAsync(IReadOnlyDictionary<string, JsonObject?> docs, CancellationToken token) =>
        WriteImportBytesAsync(docs.ToDictionary(p => p.Key, p => p.Value is null ? null : JsonSerializer.SerializeToUtf8Bytes(p.Value, Options)), token);
    /// <summary>共用原五文件提交与中断恢复原语，null 明确表示恢复原缺失状态。</summary>
    /// <param name="values">固定 Profile 文件及目标字节。</param><param name="token">提交标记前取消。</param>
    private async Task WriteImportBytesAsync(IReadOnlyDictionary<string, byte[]?> values, CancellationToken token)
    {
        var names = values.Keys.ToArray(); var marker = System.IO.Path.Combine(DirectoryPath, ".save-pending.json");
        try
        {
            foreach (var pair in values) if (pair.Value is { } bytes) await WriteDurableBytesAsync(System.IO.Path.Combine(DirectoryPath, pair.Key) + ".tmp", bytes, token);
            await CommitTemporaryFilesAsync(names, values.Where(p => p.Value is null).Select(p => p.Key).ToHashSet(), token);
        }
        catch { RecoverInterruptedSave(); throw; }
        finally
        {
            foreach (var name in names.Append(".save-pending.json"))
            {
                var path = System.IO.Path.Combine(DirectoryPath, name);
                if (File.Exists(path + ".tmp")) File.Delete(path + ".tmp");
                if (!File.Exists(marker) && File.Exists(path + ".save-backup")) File.Delete(path + ".save-backup");
            }
        }
    }
    /// <summary>同目录准备与持久备份共用 durable 写入，不替换权威文件。</summary>
    /// <param name="path">应用拥有的准备路径。</param><param name="bytes">完整文件内容。</param><param name="token">准备取消。</param>
    private static async Task WriteDurableBytesAsync(string path, byte[] bytes, CancellationToken token)
    {
        await using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous);
        await stream.WriteAsync(bytes, token); await stream.FlushAsync(token); stream.Flush(true);
    }
}
