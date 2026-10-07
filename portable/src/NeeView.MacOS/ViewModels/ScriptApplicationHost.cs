// Copyright (c) NeeLaboratory. 原CommandHost对象图的Mac装配，MIT。
namespace NeeView.MacOS.ViewModels;
/// <summary>nv根对象；业务访问使用Engine，系统和窗口交互由唯一宿主注入。</summary>
public sealed class ScriptApplicationHost
{
    private readonly ScriptInvocation _invocation;
    private readonly ScriptAccessContext _context;
    private readonly Action<string> _message;
    private readonly Func<string, string, int, Task<bool>> _dialog;
    private readonly Func<string, string?, string?, Task<string?>> _input;
    private readonly Func<string?, bool, Task<string?>> _pick;
    public ScriptApplicationHost(ScriptInvocation run, ScriptAccessContext context, ScriptPanelAccessors panels,
        Action<string> message, Func<string, string, int, Task<bool>> dialog,
        Func<string, string?, string?, Task<string?>> input, Func<string?, bool, Task<string?>> pick)
    {
        _invocation = run; _context = context; _message = message; _dialog = dialog; _input = input; _pick = pick;
        Config = new ConfigMap(NeeView.Config.Current, PropertyMapOptions.Create(context.Dispatcher), context.Diagnostics).Map;
        Command = new(context); Book = new(context); Panels = panels; ImageEffect = new(context);
        Environment = new(context.State, context.Diagnostics);
        ExternalAppCollection = new(context); DestinationFolderCollection = new(context);
    }
    private ScriptPanelAccessors Panels { get; }
    public string? ScriptPath => _invocation.Runtime.ScriptPath;
    public object?[] Args => _invocation.Args;
    public System.Collections.Concurrent.ConcurrentDictionary<string, object?> Values => _invocation.Values;
    public PropertyMap Config { get; }
    public CommandAccessorMap Command { get; }
    public CommandAccessor? CurrentCommand => _invocation.CommandName is { } name ? Command[name] : null;
    public EnvironmentAccessor Environment { get; }
    public BookAccessor Book { get; }
    public BookshelfPanelAccessor Bookshelf => Panels.Bookshelf;
    public PageListPanelAccessor PageList => Panels.PageList;
    public BookmarkPanelAccessor Bookmark => Panels.Bookmark;
    public PlaylistPanelAccessor Playlist => Panels.Playlist;
    public HistoryPanelAccessor History => Panels.History;
    public LayoutPanelAccessor Information => Panels.Information;
    public LayoutPanelAccessor Effect => Panels.Effect;
    public LayoutPanelAccessor Navigator => Panels.Navigator;
    public WindowAccessor Window => Panels.Window;
    public MainViewPanelAccessor MainView => Panels.MainView;
    public ImageEffectAccessor ImageEffect { get; }
    public ExternalAppCollectionAccessor ExternalAppCollection { get; }
    public DestinationFolderCollectionAccessor DestinationFolderCollection { get; }
    public object SusiePluginCollection => throw new NotSupportedException("Susie是Windows专用插件，Mac不装载。");
    public object Pagemark => throw new NotSupportedException("Pagemark已废弃，使用Playlist。");
    public void ShowMessage(string message) => _context.Write(() => _message(message));
    public void ShowToast(string message) => ShowMessage(message);
    /// <summary>原脚本文件入口等待实际事务；同名确认和恢复仍由既有服务处理。</summary>
    public void CopyFile(string source, string destination) => _context.Run(() => _context.Operation.TransferScriptPathsAsync([source], destination, false, false, _context.Token));
    public void MoveFile(string source, string destination) => _context.Run(() => _context.Operation.TransferScriptPathsAsync([source], destination, true, false, _context.Token));
    public void DeleteFile(string path) => _context.Run(() => _context.Operation.DeleteScriptPathAsync(path, _context.Token));
    public bool ShowDialog(string title, string message = "", int commands = 0)
    { bool result = false; _context.Run(async () => result = await _dialog(title, message, commands)); return result; }
    public string? ShowInputDialog(string title) => ShowInputDialog(title, "", "");
    public string? ShowInputDialog(string title, string text) => ShowInputDialog(title, "", text);
    public string? ShowInputDialog(string title, string? message, string? text)
    { string? result = null; _context.Run(async () => result = await _input(title, message, text)); return result; }
    public string? OpenFileDialog(string? initialDirectory = null) => Pick(initialDirectory, false);
    public string? OpenFolderDialog(string? initialDirectory = null) => Pick(initialDirectory, true);
    private string? Pick(string? initial, bool folder)
    { string? result = null; _context.Run(async () => result = await _pick(initial, folder)); return result; }
}
