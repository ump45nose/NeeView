using Avalonia.Controls;
using Avalonia.VisualTree;
using Avalonia.Controls.Templates;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using NeeView.MacOS.ViewModels;
namespace NeeView.MacOS.Views;

/// <summary>原面板选择、显示租约与菜单反馈；字段/读取/配置规则留在Engine。</summary>
public sealed partial class FileInformationView : UserControl, IDisposable
{
    /// <summary>关闭先脱离列表模板并归还显示租约，工厂随后才能释放缓存。</summary>
    public Task CloseAsync()
    {
        var covers = this.GetVisualDescendants().OfType<ListCoverImage>().ToArray();
        foreach (var cover in covers) cover.Dispose();
        Dispose();
        return Task.WhenAll(covers.Select(cover => cover.Loading));
    }
    public void Dispose()
    {
        CanAct = () => false;
        var list = this.FindControl<ListBox>("InformationPages")!;
        list.ItemsSource = null; list.ItemTemplate = null; DataContext = null;
        _operation = null; _platform = null;
    }
    private BookOperation? _operation;
    private IPlatformService? _platform;
    public Func<bool>? CanAct { get; set; }
    public Task PendingAction { get; private set; } = Task.CompletedTask;
    private async Task RunActionAsync(Func<Task> action)
    {
        if (CanAct?.Invoke() == false || !PendingAction.IsCompleted) return;
        PendingAction = ExecuteActionAsync(action); await PendingAction;
    }
    private async Task ExecuteActionAsync(Func<Task> action)
    { try { await action(); } catch (Exception ex) { Failed?.Invoke(this, ex.Message); } }
    public FileInformationView() => AvaloniaXamlLoader.Load(this);
    public void Attach(FileInformationViewModel model, BookOperation operation, BitmapFactory images, IPlatformService platform)
    {
        _operation = operation; _platform = platform; DataContext = model;
        this.FindControl<ListBox>("InformationPages")!.ItemTemplate = new FuncDataTemplate<Page>((page, _) =>
            new StackPanel { Width = 104, Children = { new ListCoverImage { Width = 96, Height = 80, PageSource = page,
                LoadPageAsync = (p, request, token) => images.GetAsync(p, request, token, true) },
                new TextBlock { Text = page?.EntryName.Split('/')[^1], TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis } } });
    }
    private void Groups_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button button || _operation is null || DataContext is not FileInformationViewModel model) return;
        var menu = new ContextMenu();
        foreach (var group in Enum.GetValues<InformationGroup>())
        {
            var item = new MenuItem { Header = FileInformationViewModel.Label("InformationGroup." + group), ToggleType = MenuItemToggleType.CheckBox,
                IsChecked = Config.Current.Information.IsVisibleGroup(group) };
            item.Click += async (_, _) =>
            {
                await RunActionAsync(async () =>
                {
                    var property = typeof(InformationConfig).GetProperty("IsVisible" + group)!;
                    await _operation.ApplyOptionsAsync(() => property.SetValue(Config.Current.Information, !Config.Current.Information.IsVisibleGroup(group)),
                        (Config.Current.History.LimitSize, Config.Current.History.LimitSpan));
                    model.Refresh();
                });
                item.IsChecked = Config.Current.Information.IsVisibleGroup(group);
            };
            menu.Items.Add(item);
        }
        button.ContextMenu = menu; menu.Open(button);
    }
    public event EventHandler<string>? Failed;
    private async void Map_Click(object? sender, RoutedEventArgs e)
    {
        if (_platform is null || DataContext is not FileInformationViewModel model) return;
        await RunActionAsync(async () => { if (model.CreateMapUri() is { } uri) await _platform.OpenUriAsync(uri); });
    }
    private async void Folder_Click(object? sender, RoutedEventArgs e)
    {
        if (_platform is null || DataContext is not FileInformationViewModel { Selected: { } page }) return;
        var entry = page.ArchiveEntry.TargetArchiveEntry;
        var root = entry.Archive.RootArchivePath;
        var path = entry.Archive.IsDirectory ? entry.Archive.Path : System.IO.Path.GetDirectoryName(root)!;
        await RunActionAsync(() => _platform.OpenFolderAsync(path));
    }
    /// <summary>原属性列分隔；只提交实际显示宽度，全部行共用原配置，失败恢复原列宽。</summary>
    private async void Header_DragCompleted(object? sender, VectorEventArgs e)
    {
        if (_operation is null || sender is not GridSplitter { Parent: Grid grid } || DataContext is not FileInformationViewModel model) return;
        var width = grid.ColumnDefinitions[0].ActualWidth;
        await RunActionAsync(() => _operation.ApplyOptionsAsync(() => Config.Current.Information.PropertyHeaderWidth = width.ToString(System.Globalization.CultureInfo.InvariantCulture),
            (Config.Current.History.LimitSize, Config.Current.History.LimitSpan)));
        model.Refresh();
    }
}
