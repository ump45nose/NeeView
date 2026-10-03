using CommunityToolkit.Mvvm.ComponentModel;
using Avalonia.Threading;
using NeeView;
namespace NeeView.MacOS.ViewModels;

/// <summary>书签列表独立表现模型；结构和主题可变，导航次序由原业务适配计算。</summary>
public sealed class BookmarkListViewModel : ObservableObject, IDisposable
{
    private readonly SaveData _state;
    private bool _disposed;
    public BookmarkFolderList List { get; }
    public IReadOnlyList<BookmarkNode> Items => List.Items;
    public string Place => List.FullPath;
    public string Count => $"{Items.Count} 项";
    public bool CountVisible => Config.Current.Bookmark.IsVisibleItemsCount;
    public bool TreeVisible => Config.Current.Bookmark.IsFolderTreeVisible;
    public bool CanMoveToParent => List.CanMoveToParent;
    public string? CapabilityMessage => List.CapabilityMessage;
    public static IReadOnlyList<BookmarkOrderChoice> Orders { get; } =
    [new(FolderOrder.FileName, "名称"), new(FolderOrder.FileNameDescending, "名称（降序）"),
     new(FolderOrder.Path, "路径"), new(FolderOrder.PathDescending, "路径（降序）"),
     new(FolderOrder.FileType, "类型"), new(FolderOrder.FileTypeDescending, "类型（降序）"),
     new(FolderOrder.TimeStamp, "时间（待迁移）"), new(FolderOrder.TimeStampDescending, "时间降序（待迁移）"),
     new(FolderOrder.Size, "大小（待迁移）"), new(FolderOrder.SizeDescending, "大小降序（待迁移）"),
     new(FolderOrder.EntryTime, "注册顺序"), new(FolderOrder.EntryTimeDescending, "注册顺序（逆序）"), new(FolderOrder.Random, "随机")];
    public BookmarkOrderChoice SelectedOrder => Orders.First(choice => choice.Mode == List.FolderOrder);
    public event EventHandler? Refreshed;

    /// <summary>接入唯一已加载集合，只订阅书签事务回报，不因翻页保存重复重排。</summary>
    public BookmarkListViewModel(SaveData state)
    {
        _state = state; List = new(state.Bookmarks); state.BookmarksChanged += Bookmarks_Changed;
    }
    /// <summary>后台提交先回到 UI 线程；窗口已释放时忽略晚到回报。</summary>
    private void Bookmarks_Changed(object? sender, EventArgs e) => Dispatcher.UIThread.Post(Refresh);
    /// <summary>同步原节点身份和列表表现，不发布阅读刷新。</summary>
    public void Refresh()
    {
        if (_disposed) return; List.Refresh(); OnPropertyChanged(""); Refreshed?.Invoke(this, EventArgs.Empty);
    }
    /// <summary>关闭面板解除数据订阅；不释放共享 SaveData 或书签节点。</summary>
    public void Dispose() { _disposed = true; _state.BookmarksChanged -= Bookmarks_Changed; }
}

/// <summary>原排序项的界面文案；未迁移项保留禁用占位。</summary>
public sealed record BookmarkOrderChoice(FolderOrder Mode, string Label)
{
    public bool IsEnabled => BookmarkFolderList.SupportsOrder(Mode);
}
