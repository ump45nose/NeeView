using System.Reflection;
using System.Text.Json;
namespace NeeView;

/// <summary>固定 Windows 命令元数据；暂未迁移命令保留名称和默认输入。</summary>
public sealed record CommandDefinition(string Name, string Text, string Shortcut, string Source, string Stage, string? MenuText = null);

/// <summary>原命令表的 P1 登记，菜单和输入使用同一命令标识。</summary>
public sealed class CommandTable
{
    private readonly Dictionary<string, Func<Task>> _actions = [];
    public IReadOnlyList<CommandDefinition> Definitions { get; }
    /// <summary>原普通书架排序命令；执行与菜单勾选共用，其他来源能力后续扩展。</summary>
    public static IReadOnlyDictionary<string, FolderOrder> BookOrderCommands { get; } = new Dictionary<string, FolderOrder>
    {
        ["SetBookOrderByFileNameA"] = FolderOrder.FileName, ["SetBookOrderByFileNameD"] = FolderOrder.FileNameDescending,
        ["SetBookOrderByFileTypeA"] = FolderOrder.FileType, ["SetBookOrderByFileTypeD"] = FolderOrder.FileTypeDescending,
        ["SetBookOrderByTimeStampA"] = FolderOrder.TimeStamp, ["SetBookOrderByTimeStampD"] = FolderOrder.TimeStampDescending,
        ["SetBookOrderBySizeA"] = FolderOrder.Size, ["SetBookOrderBySizeD"] = FolderOrder.SizeDescending,
        ["SetBookOrderByRandom"] = FolderOrder.Random
    };
    /// <summary>装配原阅读命令及已迁移设置命令；没有实现的命令不可执行。</summary>
    public CommandTable(BookOperation operation)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("NeeView.Command.command-manifest.json")!;
        Definitions = JsonSerializer.Deserialize<List<CommandDefinition>>(stream)!;
        // 来源：NextPage/PrevPage/NextOnePage/PrevOnePageCommand.Execute，保留帧与单页之别。
        _actions["NextPage"] = () => operation.MoveAsync(1);
        _actions["PrevPage"] = () => operation.MoveAsync(-1);
        _actions["NextOnePage"] = () => operation.MoveAsync(1, true);
        _actions["Unload"] = () => operation.UnloadAsync();
        _actions["ToggleBookLock"] = () => { operation.SetBookLock(!operation.IsBookLocked); return Task.CompletedTask; };
        _actions["TogglePageOrientation"] = () => operation.SetOrientationAsync(Config.Current.Book.Orientation.GetToggle());
        _actions["SetPageOrientationHorizontal"] = () => operation.SetOrientationAsync(PageFrameOrientation.Horizontal);
        _actions["SetPageOrientationVertical"] = () => operation.SetOrientationAsync(PageFrameOrientation.Vertical);
        _actions["PrevOnePage"] = () => operation.MoveAsync(-1, true);
        _actions["FirstPage"] = () => operation.JumpAsync(0);
        _actions["LastPage"] = () => operation.JumpAsync((operation.Book?.Pages.Count ?? 1) - 1, true);
        _actions["PrevHistoryPage"] = () => operation.NavigateHistoryAsync(-1);
        _actions["NextHistoryPage"] = () => operation.NavigateHistoryAsync(1);
        _actions["PrevBookHistory"] = () => operation.NavigateHistoryAsync(-1, true);
        _actions["NextBookHistory"] = () => operation.NavigateHistoryAsync(1, true);
        _actions["PrevHistory"] = () => operation.MoveHistoryListAsync(-1);
        _actions["NextHistory"] = () => operation.MoveHistoryListAsync(1);
        _actions["PrevBook"] = () => operation.MoveBookAsync(-1);
        _actions["NextBook"] = () => operation.MoveBookAsync(1);
        _actions["RandomBook"] = operation.RandomBookAsync;
        _actions["MoveToParentBook"] = operation.MoveToParentBookAsync;
        _actions["MoveToChildBook"] = operation.MoveToChildBookAsync;
        _actions["ToggleIsRecursiveFolder"] = operation.ToggleRecursiveFolderAsync;
        _actions["ToggleBookOrder"] = operation.ToggleFolderOrderAsync;
        foreach (var pair in BookOrderCommands) _actions[pair.Key] = () => operation.ChangeFolderOrderAsync(pair.Value);
        _actions["PrevFolderPage"] = () => operation.MoveFolderPageAsync(-1);
        _actions["NextFolderPage"] = () => operation.MoveFolderPageAsync(1);
        _actions["TogglePlaylistItem"] = () => operation.TogglePlaylistItemAsync();
        _actions["PrevPlaylistItemInBook"] = () => operation.MovePlaylistItemInBookAsync(-1);
        _actions["NextPlaylistItemInBook"] = () => operation.MovePlaylistItemInBookAsync(1);
        _actions["PrevPlaylistItem"] = () => operation.MovePlaylistItemAsync(-1);
        _actions["NextPlaylistItem"] = () => operation.MovePlaylistItemAsync(1);
        _actions["PrevPlaylist"] = () => operation.Playlists.MovePlaylistAsync(-1);
        _actions["NextPlaylist"] = () => operation.Playlists.MovePlaylistAsync(1);
        _actions["SetPageModeOne"] = () => operation.ApplySettingAsync(e => e.PageMode = PageMode.SinglePage);
        _actions["SetPageModeTwo"] = () => operation.ApplySettingAsync(e => e.PageMode = PageMode.WidePage);
        _actions["TogglePageMode"] = () => operation.ApplySettingAsync(e => e.PageMode = e.PageMode.GetToggle(1, true));
        _actions["SetBookReadOrderRight"] = () => operation.ApplySettingAsync(e => e.BookReadOrder = PageReadOrder.RightToLeft);
        _actions["SetBookReadOrderLeft"] = () => operation.ApplySettingAsync(e => e.BookReadOrder = PageReadOrder.LeftToRight);
        _actions["ToggleBookReadOrder"] = () => operation.ApplySettingAsync(e => e.BookReadOrder = e.BookReadOrder.GetToggle());
        _actions["ToggleIsSupportedDividePage"] = () => operation.ApplySettingAsync(e => e.IsSupportedDividePage = !e.IsSupportedDividePage);
        _actions["ToggleIsSupportedWidePage"] = () => operation.ApplySettingAsync(e => e.IsSupportedWidePage = !e.IsSupportedWidePage);
        _actions["ToggleIsSupportedSingleFirstPage"] = () => operation.ApplySettingAsync(e => e.IsSupportedSingleFirstPage = !e.IsSupportedSingleFirstPage);
        _actions["ToggleIsSupportedSingleLastPage"] = () => operation.ApplySettingAsync(e => e.IsSupportedSingleLastPage = !e.IsSupportedSingleLastPage);
        _actions["SetSortModeFileName"] = () => operation.ApplySettingAsync(e => e.SortMode = PageSortMode.FileName);
        _actions["SetSortModeFileNameDescending"] = () => operation.ApplySettingAsync(e => e.SortMode = PageSortMode.FileNameDescending);
        _actions["SetSortModeTimeStamp"] = () => operation.ApplySettingAsync(e => e.SortMode = PageSortMode.TimeStamp);
        _actions["SetSortModeSize"] = () => operation.ApplySettingAsync(e => e.SortMode = PageSortMode.Size);
        _actions["SetSortModeRandom"] = () => operation.ApplySettingAsync(e => e.SortMode = PageSortMode.Random);
        _actions["SetSortModeTimeStampDescending"] = () => operation.ApplySettingAsync(e => e.SortMode = PageSortMode.TimeStampDescending);
        _actions["SetSortModeSizeDescending"] = () => operation.ApplySettingAsync(e => e.SortMode = PageSortMode.SizeDescending);
        // AutoRotate 的原判断仍在 PageFrameFactory；命令只切换原枚举。
        foreach (var rotate in new[] { AutoRotateType.Left, AutoRotateType.Right, AutoRotateType.ForcedLeft, AutoRotateType.ForcedRight })
            _actions["ToggleIsAutoRotate" + rotate] = () => operation.ApplySettingAsync(e => e.AutoRotate = e.AutoRotate == rotate ? AutoRotateType.None : rotate);
    }
    /// <summary>返回命令是否已迁移；不能用空实现冒充可执行命令。</summary>
    public bool IsAvailable(string name) => _actions.ContainsKey(name);
    /// <summary>异步执行已登记命令，未知或未迁移命令返回能力错误。</summary>
    public Task ExecuteAsync(string name) => _actions.TryGetValue(name, out var action) ? action() : throw new NotSupportedException($"命令 {name} 尚未迁移。");
}
