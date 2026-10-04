using Avalonia.Controls;
using Avalonia.Interactivity;
namespace NeeView.MacOS.Views;

public sealed partial class MainWindow
{
    private PanelListPresentation? _historyPresentation, _bookshelfPresentation, _pagePresentation;
    private Task _listStyleTask = Task.CompletedTask;
    private bool _listStyleBusy;
    /// <summary>唯一工厂与原加载业务装配到纯表现模板，不创建第二封面服务。</summary>
    private void AttachListTemplates()
    {
        if (_model is null || _images is null) return;
        Task<BitmapLease> Load(string path, DecodeRequest request, CancellationToken token) => _model.Operation.GetCoverAsync(_images, path, request, token);
        _historyPresentation = new(this.FindControl<ListBox>("HistoryList")!, Load);
        _bookshelfPresentation = new(this.FindControl<ListBox>("FolderList")!, Load);
        _historyPresentation.Apply(Config.Current.History.PanelListItemStyle);
        _bookshelfPresentation.Apply(Config.Current.Bookshelf.PanelListItemStyle);
        AttachPageListTemplates();
        this.FindControl<BookmarkListView>("BookmarkPanelList")!.AttachCovers(Load, RefreshListCovers);
    }
    /// <summary>显式刷新失效原路径封面；排序、正文翻页和模板切换复用缓存。</summary>
    private void RefreshListCovers()
    {
        _images?.InvalidateCovers(); _historyPresentation?.RefreshCovers(); _bookshelfPresentation?.RefreshCovers();
        this.FindControl<BookmarkListView>("BookmarkPanelList")!.RefreshCoverPresentation();
    }
    /// <summary>持久化原History/Bookshelf字段；失败恢复模板、选中身份与可重试状态。</summary>
    public Task SetListStyleAsync(bool history, PanelListItemStyle style)
    {
        var before = history ? Config.Current.History.PanelListItemStyle : Config.Current.Bookshelf.PanelListItemStyle;
        return ChangeListStyleAsync(style, before, Set);
        void Set(PanelListItemStyle value)
        {
            if (history) { Config.Current.History.PanelListItemStyle = value; _historyPresentation?.Apply(value); }
            else { Config.Current.Bookshelf.PanelListItemStyle = value; _bookshelfPresentation?.Apply(value); }
        }
    }
    /// <summary>四列表显示设置共享已授权保存和关闭等待；失败回滚对应原分支。</summary>
    private Task ChangeListStyleAsync(PanelListItemStyle style, PanelListItemStyle before, Action<PanelListItemStyle> set)
    {
        if (_model is null || _preparing || _closedPrepared || _listStyleBusy || !Enum.IsDefined(style)) return Task.CompletedTask;
        return _listStyleTask = RunAsync();
        async Task RunAsync()
        {
            _listStyleBusy = true;
            try { set(style); await _model.Operation.SaveConfigurationAsync(); }
            catch (Exception ex) { set(before); ShowError(ex.Message); }
            finally { _listStyleBusy = false; }
        }
    }
    /// <summary>书架更多菜单只改变原列表显示，不改变当前目录或正文阅读规则。</summary>
    private void Bookshelf_More(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button button) return;
        var menu = new ContextMenu();
        foreach (var item in PanelListPresentation.CreateStyleMenuItems(Config.Current.Bookshelf.PanelListItemStyle, style => SetListStyleAsync(false, style))) menu.Items.Add(item);
        AddFolderTreeMenu(menu);
        button.ContextMenu = menu; menu.Open(button);
    }
}
