// Copyright (c) NeeLaboratory. 原面板/Item访问器的数据与表现适配，MIT。
using Avalonia.Controls;
namespace NeeView.MacOS.ViewModels;
/// <summary>仅桥接现有列表选区与模板；不拥有业务集合或新的选中状态。</summary>
public sealed record ScriptPanelBindings(Func<string, ListBox?> List, Func<string, PanelListItemStyle, Task> SetStyle, BookmarkListViewModel? Bookmarks, Func<string, TreeView?>? Tree = null);

public abstract class ScriptListPanelAccessor(ScriptAccessContext context, ReaderWorkspaceViewModel model, string key,
    Func<string, Window?> resolve, ScriptPanelBindings? bindings) : LayoutPanelAccessor(context, model, key, resolve)
{
    private readonly string _listKey = key;
    protected ScriptPanelBindings? Bindings => bindings;
    protected T[] Selected<T>() => Context.Read(() => bindings?.List(_listKey)?.SelectedItems?.OfType<T>().ToArray() ?? []);
    protected void Select<T>(IEnumerable<T> items) => Context.Write(() =>
    {
        var list = bindings?.List(_listKey) ?? throw new NotSupportedException("面板选区尚未装配。");
        var selected = items.ToHashSet(); list.SelectedItems?.Clear();
        foreach (var item in list.Items.OfType<T>().Where(selected.Contains)) list.SelectedItems?.Add(item);
    });
    protected void SetStyle(string value) => Context.Run(() => bindings?.SetStyle(_listKey, ScriptEnum.Parse<PanelListItemStyle>(value))
        ?? throw new NotSupportedException("面板样式入口尚未装配。"));
}
/// <summary>原页面列表读取原Page，实际多选只修改列表选区，不翻页。</summary>
public sealed class PageListPanelAccessor(ScriptAccessContext context, ReaderWorkspaceViewModel model, Func<string, Window?> resolve, ScriptPanelBindings? bindings = null)
    : ScriptListPanelAccessor(context, model, "PageListPanel", resolve, bindings)
{
    public string? Path => Context.Read(() => Model.Operation.Book?.Path);
    public string SearchWord { get => Context.Read(() => Model.PageSearch.Keyword); set => Context.Write(() => Model.PageSearch.Keyword = value); }
    public string Style { get => Context.Read(() => Config.Current.PageList.PanelListItemStyle.ToString()); set => SetStyle(value); }
    public string Format { get => Context.Read(() => Config.Current.PageList.Format.ToString()); set => Context.Write(() => Config.Current.PageList.Format = ScriptEnum.Parse<PageNameFormat>(value)); }
    public string SortMode { get => Context.Read(() => Model.Operation.Book?.Setting.SortMode.ToString() ?? Config.Current.BookSetting.SortMode.ToString());
        set => Context.Run(() => Model.Operation.ApplySettingAsync(s => s.SortMode = ScriptEnum.Parse<PageSortMode>(value))); }
    public PageAccessor[] Items => Context.Read(() => Model.Pages.Select(p => new PageAccessor(Context, p)).ToArray());
    public PageAccessor[] SelectedItems
    {
        get => Selected<Page>().Select(p => new PageAccessor(Context, p)).ToArray();
        set { var paths = (value ?? []).Select(p => p.RawPath).ToHashSet(); Select(Context.Read(() => Model.Pages.Where(p => paths.Contains(p.EntryFullName)).ToArray())); }
    }
}
public sealed class HistoryPanelAccessor(ScriptAccessContext context, ReaderWorkspaceViewModel model, Func<string, Window?> resolve, ScriptPanelBindings? bindings = null)
    : ScriptListPanelAccessor(context, model, "HistoryPanel", resolve, bindings)
{
    public string SearchWord { get => Context.Read(() => Model.HistorySearch.Keyword); set => Context.Write(() => Model.HistorySearch.Keyword = value); }
    public string Style { get => Context.Read(() => Config.Current.History.PanelListItemStyle.ToString()); set => SetStyle(value); }
    public HistoryItemAccessor[] Items => Context.Read(() => Model.Operation.HistoryList.GetViewItems().Select(e => new HistoryItemAccessor(Context, e)).ToArray());
    public HistoryItemAccessor[] SelectedItems
    {
        get => Selected<HistoryRow>().Select(r => new HistoryItemAccessor(Context, r.Entry)).ToArray();
        set { var paths = (value ?? []).Select(p => p.Path).ToHashSet(); Select(Context.Read(() => Model.History.Where(r => paths.Contains(r.Path)).ToArray())); }
    }
}
public sealed class PlaylistPanelAccessor(ScriptAccessContext context, ReaderWorkspaceViewModel model, Func<string, Window?> resolve, ScriptPanelBindings? bindings = null)
    : ScriptListPanelAccessor(context, model, "PlaylistPanel", resolve, bindings)
{
    public string Path { get => Context.Read(() => Model.Operation.Playlists.Current?.Path ?? ""); set => Context.Run(() => Model.Operation.Playlists.SwitchAsync(value, Context.Token)); }
    public string Name { get => Context.Read(() => System.IO.Path.GetFileNameWithoutExtension(Model.Operation.Playlists.Current?.Path) ?? "");
        set => Context.Run(() => Model.Operation.Playlists.SwitchAsync(System.IO.Path.Combine(Config.Current.Playlist.PlaylistFolder, value + ".nvpls"), Context.Token)); }
    public string Style { get => Context.Read(() => Config.Current.Playlist.PanelListItemStyle.ToString()); set => SetStyle(value); }
    public PlaylistItemAccessor[] Items => Context.Read(() => Model.Operation.Playlists.GetViewItems(Model.Operation.Book).Select(e => new PlaylistItemAccessor(Context, e)).ToArray());
    public PlaylistItemAccessor[] SelectedItems
    {
        get => Selected<PlaylistRow>().Select(r => new PlaylistItemAccessor(Context, r.Item)).ToArray();
        set { var paths = (value ?? []).Select(p => p.Path).ToHashSet(); Select(Context.Read(() => Bindings?.List("PlaylistPanel")?.Items.OfType<PlaylistRow>().Where(r => paths.Contains(r.Path)).ToArray() ?? [])); }
    }
}
public sealed class BookshelfPanelAccessor(ScriptAccessContext context, ReaderWorkspaceViewModel model, Func<string, Window?> resolve, ScriptPanelBindings? bindings = null)
    : ScriptListPanelAccessor(context, model, "FolderPanel", resolve, bindings)
{
    public string? Path { get => Context.Read(() => Model.Operation.Bookshelf.Place); set => Context.Run(() => Model.Operation.Bookshelf.SetPlaceAsync(value ?? "", token: Context.Token)); }
    public string SearchWord { get => Context.Read(() => Model.FolderSearch.Keyword); set => Context.Write(() => Model.FolderSearch.Keyword = value); }
    public string Style { get => Context.Read(() => Config.Current.Bookshelf.PanelListItemStyle.ToString()); set => SetStyle(value); }
    public string FolderOrder { get => Context.Read(() => Model.Operation.Bookshelf.FolderOrder.ToString()); set => Context.Run(() => Model.Operation.ChangeFolderOrderAsync(ScriptEnum.Parse<FolderOrder>(value))); }
    public BookshelfItemAccessor[] Items => Context.Read(() => Model.Folders.Select(e => new BookshelfItemAccessor(Context, e)).ToArray());
    public BookshelfItemAccessor[] SelectedItems
    {
        get => Selected<FolderItem>().Select(e => new BookshelfItemAccessor(Context, e)).ToArray();
        set { var paths = (value ?? []).Select(p => p.Path).ToHashSet(); Select(Context.Read(() => Model.Folders.Where(r => paths.Contains(r.Path)).ToArray())); }
    }
    public void Sync() => Context.Run(async () => { if (Model.Operation.Book is { } book) await Model.Operation.Bookshelf.SyncAsync(book, Context.Token, force: true); });
    public BookshelfFolderTreeAccessor FolderTree => new(Context, Model, Bindings);
    public string[] PreviousHistory => Context.Read(() => Model.Operation.Bookshelf.PreviousHistory.ToArray());
    public string[] NextHistory => Context.Read(() => Model.Operation.Bookshelf.NextHistory.ToArray());
    public void MoveToPrevious() => Context.Run(() => Model.Operation.Bookshelf.MoveFolderHistoryAsync(-1, Context.Token));
    public void MoveToNext() => Context.Run(() => Model.Operation.Bookshelf.MoveFolderHistoryAsync(1, Context.Token));
    public void MoveToParent() => Context.Run(() => Model.Operation.Bookshelf.UpAsync(Context.Token));
    public void Wait() => Context.Run(async () => { while (Model.Operation.Bookshelf.IsLoading) await Task.Delay(10, Context.Token); });
}
public sealed class BookmarkPanelAccessor(ScriptAccessContext context, ReaderWorkspaceViewModel model, Func<string, Window?> resolve, ScriptPanelBindings? bindings = null)
    : ScriptListPanelAccessor(context, model, "BookmarkPanel", resolve, bindings)
{
    public BookmarkFolderTreeAccessor FolderTree => new(Context, Bindings, "BookmarkTree");
    private BookmarkListViewModel ListModel => Bindings?.Bookmarks ?? throw new NotSupportedException("书签面板尚未装配。");
    public string? Path { get => Context.Read(() => ListModel.List.ParameterPath); set => Context.Write(() =>
        { var folder = ListModel.List.FindFolder(value ?? "") ?? throw new DirectoryNotFoundException("书签文件夹不存在。"); ListModel.List.SetPlace(folder); ListModel.Refresh(true); }); }
    public string SearchWord { get => Context.Read(() => ListModel.Keyword); set => Context.Write(() => ListModel.Keyword = value); }
    public string Style { get => Context.Read(() => Config.Current.Bookmark.PanelListItemStyle.ToString()); set => SetStyle(value); }
    public string FolderOrder { get => Context.Read(() => ListModel.List.FolderOrder.ToString()); set => Context.Run(() => ListModel.ChangeOrderAsync(ScriptEnum.Parse<FolderOrder>(value))); }
    public BookmarkItemAccessor[] Items => Context.Read(() => ListModel.Items.Select(n => new BookmarkItemAccessor(Context, ListModel, n)).ToArray());
    public BookmarkItemAccessor[] SelectedItems
    {
        get => Selected<BookmarkNode>().Select(n => new BookmarkItemAccessor(Context, ListModel, n)).ToArray();
        set { var nodes = (value ?? []).Select(x => x.Source).ToHashSet(); Select(Context.Read(() => ListModel.Items.Where(nodes.Contains).ToArray())); }
    }
    public void Sync() => Context.Write(() => { ListModel.List.Sync(Model.Operation.Book?.Path); ListModel.Refresh(true); });
    public void MoveToParent() => Context.Write(() => { ListModel.List.MoveToParent(); ListModel.Refresh(true); });
    public void NewFolder(string? name) => Context.Run(async () => { await Context.State.AddBookmarkFolderAsync(ListModel.List.Place, name ?? "新建文件夹"); ListModel.Refresh(); });
}
public sealed class HistoryItemAccessor(ScriptAccessContext context, HistoryEntry entry)
{
    public string Name => context.Read(() => System.IO.Path.GetFileName(entry.Path));
    public string Path => context.Read(() => entry.Path);
    public DateTime LastAccessTime => context.Read(() => entry.LastAccessTime);
    public void Open() => context.Run(() => context.Operation.OpenHistoryAsync(Path));
}
public sealed class PlaylistItemAccessor(ScriptAccessContext context, PlaylistItem item)
{
    public string Name => context.Read(() => item.Name);
    public string Path => context.Read(() => item.Path);
    public void Open() => context.Run(() => context.Operation.OpenPlaylistItemAsync(item));
}
public sealed class BookshelfItemAccessor(ScriptAccessContext context, FolderItem item)
{
    public string Name => context.Read(() => item.Name);
    public string Path => context.Read(() => item.Path);
    public long Size => context.Read(() => item.Length);
    public DateTime LastWriteTime => context.Read(() => item.LastWriteTime);
    public void Open() => context.Run(() => context.Operation.OpenAsync(item.Path, context.Token));
}
public sealed class BookmarkItemAccessor(ScriptAccessContext context, BookmarkListViewModel model, BookmarkNode node)
{
    internal BookmarkNode Source => node;
    public string Name { get => context.Read(() => node.DisplayName); set => context.Run(() => context.State.RenameBookmarkAsync(node, value)); }
    public string Path => context.Read(() => model.List.GetTargetPath(node));
    public void Open() => context.Run(async () => { if (node.IsFolder) { model.List.SetPlace(node); model.Refresh(true); } else if (node.Path is { } path) await context.Operation.OpenAsync(path, context.Token); });
}
