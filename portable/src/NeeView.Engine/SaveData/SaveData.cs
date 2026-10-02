using System.Text.Json;
using System.Text.Json.Nodes;
namespace NeeView;

/// <summary>沿用 UserSetting/History JSON 和 BookMemento；原始节点保留未迁移字段。</summary>
public sealed class SaveData(string directory)
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true, PropertyNameCaseInsensitive = true, AllowTrailingCommas = true, ReadCommentHandling = JsonCommentHandling.Skip };
    private readonly SemaphoreSlim _gate = new(1);
    private JsonObject _setting = new();
    private JsonObject _history = new();
    private JsonObject _bookmarks = new();
    public BookmarkCollection Bookmarks { get; private set; } = new(new() { Children = [] });
    public BookmarkNode BookmarkRoot => Bookmarks.Items;
    private BookmarkNodeMemento[] _removedBookmarks = [];
    public bool CanRestoreBookmarks => _removedBookmarks.Any(e => BookmarkRoot.Walk().Contains(e.Parent));
    public IReadOnlyList<HistoryEntry> HistoryEntries { get; private set; } = [];
    public event EventHandler? Changed;
    public string DirectoryPath { get; } = directory;
    public string? LastBookPath => _setting["Config"]?["StartUp"]?["LastBookV2"]?["Path"]?.GetValue<string>();

    /// <summary>加载原命名文件并恢复 P1 已支持配置；损坏文件不覆盖。</summary>
    public async Task LoadAsync(CancellationToken token = default)
    {
        RecoverInterruptedSave();
        _setting = await ReadAsync("UserSetting.json", token);
        _history = await ReadAsync("History.json", token);
        _bookmarks = await ReadAsync("Bookmark.json", token);
        Bookmarks = new(_bookmarks["Nodes"]?.Deserialize<BookmarkNode>(Options) ?? new() { Children = [] });
        _removedBookmarks = [];
        if (!BookmarkRoot.IsFolder) throw new JsonException("Bookmark.Nodes 必须是根文件夹。");
        RefreshHistory();
        var raw = _setting["Config"] as JsonObject;
        var config = new Config();
        if (raw is not null)
        {
            config.BookSetting = ReadBranch<BookSettingConfig>(raw, "BookSetting");
            config.BookSettingDefault = ReadBranch<BookSettingConfig>(raw, "BookSettingDefault");
            config.BookSettingPolicy = ReadBranch<BookSettingPolicyConfig>(raw, "BookSettingPolicy");
            config.Book = ReadBranch<BookConfig>(raw, "Book");
            config.View = ReadBranch<ViewConfig>(raw, "View");
            config.Panels = ReadBranch<PanelsConfig>(raw, "Panels");
            config.FilmStrip = ReadBranch<FilmStripConfig>(raw, "FilmStrip");
            config.Slider = ReadBranch<SliderConfig>(raw, "Slider");
            config.Bookshelf = ReadBranch<BookshelfConfig>(raw, "Bookshelf");
        }
        Config.SetCurrent(config);
    }

    /// <summary>按原 Path/Page/Props 恢复；Page 是条目名，不是数字页码。</summary>
    public (BookMemento? Memento, int Part) Find(string path)
    {
        var entry = (_history["Items"] as JsonArray)?.OfType<JsonObject>().FirstOrDefault(e => e["Path"]?.GetValue<string>() == path);
        if (entry is null && BookmarkRoot.Walk().FirstOrDefault(e => e.Path == path) is { } bookmark)
            entry = JsonSerializer.SerializeToNode(bookmark, Options)!.AsObject();
        if (entry is null) return (null, 0);
        var props = entry["Props"]?.GetValue<string>();
        // 新版未知 Props 仍保留在节点中，只将当前已识别 token 传入原解析器。
        var known = FilterProps(props, true);
        var memento = BookMemento.ParseWithProperties(path, entry["Page"]?.GetValue<string>(), known);
        // 原 Props 无法无歧义编码 IsWide=false；Mac 扩展仅补充这一缺失值，不改原解析器。
        if (memento is not null && entry["MacIsSupportedWidePage"] is JsonValue wide) memento.IsSupportedWidePage = wide.GetValue<bool>();
        return (memento, entry["MacPagePart"]?.GetValue<int>() ?? 0);
    }

    /// <summary>读取差分命令键位；未配置时使用固定基线默认值。</summary>
    public string GetShortcut(string name, string fallback)
    {
        var item = _setting["Commands"]?[name];
        return item?["ShortCutKey"]?.GetValue<string>() ?? fallback;
    }

    /// <summary>读取原滚动命令差分 Parameter；缺省使用原参数，未知字段不写回。</summary>
    public ScrollPageCommandParameter GetScrollParameter(string name)
    {
        var options = new JsonSerializerOptions(Options);
        options.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
        return _setting["Commands"]?[name]?["Parameter"]?.Deserialize<ScrollPageCommandParameter>(options) ?? new();
    }

    /// <summary>原相反方向的指定步长命令共享参数，NextSizePage 读取 PrevSizePage 的差分。</summary>
    public MoveSizePageCommandParameter GetMoveSizeParameter() => _setting["Commands"]?["PrevSizePage"]?["Parameter"]?.Deserialize<MoveSizePageCommandParameter>(Options) ?? new();

    /// <summary>更新已支持参数并保留原节点的未知字段；原差分快捷键不会被覆盖。</summary>
    public void SetCommandParameter<T>(string name, T value) => Merge(Object(Object(Object(_setting, "Commands"), name), "Parameter"), JsonSerializer.SerializeToNode(value, Options)!.AsObject());

    /// <summary>编辑原 Commands 差分键位；空字符串表示解绑，未知参数保持。</summary>
    public void SetShortcut(string name, string value) => Object(Object(_setting, "Commands"), name)["ShortCutKey"] = value;

    /// <summary>按原书籍路径查询书签，不把当前页面独立注册为另一本书。</summary>
    public bool IsBookmark(string path) => BookmarkRoot.Walk().Any(e => !e.IsFolder && e.Path == path);

    /// <summary>切换书籍书签；保存失败回滚树，书籍阅读状态仍由 History 共享。</summary>
    public Task ToggleBookmarkAsync(Book book, CancellationToken token = default) => EditBookmarkAsync(() =>
    {
        var existing = BookmarkRoot.Walk().FirstOrDefault(e => !e.IsFolder && e.Path == book.Path);
        if (existing is not null) RemoveNode(existing);
        else
        {
            var memento = book.CreateMemento();
            BookmarkRoot.Children!.Add(new() { Path = book.Path, Page = memento.Page, Props = memento.ToPropertiesString() });
        }
    }, token);

    /// <summary>在所选文件夹新建目录，沿用原名称校验及同名递增规则。</summary>
    /// <returns>保存成功的新节点。</returns>
    public Task<BookmarkNode> AddBookmarkFolderAsync(BookmarkNode? parent, string name, CancellationToken token = default) =>
        EditBookmarkAsync(() => Bookmarks.AddNewFolder(parent ?? BookmarkRoot, name), token);

    /// <summary>将当前书籍注册到所选文件夹；同父路径重复时保留原节点。</summary>
    public Task<BookmarkNode> RegisterBookmarkAsync(Book book, BookmarkNode? parent = null, CancellationToken token = default) =>
        EditBookmarkAsync(() => Bookmarks.AddTo(parent ?? BookmarkRoot, book.CreateMemento()), token);

    /// <summary>原登记弹窗 Add/Edit/Remove 在一次保存事务执行；弹窗取消不调用此入口。</summary>
    /// <returns>实际保留节点；删除或没有目标时为 null。</returns>
    public Task<BookmarkNode?> ApplyBookmarkEditAsync(BookmarkPopupEdit edit, BookmarkNode parent, BookmarkPopupResult result, CancellationToken token = default) => EditBookmarkAsync(() =>
    {
        if (!parent.IsFolder || !BookmarkRoot.Walk().Contains(parent)) throw new InvalidOperationException("书签文件夹已失效。");
        switch (result)
        {
            case BookmarkPopupResult.Add:
                var existing = parent.Children!.FirstOrDefault(e => !e.IsFolder && e.Path == edit.Memento.Path);
                if (existing is not null) return existing;
                var added = Bookmarks.AddTo(parent, edit.Memento); return Bookmarks.Rename(added, edit.Name, null);
            case BookmarkPopupResult.Edit:
                if (edit.Node is null) return null;
                var moved = Bookmarks.MoveToChild(edit.Node, parent) ?? edit.Node;
                return Bookmarks.Rename(moved, edit.Name, null);
            case BookmarkPopupResult.Remove:
                var node = parent.Children!.FirstOrDefault(e => !e.IsFolder && e.Path == edit.Memento.Path);
                if (node is not null) _removedBookmarks = [Bookmarks.Remove(node)];
                return null;
            case BookmarkPopupResult.None: return null;
            default: throw new ArgumentOutOfRangeException(nameof(result));
        }
    }, token);

    /// <summary>重命名书籍或文件夹，保留 Path/Page/Props、颜色及未知字段。</summary>
    /// <param name="confirmedTarget">用户确认的合并目标；目标变化时必须重新确认。</param>
    public Task<BookmarkNode> RenameBookmarkAsync(BookmarkNode node, string name, CancellationToken token = default, BookmarkNode? confirmedTarget = null) =>
        EditBookmarkAsync(() => Bookmarks.Rename(node, name, confirmedTarget), token);

    /// <summary>按原规则移入文件夹或移动到最终索引；失败恢复原层级和节点引用。</summary>
    /// <param name="index">null 表示 MoveToChild；数字表示原 Move 的顺序插入。</param>
    public Task<BookmarkNode?> MoveBookmarkAsync(BookmarkNode node, BookmarkNode parent, int? index = null, CancellationToken token = default) =>
        EditBookmarkAsync(() => index.HasValue ? Bookmarks.Move(node, parent, index.Value) : Bookmarks.MoveToChild(node, parent), token);

    /// <summary>仅为文件夹设置原 JSON 颜色，空值恢复默认；不引入界面颜色类型。</summary>
    public Task SetBookmarkColorAsync(BookmarkNode node, string? color, CancellationToken token = default) => EditBookmarkAsync(() =>
    {
        if (!node.IsFolder || !BookmarkRoot.Walk().Contains(node)) throw new InvalidOperationException("只能设置书签文件夹颜色。");
        if (color is not null && (color.Length != 9 || color[0] != '#' || !uint.TryParse(color.AsSpan(1), System.Globalization.NumberStyles.HexNumber, null, out _)))
            throw new ArgumentException("颜色必须为 #AARRGGBB。");
        node.Color = color;
    }, token);

    /// <summary>删除指定书签节点或文件夹，仅更改书签记录，不删除源文件。</summary>
    public Task RemoveBookmarkAsync(BookmarkNode node, CancellationToken token = default) => RemoveBookmarksAsync([node], token);

    /// <summary>一次事务移除选中批次；选中父级时子级由父级携带，恢复顺序沿用原逆序记录。</summary>
    public Task RemoveBookmarksAsync(IEnumerable<BookmarkNode> nodes, CancellationToken token = default)
    {
        var selected = nodes.Distinct().ToArray();
        return EditBookmarkAsync(() =>
        {
            // 保存失败不会替换上一批可恢复记录；单项也提供显式恢复，作为 Mac 表现扩展。
            if (selected.Length == 0) return;
            foreach (var node in selected) if (Bookmarks.ParentOf(node) is null) throw new InvalidOperationException("书签节点已失效。");
            _removedBookmarks = selected.Where(node => !selected.Any(parent => parent != node && parent.Walk().Contains(node)))
                .Select(Bookmarks.Remove).ToArray();
        }, token);
    }

    /// <summary>按原逆序 memento 恢复上一批删除；原父级失效时不创建新路径。</summary>
    /// <returns>保存成功后的实际恢复节点。</returns>
    public Task<BookmarkNode?> RestoreBookmarksAsync(CancellationToken token = default) => EditBookmarkAsync(() =>
    {
        BookmarkNode? restored = null;
        foreach (var memento in _removedBookmarks.Reverse()) if (Bookmarks.Restore(memento)) restored = memento.Node;
        _removedBookmarks = []; return restored;
    }, token);

    /// <summary>统一树编辑与文件提交；失败恢复编辑前数据供用户重试。</summary>
    private Task EditBookmarkAsync(Action edit, CancellationToken token) => EditBookmarkAsync(() => { edit(); return true; }, token);

    /// <summary>串行执行树操作与三文件事务，返回值只在提交成功后交给调用方。</summary>
    private async Task<T> EditBookmarkAsync<T>(Func<T> edit, CancellationToken token)
    {
        await _gate.WaitAsync(token);
        // 保留节点身份，失败后树控件的选择和调用方持有的节点仍可用于重试。
        var previous = BookmarkRoot.Walk().Concat(_removedBookmarks.SelectMany(e => e.Node.Walk())).Distinct()
            .Select(node => (Node: node, node.Name, node.Color, Children: node.Children?.ToArray())).ToArray();
        var removed = _removedBookmarks;
        try { var result = edit(); await WritePairAsync(token); return result; }
        catch
        {
            foreach (var snapshot in previous)
            {
                snapshot.Node.Name = snapshot.Name;
                snapshot.Node.Color = snapshot.Color;
                if (snapshot.Children is null) continue;
                snapshot.Node.Children!.Clear();
                foreach (var child in snapshot.Children) snapshot.Node.Children.Add(child);
            }
            _removedBookmarks = removed;
            throw;
        }
        finally { _gate.Release(); Changed?.Invoke(this, EventArgs.Empty); }
    }
    /// <summary>定位父节点后移除，禁止移除根目录或不属于当前树的旧节点。</summary>
    private void RemoveNode(BookmarkNode node)
    {
        Bookmarks.Remove(node);
    }

    /// <summary>生成按访问时间倒序的只读历史；未知字段仍保留在权威 JSON 中。</summary>
    private void RefreshHistory() => HistoryEntries = (_history["Items"] as JsonArray)?.OfType<JsonObject>()
        .Where(e => e["Path"] is JsonValue).Select(e => new HistoryEntry(e["Path"]!.GetValue<string>(), e["Page"]?.GetValue<string>(),
            e["LastAccessTime"]?.GetValue<DateTime>() ?? DateTime.MinValue)).OrderByDescending(e => e.LastAccessTime).ToArray() ?? [];

    /// <summary>保存阅读状态与布局，已知字段合并进原节点后原子替换文件。</summary>
    public async Task SaveAsync(Book? book, int part, CancellationToken token = default, bool keepHistoryOrder = false)
    {
        await _gate.WaitAsync(token);
        var previousSetting = _setting.DeepClone().AsObject();
        var previousHistory = _history.DeepClone().AsObject();
        try
        {
            var config = Object(_setting, "Config");
            foreach (var branch in new[] { "BookSetting", "BookSettingDefault", "BookSettingPolicy", "Book", "View", "Panels", "FilmStrip", "Slider", "Bookshelf" })
            {
                var value = typeof(Config).GetProperty(branch)!.GetValue(Config.Current);
                Merge(Object(config, branch), JsonSerializer.SerializeToNode(value, Options)!.AsObject());
            }
            if (book is not null)
            {
                var memento = book.CreateMemento();
                var items = _history["Items"] as JsonArray ?? new JsonArray();
                _history["Items"] = items;
                var old = items.OfType<JsonObject>().FirstOrDefault(e => e["Path"]?.GetValue<string>() == book.Path);
                var item = old ?? new JsonObject();
                var unknownProps = FilterProps(item["Props"]?.GetValue<string>(), false);
                item["Path"] = book.Path; item["Page"] = memento.Page;
                item["Props"] = string.Join(' ', new[] { memento.ToPropertiesString(), unknownProps }.Where(e => !string.IsNullOrEmpty(e)));
                if (!keepHistoryOrder || old is null) item["LastAccessTime"] = DateTime.Now;
                item["MacPagePart"] = part;
                item["MacIsSupportedWidePage"] = memento.IsSupportedWidePage;
                // 原 KeepHistoryOrder：阅读位置仍更新，重放不改变访问排序或原数组位置。
                if (!keepHistoryOrder || old is null)
                { if (old is not null) items.Remove(old); items.Insert(0, item); }
                Object(config, "StartUp")["LastBookV2"] = new JsonObject { ["Path"] = book.Path, ["Page"] = memento.Page, ["Props"] = item["Props"]!.DeepClone() };
            }
            // 保留原 Format；新文件使用原名称和版本结构，避免添加另一套存储格式。
            _setting["Format"] ??= JsonValue.Create("NeeView.UserSetting/46.3.0");
            _history["Format"] ??= JsonValue.Create("NeeView.History/46.3.0");
            await WritePairAsync(token);
            RefreshHistory();
        }
        // 文件事务失败时恢复同一权威内存状态；用户尚未保存的表单/Config 可继续重试。
        catch { _setting = previousSetting; _history = previousHistory; throw; }
        finally { _gate.Release(); }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>读取单个 JSON 文件；无法解析时传播错误，保留原文件。</summary>
    private async Task<JsonObject> ReadAsync(string name, CancellationToken token)
    {
        var path = System.IO.Path.Combine(DirectoryPath, name);
        if (!File.Exists(path)) return new();
        await using var stream = File.OpenRead(path);
        return (await JsonNode.ParseAsync(stream, documentOptions: new() { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip }, cancellationToken: token))?.AsObject() ?? new();
    }
    /// <summary>反序列化已迁移分支，缺失字段保留原默认值。</summary>
    private static T ReadBranch<T>(JsonObject root, string name) where T : class, new() => root[name]?.Deserialize<T>(Options) ?? new();
    /// <summary>获取或创建对象分支；已有未知字段保持原节点。</summary>
    private static JsonObject Object(JsonObject root, string name)
    {
        if (root[name] is JsonObject obj) return obj;
        var created = new JsonObject(); root[name] = created; return created;
    }
    /// <summary>递归更新已知字段，原节点中的其他字段不会被删除。</summary>
    private static void Merge(JsonObject target, JsonObject source)
    {
        foreach (var pair in source)
            if (pair.Value is JsonObject nested) Merge(Object(target, pair.Key), nested);
            else target[pair.Key] = pair.Value?.DeepClone();
    }
    /// <summary>按原 Props 标记集合分离未知 token，便于后续迁移。</summary>
    private static string FilterProps(string? props, bool known)
    {
        var names = new HashSet<string> { "SinglePage", "WidePage", "RightToLeft", "LeftToRight", "IsDivide", "IsSingleFirst", "IsSingleLast", "IsWide", "IsRecursive", "Sort", "Rot", "Base", "Seed", "Fx" };
        return string.Join(' ', (props ?? "").Split([' ', ','], StringSplitOptions.RemoveEmptyEntries).Where(e => names.Contains(e.Split('=')[0]) == known));
    }
    /// <summary>先写完三个临时文件，再保留回滚副本；记录存在时启动恢复旧完整状态。</summary>
    private async Task WritePairAsync(CancellationToken token)
    {
        Directory.CreateDirectory(DirectoryPath);
        string[] names = ["History.json", "UserSetting.json", "Bookmark.json"];
        var marker = System.IO.Path.Combine(DirectoryPath, ".save-pending.json");
        try
        {
            // 准备阶段不改动权威文件；任一序列化或权限失败均可直接重试。
            await WriteTemporaryAsync(names[0], _history, token);
            await WriteTemporaryAsync(names[1], _setting, token);
            // 在副本上准备书签文件，提交失败不能污染权威内存节点。
            var preparedBookmarks = _bookmarks.DeepClone().AsObject();
            preparedBookmarks["Format"] ??= JsonValue.Create("NeeView.Bookmark/46.3.0");
            preparedBookmarks["Nodes"] = JsonSerializer.SerializeToNode(BookmarkRoot, Options);
            await WriteTemporaryAsync(names[2], preparedBookmarks, token);
            var previous = new JsonObject();
            foreach (var name in names)
            {
                var path = System.IO.Path.Combine(DirectoryPath, name);
                previous[name] = File.Exists(path);
                if (File.Exists(path)) File.Copy(path, path + ".save-backup", true);
            }
            await WriteTemporaryAsync(".save-pending.json", previous, token);
            File.Move(marker + ".tmp", marker, true);
            // 提交阶段不接受中途取消；异常由副本回滚，崩溃由下次 Load 恢复。
            foreach (var name in names)
            {
                var path = System.IO.Path.Combine(DirectoryPath, name);
                File.Move(path + ".tmp", path, true);
            }
            File.Delete(marker);
            _bookmarks = preparedBookmarks;
        }
        catch { RecoverInterruptedSave(); throw; }
        finally
        {
            foreach (var name in names.Append(".save-pending.json"))
            {
                var path = System.IO.Path.Combine(DirectoryPath, name);
                if (File.Exists(path + ".tmp")) File.Delete(path + ".tmp");
                // 回滚失败时必须保留副本和标记，不能在 finally 删除唯一恢复材料。
                if (!File.Exists(marker) && File.Exists(path + ".save-backup")) File.Delete(path + ".save-backup");
            }
        }
    }
    /// <summary>恢复未完成的保存，兼容旧双文件标记；损坏标记保留恢复材料。</summary>
    private void RecoverInterruptedSave()
    {
        var marker = System.IO.Path.Combine(DirectoryPath, ".save-pending.json");
        if (!File.Exists(marker)) return;
        var previous = JsonNode.Parse(File.ReadAllText(marker))!.AsObject();
        foreach (var name in new[] { "History.json", "UserSetting.json", "Bookmark.json" }.Where(previous.ContainsKey))
        {
            var path = System.IO.Path.Combine(DirectoryPath, name);
            if (previous[name]!.GetValue<bool>()) File.Copy(path + ".save-backup", path, true);
            else if (File.Exists(path)) File.Delete(path);
        }
        File.Delete(marker);
    }
    /// <summary>写入同目录临时 JSON 并刷新到磁盘；不替换权威文件。</summary>
    private async Task WriteTemporaryAsync(string name, JsonObject value, CancellationToken token)
    {
        var temp = System.IO.Path.Combine(DirectoryPath, name) + ".tmp";
        await using var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous);
        await JsonSerializer.SerializeAsync(stream, value, Options, token);
        await stream.FlushAsync(token); stream.Flush(true);
    }
}

/// <summary>原 History.Items 的只读投影；位置仍为条目名，路径是书籍定位。</summary>
public sealed record HistoryEntry(string Path, string? Page, DateTime LastAccessTime)
{
    public string Name => System.IO.Path.GetFileName(Path.TrimEnd(System.IO.Path.DirectorySeparatorChar));
}
