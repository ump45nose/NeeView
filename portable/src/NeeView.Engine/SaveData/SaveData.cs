using System.Text.Json;
using System.Text.Json.Nodes;
namespace NeeView;

/// <summary>沿用 UserSetting/History JSON 和 BookMemento；原始节点保留未迁移字段。</summary>
/// <param name="directory">独立Mac用户状态目录。</param>
/// <param name="temporaryDirectory">启动层提供的应用临时根；不会排除整个系统临时目录。</param>
public sealed partial class SaveData(string directory, string? temporaryDirectory = null)
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true, PropertyNameCaseInsensitive = true, AllowTrailingCommas = true, ReadCommentHandling = JsonCommentHandling.Skip };
    // 原导出使用字符串枚举；读取兼容原字符串及早期 Mac 数值，保存格式不全局改写。
    private static readonly JsonSerializerOptions ReadOptions = CreateReadOptions();
    private static JsonSerializerOptions CreateReadOptions()
    { var options = new JsonSerializerOptions(Options); options.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()); return options; }
    private readonly SemaphoreSlim _gate = new(1);
    private readonly string? _temporaryDirectory = temporaryDirectory is null ? null : System.IO.Path.TrimEndingDirectorySeparator(System.IO.Path.GetFullPath(temporaryDirectory));
    private JsonObject _setting = new();
    private JsonObject _history = new();
    private JsonObject _bookmarks = new();
    // 原Remove/Clear抑制同主图保存；下一次真实主图变化或新访问按原阈值重启登记。
    private readonly HashSet<string> _suppressedHistoryPaths = new(StringComparer.Ordinal);
    private string? _activeHistoryPath;
    private Book? _activeHistoryBook;
    public BookmarkCollection Bookmarks { get; private set; } = new(new() { Children = [] });
    public BookmarkNode BookmarkRoot => Bookmarks.Items;
    private BookmarkNodeMemento[] _removedBookmarks = [];
    public bool CanRestoreBookmarks => _removedBookmarks.Any(e => BookmarkRoot.Walk().Contains(e.Parent));
    public IReadOnlyList<HistoryEntry> HistoryEntries { get; private set; } = [];
    public HistoryStringCollection BookmarkSearchHistory { get; } = new();
    public HistoryStringCollection BookHistorySearchHistory { get; } = new();
    public HistoryStringCollection PageListSearchHistory { get; } = new();
    public HistoryStringCollection BookshelfSearchHistory { get; } = new();
    public event EventHandler? BookmarkSearchHistoryChanged;
    public event EventHandler? BookHistorySearchHistoryChanged;
    public event EventHandler? Changed;
    /// <summary>仅书签编辑提交或回滚后回报；阅读进度保存不触发书签列表重排。</summary>
    public event EventHandler? BookmarksChanged;
    public string DirectoryPath { get; } = directory;
    /// <summary>文件动作检查的应用临时根；仍由唯一启动装配提供，不另存状态。</summary>
    internal string? TemporaryDirectoryPath => _temporaryDirectory;
    public PlaylistHub Playlists { get; private set; } = null!;
    public FolderConfigCollection FolderConfigs { get; } = new();
    public string? LastBookPath => _setting["Config"]?["StartUp"]?["LastBookV2"]?["Path"]?.GetValue<string>();
    /// <summary>原FirstLoader不恢复应用临时来源；历史写出采用同一明确目录边界。</summary>
    /// <param name="path">原书籍定位，不读取文件。</param><returns>仅应用临时根或其子路径为true。</returns>
    public bool IsTemporaryPath(string path) => _temporaryDirectory is not null &&
        (path == _temporaryDirectory || path.StartsWith(_temporaryDirectory + System.IO.Path.DirectorySeparatorChar, StringComparison.Ordinal));

    /// <summary>加载原命名文件并恢复 P1 已支持配置；损坏文件不覆盖。</summary>
    public async Task LoadAsync(CancellationToken token = default)
    {
        RecoverInterruptedSave();
        _setting = await ReadAsync("UserSetting.json", token);
        _history = await ReadAsync("History.json", token);
        _bookmarks = await ReadAsync("Bookmark.json", token);
        _quickAccess = await ReadAsync(QuickAccessCollection.FileName, token);
        QuickAccess.Restore(_quickAccess);
        Bookmarks = new(_bookmarks["Nodes"]?.Deserialize<BookmarkNode>(Options) ?? new() { Children = [] });
        _removedBookmarks = [];
        _suppressedHistoryPaths.Clear();
        _activeHistoryPath = null; _activeHistoryBook = null;
        if (!BookmarkRoot.IsFolder) throw new JsonException("Bookmark.Nodes 必须是根文件夹。");
        var config = ReadProfileConfig(_setting);
        config.Theme.DefaultFolder = System.IO.Path.Combine(DirectoryPath, "Themes");
        config.Playlist.DefaultFolder = System.IO.Path.Combine(DirectoryPath, "Playlists");
        Playlists = new(config.Playlist);
        Config.SetCurrent(config);
        FolderConfigs.Restore(await ReadAsync(FolderConfigCollection.FileName, token));
        // 原Restore(fromLoad:true)先按已加载配置限制；只改本次内存，不写回来源文件。
        if (_history["Items"] is JsonArray loadedItems)
            _history["Items"] = CreateLimitedHistoryItems(loadedItems, config.History, sort: false);
        RefreshHistory();
        BookmarkSearchHistory.Replace(_history["BookmarkSearchHistory"]?.Deserialize<string[]>(Options));
        BookHistorySearchHistory.Replace(_history["BookHistorySearchHistory"]?.Deserialize<string[]>(Options));
        PageListSearchHistory.Replace(_history["PageListSearchHistory"]?.Deserialize<string[]>(Options));
        BookshelfSearchHistory.Replace(_history["BookshelfSearchHistory"]?.Deserialize<string[]>(Options));
    }

    /// <summary>构造独立配置投影，加载和导入验证共用；不改变 Config.Current 或访问磁盘。</summary>
    /// <param name="setting">完整原 UserSetting 节点。</param><returns>使用原分支默认值的独立配置。</returns>
    private static Config ReadProfileConfig(JsonObject setting)
    {
        var raw = setting["Config"] as JsonObject;
        var config = new Config();
        if (raw is not null)
        {
            config.BookSetting = ReadBranch<BookSettingConfig>(raw, "BookSetting");
            config.BookSettingDefault = ReadBranch<BookSettingConfig>(raw, "BookSettingDefault");
            config.BookSettingPolicy = ReadBranch<BookSettingPolicyConfig>(raw, "BookSettingPolicy");
            config.Book = ReadBranch<BookConfig>(raw, "Book");
            config.View = ReadBranch<ViewConfig>(raw, "View");
            config.Panels = ReadPanelsBranch(raw);
            config.FilmStrip = ReadBranch<FilmStripConfig>(raw, "FilmStrip");
            config.Slider = ReadBranch<SliderConfig>(raw, "Slider");
            config.Bookshelf = ReadBranch<BookshelfConfig>(raw, "Bookshelf");
            config.PageList = ReadBranch<PageListConfig>(raw, "PageList");
            config.History = ReadBranch<HistoryConfig>(raw, "History");
            config.Bookmark = ReadBranch<BookmarkConfig>(raw, "Bookmark");
            config.System = ReadBranch<SystemConfig>(raw, "System");
            config.Archive = ReadBranch<ArchiveConfig>(raw, "Archive");
            config.Background = ReadBranch<BackgroundConfig>(raw, "Background");
            config.ImageDotKeep = ReadBranch<ImageDotKeepConfig>(raw, "ImageDotKeep");
            var customSize = raw["ImageCustomSize"]?.DeepClone().AsObject() ?? new();
            if (customSize.ContainsKey("AspectRatio")) customSize.Remove("IsUniformed");
            config.ImageCustomSize = customSize.Deserialize<ImageCustomSizeConfig>(ReadOptions) ?? new();
            config.ImageTrim = ReadBranch<ImageTrimConfig>(raw, "ImageTrim");
            config.ImageResizeFilter = ReadBranch<ImageResizeFilterConfig>(raw, "ImageResizeFilter");
            config.ImageGrid = ReadBranch<ImageGridConfig>(raw, "ImageGrid");
            config.ImageEffect = ReadBranch<ImageEffectConfig>(raw, "ImageEffect");
            config.ImageEffectCache = ReadBranch<EffectUnitCache>(raw, "ImageEffectCache");
            config.EffectProfiles = ReadBranch<EffectProfileCollectionConfig>(raw, "EffectProfiles");
            if (raw["EffectProfiles"] is null) new EffectProfileCollection(config).Store();
            config.Image = ReadBranch<ImageConfig>(raw, "Image");
            config.SlideShow = ReadSlideShowBranch(raw);
            config.Performance = ReadBranch<PerformanceConfig>(raw, "Performance");
            config.Playlist = ReadBranch<PlaylistConfig>(raw, "Playlist");
            config.Window = ReadBranch<WindowConfig>(raw, "Window");
            config.WindowTitle = ReadBranch<WindowTitleConfig>(raw, "WindowTitle");
            config.MenuBar = ReadBranch<MenuBarConfig>(raw, "MenuBar");
            config.Command = ReadBranch<CommandConfig>(raw, "Command");
            config.Mouse = ReadBranch<MouseConfig>(raw, "Mouse");
            config.Loupe = ReadBranch<LoupeConfig>(raw, "Loupe");
            config.StartUp = ReadBranch<StartUpConfig>(raw, "StartUp");
            config.Fonts = ReadBranch<FontsConfig>(raw, "Fonts");
            var theme = raw["Theme"]?.DeepClone().AsObject() ?? new JsonObject();
            // 明确的现代值优先，旧PanelColor仅作读取后备，避免顺序依赖。
            if (theme.ContainsKey("ThemeType")) theme.Remove("PanelColor");
            config.Theme = theme.Deserialize<ThemeConfig>(ReadOptions) ?? new();
            // 原旧拼写与初期 Mac 字段只作读取别名；原新字段明确存在时优先。
            var auto = raw["AutoHide"]?.DeepClone().AsObject() ?? new JsonObject();
            if (auto["AutoHideHitTestMargin"] is { } margin)
            {
                auto["AutoHideHitTestHorizontalMargin"] ??= margin.DeepClone();
                auto["AutoHideHitTestVerticalMargin"] ??= margin.DeepClone();
            }
            foreach (var part in new[] { "Top", "Bottom" })
                if (auto[$"AutoHideConflict{part}Margin"] is null && auto[$"AutoHideConfrict{part}Margin"] is { } legacy)
                    auto[$"AutoHideConflict{part}Margin"] = legacy.DeepClone();
            var autoOptions = new JsonSerializerOptions(Options);
            autoOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
            config.AutoHide = auto.Deserialize<AutoHideConfig>(autoOptions) ?? new();
            var panels = raw["Panels"] as JsonObject;
            if (panels?["IsHideLeftPanel"] is null && panels?["IsLeftAutoHide"] is JsonValue left) config.Panels.IsHideLeftPanel = left.GetValue<bool>();
            if (panels?["IsHideRightPanel"] is null && panels?["IsRightAutoHide"] is JsonValue right) config.Panels.IsHideRightPanel = right.GetValue<bool>();
            if (raw["MenuBar"]?["IsAddressBarEnabled"] is null && raw["IsAddressBarEnabled"] is JsonValue address) config.MenuBar.IsAddressBarEnabled = address.GetValue<bool>();
        }
        return config;
    }

    /// <summary>原书签搜索历史追加/删除；复用三文件事务，失败恢复同一集合供重试。</summary>
    /// <param name="keyword">已规范化的有效关键字；空白不登记。</param>
    /// <param name="remove">删除指定历史，而非追加至首位。</param><param name="token">窗口关闭或调用方取消。</param>
    public Task EditBookmarkSearchHistoryAsync(string keyword, bool remove = false, CancellationToken token = default) =>
        EditSearchHistoryAsync(BookmarkSearchHistory, keyword, remove, token, () => BookmarkSearchHistoryChanged?.Invoke(this, EventArgs.Empty));

    /// <summary>编辑原BookHistorySearchHistory；与书签表达式历史独立，使用同一可靠保存事务。</summary>
    /// <param name="keyword">已Trim的有效表达式。</param><param name="remove">删除指定表达式。</param><param name="token">排队/保存取消。</param>
    public Task EditBookHistorySearchHistoryAsync(string keyword, bool remove = false, CancellationToken token = default) =>
        EditSearchHistoryAsync(BookHistorySearchHistory, keyword, remove, token, () => BookHistorySearchHistoryChanged?.Invoke(this, EventArgs.Empty));
    /// <summary>原页面列表与书架的表达式历史，复用唯一保存事务及原地回滚。</summary>
    public Task EditPageListSearchHistoryAsync(string keyword, bool remove = false, CancellationToken token = default) =>
        EditSearchHistoryAsync(PageListSearchHistory, keyword, remove, token, () => Changed?.Invoke(this, EventArgs.Empty));
    public Task EditBookshelfSearchHistoryAsync(string keyword, bool remove = false, CancellationToken token = default) =>
        EditSearchHistoryAsync(BookshelfSearchHistory, keyword, remove, token, () => Changed?.Invoke(this, EventArgs.Empty));

    /// <summary>共用原字符串历史的追加/删除及原地回滚，避免复制第二套事务。</summary>
    /// <param name="history">本次模块的原集合。</param><param name="keyword">非空表达式。</param><param name="remove">删除开关。</param>
    /// <param name="token">等待与保存取消。</param><param name="notify">提交或回滚后的模块通知。</param>
    private async Task EditSearchHistoryAsync(HistoryStringCollection history, string keyword, bool remove, CancellationToken token, Action notify)
    {
        if (string.IsNullOrWhiteSpace(keyword)) return;
        await _gate.WaitAsync(token);
        var previous = history.ToArray(); var raw = _history.DeepClone().AsObject();
        try
        {
            if (remove) history.Remove(keyword); else history.Append(keyword);
            WriteSearchHistories(); await WritePairAsync(token);
        }
        catch { _history = raw; history.Replace(previous); throw; }
        finally { _gate.Release(); notify(); }
    }

    /// <summary>沿原统一保存开关写入书签、历史、页面和书架四类表达式历史；未知字段保留。</summary>
    private void WriteSearchHistories()
    {
        _history["BookmarkSearchHistory"] = Config.Current.History.IsKeepSearchHistory && BookmarkSearchHistory.Count > 0
            ? JsonSerializer.SerializeToNode(BookmarkSearchHistory, Options) : null;
        _history["BookHistorySearchHistory"] = Config.Current.History.IsKeepSearchHistory && BookHistorySearchHistory.Count > 0
            ? JsonSerializer.SerializeToNode(BookHistorySearchHistory, Options) : null;
        _history["PageListSearchHistory"] = Config.Current.History.IsKeepSearchHistory && PageListSearchHistory.Count > 0 ? JsonSerializer.SerializeToNode(PageListSearchHistory, Options) : null;
        _history["BookshelfSearchHistory"] = Config.Current.History.IsKeepSearchHistory && BookshelfSearchHistory.Count > 0 ? JsonSerializer.SerializeToNode(BookshelfSearchHistory, Options) : null;
    }

    /// <summary>按原 Path/Page/Props 恢复；Page 是条目名，不是数字页码。</summary>
    public BookMemento? Find(string path)
    {
        var entry = (_history["Items"] as JsonArray)?.OfType<JsonObject>().FirstOrDefault(e => e["Path"]?.GetValue<string>() == path);
        if (entry is null && BookmarkRoot.Walk().FirstOrDefault(e => e.Path == path) is { } bookmark)
            entry = JsonSerializer.SerializeToNode(bookmark, Options)!.AsObject();
        return ReadMemento(path, entry);
    }

    /// <summary>读取原启动快照；历史移除或存在旧记录时仍使用独立的 LastBookV2。</summary>
    /// <returns>最后书籍的原页面条目与设置；没有快照时返回空记录。</returns>
    public BookMemento? GetLastBook() => LastBookPath is { } path
        ? ReadMemento(path, _setting["Config"]?["StartUp"]?["LastBookV2"] as JsonObject) : null;

    /// <summary>共用原 Path/Page/Props 解析，不为启动恢复建立第二套状态模型。</summary>
    /// <param name="path">记录所指向的真实书籍来源。</param>
    /// <param name="entry">历史、书签或启动快照的只读 JSON 节点。</param>
    /// <returns>可恢复的原 memento；缺少页面字段时 memento 为空。</returns>
    private static BookMemento? ReadMemento(string path, JsonObject? entry)
    {
        if (entry is null) return null;
        var props = entry["Props"]?.GetValue<string>();
        // 新版未知 Props 仍保留在节点中，只将当前已识别 token 传入原解析器。
        var known = FilterProps(props, true);
        var memento = BookMemento.ParseWithProperties(path, entry["Page"]?.GetValue<string>(), known);
        // 原 Props 无法无歧义编码 IsWide=false；Mac 扩展仅补充这一缺失值，不改原解析器。
        if (memento is not null && entry["MacIsSupportedWidePage"] is JsonValue wide) memento.IsSupportedWidePage = wide.GetValue<bool>();
        // 原 BookMemento 只恢复条目名；旧 MacPagePart 不再参与半页选择。
        return memento;
    }

    /// <summary>读取差分命令键位；未配置时使用固定基线默认值。</summary>
    public string GetShortcut(string name, string fallback)
    {
        var item = _setting["Commands"]?[name];
        return item?["ShortCutKey"]?.GetValue<string>() ?? DefaultInputScheme.GetShortcut(name, fallback, Config.Current.Command);
    }

    /// <summary>原独立方向手势差分；空字符串解绑，缺省沿原方案配对。</summary>
    public MouseSequence GetMouseGesture(string name, string fallback) => new(_setting["Commands"]?[name]?["MouseGesture"]?.GetValue<string>()
        ?? DefaultInputScheme.GetMouseGesture(name, fallback, Config.Current.Command));
    /// <summary>只写修改的方向手势；默认值省略，未知参数和键位保留。</summary>
    public void SetMouseGestureDifference(string name, string value, string baseline)
    {
        var item = Object(Object(_setting, "Commands"), name);
        var normalized = new MouseSequence(value).ToString();
        if (normalized == DefaultInputScheme.GetMouseGesture(name, baseline, Config.Current.Command)) item.Remove("MouseGesture");
        else item["MouseGesture"] = normalized;
    }
    /// <summary>读取原滚动命令差分 Parameter；缺省使用原参数，未知字段不写回。</summary>
    public ScrollPageCommandParameter GetScrollParameter(string name)
    {
        return GetCommandParameter<ScrollPageCommandParameter>(name);
    }

    /// <summary>原相反方向的指定步长命令共享参数，NextSizePage 读取 PrevSizePage 的差分。</summary>
    public MoveSizePageCommandParameter GetMoveSizeParameter() => _setting["Commands"]?["PrevSizePage"]?["Parameter"]?.Deserialize<MoveSizePageCommandParameter>(Options) ?? new();

    /// <summary>读取原命令差分参数，缺失值用原默认值，兼容数值及字符串枚举。</summary>
    public T GetCommandParameter<T>(string name) where T : class, new()
    {
        var options = new JsonSerializerOptions(Options);
        options.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
        var owner = DefaultInputScheme.GetParameterOwner(name);
        // 早期Mac各方向独立写出的参数仅作读取兼容；原共享节点明确存在时优先。
        return (_setting["Commands"]?[owner]?["Parameter"] ?? _setting["Commands"]?[name]?["Parameter"])?.Deserialize<T>(options) ?? new();
    }
    /// <summary>九数字实例默认索引来自原构造器；差分未写Index时仍保留该默认值。</summary>
    public MoveToFolderAsCommandParameter GetDestinationParameter(string name)
    {
        int index = name.StartsWith("MoveToDestinationFolder", StringComparison.Ordinal) && int.TryParse(name["MoveToDestinationFolder".Length..], out var number) ? number : 0;
        var defaults = JsonSerializer.SerializeToNode(new MoveToFolderAsCommandParameter { Index = index }, Options)!.AsObject();
        if (_setting["Commands"]?[name]?["Parameter"] is JsonObject raw) Merge(defaults, raw);
        var options = new JsonSerializerOptions(Options); options.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
        return defaults.Deserialize<MoveToFolderAsCommandParameter>(options)!;
    }

    /// <summary>更新已支持参数并保留原节点的未知字段；原差分快捷键不会被覆盖。</summary>
    public void SetCommandParameter<T>(string name, T value)
    {
        var command = Object(Object(_setting, "Commands"), DefaultInputScheme.GetParameterOwner(name));
        var parameter = Object(command, "Parameter");
        // 原差分写出不再包含只读兼容值。直接递归Merge会留下旧setter字段，重启时覆盖新ScrollType/停顿。
        if (value is ScrollPageCommandParameter)
            foreach (var field in new[] { "IsNScroll", "PageMoveMargin" })
                foreach (var key in parameter.Select(pair => pair.Key).Where(key => key.Equals(field, StringComparison.OrdinalIgnoreCase)).ToArray())
                {
                    Object(command, "MacImportedLegacyParameterFields")[key] = parameter[key]?.DeepClone();
                    parameter.Remove(key);
                }
        MergeTyped(parameter, value!);
    }

    /// <summary>编辑原 Commands 差分键位；空字符串表示解绑，未知参数保持。</summary>
    public void SetShortcut(string name, string value) => Object(Object(_setting, "Commands"), name)["ShortCutKey"] = value;

    /// <summary>输入表单专用回滚点；原命令参数/未知字段和配置仍属于唯一JSON链。</summary>
    public sealed class CommandSettingsSnapshot
    {
        internal JsonNode? Commands { get; init; }
        internal CommandConfig Config { get; init; } = null!;
    }
    /// <summary>在表单应用前保留输入字段，保存失败后可取消或同草稿重试。</summary>
    public CommandSettingsSnapshot CaptureCommandSettings() => new() { Commands = _setting["Commands"]?.DeepClone(), Config = Config.Current.Command };
    /// <summary>等待此前已进入保存事务的写入/取消清理完成，表单随后才能拍快照及编辑。</summary>
    public async Task SynchronizeWritesAsync()
    { await _gate.WaitAsync(); _gate.Release(); }
    /// <summary>恢复输入编辑前的原节点；不读取文件，也不修改其他配置。</summary>
    public void RestoreCommandSettings(CommandSettingsSnapshot snapshot)
    { _setting["Commands"] = snapshot.Commands?.DeepClone(); Config.Current.Command = snapshot.Config; }
    /// <summary>保存方案的已改键位；值等于该方案默认时恢复差分省略，未知参数保留。</summary>
    public void SetShortcutDifference(string name, string value, string baseline)
    {
        var item = Object(Object(_setting, "Commands"), name);
        if (value == DefaultInputScheme.GetShortcut(name, baseline, Config.Current.Command)) item.Remove("ShortCutKey");
        else item["ShortCutKey"] = value;
    }

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
        finally { _gate.Release(); Changed?.Invoke(this, EventArgs.Empty); BookmarksChanged?.Invoke(this, EventArgs.Empty); }
    }
    /// <summary>定位父节点后移除，禁止移除根目录或不属于当前树的旧节点。</summary>
    private void RemoveNode(BookmarkNode node)
    {
        Bookmarks.Remove(node);
    }

    /// <summary>在历史编辑锁内提交真实新书，再解除登记抑制，避免取消或清空与新访问交错。</summary>
    /// <param name="commit">原打开流程的最后代次检查与书籍赋值；返回 false 表示未提交。</param>
    /// <returns>书籍及新访问是否同时提交；没有额外文件写入。</returns>
    internal async Task<bool> BeginHistoryVisitAsync(Book book, Func<bool> commit)
    {
        await _gate.WaitAsync();
        try
        {
            if (!commit()) return false;
            _suppressedHistoryPaths.Remove(book.Path); _activeHistoryPath = book.Path; _activeHistoryBook = book; return true;
        }
        finally { _gate.Release(); }
    }

    /// <summary>移除指定历史记录，沿用原 BookHistoryCollection.Remove；不删除源文件或书签。</summary>
    /// <param name="paths">选中批次的书籍路径，精确比较，不将路径统一转小写。</param>
    /// <returns>实际删除数；失败恢复原 JSON、抑制记录及可重试状态。</returns>
    public Task<int> RemoveHistoryAsync(IEnumerable<string> paths, CancellationToken token = default) =>
        EditHistoryAsync(paths.ToHashSet(StringComparer.Ordinal), token);

    /// <summary>清空全部访问记录，保留搜索历史与未知根字段；不受界面过滤限制。</summary>
    public Task<int> ClearHistoryAsync(CancellationToken token = default) => EditHistoryAsync(null, token);

    /// <summary>串行编辑原目录参数并提交统一文件事务；失败恢复集合供同路径重试。</summary>
    /// <param name="edit">仅修改目录参数/现有书架排序，不进行外部I/O。</param><param name="token">准备阶段取消。</param>
    public async Task EditFolderParametersAsync(Action edit, CancellationToken token = default)
    {
        await _gate.WaitAsync(token);
        var previous = FolderConfigs.CreateMemento(forSave: false);
        try { edit(); await WritePairAsync(token); }
        catch { FolderConfigs.Restore(previous); throw; }
        finally { _gate.Release(); }
    }

    /// <summary>串行修改历史与三文件事务；选中删除和清空使用同一失败回滚。</summary>
    private async Task<int> EditHistoryAsync(HashSet<string>? paths, CancellationToken token, IReadOnlyDictionary<string, string>? expected = null)
    {
        await _gate.WaitAsync(token);
        var previous = _history.DeepClone().AsObject();
        var suppressed = _suppressedHistoryPaths.ToArray();
        try
        {
            var items = _history["Items"] as JsonArray;
            var removed = items?.OfType<JsonObject>().Where(item => item["Path"] is JsonValue value &&
                (paths is null || paths.Contains(value.GetValue<string>())) &&
                (expected is null || expected.TryGetValue(value.GetValue<string>(), out var version) && item.ToJsonString() == version)).ToArray() ?? [];
            if (removed.Length == 0 && paths is not null) return 0;
            // 首次打开还未到防抖保存时也允许清空；不能让晚到的当前书保存重新登记。
            if (paths is null && _activeHistoryPath is not null) _suppressedHistoryPaths.Add(_activeHistoryPath);
            foreach (var item in removed)
            { _suppressedHistoryPaths.Add(item["Path"]!.GetValue<string>()); items!.Remove(item); }
            await WritePairAsync(token);
            if (_activeHistoryBook is { } current && (paths is null || removed.Any(e => e["Path"]!.GetValue<string>() == current.Path))) current.MementoControl.OnHistoryRemoved();
            RefreshHistory(); return removed.Length;
        }
        catch
        {
            _history = previous; _suppressedHistoryPaths.Clear(); _suppressedHistoryPaths.UnionWith(suppressed);
            throw;
        }
        finally { _gate.Release(); Changed?.Invoke(this, EventArgs.Empty); }
    }

    /// <summary>生成按访问时间倒序的只读历史；未知字段仍保留在权威 JSON 中。</summary>
    private void RefreshHistory() => HistoryEntries = (_history["Items"] as JsonArray)?.OfType<JsonObject>()
        .Where(e => e["Path"] is JsonValue).Select(e => new HistoryEntry(e["Path"]!.GetValue<string>(), e["Page"]?.GetValue<string>(),
            e["LastAccessTime"]?.GetValue<DateTime>() ?? DateTime.MinValue)).OrderByDescending(e => e.LastAccessTime).ToArray() ?? [];

    /// <summary>保存阅读状态与布局，已知字段合并进原节点后原子替换文件。</summary>
    /// <param name="historyLimits">设置表单的可选数量/期限副本；事务成功后才更新运行配置。</param>
    public async Task SaveAsync(Book? book, CancellationToken token = default, bool keepHistoryOrder = false,
        (int Size, TimeSpan Span)? historyLimits = null, bool clearLastBook = false)
    {
        await _gate.WaitAsync(token);
        var previousSetting = _setting.DeepClone().AsObject();
        var previousHistory = _history.DeepClone().AsObject();
        var historyConfig = JsonSerializer.Deserialize<HistoryConfig>(JsonSerializer.Serialize(Config.Current.History, Options), Options)!;
        if (historyLimits is { } limits) { historyConfig.LimitSize = limits.Size; historyConfig.LimitSpan = limits.Span; }
        bool historyEntry = false;
        try
        {
            new EffectProfileCollection(Config.Current).Store();
            var config = Object(_setting, "Config");
            foreach (var branch in new[] { "BookSetting", "BookSettingDefault", "BookSettingPolicy", "Book", "View", "Panels", "FilmStrip", "Slider", "Bookshelf", "PageList", "History", "Bookmark", "System", "Archive", "Background", "ImageDotKeep", "ImageCustomSize", "ImageTrim", "ImageGrid", "ImageEffect", "EffectProfiles", "Image", "SlideShow", "Performance", "Playlist", "AutoHide", "Window", "WindowTitle", "MenuBar", "Command", "Mouse", "Loupe", "StartUp", "Theme", "Fonts" })
            {
                var value = typeof(Config).GetProperty(branch)!.GetValue(Config.Current);
                MergeTyped(Object(config, branch), value!);
            }
            Object(config, "ImageCustomSize").Remove("IsUniformed");
            config["ImageEffectCache"] = JsonSerializer.SerializeToNode(Config.Current.ImageEffectCache, Options);
            MergeTyped(Object(config, "ImageResizeFilter"), Config.Current.ImageResizeFilter);
            // 仅覆盖本次编辑的两字段；配置在等待/失败时不暴露给防抖和其他保存。
            Object(config, "History")["LimitSize"] = historyConfig.LimitSize;
            Object(config, "History")["LimitSpan"] = JsonSerializer.SerializeToNode(historyConfig.LimitSpan, Options);
            // 退役的 Mac 别名统一归入原字段，避免下一次加载出现两套相反的权威值。
            Object(config, "Panels").Remove("IsLeftAutoHide"); Object(config, "Panels").Remove("IsRightAutoHide");
            Object(config, "System").Remove("DestinationFodlerCollection");
            config.Remove("IsAddressBarEnabled");
            Object(config, "View").Remove("ViewOrigin");
            if (book is not null)
            {
                var memento = book.CreateMemento();
                var items = _history["Items"] as JsonArray ?? new JsonArray();
                _history["Items"] = items;
                var old = items.OfType<JsonObject>().FirstOrDefault(e => e["Path"]?.GetValue<string>() == book.Path);
                var item = old?.DeepClone().AsObject() ?? new JsonObject();
                var unknownProps = FilterProps(item["Props"]?.GetValue<string>(), false);
                item["Path"] = book.Path; item["Page"] = memento.Page;
                item["Props"] = string.Join(' ', new[] { memento.ToPropertiesString(), unknownProps }.Where(e => !string.IsNullOrEmpty(e)));
                keepHistoryOrder &= !historyConfig.IsForceUpdateHistory;
                if (!keepHistoryOrder || old is null) item["LastAccessTime"] = DateTime.Now;
                // 收回早期 Mac 半页扩展，打开与原版一样从阅读方向的首半页开始。
                item.Remove("MacPagePart");
                item["MacIsSupportedWidePage"] = memento.IsSupportedWidePage;
                // 原 KeepHistoryOrder：阅读位置仍更新，重放不改变访问排序或原数组位置。
                if (book.MementoControl.CanHistory(historyConfig) && (!_suppressedHistoryPaths.Contains(book.Path)
                    || ReferenceEquals(book, _activeHistoryBook) && !book.MementoControl.IsHistoryRemoved))
                {
                    if (old is not null) { var oldIndex = items.IndexOf(old); items.Remove(old); items.Insert(keepHistoryOrder ? oldIndex : 0, item); }
                    else items.Insert(0, item);
                    historyEntry = true;
                }
                // 同一本书的启动快照保留未知字段/Props；切书不把旧书扩展带入新书。
                // LastBook 独立于可移除的历史记录，半页扩展只退役已知的 MacPagePart。
                var startup = Object(config, "StartUp");
                var previousLastBook = startup["LastBookV2"] as JsonObject;
                var lastBook = previousLastBook?["Path"]?.GetValue<string>() == book.Path
                    ? previousLastBook.DeepClone().AsObject() : new JsonObject();
                var lastUnknownProps = FilterProps(lastBook["Props"]?.GetValue<string>(), false);
                lastBook["Path"] = book.Path; lastBook["Page"] = memento.Page;
                lastBook["Props"] = string.Join(' ', (item["Props"]!.GetValue<string>() + " " + lastUnknownProps)
                    .Split(' ', StringSplitOptions.RemoveEmptyEntries).Distinct(StringComparer.Ordinal));
                lastBook["MacIsSupportedWidePage"] = memento.IsSupportedWidePage;
                lastBook.Remove("MacPagePart"); startup["LastBookV2"] = lastBook;
            }
            if (clearLastBook) Object(config, "StartUp").Remove("LastBookV2");
            // 保留原 Format；新文件使用原名称和版本结构，避免添加另一套存储格式。
            _setting["Format"] ??= JsonValue.Create("NeeView.UserSetting/46.3.0");
            _history["Format"] ??= JsonValue.Create("NeeView.History/46.3.0");
            WriteSearchHistories();
            await WritePairAsync(token, historyConfig);
            if (historyLimits.HasValue)
            {
                Config.Current.History.LimitSize = historyConfig.LimitSize;
                Config.Current.History.LimitSpan = historyConfig.LimitSpan;
            }
            if (clearLastBook) { _activeHistoryBook = null; _activeHistoryPath = null; }
            if (historyEntry && book is not null) { book.MementoControl.CommitHistoryEntry(); _suppressedHistoryPaths.Remove(book.Path); }
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
    private static T ReadBranch<T>(JsonObject root, string name) where T : class, new() => root[name]?.Deserialize<T>(ReadOptions) ?? new();
    /// <summary>原四种Profile各有构造默认值；差分字段在对应模板默认副本上合并，不退回空Profile默认。</summary>
    private static PanelsConfig ReadPanelsBranch(JsonObject root)
    {
        var defaults = JsonSerializer.SerializeToNode(new PanelsConfig(), Options)!.AsObject();
        if (root["Panels"] is JsonObject raw) Merge(defaults, raw);
        return defaults.Deserialize<PanelsConfig>(ReadOptions)!;
    }
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
    /// <summary>先准备原四个JSON文件，再保留回滚副本；中断恢复旧完整状态。</summary>
    /// <param name="token">准备阶段取消；提交开始后完成或回滚。</param>
    /// <param name="historyConfig">本次设置副本；其他调用使用已提交配置。</param>
    private async Task WritePairAsync(CancellationToken token, HistoryConfig? historyConfig = null)
    {
        Directory.CreateDirectory(DirectoryPath);
        string[] names = ["History.json", "UserSetting.json", "Bookmark.json", FolderConfigCollection.FileName, QuickAccessCollection.FileName];
        var marker = System.IO.Path.Combine(DirectoryPath, ".save-pending.json");
        try
        {
            // 准备阶段不改动权威文件；任一序列化或权限失败均可直接重试。
            // 原CreateMemento只限制写出的副本；本进程集合、Find和历史导航保持完整。
            var preparedHistory = _history.DeepClone().AsObject();
            if (_history["Items"] is JsonArray items)
                preparedHistory["Items"] = CreateLimitedHistoryItems(items, historyConfig ?? Config.Current.History, sort: true);
            var saveHistory = (historyConfig ?? Config.Current.History).IsSaveHistory;
            if (saveHistory) await WriteTemporaryAsync(names[0], preparedHistory, token);
            await WriteTemporaryAsync(names[1], CreateSettingMemento(), token);
            // 在副本上准备书签文件，提交失败不能污染权威内存节点。
            var preparedBookmarks = _bookmarks.DeepClone().AsObject();
            preparedBookmarks["Format"] ??= JsonValue.Create("NeeView.Bookmark/46.3.0");
            preparedBookmarks["Nodes"] = JsonSerializer.SerializeToNode(BookmarkRoot, Options);
            await WriteTemporaryAsync(names[2], preparedBookmarks, token);
            await WriteTemporaryAsync(names[3], FolderConfigs.CreateMemento(), token);
            var preparedQuickAccess = QuickAccess.CreateMemento(_quickAccess);
            await WriteTemporaryAsync(names[4], preparedQuickAccess, token);
            await CommitTemporaryFilesAsync(names, saveHistory ? [] : new HashSet<string> { "History.json" }, token);
            _bookmarks = preparedBookmarks;
            _quickAccess = preparedQuickAccess;
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
    /// <summary>统一文件提交原语，普通保存、导入和备份恢复共用同一 marker 格式。</summary>
    /// <param name="names">已准备临时文件的受限 Profile 名称。</param>
    /// <param name="deleted">事务明确删除的文件；缺省文件不会误当作删除。</param>
    /// <param name="token">提交标记前可取消，标记之后完成或回滚。</param>
    private async Task CommitTemporaryFilesAsync(IReadOnlyList<string> names, IReadOnlySet<string> deleted, CancellationToken token)
    {
        var marker = System.IO.Path.Combine(DirectoryPath, ".save-pending.json");
        var previous = new JsonObject();
        var targets = names.ToDictionary(name => name, name => ProfileImportAssets.ResolveTarget(DirectoryPath, name));
        ProfileImportAssets.RejectLink(marker); ProfileImportAssets.RejectLink(marker + ".tmp");
        foreach (var name in names)
        {
            token.ThrowIfCancellationRequested();
            var path = targets[name];
            previous[name] = File.Exists(path);
            if (File.Exists(path)) File.Copy(path, path + ".save-backup", true);
        }
        await WriteTemporaryAsync(".save-pending.json", previous, token);
        token.ThrowIfCancellationRequested();
        File.Move(marker + ".tmp", marker, true);
        foreach (var name in names)
        {
            var path = targets[name];
            if (deleted.Contains(name)) File.Delete(path);
            else File.Move(path + ".tmp", path, true);
        }
        File.Delete(marker);
    }

    /// <summary>共用原Limit生成JSON副本，保留幸存条目的Page/Props与未知字段。</summary>
    /// <param name="items">权威运行集合或原磁盘序列。</param><param name="config">原数量/期限配置。</param>
    /// <param name="sort">保存按原倒序；加载遵循原文件已排序约定。</param>
    /// <returns>不与原节点共享父级的保留数组。</returns>
    private JsonArray CreateLimitedHistoryItems(JsonArray items, HistoryConfig config, bool sort)
    {
        var source = items.OfType<JsonObject>();
        if (sort)
        {
            // 原CreateMemento在数量限制前排除应用临时来源，保留同前缀的普通用户目录。
            if (_temporaryDirectory is not null) source = source.Where(item => item["Path"]?.GetValue<string>() is not { } path || !IsTemporaryPath(path));
            source = source.OrderByDescending(item => item["LastAccessTime"]?.GetValue<DateTime>() ?? DateTime.MinValue);
        }
        return new JsonArray(BookHistoryCollection.Limit(source, config.LimitSize, config.LimitSpan,
            item => item["LastAccessTime"]?.GetValue<DateTime>() ?? DateTime.MinValue).Select(item => item.DeepClone()).ToArray());
    }
    /// <summary>恢复未完成的保存，兼容旧双文件标记；损坏标记保留恢复材料。</summary>
    private void RecoverInterruptedSave()
    {
        var marker = System.IO.Path.Combine(DirectoryPath, ".save-pending.json");
        ProfileImportAssets.RejectLink(marker);
        if (!File.Exists(marker)) return;
        var previous = JsonNode.Parse(File.ReadAllText(marker))!.AsObject();
        if (previous.Count > ProfileImportFiles.MaxEntries) throw new InvalidDataException("恢复清单数量超限。");
        if (previous.Select(p => ProfileImportAssets.CollisionKey(p.Key)).Distinct().Count() != previous.Count) throw new InvalidDataException("恢复清单文件名冲突。");
        // 验证完整清单后才恢复；兼容原双/三/四/五文件 marker，没有第二恢复协议。
        var targets = previous.ToDictionary(pair => pair.Key, pair => (Path: ProfileImportAssets.ResolveTarget(DirectoryPath, pair.Key), Exists: pair.Value!.GetValue<bool>()));
        foreach (var pair in targets)
        {
            var path = pair.Value.Path;
            if (pair.Value.Exists) File.Copy(path + ".save-backup", path, true);
            else if (File.Exists(path)) File.Delete(path);
        }
        File.Delete(marker);
    }
    /// <summary>写入同目录临时 JSON 并刷新到磁盘；不替换权威文件。</summary>
    private async Task WriteTemporaryAsync(string name, JsonObject value, CancellationToken token)
    {
        // 两个内部marker由既有保存/重命名调用方给出，不能作为附属导入/恢复清单目标。
        var path = name is ".save-pending.json" or ".book-rename-pending.json" ? System.IO.Path.Combine(DirectoryPath, name) : ProfileImportAssets.ResolveTarget(DirectoryPath, name);
        ProfileImportAssets.RejectLink(DirectoryPath); ProfileImportAssets.RejectLink(path + ".tmp");
        var temp = path + ".tmp";
        await using var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous);
        await JsonSerializer.SerializeAsync(stream, value, Options, token);
        await stream.FlushAsync(token); stream.Flush(true);
    }
}

/// <summary>原 History.Items 的只读投影；位置仍为条目名，路径是书籍定位。</summary>
public sealed record HistoryEntry(string Path, string? Page, DateTime LastAccessTime)
{
    public string Name => LoosePath.GetFileName(Path);
}
