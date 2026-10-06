using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
namespace NeeView;

/// <summary>导入结果包含持久备份，失败重建时恢复五文件和实际覆盖附属项的原字节/缺失状态。</summary>
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
                var names = request.Selection.Files.Where(name => request.GetEffectiveDocument(name) is not null).ToList();
                var assets = request.GetAssets();
                // 列表导入显式切换到Mac管理目录；目录配置与文件参加同一事务和备份。
                if (assets.Keys.Any(name => ProfileImportAssets.Kind(name) == ProfileImportAssetKind.Playlists) && !names.Contains("UserSetting.json")) names.Add("UserSetting.json");
                var values = names.ToDictionary(name => name, name => documents[name] is null ? null : JsonSerializer.SerializeToUtf8Bytes(documents[name], Options));
                foreach (var pair in assets) values.Add(pair.Key, pair.Value);
                var backup = await CreateImportBackupAsync(assets.Keys, token);
                await WriteImportBytesAsync(values, token);
                return new ProfileImportResult(backup, values.Keys.ToArray());
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
                if (!full.StartsWith(root, StringComparison.Ordinal) || System.IO.Path.GetDirectoryName(full) != root.TrimEnd(System.IO.Path.DirectorySeparatorChar))
                    throw new InvalidDataException("备份不属于当前 Profile 的直接备份目录。");
                ProfileImportAssets.RejectLink(root.TrimEnd(System.IO.Path.DirectorySeparatorChar));
                ProfileImportAssets.RejectLink(full);
                ProfileImportAssets.RejectLink(System.IO.Path.Combine(full, "manifest.json"));
                JsonObject manifest;
                try { manifest = JsonNode.Parse(await File.ReadAllTextAsync(System.IO.Path.Combine(full, "manifest.json"), token)) as JsonObject ?? throw new InvalidDataException("备份清单必须是对象。"); }
                catch (JsonException ex) { throw new InvalidDataException("备份清单损坏。", ex); }
                var values = new Dictionary<string, byte[]?>();
                if (manifest.Count > ProfileImportFiles.MaxEntries) throw new InvalidDataException("备份清单数量超限。");
                if (manifest.Select(p => ProfileImportAssets.CollisionKey(p.Key)).Distinct().Count() != manifest.Count) throw new InvalidDataException("备份清单文件名冲突。");
                foreach (var name in ProfileImportFiles.Names) if (!manifest.ContainsKey(name)) throw new InvalidDataException("备份清单缺失：" + name);
                foreach (var name in manifest.Select(p => p.Key))
                {
                    var source = ProfileImportAssets.ResolveTarget(full, name);
                    _ = ProfileImportAssets.ResolveTarget(DirectoryPath, name);
                    if (manifest[name] is null) { values[name] = null; continue; }
                    var bytes = await File.ReadAllBytesAsync(source, token);
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
            documents[name] = File.Exists(ProfileImportAssets.ResolveTarget(DirectoryPath, name)) ? await ReadAsync(name, token) : null;
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
                // 效果暂无运行类型投影；沿原默认恢复边界替换这三个纯数据分支，避免递归合并复活旧层/缓存。
                var resultConfig = Object(result, "Config");
                foreach (var branch in new[] { "ImageEffect", "ImageEffectCache", "EffectProfiles" })
                    if (imported["Config"] is JsonObject importedConfig && importedConfig.ContainsKey(branch)) resultConfig[branch] = importedConfig[branch]?.DeepClone();
                    else resultConfig.Remove(branch);
                // 兼容材料归属于来源效果，不能把当前Profile的成功标记贴到未知/未转换的新来源上。
                foreach (var field in new[] { "MacImportedLegacyEffectFormat", "MacImportedLegacyEffectUpgrade", "MacImportedLegacyEffectIssue", "MacImportedLegacyImageEffects" }) result.Remove(field);
                foreach (var pair in imported.Where(p => p.Key != "Config")) result[pair.Key] = pair.Value?.DeepClone();
                // 原 RestoreCommandCollection(reset:true)：来源没记录的键位回到默认，不继承旧差分。
                result["Commands"] = imported["Commands"]?.DeepClone();
                documents[name] = result;
            }
            else documents[name] = imported;
        }
        if (request.Selection.Folders && documents["Foldres.json"] is { } folders && request.GetEffectiveDocument("Foldres.json") is not null)
            LegacyFolderConfigUpgrade.NormalizeOrders(folders, ReadProfileConfig(documents["UserSetting.json"] ?? new()));
        var playlists = request.AssetNames.Where(name => ProfileImportAssets.Kind(name) == ProfileImportAssetKind.Playlists).ToArray();
        if (playlists.Length > 0)
        {
            documents["UserSetting.json"] ??= new JsonObject();
            var config = Object(Object(documents["UserSetting.json"]!, "Config"), "Playlist");
            // 不跟随旧自定义目录写入；原默认目录契约由当前Profile提供，未知设置继续保留。
            config["PlaylistFolder"] = null;
            var current = config["CurrentPlaylist"]?.GetValue<string>();
            if (current is not null)
            {
                var fileName = current.Replace('\\', '/').Split('/')[^1];
                var imported = playlists.FirstOrDefault(name => ProfileImportAssets.CollisionKey(name) == ProfileImportAssets.CollisionKey("Playlists/" + fileName));
                if (imported is not null) config["CurrentPlaylist"] = imported.Split('/')[1];
            }
        }
        foreach (var name in request.AssetNames) _ = ProfileImportAssets.ResolveTarget(DirectoryPath, name);
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
    /// <param name="assets">实际覆盖的附属项；未选中材料不备份或触碰。</param>
    /// <param name="token">备份准备取消。</param><returns>当前 Profile 下带哈希清单的备份目录。</returns>
    private async Task<string> CreateImportBackupAsync(IEnumerable<string> assets, CancellationToken token)
    {
        ProfileImportAssets.RejectLink(DirectoryPath);
        ProfileImportAssets.RejectLink(System.IO.Path.Combine(DirectoryPath, "ImportBackups"));
        var path = System.IO.Path.Combine(DirectoryPath, "ImportBackups", DateTime.UtcNow.ToString("yyyyMMddTHHmmssfff") + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path); var manifest = new JsonObject();
        foreach (var name in ProfileImportFiles.Names.Concat(assets).Distinct())
        {
            var source = ProfileImportAssets.ResolveTarget(DirectoryPath, name);
            if (!File.Exists(source)) { manifest[name] = null; continue; }
            var bytes = await File.ReadAllBytesAsync(source, token);
            var target = ProfileImportAssets.ResolveTarget(path, name);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(target)!);
            await WriteDurableBytesAsync(target, bytes, token);
            manifest[name] = Convert.ToHexString(SHA256.HashData(bytes));
        }
        await WriteDurableBytesAsync(System.IO.Path.Combine(path, "manifest.json"), JsonSerializer.SerializeToUtf8Bytes(manifest, Options), token);
        return path;
    }
    /// <summary>共用原提交与中断恢复原语，null 明确表示恢复原缺失状态。</summary>
    /// <param name="values">受限 Profile 文件及目标字节。</param><param name="token">提交标记前取消。</param>
    private async Task WriteImportBytesAsync(IReadOnlyDictionary<string, byte[]?> values, CancellationToken token)
    {
        var names = values.Keys.ToArray(); var marker = System.IO.Path.Combine(DirectoryPath, ".save-pending.json");
        // 目标验证失败时不能进入清理：未经验证的来源名绝不用于删除暂存文件。
        var targets = values.Keys.ToDictionary(name => name, name => ProfileImportAssets.ResolveTarget(DirectoryPath, name));
        ProfileImportAssets.RejectLink(marker); ProfileImportAssets.RejectLink(marker + ".tmp");
        try
        {
            foreach (var pair in values) if (pair.Value is { } bytes)
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(targets[pair.Key])!);
                await WriteDurableBytesAsync(targets[pair.Key] + ".tmp", bytes, token);
            }
            await CommitTemporaryFilesAsync(names, values.Where(p => p.Value is null).Select(p => p.Key).ToHashSet(), token);
        }
        catch { RecoverInterruptedSave(); throw; }
        finally
        {
            foreach (var name in names.Append(".save-pending.json"))
            {
                var path = name == ".save-pending.json" ? marker : ProfileImportAssets.ResolveTarget(DirectoryPath, name);
                ProfileImportAssets.RejectLink(path + ".tmp"); ProfileImportAssets.RejectLink(path + ".save-backup");
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
