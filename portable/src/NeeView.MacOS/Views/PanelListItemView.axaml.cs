using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using System.ComponentModel;
using NeeView.MacOS.ViewModels;
namespace NeeView.MacOS.Views;

/// <summary>原列表模板布局适配，DataContext仍是原条目身份；展示投影只属于本控件。</summary>
public sealed partial class PanelListItemView : UserControl
{
    public PanelListItemStyle DisplayStyle { get; }
    public ListCoverImage Cover { get; }
    private readonly PanelListItemProfile _profile;
    private readonly Func<Page, string?>? _pageHeader;
    private BookmarkNode? _observed;
    /// <summary>XAML工具构造默认Content；产品列表由宿主传入真实封面契约。</summary>
    public PanelListItemView() : this(PanelListItemStyle.Content, PanelListItemProfile.Create(PanelListItemStyle.Content), null) { }
    public PanelListItemView(PanelListItemStyle style, PanelListItemProfile profile, Func<string, DecodeRequest, CancellationToken, Task<BitmapLease>>? load,
        Func<Page, DecodeRequest, CancellationToken, Task<BitmapLease>>? loadPage = null, Func<Page, string?>? pageHeader = null)
    {
        DisplayStyle = style; _profile = profile; _pageHeader = pageHeader; AvaloniaXamlLoader.Load(this);
        Cover = new() { Profile = profile, LoadCoverAsync = load, LoadPageAsync = loadPage };
        this.FindControl<ContentControl>("CoverHost")!.Content = Cover;
        var content = this.FindControl<Grid>("ContentGrid")!; var text = this.FindControl<StackPanel>("TextHost")!;
        bool vertical = style is PanelListItemStyle.Banner or PanelListItemStyle.Thumbnail;
        content.ColumnDefinitions = new(vertical ? "*" : "Auto,*"); content.RowDefinitions = new(vertical ? "Auto,Auto" : "*");
        Grid.SetColumn(text, vertical ? 0 : 1); Grid.SetRow(text, vertical ? 1 : 0);
        Cover.IsVisible = style != PanelListItemStyle.Normal && profile.ImageWidth > 0;
        Cover.Width = style == PanelListItemStyle.Banner ? double.NaN : Math.Clamp(profile.ShapeWidth, 0, 1024);
        Cover.Height = Math.Clamp(profile.ShapeHeight, 0, 1024);
        if (style == PanelListItemStyle.Thumbnail) { Width = Math.Clamp(profile.ShapeWidth, 24, 1024) + 4; text.HorizontalAlignment = HorizontalAlignment.Stretch; }
        DataContextChanged += (_, _) => { ObserveNode(); RefreshRow(); }; RefreshRow();
        // 原面板字体变化只补偿两行文本高度，不重新构造封面或请求像素。
        PropertyChanged += (_, e) => { if (e.Property == FontSizeProperty) UpdateTextHeight(); };
    }
    /// <summary>原节点原地编辑时更新展示，退树解除订阅，回收控件不保留旧节点。</summary>
    private void ObserveNode()
    {
        if (_observed is not null) _observed.PropertyChanged -= NodeChanged;
        _observed = DataContext as BookmarkNode ?? (DataContext as FolderItem)?.Bookmark;
        if (_observed is not null) _observed.PropertyChanged += NodeChanged;
    }
    private void NodeChanged(object? sender, PropertyChangedEventArgs e) => RefreshRow();
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e) { base.OnAttachedToVisualTree(e); ObserveNode(); RefreshRow(); }
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    { base.OnDetachedFromVisualTree(e); if (_observed is not null) _observed.PropertyChanged -= NodeChanged; _observed = null; }
    /// <summary>只读取已有条目字段；历史日期始终为访问时间，不替换为源文件修改时间。</summary>
    private void RefreshRow()
    {
        string name = "", path = "", page = ""; string? header = null, color = null; DateTime date = default; bool folder = false, directory = false;
        switch (DataContext)
        {
            case HistoryRow row: name = row.Name; path = row.Path; page = row.Page ?? ""; date = row.Entry.LastAccessTime; header = row.GroupHeader; break;
            case FolderItem item: name = item.Name; path = item.Path; date = item.LastWriteTime; folder = item.Bookmark?.IsFolder == true; directory = item.IsDirectory; color = item.Bookmark?.Color; break;
            case BookmarkNode node: name = node.DisplayName; path = node.Path ?? ""; page = node.Page ?? ""; folder = directory = node.IsFolder; color = node.Color; date = node.EntryTime; break;
            case Page item: name = item.GetDisplayName(Config.Current.PageList.Format); header = _pageHeader?.Invoke(item); path = item.ArchiveEntry.SystemPath; date = item.ArchiveEntry.LastWriteTime; directory = item.PageType.IsFolder(); break;
        }
        var model = new ListItemText(name, path, page, header, date, _profile, DisplayStyle, directory);
        this.FindControl<Grid>("ItemRoot")!.DataContext = model;
        UpdateTextHeight();
        bool thumbnail = !folder && path.Length > 0 && DisplayStyle != PanelListItemStyle.Normal;
        Cover.Source = thumbnail && DataContext is not Page ? path : null;
        Cover.PageSource = thumbnail ? DataContext as Page : null;
        Cover.Placeholder = directory ? "▸" : "▱";
        Cover.IconBrush = color is not null && Color.TryParse(color, out var parsed) ? new SolidColorBrush(parsed) : Brushes.LightGray; Cover.InvalidateVisual();
    }
    private void UpdateTextHeight()
    { this.FindControl<TextBlock>("ItemName")!.MaxHeight = _profile.IsTextWrapped ? FontSize * 2.8 : double.PositiveInfinity; }
}
/// <summary>文本与布局参数是表现数据，不参与来源定位、历史更新或打开规则。</summary>
public sealed record ListItemText(string RawName, string Path, string Page, string? GroupHeader, DateTime LastAccessTime, PanelListItemProfile Profile, PanelListItemStyle Style, bool Folder)
{
    public string Name => (Folder && Profile.IsTagVisible ? "▸ " : "") + RawName;
    public bool HasGroupHeader => GroupHeader is not null;
    public string Date => LastAccessTime == default ? "" : LastAccessTime.ToString("g");
    public bool DateVisible => Style == PanelListItemStyle.Content && LastAccessTime != default;
    public bool TextVisible => Profile.IsTextVisible;
    public TextWrapping Wrapping => Profile.IsTextWrapped ? TextWrapping.Wrap : TextWrapping.NoWrap;
    public string? Detail => Profile.IsDetailPopupEnabled ? RawName + "\n" + Path + (Page.Length > 0 ? "\n" + Page : "") + (Date.Length > 0 ? "\n" + Date : "") : null;
}
