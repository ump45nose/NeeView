using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using NeeView.Application;
using NeeView.Core;

namespace NeeView.Desktop;

public sealed record SettingsSelection(AppSettings Settings, ReaderOptions Current);
/// <summary>独立设置视图；只收集选项，不保存配置或执行阅读命令。</summary>
public sealed class SettingsWindow : Window
{
    /// <summary>输入配置与当前书籍选项，返回用户确认的修改；取消不产生副作用。</summary>
    public SettingsWindow(AppSettings settings, ReaderOptions current)
    {
        Title = "NeeView 设置"; Width = 760; Height = 650; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var dock = new DockPanel { Margin = new(16) }; Content = dock;
        var tabs = new TabControl();
        var currentEditor = new ReadingOptionsEditor(current);
        var defaultsEditor = new ReadingOptionsEditor(settings.Defaults);
        var shortcuts = new StackPanel { Spacing = 8 };
        var rows = new List<(TextBox Gesture, ComboBox Command, TextBox Parameter)>();
        var shortcutPanel = new DockPanel();
        var description = new TextBlock { Text = "数字键保持 NeeView 含义；系统命令使用 Meta（Command）。鼠标可填写 WheelUp、WheelDown、LeftClick、RightClick、MiddleClick、DoubleClick。", TextWrapping = Avalonia.Media.TextWrapping.Wrap, Margin = new(0, 8) };
        DockPanel.SetDock(description, Dock.Top); shortcutPanel.Children.Add(description);
        var add = new Button { Content = "添加绑定", HorizontalAlignment = HorizontalAlignment.Left }; DockPanel.SetDock(add, Dock.Bottom); shortcutPanel.Children.Add(add);
        add.Click += (_, _) => AddBinding(new("", "NextPage"));
        foreach (var binding in settings.Shortcuts) AddBinding(binding);
        shortcutPanel.Children.Add(new ScrollViewer { Content = shortcuts });
        void AddBinding(ShortcutBinding binding)
        {
            var row = new Grid { ColumnDefinitions = new("160,*,140,55") };
            var gesture = new TextBox { Text = binding.Gesture, PlaceholderText = "手势" };
            var command = new ComboBox { ItemsSource = ReaderLabels.Choices(CommandCatalog.Supported.Order()), SelectedItem = new ReaderChoice<string>(binding.Command, ReaderLabels.Text(binding.Command)) };
            var parameter = new TextBox { Text = binding.Parameter, PlaceholderText = "可选参数" };
            var delete = new Button { Content = "删除" }; var entry = (gesture, command, parameter);
            rows.Add(entry); delete.Click += (_, _) => { rows.Remove(entry); shortcuts.Children.Remove(row); };
            row.Children.Add(gesture); Grid.SetColumn(command, 1); row.Children.Add(command); Grid.SetColumn(parameter, 2); row.Children.Add(parameter); Grid.SetColumn(delete, 3); row.Children.Add(delete); shortcuts.Children.Add(row);
        }
        var classification = new StackPanel { Spacing = 14, Margin = new(12) };
        var capacity = new NumericUpDown { Minimum = 0, Maximum = 1000, Value = settings.MoveHistoryCapacity };
        var copy = new CheckBox { Content = "数字分类默认复制", IsChecked = settings.CopyMode };
        var auto = new CheckBox { Content = "图片目录变化时自动刷新子目录", IsChecked = settings.AutoRefreshDestinations };
        var left = new CheckBox { Content = "显示导航侧栏", IsChecked = settings.LeftVisible };
        var right = new CheckBox { Content = "显示分类侧栏", IsChecked = settings.RightVisible };
        classification.Children.Add(new TextBlock { Text = "移动历史容量（退出时清空）" }); classification.Children.Add(capacity);
        classification.Children.Add(copy); classification.Children.Add(auto); classification.Children.Add(left); classification.Children.Add(right);
        var policies = new StackPanel { Spacing = 8, Margin = new(12) };
        var policyRows = new List<(string Field, ComboBox Value)>();
        foreach (var (field, name) in new[] { ("DoublePage", "单双页"), ("Direction", "阅读方向"), ("DivideWide", "宽图分割"), ("SingleFirst", "封面单页"), ("SingleLast", "末页单页"), ("WidePage", "宽图独占双页"), ("Sort", "排序"), ("Zoom", "缩放") })
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
            var value = new ComboBox { ItemsSource = ReaderLabels.Choices(Enum.GetValues<RestorePolicy>()), SelectedItem = new ReaderChoice<RestorePolicy>(settings.RestorePolicies.GetValueOrDefault(field, RestorePolicy.RestoreOrDefault), ReaderLabels.Text(settings.RestorePolicies.GetValueOrDefault(field, RestorePolicy.RestoreOrDefault))), MinWidth = 230 };
            row.Children.Add(new TextBlock { Text = name, Width = 130, VerticalAlignment = VerticalAlignment.Center }); row.Children.Add(value); policies.Children.Add(row); policyRows.Add((field, value));
        }
        tabs.ItemsSource = new[] { new TabItem { Header = "当前书籍", Content = currentEditor }, new TabItem { Header = "默认阅读", Content = defaultsEditor }, new TabItem { Header = "恢复规则", Content = policies }, new TabItem { Header = "快捷键与鼠标", Content = shortcutPanel }, new TabItem { Header = "分类与侧栏", Content = classification } };
        var bottom = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8 };
        var message = new TextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap, MaxWidth = 460 };
        var cancel = new Button { Content = "取消" }; cancel.Click += (_, _) => Close();
        var save = new Button { Content = "保存" }; save.Click += (_, _) =>
        {
            var bindings = rows.Where(r => !string.IsNullOrWhiteSpace(r.Gesture.Text)).Select(r => new ShortcutBinding(r.Gesture.Text!.Trim(), ReaderLabels.Value<string>(r.Command.SelectedItem), string.IsNullOrWhiteSpace(r.Parameter.Text) ? null : r.Parameter.Text)).ToList();
            var conflicts = CommandCatalog.Conflicts(bindings);
            if (conflicts.Count > 0) { message.Text = string.Join("；", conflicts); return; }
            Close(new SettingsSelection(settings with { Defaults = defaultsEditor.Value(), Shortcuts = bindings, RestorePolicies = policyRows.ToDictionary(r => r.Field, r => ReaderLabels.Value<RestorePolicy>(r.Value.SelectedItem)),
                CopyMode = copy.IsChecked == true, AutoRefreshDestinations = auto.IsChecked == true, MoveHistoryCapacity = (int)(capacity.Value ?? 300), LeftVisible = left.IsChecked == true, RightVisible = right.IsChecked == true }, currentEditor.Value()));
        };
        bottom.Children.Add(message); bottom.Children.Add(cancel); bottom.Children.Add(save); DockPanel.SetDock(bottom, Dock.Bottom); dock.Children.Add(bottom); dock.Children.Add(tabs);
    }
}
/// <summary>阅读选项编辑组件可用于设置页或未来侧栏，不依赖窗口和会话。</summary>
public sealed class ReadingOptionsEditor : ScrollViewer
{
    private readonly ReaderOptions _original;
    private readonly ComboBox _mode, _direction, _scale, _sort;
    private readonly CheckBox _double, _divide, _first, _last, _wide;
    private readonly NumericUpDown _zoom, _column;
    /// <summary>输入不可变阅读选项，建立本地编辑状态。</summary>
    public ReadingOptionsEditor(ReaderOptions options)
    {
        _original = options; var panel = new StackPanel { Spacing = 12, Margin = new(12) }; Content = panel;
        _mode = Choice(panel, "查看模式", Enum.GetValues<ReaderMode>(), options.Mode); _direction = Choice(panel, "阅读方向", Enum.GetValues<ReadDirection>(), options.Direction);
        _scale = Choice(panel, "缩放规则", Enum.GetValues<ScaleMode>(), options.Scale); _sort = Choice(panel, "排序", Enum.GetValues<SortMode>(), options.Sort);
        _double = Flag(panel, "双页阅读", options.DoublePage); _divide = Flag(panel, "单页模式分割宽图", options.DivideWide);
        _first = Flag(panel, "封面单页", options.SingleFirst); _last = Flag(panel, "末页单页", options.SingleLast); _wide = Flag(panel, "宽图独占双页", options.WidePage);
        panel.Children.Add(new TextBlock { Text = "缩放倍数" }); _zoom = new() { Minimum = 0.1m, Maximum = 8, Increment = 0.1m, Value = (decimal)options.Zoom }; panel.Children.Add(_zoom);
        panel.Children.Add(new TextBlock { Text = "瀑布流目标列宽（DIP）" }); _column = new() { Minimum = 80, Maximum = 1600, Increment = 20, Value = (decimal)options.ColumnWidth }; panel.Children.Add(_column);
    }
    /// <summary>返回编辑值，保留没有在本组件暴露的选项。</summary>
    public ReaderOptions Value() => _original with { Mode = ReaderLabels.Value<ReaderMode>(_mode.SelectedItem), Direction = ReaderLabels.Value<ReadDirection>(_direction.SelectedItem), Scale = ReaderLabels.Value<ScaleMode>(_scale.SelectedItem), Sort = ReaderLabels.Value<SortMode>(_sort.SelectedItem),
        DoublePage = _double.IsChecked == true, DivideWide = _divide.IsChecked == true, SingleFirst = _first.IsChecked == true, SingleLast = _last.IsChecked == true, WidePage = _wide.IsChecked == true,
        Zoom = (double)(_zoom.Value ?? 1), ColumnWidth = (double)(_column.Value ?? 320) };
    /// <summary>建立有标签的枚举编辑项。</summary>
    private static ComboBox Choice<T>(StackPanel parent, string label, T[] values, T selected)
    { parent.Children.Add(new TextBlock { Text = label }); var box = new ComboBox { ItemsSource = ReaderLabels.Choices(values), SelectedItem = new ReaderChoice<T>(selected, ReaderLabels.Text(selected!)), MinWidth = 280 }; parent.Children.Add(box); return box; }
    /// <summary>建立布尔阅读选项。</summary>
    private static CheckBox Flag(StackPanel parent, string label, bool value)
    { var box = new CheckBox { Content = label, IsChecked = value }; parent.Children.Add(box); return box; }
}
