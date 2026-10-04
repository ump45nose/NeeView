using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
namespace NeeView.MacOS.ViewModels;

/// <summary>原两区分类面板的独立表现模型；配置与枚举在Engine，界面可独立换主题。</summary>
public sealed class DestinationFolderPanelViewModel : ObservableObject, IDisposable
{
    public BookOperation Operation { get; }
    public DestinationFolderPanel Panel { get; }
    public IReadOnlyList<DestinationFolderPanelItem> ManagedFolders => Panel.ManagedFolders;
    public IReadOnlyList<DestinationFolderPanelItem> CurrentFolderChildren => Panel.CurrentFolderChildren;
    public bool CopyMode => Config.Current.Panels.IsDestinationFolderCopyMode;
    public bool WriteAccess => Config.Current.System.IsFileWriteAccessEnabled;
    public bool AutoRefresh => Config.Current.Panels.IsDestinationFolderAutoRefreshEnabled;
    public bool CanClassify => Operation.CanFileAction;
    public bool CanUndo => Operation.DestinationMoves?.CanUndo == true;
    public bool CanRedo => Operation.DestinationMoves?.CanRedo == true;
    public string CurrentDirectory => Panel.CurrentDirectory ?? "没有普通目录图片";
    public string Target => Operation.FileActionPage?.EntryName ?? (Operation.BrowseMode == BrowseLayoutMode.Masonry ? "请点击选中要分类的图片" : "没有可分类图片");
    public string Status => Panel.Error ?? Operation.DestinationMoves?.Error ?? (Panel.IsRefreshing ? "正在读取直接子目录…" : "");
    private bool _disposed;
    public event EventHandler? Refreshed;
    public DestinationFolderPanelViewModel(BookOperation operation)
    {
        Operation = operation; Panel = operation.DestinationFolders;
        Panel.Changed += Changed; Operation.Changed += Changed;
        if (operation.DestinationMoves is { } moves) moves.StateChanged += Changed;
    }
    private void Changed(object? sender, EventArgs e) => Dispatcher.UIThread.Post(() =>
    {
        if (_disposed) return;
        foreach (var name in new[] { nameof(ManagedFolders), nameof(CurrentFolderChildren), nameof(WriteAccess), nameof(CopyMode), nameof(AutoRefresh), nameof(CanClassify), nameof(CanUndo), nameof(CanRedo), nameof(CurrentDirectory), nameof(Target), nameof(Status) }) OnPropertyChanged(name);
        Refreshed?.Invoke(this, EventArgs.Empty);
    });
    public Task SetModeAsync(bool copy) => Operation.EditDestinationFoldersAsync(() => Config.Current.Panels.IsDestinationFolderCopyMode = copy);
    public Task SetWriteAccessAsync(bool enabled) => Operation.EditDestinationFoldersAsync(() => Config.Current.System.IsFileWriteAccessEnabled = enabled);
    public Task SetAutoRefreshAsync(bool enabled) => Operation.EditDestinationFoldersAsync(() => Config.Current.Panels.IsDestinationFolderAutoRefreshEnabled = enabled);
    public Task SetRatioAsync(double ratio) => Operation.EditDestinationFoldersAsync(() => Config.Current.Panels.DestinationFolderSectionRatio = ratio);
    public Task SaveFoldersAsync(DestinationFolderCollection folders) => Operation.EditDestinationFoldersAsync(() => Config.Current.System.DestinationFolderCollection = folders);
    public void Dispose()
    {
        _disposed = true; Panel.Changed -= Changed; Operation.Changed -= Changed;
        if (Operation.DestinationMoves is { } moves) moves.StateChanged -= Changed;
    }
}
