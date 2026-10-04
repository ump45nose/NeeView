using Avalonia.Controls;
using Avalonia.Interactivity;
using NeeView;
namespace NeeView.MacOS.Views;
public sealed partial class MainWindow
{
    /// <summary>原FocusBookmarkList进入普通书架的书签根，不替换为独立面板/搜索框焦点。</summary>
    private async Task FocusBookshelfBookmarksAsync(bool fromMenu)
    {
        if (_model is null || _preparing || _closedPrepared) return;
        if (!await _model.Operation.Bookshelf.SetPlaceAsync("bookmark:")) return;
        _model.ShowPanel("FolderPanel"); UpdateLayout();
        if (!fromMenu) this.FindControl<ListBox>("FolderList")!.Focus();
    }
    /// <summary>书签文件夹只浏览原节点，真实书籍仍走唯一加载链；普通目录保持原打开行为。</summary>
    private async Task OpenBookshelfItemAsync(FolderItem item)
    {
        if (_model is null || _preparing || _closedPrepared) return;
        if (item.QuickAccess?.IsFolder == true || item.Bookmark?.IsFolder == true) await _model.Operation.Bookshelf.SetPlaceAsync(item.Path);
        else { if (item.Bookmark is not null) _bookmarkOpenTarget = item.Path; await OpenAsync(item.Path); }
    }
    private async void Bookshelf_Open(object? sender, RoutedEventArgs e)
    { if (_model?.SelectedFolder is { } item) try { await OpenBookshelfItemAsync(item); } catch (Exception ex) { ShowError(ex.Message); } }
    /// <summary>原OpenBookmarkFolder将书架当前目录与选项转到独立面板，共享节点而非列表状态。</summary>
    private void Bookshelf_RevealBookmark(object? sender, RoutedEventArgs e)
    {
        if (_model?.Operation.Bookshelf.BookmarkPlace is not { } place || _preparing || _closedPrepared) return;
        _model.ShowPanel("BookmarkPanel");
        this.FindControl<BookmarkListView>("BookmarkPanelList")!.SetPlace(place, _model.Operation.Bookshelf.SelectedItem?.Bookmark);
    }
    /// <summary>书架书签树选择只同步普通列表，不改变独立面板或正文。</summary>
    private async void BookshelfBookmarkTree_Changed(object? sender, SelectionChangedEventArgs e)
    {
        if (_model is null || _preparing || _closedPrepared || sender is not TreeView { SelectedItem: BookmarkNode node }) return;
        var collection = _model.SaveData.Bookmarks;
        var folder = node.IsFolder ? node : collection.ParentOf(node);
        if (folder is null) return;
        using var navigation = new BookmarkFolderList(collection, _model.SaveData.FolderConfigs);
        try { await _model.Operation.Bookshelf.SetPlaceAsync(navigation.GetTargetPath(folder), node.IsFolder ? null : node.Path); }
        catch (Exception ex) { ShowError(ex.Message); }
    }
}
