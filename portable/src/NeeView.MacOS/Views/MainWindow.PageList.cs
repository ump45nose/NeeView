// Copyright (c) NeeLaboratory. 原PageListBox的点击/确认/焦点和四模板Mac适配。
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
namespace NeeView.MacOS.Views;

public sealed partial class MainWindow
{
    private Page? _pagePress;
    private Task _pagePressTask = Task.CompletedTask;
    /// <summary>页面四模板直接使用原Page；归档页面共用当前来源和同一缩略预算。</summary>
    private void AttachPageListTemplates()
    {
        var list = this.FindControl<ListBox>("PageList")!;
        _pagePresentation = new(list, null, (page, request, token) => _images!.GetAsync(page, request, token, true), GetPageGroupHeader);
        _pagePresentation.Apply(Config.Current.PageList.PanelListItemStyle);
        list.AddHandler(PointerPressedEvent, PageList_Pressed, RoutingStrategies.Tunnel, true);
        list.AddHandler(PointerReleasedEvent, PageList_Released, RoutingStrategies.Bubble, true);
        AttachPageNavigation();
    }
    /// <summary>持久化原PageList样式，模板改变不跳页或创建新书籍。</summary>
    /// <param name="style">原Normal/Content/Banner/Thumbnail枚举。</param>
    /// <returns>共享设置保存及必要的失败回滚完成。</returns>
    public Task SetPageListStyleAsync(PanelListItemStyle style) => ChangeListStyleAsync(style, Config.Current.PageList.PanelListItemStyle,
        value => { Config.Current.PageList.PanelListItemStyle = value; _pagePresentation?.Apply(value); });
    /// <summary>原页面行单击确认，无修饰选择；键盘/修饰多选不改变正文。</summary>
    private async void PageList_Pressed(object? sender, PointerPressedEventArgs e)
    {
        _pagePress = null; _pagePressTask = Task.CompletedTask;
        if (e.KeyModifiers != KeyModifiers.None || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed || PageFromRow(e.Source) is not { } page) return;
        _pagePress = page;
        _pagePressTask = CommitPageListAsync(page); await _pagePressTask;
    }
    /// <summary>原MoveEnd在鼠标释放后按FocusMainView选择是否回正文焦点。</summary>
    private async void PageList_Released(object? sender, PointerReleasedEventArgs e)
    {
        var pressed = _pagePress; var navigation = _pagePressTask; _pagePress = null; _pagePressTask = Task.CompletedTask;
        if (pressed is null || e.KeyModifiers != KeyModifiers.None || e.InitialPressMouseButton != MouseButton.Left || !ReferenceEquals(pressed, PageFromRow(e.Source))) return;
        // 快速释放时原导航可能仍在等待互斥；完成后按当前Page裁决，不能把焦点给旧书请求。
        await navigation;
        if (Config.Current.PageList.FocusMainView && !_preparing && !_closedPrepared && ReferenceEquals(_model?.Operation.Book?.CurrentPage, pressed)) Viewer.Focus();
    }
    /// <summary>从实际命中容器取原Page，不能用旧SelectedItem解释背景或滚动条点击。</summary>
    private static Page? PageFromRow(object? source) => source is Avalonia.Visual visual
        ? visual.GetVisualAncestors().Prepend(visual).OfType<ListBoxItem>().FirstOrDefault()?.DataContext as Page : null;
    /// <summary>原MoveTo进入唯一JumpAsync，在进入原导航锁前核对页面身份及书籍。</summary>
    /// <param name="page">待确认的原Page，空值取当前列表选择。</param>
    /// <returns>真实页面定位完成；旧来源或关闭后的动作忽略。</returns>
    public async Task CommitPageListAsync(Page? page = null)
    {
        if (_model is null || _preparing || _closedPrepared) return;
        page ??= _model.SelectedPage; var book = _model.Operation.Book;
        if (page is null || book is null || page.Index < 0 || page.Index >= book.Pages.Count || !ReferenceEquals(book.Pages[page.Index], page)) return;
        try { await _model.Operation.JumpAsync(page.Index, expectedBook: book); }
        catch (Exception ex) { ShowError(ex.Message); }
    }
    /// <summary>原目录/归档页双击进入子书，普通图片已由点击定位，不另建打开链。</summary>
    private async void PageList_DoubleTapped(object? sender, TappedEventArgs e)
    {
        if (_model is null || _preparing || _closedPrepared || PageFromRow(e.Source) is not { } page || !page.PageType.IsFolder()) return;
        try { await _model.Operation.OpenChildBookAsync(page, _model.Operation.Book); } catch (Exception ex) { ShowError(ex.Message); }
    }
    /// <summary>菜单只编辑原页面列表样式；未迁目录组树/搜索保持原清单占位。</summary>
    private void PageList_More(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button button) return;
        var menu = new ContextMenu();
        foreach (var item in PanelListPresentation.CreateStyleMenuItems(Config.Current.PageList.PanelListItemStyle, SetPageListStyleAsync)) menu.Items.Add(item);
        AddPageNavigationMenu(menu);
        button.ContextMenu = menu; menu.Open(button);
    }
}
