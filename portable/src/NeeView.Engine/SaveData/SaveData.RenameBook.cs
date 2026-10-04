using System.Text.Json;
using System.Text.Json.Nodes;
namespace NeeView;

public sealed partial class SaveData
{
    private BookRenamePlan? _pendingBookRename;
    private string RenameMarker => System.IO.Path.Combine(DirectoryPath, ".book-rename-pending.json");
    /// <summary>改动实体前保存小型路径联动记录；不建立第二状态数据库。</summary>
    /// <param name="plan">已确认的实体改名计划。</param><param name="token">准备阶段的取消令牌。</param>
    /// <returns>记录落盘完成的任务；未完成旧记录阻止新实体操作。</returns>
    public async Task PrepareBookRenameAsync(BookRenamePlan plan, CancellationToken token)
    {
        await _gate.WaitAsync(token);
        try
        {
            if (File.Exists(RenameMarker)) throw new IOException("上次书籍重命名的路径联动尚未完成，请先保存或处理恢复提示。");
            var node = JsonSerializer.SerializeToNode(plan, Options)!.AsObject();
            Directory.CreateDirectory(DirectoryPath);
            await WriteTemporaryAsync(".book-rename-pending.json", node, token);
            File.Move(RenameMarker + ".tmp", RenameMarker, false);
        }
        finally { if (File.Exists(RenameMarker + ".tmp")) File.Delete(RenameMarker + ".tmp"); _gate.Release(); }
    }
    /// <summary>实体改名失败时撤销尚未提交的记录；成功操作不调用此入口。</summary>
    public void CancelBookRename() { File.Delete(RenameMarker); }
    /// <summary>启动装配通过真实后端核对记录；模糊情况保留材料，不猜测外部移动。</summary>
    /// <param name="backend">核对实体是否已提交的系统后端。</param><param name="token">读取与探测的取消令牌。</param>
    /// <returns>未完成或模糊恢复的提示；没有待办时返回空集合。</returns>
    public async Task<IReadOnlyList<string>> RecoverBookRenameAsync(IBookRenameBackend backend, CancellationToken token = default)
    {
        if (!File.Exists(RenameMarker)) return [];
        try
        {
            var plan = JsonSerializer.Deserialize<BookRenamePlan>(await File.ReadAllTextAsync(RenameMarker, token), Options)
                ?? throw new JsonException("重命名记录为空。");
            var committed = await backend.WasRenamedAsync(plan, token);
            if (committed is null) return ["书籍重命名恢复无法确认来源身份，记录已保留：" + RenameMarker];
            if (!committed.Value) { CancelBookRename(); return []; }
            await RenameBookPathsAsync(plan); return [];
        }
        catch (Exception ex) when (ex is not OperationCanceledException) { return ["书籍重命名路径恢复未完成：" + ex.Message]; }
    }
    /// <summary>沿原RenameRecursive更新已知字段和原节点；实体已提交，保存失败保留新内存路径及恢复记录。</summary>
    /// <param name="plan">实体已成功提交的原路径与实际新路径。</param>
    /// <returns>全部明确引用保存完成的任务；部分失败抛出并保留重试记录。</returns>
    public async Task RenameBookPathsAsync(BookRenamePlan plan)
    {
        _pendingBookRename = plan;
        await _gate.WaitAsync();
        try
        {
            string Rename(string path) => BookMementoTools.RenamePath(path, plan.Target.Path, plan.Destination);
            if (_history["Items"] is JsonArray items)
            {
                var changed = items.OfType<JsonObject>().Where(item => item["Path"]?.GetValue<string>() is { } path && Rename(path) != path).ToArray();
                foreach (var item in changed)
                {
                    var next = Rename(item["Path"]!.GetValue<string>());
                    foreach (var collision in items.OfType<JsonObject>().Where(e => !ReferenceEquals(e, item) && e["Path"]?.GetValue<string>() == next).ToArray()) items.Remove(collision);
                    item["Path"] = next;
                }
            }
            var last = _setting["Config"]?["StartUp"]?["LastBookV2"];
            if (last?["Path"]?.GetValue<string>() is { } lastPath) last["Path"] = Rename(lastPath);
            Config.Current.StartUp.LastFolder = RenameFolder(Config.Current.StartUp.LastFolder);
            Config.Current.StartUp.LastBookmarkFolder = RenameFolder(Config.Current.StartUp.LastBookmarkFolder);
            BookshelfFolderMemento? RenameFolder(BookshelfFolderMemento? folder) => folder is null ? null : folder with
                { Path = Rename(folder.Path), Select = folder.Select is null ? null : Rename(folder.Select) };
            if (_setting["Config"]?["StartUp"] is JsonObject startup)
                foreach (var key in new[] { "LastFolder", "LastBookmarkFolder" })
                    if (startup[key] is JsonObject folder)
                        foreach (var field in new[] { "Path", "Select" }) if (folder[field]?.GetValue<string>() is { } path) folder[field] = Rename(path);
            // 自定义列表文件夹也可能位于书籍目录内；先迁移已知地址，禁止保存时重建旧书籍目录。
            var playlistConfig = Config.Current.Playlist; var playlistFolder = playlistConfig.PlaylistFolder; var currentPlaylist = playlistConfig.CurrentPlaylist;
            if (Rename(playlistFolder) != playlistFolder) playlistConfig.PlaylistFolderRaw = Rename(playlistFolder);
            playlistConfig.CurrentPlaylist = Rename(currentPlaylist);
            Merge(Object(Object(_setting, "Config"), "Playlist"), JsonSerializer.SerializeToNode(playlistConfig, Options)!.AsObject());
            foreach (var node in BookmarkRoot.Walk()) if (node.Path is { } path) node.Path = Rename(path);
            foreach (var node in QuickAccess.Root.Walk()) if (node.Path is { } path) node.Path = Rename(path);
            var folders = FolderConfigs.CreateMemento(forSave: false);
            if (folders["Folders"] is JsonArray entries)
            {
                var incoming = entries.OfType<JsonObject>().Where(e => Rename(e["Place"]!.GetValue<string>()) != e["Place"]!.GetValue<string>())
                    .Select(e => Rename(e["Place"]!.GetValue<string>())).ToHashSet(StringComparer.Ordinal);
                foreach (var collision in entries.OfType<JsonObject>().Where(e => incoming.Contains(e["Place"]!.GetValue<string>())).ToArray()) entries.Remove(collision);
            }
            foreach (var unit in (folders["Folders"] as JsonArray)?.OfType<JsonObject>() ?? [])
            {
                var oldPlace = unit["Place"]!.GetValue<string>(); var newPlace = Rename(oldPlace); unit["Place"] = newPlace;
                if (unit["Thumbs"] is not JsonObject thumbs) continue;
                foreach (var pair in thumbs.ToArray())
                {
                    if (pair.Value is not JsonValue value || !value.TryGetValue<string>(out var target)) continue;
                    var oldBook = System.IO.Path.Combine(oldPlace, pair.Key); var newBook = Rename(oldBook);
                    var targetPath = System.IO.Path.IsPathFullyQualified(target) ? target : System.IO.Path.GetFullPath(System.IO.Path.Combine(oldBook, target));
                    if (oldPlace == newPlace && oldBook == newBook && targetPath == Rename(targetPath)) continue;
                    var newName = System.IO.Path.GetDirectoryName(newBook) == newPlace ? System.IO.Path.GetFileName(newBook) : pair.Key;
                    var newTarget = System.IO.Path.IsPathFullyQualified(target) ? Rename(target) :
                        System.IO.Path.GetRelativePath(newBook, Rename(System.IO.Path.GetFullPath(System.IO.Path.Combine(oldBook, target))));
                    if (newName != pair.Key) thumbs.Remove(pair.Key);
                    thumbs[newName] = newTarget;
                }
            }
            FolderConfigs.Restore(folders);
            var suppressed = _suppressedHistoryPaths.Select(Rename).ToArray(); _suppressedHistoryPaths.Clear(); _suppressedHistoryPaths.UnionWith(suppressed);
            if (_activeHistoryPath is not null) _activeHistoryPath = Rename(_activeHistoryPath);
            RefreshHistory(); await WritePairAsync(CancellationToken.None);
        }
        finally { _gate.Release(); Changed?.Invoke(this, EventArgs.Empty); BookmarksChanged?.Invoke(this, EventArgs.Empty); QuickAccessChanged?.Invoke(this, EventArgs.Empty); }
        await Playlists.RenameItemPathRecursiveAsync(plan.Target.Path, plan.Destination);
        File.Delete(RenameMarker); _pendingBookRename = null;
    }
    /// <summary>防抖/切书/退出保存重试已成功实体的路径联动，错误不能假装实体回滚。</summary>
    /// <returns>待办路径联动完成的任务；无待办时为已完成任务。</returns>
    public Task FlushBookRenameAsync() => _pendingBookRename is { } plan ? RenameBookPathsAsync(plan) : Task.CompletedTask;
}
