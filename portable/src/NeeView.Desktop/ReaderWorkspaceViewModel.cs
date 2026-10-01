using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using NeeView.Application;
using NeeView.Core;

namespace NeeView.Desktop;

/// <summary>界面交互端口；视图模型不引用窗口、控件或文件选择器。</summary>
public interface IReaderDialogs
{
    Task<string?> PickFileAsync();
    /// <summary>选择目标目录，取消返回空。</summary>
    Task<string?> PickFolderAsync();
    Task<string?> TextAsync(string title, string value, bool multiline = false);
    Task<bool> ConfirmAsync(string title, string description, string accept);
}

/// <summary>阅读工作区的表现状态和命令；页面布局可独立替换，不把业务流程放入窗口。</summary>
public sealed partial class ReaderWorkspaceViewModel : ObservableObject
{
    private readonly IReaderSession _session;
    private readonly ISettingsStore _settingsStore;
    private readonly IReaderStateStore _states;
    private readonly IFileActionService _files;
    private readonly IDestinationFolderService _destinations;
    private readonly IPlatformService _platform;
    private readonly IFolderNavigator _folders;
    private string? _status;
    public string? Status { get => _status; private set => SetProperty(ref _status, value); }
    public AppSettings Settings { get; private set; } = new();
    public IReaderDialogs Dialogs { get; set; } = null!;
    public event Func<Task>? PanelsChanged;
    public event Action? SettingsChanged;
    public event Action? CloseRequested;
    public event Action? QuitRequested;
    /// <summary>注入应用契约；可使用假对话框进行无需 Avalonia 的命令测试。</summary>
    public ReaderWorkspaceViewModel(IReaderSession session, ISettingsStore settings, IReaderStateStore states,
        IFileActionService files, IDestinationFolderService destinations, IPlatformService platform, IFolderNavigator folders)
    { _session = session; _settingsStore = settings; _states = states; _files = files; _destinations = destinations; _platform = platform; _folders = folders; }
    /// <summary>重新载入设置并应用共享移动历史容量。</summary>
    public async Task ReloadSettingsAsync() { Settings = await _settingsStore.LoadAsync(); _files.Capacity = Settings.MoveHistoryCapacity; SettingsChanged?.Invoke(); }
    /// <summary>输入字段更新器，在存储互斥内合并最新设置，避免覆盖 LastSource。</summary>
    public async Task UpdateSettingsAsync(Func<AppSettings, AppSettings> update) { Settings = await _settingsStore.UpdateAsync(update); SettingsChanged?.Invoke(); }
    /// <summary>统一打开来源；错误由会话快照呈现。</summary>
    public Task OpenAsync(string path) => _session.OpenAsync(new(path));
    /// <summary>刷新数据面板由视图自行处理，命令不依赖面板结构。</summary>
    private async Task RefreshPanelsAsync()
    { if (PanelsChanged is { } handlers) foreach (Func<Task> handler in handlers.GetInvocationList()) await handler(); }
    /// <summary>视图异步错误进入表现状态，不从 async void 事件退出进程。</summary>
    public void ReportError(Exception error) => Status = ReaderException.From(error).Message;
    /// <summary>解析原 fork 的 Index 参数；无参数时使用命令本身的默认数字。</summary>
    private static int DestinationIndex(string? parameter, int fallback)
    {
        if (int.TryParse(parameter, out var index)) return Math.Clamp(index, 1, 9);
        if (parameter is not null)
        {
            try { using var json = JsonDocument.Parse(parameter); if (json.RootElement.TryGetProperty("Index", out var value) && value.TryGetInt32(out index)) return Math.Clamp(index, 1, 9); }
            catch (JsonException) { }
        }
        return fallback;
    }
    /// <summary>执行稳定命令；系统命令与阅读/文件目标分开。</summary>
    public async Task ExecuteAsync(string command, string? parameter = null)
    {
        try
        {
            var snapshot = _session.Snapshot; var options = snapshot.Options;
            var resolved = CommandCatalog.Resolve(command);
            switch (resolved)
            {
                case "NextPage": case "MoveNext": await _session.NavigateAsync(1); break;
                case "PrevPage": case "MovePrev": await _session.NavigateAsync(-1); break;
                case "NextOnePage": await _session.NavigateAsync(1, true); break;
                case "PrevOnePage": await _session.NavigateAsync(-1, true); break;
                case "FirstPage": if (snapshot.Index?.Pages.FirstOrDefault() is { } first) await _session.LocateAsync(new(first.Id)); break;
                case "LastPage": if (snapshot.Index?.Pages.LastOrDefault() is { } last) await _session.LocateAsync(new(last.Id)); break;
                case "Open": if (await Dialogs.PickFileAsync() is { } file) await OpenAsync(file); break;
                case "Paged": case "Continuous": case "Masonry": await _session.SetOptionsAsync(options with { Mode = Enum.Parse<ReaderMode>(command) }); break;
                case "ToggleDouble": await _session.SetOptionsAsync(options with { DoublePage = !options.DoublePage }); break;
                case "SetPageModeOne": case "SetPageModeTwo": await _session.SetOptionsAsync(options with { DoublePage = command == "SetPageModeTwo" }); break;
                case "SetBookReadOrderLeft": case "SetBookReadOrderRight": await _session.SetOptionsAsync(options with { Direction = command == "SetBookReadOrderLeft" ? ReadDirection.LeftToRight : ReadDirection.RightToLeft }); break;
                case "ToggleDirection": await _session.SetOptionsAsync(options with { Direction = options.Direction == ReadDirection.RightToLeft ? ReadDirection.LeftToRight : ReadDirection.RightToLeft }); break;
                case "Fit": await _session.SetOptionsAsync(options with { Scale = ScaleMode.Fit, Zoom = 1 }); break;
                case "ViewReset": await _session.SetOptionsAsync(options with { Zoom = 1, Rotation = 0 }); break;
                case "ActualPixels": await _session.SetOptionsAsync(options with { Scale = ScaleMode.ActualPixels, Zoom = 1 }); break;
                case "ZoomIn": case "ZoomOut": await _session.SetOptionsAsync(options with { Zoom = Math.Clamp(options.Zoom * (resolved == "ZoomIn" ? 1.15 : 1 / 1.15), 0.1, 8) }); break;
                case "Rotate": await _session.SetOptionsAsync(options with { Rotation = (options.Rotation + 90) % 360 }); break;
                case "Bookmark":
                    if (snapshot.Index is { } index) { var name = await Dialogs.TextAsync("书签名称", snapshot.Current?.Name ?? Path.GetFileName(index.Locator.Path)); if (name is not null) await _states.SaveBookmarkAsync(new(Guid.NewGuid().ToString("N"), null, (await _states.BookmarksAsync()).Count, name, index.Book, index.Locator, snapshot.Anchor)); await RefreshPanelsAsync(); } break;
                case "Reveal": if (snapshot.ActionTarget is { } reveal) await _platform.RevealAsync(reveal.Locator.Path); break;
                case "Rename":
                    if (snapshot.ActionTarget is { } rename) { var name = await Dialogs.TextAsync("新的文件名", rename.Name); if (name is not null && Path.GetFileName(name) == name) await FileActionAsync(rename, FileActionKind.Rename, Path.Combine(Path.GetDirectoryName(rename.Locator.Path)!, name)); } break;
                case "Trash": if (snapshot.ActionTarget is { } trash) await FileActionAsync(trash, FileActionKind.Trash, null); break;
                case "Copy": case "MoveToFolderAs": if (parameter is not null) await ClassifyAsync(parameter, command == "MoveToFolderAs", command == "Copy"); break;
                case "UndoDestinationMove": case "RedoDestinationMove":
                    var generation = snapshot.Generation;
                    var undo = command == "UndoDestinationMove";
                    var result = undo ? await _files.UndoAsync() : await _files.RedoAsync();
                    if (!result.Success && result.Error?.Contains("同名") == true && await Dialogs.ConfirmAsync("同名冲突", "覆盖前将保留可恢复备份。", "覆盖")) result = undo ? await _files.UndoAsync(ConflictChoice.Overwrite) : await _files.RedoAsync(ConflictChoice.Overwrite);
                    Status = result.Success ? $"已{(undo ? "撤销" : "重做")}：{result.Target}" : result.Error;
                    if (result.Success && generation == _session.Snapshot.Generation)
                    {
                        var indexNow = _session.Snapshot.Index;
                        var restored = result.Target is { } target && Path.GetDirectoryName(target) == indexNow?.Locator.Path ? result.Content : null;
                        await _session.RefreshAsync(restored);
                    }
                    break;
                case "Close": CloseRequested?.Invoke(); break;
                case "Quit": QuitRequested?.Invoke(); break;
                default:
                    if (command.StartsWith("MoveToDestinationFolder", StringComparison.Ordinal) && int.TryParse(command[23..], out var number) && number is >= 1 and <= 9 && _destinations.Managed.ElementAtOrDefault(DestinationIndex(parameter, number) - 1) is { } destination) await ClassifyAsync(destination, false);
                    else Status = $"当前未支持命令：{command}"; break;
            }
        }
        catch (Exception error) { Status = ReaderException.From(error).Message; }
    }
    /// <summary>分类目标固定为命令开始时的有效对象；复制不推进，移动重建索引。</summary>
    public async Task ClassifyAsync(string target, bool fixedMove = false, bool fixedCopy = false)
    {
        if (_session.Snapshot.ActionTarget is not { } page) { Status = "请先明确选择图片。"; return; }
        await FileActionAsync(page, fixedCopy || !fixedMove && Settings.CopyMode ? FileActionKind.Copy : FileActionKind.Move, target);
    }
    private async Task FileActionAsync(PageDescriptor page, FileActionKind action, string? target)
    {
        var generation = _session.Snapshot.Generation;
        var result = await _files.ExecuteAsync(page, action, target);
        if (!result.Success && result.Error?.Contains("同名") == true && await Dialogs.ConfirmAsync("同名冲突", "覆盖前将保留可恢复备份。", "覆盖")) result = await _files.ExecuteAsync(page, action, target, ConflictChoice.Overwrite);
        Status = result.Success ? $"完成：{result.Target ?? result.Source}" : result.Error;
        if (result.Success && action != FileActionKind.Copy && generation == _session.Snapshot.Generation) await _session.RefreshAsync();
    }
}
