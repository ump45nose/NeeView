using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using NeeView.Application;

namespace NeeView.Desktop;

/// <summary>独立分类视图；两区布局和模板仅在此维护，文件语义由工作区负责。</summary>
public sealed class ReaderDestinationPanel : UserControl
{
    private readonly ListBox _managed = new();
    private readonly ListBox _children = new();
    private readonly Grid _sections = new() { RowDefinitions = new("*,5,*") };
    private readonly CheckBox _copy = new() { Content = "复制" };
    private readonly CheckBox _autoRefresh = new() { Content = "自动刷新" };
    private bool _updating;
    private IReadOnlyList<string> _paths = [];
    /// <summary>当前两区分隔比例，供窗口持久化；不暴露 Grid 行。</summary>
    public double SectionRatio
    {
        get { var height = _sections.RowDefinitions[0].ActualHeight + _sections.RowDefinitions[2].ActualHeight; return height > 0 ? _sections.RowDefinitions[0].ActualHeight / height : 0.5; }
        set { var ratio = Math.Clamp(value, 0.05, 0.95); _sections.RowDefinitions[0].Height = new(ratio, GridUnitType.Star); _sections.RowDefinitions[2].Height = new(1 - ratio, GridUnitType.Star); }
    }
    /// <summary>组合分类区，刷新端口可被主窗口或其他宿主替换。</summary>
    public ReaderDestinationPanel(ReaderWorkspaceViewModel workspace, Func<bool, Task> refresh)
    {
        Classes.Add("reader-destinations"); var dock = new DockPanel(); var actions = new WrapPanel();
        actions.Children.Add(_copy); actions.Children.Add(_autoRefresh);
        actions.Children.Add(ReaderPanelControls.Button("刷新", () => refresh(true), workspace));
        actions.Children.Add(ReaderPanelControls.Button("添加目标", workspace.AddDestinationAsync, workspace));
        actions.Children.Add(ReaderPanelControls.Button("新建子目录", workspace.CreateDestinationAsync, workspace));
        foreach (var (label, command) in new[] { ("撤销", "UndoDestinationMove"), ("重做", "RedoDestinationMove"), ("Finder", "Reveal"), ("重命名", "Rename"), ("废纸篓", "Trash") })
            actions.Children.Add(ReaderPanelControls.Button(label, () => workspace.ExecuteAsync(command), workspace));
        actions.Children.Add(ReaderPanelControls.Button("恢复文件操作", workspace.RecoverFilesAsync, workspace));
        actions.Children.Add(ReaderPanelControls.Button("移除目标", async () => { if (_managed.SelectedItem is string path) await workspace.EditDestinationAsync(path, false); }, workspace));
        actions.Children.Add(ReaderPanelControls.Button("上移目标", async () => { if (_managed.SelectedItem is string path) await workspace.EditDestinationAsync(path, true); }, workspace));
        DockPanel.SetDock(actions, Dock.Bottom); dock.Children.Add(actions);
        _managed.ItemTemplate = new FuncDataTemplate<string>((path, _) => Label(path!, true));
        _children.ItemTemplate = new FuncDataTemplate<string>((path, _) => Label(path!, false));
        _managed.DoubleTapped += async (_, _) => await ReaderPanelControls.RunAsync(async () => { if (_managed.SelectedItem is string path) await workspace.ClassifyAsync(path); }, workspace);
        _children.DoubleTapped += async (_, _) => await ReaderPanelControls.RunAsync(async () => { if (_children.SelectedItem is string path) await workspace.ClassifyAsync(path); }, workspace);
        var upper = Section("目标目录（前九项对应数字键）", _managed); var lower = Section("当前目录的直接子目录", _children);
        Grid.SetRow(lower, 2); _sections.Children.Add(upper); _sections.Children.Add(lower);
        var split = new GridSplitter { ResizeDirection = GridResizeDirection.Rows }; Grid.SetRow(split, 1); _sections.Children.Add(split); dock.Children.Add(_sections); Content = dock;
        _copy.IsCheckedChanged += async (_, _) => await ReaderPanelControls.RunAsync(async () =>
        { if (!_updating) await workspace.UpdateSettingsAsync(s => s with { CopyMode = _copy.IsChecked == true }); }, workspace);
        _autoRefresh.IsCheckedChanged += async (_, _) => await ReaderPanelControls.RunAsync(async () =>
        { if (!_updating) { await workspace.UpdateSettingsAsync(s => s with { AutoRefreshDestinations = _autoRefresh.IsChecked == true }); await refresh(true); } }, workspace);
    }
    /// <summary>输入两区数据，编号只标记手动目标的前九项。</summary>
    public void SetData(DestinationData data)
    { _paths = data.Managed; _managed.ItemsSource = data.Managed; _children.ItemsSource = data.Children; }
    /// <summary>更新表现开关而不回触存储；分隔比例由窗口首次恢复。</summary>
    public void ApplySettings(AppSettings settings)
    { _updating = true; try { _copy.IsChecked = settings.CopyMode; _autoRefresh.IsChecked = settings.AutoRefreshDestinations; } finally { _updating = false; } }
    /// <summary>创建目录条目显示控件，完整定位通过提示展示。</summary>
    private Control Label(string path, bool managed)
    {
        var index = _paths.ToList().IndexOf(path);
        var text = new TextBlock { Text = (managed && index is >= 0 and < 9 ? $"{index + 1} · " : "") + Path.GetFileName(path.TrimEnd('/')), Margin = new(6) };
        ToolTip.SetTip(text, path); return text;
    }
    /// <summary>组合区标题和虚拟化列表；列表拥有各自滚动状态。</summary>
    private static Control Section(string title, Control list)
    { var dock = new DockPanel(); var text = new TextBlock { Text = title, Margin = new(6) }; DockPanel.SetDock(text, Dock.Top); dock.Children.Add(text); dock.Children.Add(list); return dock; }
}
