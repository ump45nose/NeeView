using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
namespace NeeView.MacOS.Views;

/// <summary>三个原列表共用的纯表现切换；ItemsSource、原条目、多选和业务入口保持在宿主。</summary>
public sealed class PanelListPresentation(ListBox list, Func<string, DecodeRequest, CancellationToken, Task<BitmapLease>>? load)
{
    /// <summary>三个列表共用原四个菜单入口，状态与保存由各自宿主决定。</summary>
    public static MenuItem[] CreateStyleMenuItems(PanelListItemStyle selected, Func<PanelListItemStyle, Task> apply) =>
        Enum.GetValues<PanelListItemStyle>().Select(style =>
        {
            var item = new MenuItem { Header = style switch { PanelListItemStyle.Normal => "普通列表", PanelListItemStyle.Content => "详细内容", PanelListItemStyle.Banner => "横幅", _ => "缩略图" },
                Tag = style, ToggleType = MenuItemToggleType.Radio, IsChecked = style == selected };
            item.Click += async (_, _) => await apply(style); return item;
        }).ToArray();
    private PanelListItemStyle? _style;
    private PanelListItemProfile? _profile;
    /// <summary>转换原模板及虚拟面板；宽度变化交给网格，不重新枚举来源。</summary>
    public void Apply(PanelListItemStyle style)
    {
        var profile = Config.Current.Panels.GetProfile(style);
        if (_style == style && ReferenceEquals(_profile, profile)) return;
        var selected = list.SelectedItems?.Cast<object>().ToArray() ?? [];
        bool focus = list.IsKeyboardFocusWithin;
        _style = style; _profile = profile;
        list.Classes.Add("original-list");
        list.ItemTemplate = new FuncDataTemplate<object>((item, _) => new PanelListItemView(style, profile, load) { DataContext = item });
        list.ItemsPanel = new FuncTemplate<Panel?>(() => style == PanelListItemStyle.Thumbnail
            ? new VirtualizingThumbnailPanel { CellWidth = Math.Clamp(profile.ShapeWidth, 24, 1024) + 20, CellHeight = Math.Clamp(profile.ShapeHeight, 0, 1024) + (profile.IsTextVisible ? profile.IsTextWrapped ? 42 : 24 : 0) + 16 }
            : new VirtualizingStackPanel());
        if (selected.FirstOrDefault() is { } target) list.ScrollIntoView(target);
        if (focus) { list.UpdateLayout(); if (list.SelectedItem is { } item && list.ContainerFromItem(item) is Control row) row.Focus(); else list.Focus(); }
    }
    /// <summary>显式刷新只重提当前可见控件，普通重排不重新解码。</summary>
    public void RefreshCovers() { var style = ConfigStyle; _style = null; Apply(style); }
    public PanelListItemStyle ConfigStyle => _style ?? PanelListItemStyle.Content;
}
