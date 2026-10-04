using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
namespace NeeView.MacOS.Views;

/// <summary>原目标目录管理的克隆草稿；确认才返回集合，取消不会改原配置。</summary>
public sealed class DestinationFolderDialog : Window
{
    private readonly DestinationFolderCollection _draft;
    private readonly ListBox _list = new();
    private readonly TextBox _name = new(), _path = new();
    private readonly TextBlock _error = new() { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
    public DestinationFolderDialog(DestinationFolderCollection folders)
    {
        _draft = new(folders.Select(folder => (DestinationFolder)folder.Clone()));
        Title = "管理目标文件夹"; Width = 560; Height = 460; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var add = Button("添加", () => { var item = _draft.CreateNew(); Refresh(item); });
        var remove = Button("移除", () => { if (_list.SelectedItem is DestinationFolder item) { _draft.Remove(item); Refresh(); } });
        var up = Button("上移", () => Move(-1)); var down = Button("下移", () => Move(1));
        var apply = Button("更新选中项", () => { if (_list.SelectedItem is DestinationFolder item) { item.Name = _name.Text ?? ""; item.Path = _path.Text ?? ""; Refresh(item); } });
        var choose = new Button { Content = "选择目录…" };
        choose.Click += async (_, _) => { var selected = await StorageProvider.OpenFolderPickerAsync(new() { AllowMultiple = false, Title = "选择目标目录" }); if (selected.FirstOrDefault()?.TryGetLocalPath() is { } path) _path.Text = path; };
        var ok = Button("确定", () =>
        {
            if (_list.SelectedItem is DestinationFolder item) { item.Name = _name.Text ?? ""; item.Path = _path.Text ?? ""; }
            if (_draft.Any(folder => !folder.IsValid())) { _error.Text = "每项必须填写目标目录路径。"; return; }
            Close(_draft);
        }); ok.IsDefault = true;
        var cancel = Button("取消", () => Close(null)); cancel.IsCancel = true;
        var grid = new Grid { Margin = new Thickness(16), RowDefinitions = new("*,Auto,Auto,Auto,Auto,Auto") };
        _list.SelectionChanged += (_, _) => { if (_list.SelectedItem is DestinationFolder item) { _name.Text = item.Name; _path.Text = item.Path; } };
        grid.Children.Add(_list);
        AddRow(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { add, remove, up, down } }, 1);
        AddRow(new StackPanel { Spacing = 4, Children = { new TextBlock { Text = "名称" }, _name, new TextBlock { Text = "路径" }, _path } }, 2);
        AddRow(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { choose, apply } }, 3);
        AddRow(_error, 4); AddRow(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { ok, cancel } }, 5);
        Content = grid; Refresh(_draft.FirstOrDefault());
        void AddRow(Control control, int row) { control.Margin = new Thickness(0, 6, 0, 0); Grid.SetRow(control, row); grid.Children.Add(control); }
    }
    private static Button Button(string text, Action action) { var button = new Button { Content = text }; button.Click += (_, _) => action(); return button; }
    private void Refresh(DestinationFolder? selected = null)
    { _list.ItemsSource = _draft.ToArray(); _list.ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<DestinationFolder>((item, _) => new TextBlock { Text = item?.Name }); _list.SelectedItem = selected; }
    private void Move(int delta)
    { if (_list.SelectedItem is not DestinationFolder item) return; int index = _draft.IndexOf(item), next = index + delta; if (next < 0 || next >= _draft.Count) return; _draft.RemoveAt(index); _draft.Insert(next, item); Refresh(item); }
}
