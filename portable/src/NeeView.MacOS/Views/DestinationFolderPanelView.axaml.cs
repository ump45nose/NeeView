using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using NeeView.MacOS.ViewModels;
namespace NeeView.MacOS.Views;

/// <summary>分类视图只呈现原两区/分隔及输入；文件操作、配置和资源全部经Engine。</summary>
public sealed partial class DestinationFolderPanelView : UserControl, IDisposable
{
    private DestinationFolderPanelViewModel? _model;
    private Task _action = Task.CompletedTask;
    private bool _closing, _disposed;
    public event EventHandler<string>? Failed;
    public Func<Task>? ManageAsync { get; set; }
    public Func<Task>? CreateAsync { get; set; }
    public DestinationFolderPanelView()
    {
        AvaloniaXamlLoader.Load(this);
        this.FindControl<GridSplitter>("SectionSplitter")!.AddHandler(PointerReleasedEvent, Splitter_Released, RoutingStrategies.Bubble, handledEventsToo: true);
    }
    public void Attach(BookOperation operation)
    {
        _model?.Dispose(); _model = new(operation); DataContext = _model;
        RestoreSectionRatio();
    }
    /// <summary>保存失败后恢复已提交分隔比例，表现几何不成为第二配置来源。</summary>
    private void RestoreSectionRatio()
    {
        var ratio = Config.Current.Panels.DestinationFolderSectionRatio;
        var rows = this.FindControl<Grid>("Sections")!.RowDefinitions; rows[0].Height = new GridLength(ratio, GridUnitType.Star); rows[2].Height = new GridLength(1 - ratio, GridUnitType.Star);
    }
    private async void Splitter_Released(object? sender, PointerReleasedEventArgs e)
    {
        var rows = this.FindControl<Grid>("Sections")!.RowDefinitions; double sum = rows[0].ActualHeight + rows[2].ActualHeight;
        if (_model is not null && sum > 0) await RunAsync(() => _model.SetRatioAsync(rows[0].ActualHeight / sum));
    }
    private async void Classify_Click(object? sender, RoutedEventArgs e)
    { if (_model is not null && sender is Control { DataContext: DestinationFolderPanelItem item }) await RunAsync(() => _model.Operation.ClassifyAsync(item.Folder, _model.CopyMode)); }
    private async void Mode_Click(object? sender, RoutedEventArgs e)
    { if (_model is not null && sender is CheckBox checkbox) await RunAsync(() => _model.SetModeAsync(checkbox.IsChecked == true)); }
    private async void WriteAccess_Click(object? sender, RoutedEventArgs e)
    { if (_model is not null && sender is CheckBox checkbox) await RunAsync(() => _model.SetWriteAccessAsync(checkbox.IsChecked == true)); }
    private async void AutoRefresh_Click(object? sender, RoutedEventArgs e)
    { if (_model is not null && sender is CheckBox checkbox) await RunAsync(() => _model.SetAutoRefreshAsync(checkbox.IsChecked == true)); }
    private async void Refresh_Click(object? sender, RoutedEventArgs e) { if (_model is not null) await RunAsync(() => _model.Panel.RefreshChildrenAsync()); }
    private async void Replay_Click(object? sender, RoutedEventArgs e)
    { if (_model is not null && sender is Control { Tag: string tag }) await RunAsync(() => _model.Operation.ReplayDestinationMoveAsync(tag == "undo")); }
    private async void Manage_Click(object? sender, RoutedEventArgs e) { if (ManageAsync is not null) await RunAsync(ManageAsync); }
    private async void Create_Click(object? sender, RoutedEventArgs e) { if (CreateAsync is not null) await RunAsync(CreateAsync); }
    /// <summary>追踪一个授权动作供退出等待；重入不排队、不意外操作下一图。</summary>
    private Task RunAsync(Func<Task> action)
    {
        if (_closing || _disposed || !_action.IsCompleted) return Task.CompletedTask;
        return _action = CoreAsync();
        async Task CoreAsync() { try { await action(); } catch (Exception ex) { RestoreSectionRatio(); Failed?.Invoke(this, ex.Message); } }
    }
    public async Task PrepareCloseAsync() { _closing = true; IsEnabled = false; await _action; }
    public void CancelClose() { if (!_disposed) { _closing = false; IsEnabled = true; } }
    public void Dispose() { _disposed = true; _model?.Dispose(); }
}
