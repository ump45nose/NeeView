using NeeView.Core;

namespace NeeView.Application;

public enum FailureKind { NotFound, Permission, Unavailable, Corrupt, Unsupported, Password, Timeout, Cancelled, Conflict }
public sealed class ReaderException(FailureKind kind, string message, Exception? inner = null) : Exception(message, inner)
{
    public FailureKind Kind { get; } = kind;
    /// <summary>输入底层异常，转换为用户可识别的错误类别。</summary>
    public static ReaderException From(Exception error) => error as ReaderException ?? new(error switch
    {
        OperationCanceledException => FailureKind.Cancelled,
        UnauthorizedAccessException => FailureKind.Permission,
        FileNotFoundException or DirectoryNotFoundException => FailureKind.NotFound,
        TimeoutException => FailureKind.Timeout,
        InvalidDataException => FailureKind.Corrupt,
        _ => FailureKind.Unavailable
    }, error.Message, error);
}
public sealed record OpenRequest(string Path, string? Entry = null);
public enum AccessCost { Random, Sequential, Block }
public sealed record SourceCapabilities(bool Writable, AccessCost AccessCost, bool IsArchive);
public sealed record SourceIndex(BookId Book, SourceLocator Locator, IReadOnlyList<PageDescriptor> Pages,
    ContentId? RequestedContent, SourceCapabilities Capabilities);
public interface IContentSource : IAsyncDisposable
{
    SourceLocator Locator { get; }
    SourceCapabilities Capabilities { get; }
    Task<SourceIndex> IndexAsync(CancellationToken cancellationToken);
    Task<Stream> OpenReadAsync(PageDescriptor page, CancellationToken cancellationToken);
}
public interface IContentSourceFactory { Task<IContentSource> OpenAsync(OpenRequest request, CancellationToken cancellationToken); }
public interface IIdentityRegistry
{
    Task<BookId> GetBookAsync(SourceLocator locator, CancellationToken token);
    Task<ContentId> GetContentAsync(SourceLocator locator, CancellationToken token);
    Task RelocateAsync(SourceLocator oldLocator, SourceLocator newLocator, CancellationToken token);
}
public sealed record DecodeRequest(PageDescriptor Page, int TargetWidth = 2048, int TargetHeight = 2048, bool Thumbnail = false);
public sealed record ImageInfo(PixelSize Size, string Format);

/// <summary>像素资源独占租约；租约释放后禁止访问数据。</summary>
public sealed class DecodedImageLease : IDisposable
{
    private Action? _release;
    private byte[]? _pixels;
    public PixelSize Size { get; }
    public int Stride => Size.Width * 4;
    public long ByteCount => (long)Stride * Size.Height;
    public ReadOnlyMemory<byte> Pixels => _pixels ?? throw new ObjectDisposedException(nameof(DecodedImageLease));
    public DecodedImageLease(PixelSize size, byte[] pixels, Action? release = null)
    { Size = size; _pixels = pixels; _release = release; }
    /// <summary>输入释放回调，返回共享缓存像素的独立租约。</summary>
    public DecodedImageLease Share(Action release) => new(Size, _pixels ?? throw new ObjectDisposedException(nameof(DecodedImageLease)), release);
    /// <summary>幂等归还像素租约，不提前清理其它显示者。</summary>
    public void Dispose() { _pixels = null; Interlocked.Exchange(ref _release, null)?.Invoke(); }
}
public interface IImageDecoder
{
    Task<ImageInfo> ProbeAsync(Stream stream, CancellationToken token);
    Task<DecodedImageLease> DecodeAsync(Stream stream, DecodeRequest request, CancellationToken token);
}
public enum ImagePriority { Current, Visible, Prefetch, Thumbnail }
public readonly record struct ImageCacheKey(ContentId Content, ContentVersion Version, int Width, int Height, bool Thumbnail);
public interface IImageRequestScheduler : IAsyncDisposable
{
    Task<DecodedImageLease> RequestAsync(IContentSource source, DecodeRequest request, ImagePriority priority, CancellationToken token);
    long CachedBytes { get; }
}
public sealed record ShortcutBinding(string Gesture, string Command, string? Parameter = null);
public sealed record AppSettings
{
    public int Version { get; init; } = 1;
    public ReaderOptions Defaults { get; init; } = new();
    public Dictionary<string, RestorePolicy> RestorePolicies { get; init; } = [];
    public List<ShortcutBinding> Shortcuts { get; init; } = CommandCatalog.DefaultBindings().ToList();
    public List<string> DestinationFolders { get; init; } = [];
    public bool CopyMode { get; init; }
    public bool AutoRefreshDestinations { get; init; } = true;
    public int MoveHistoryCapacity { get; init; } = 300;
    public double DestinationRatio { get; init; } = 0.5;
    public bool LeftVisible { get; init; } = true;
    public bool RightVisible { get; init; } = true;
    public double LeftWidth { get; init; } = 240;
    public double RightWidth { get; init; } = 260;
    public string? LastSource { get; init; }
}
public interface ISettingsStore
{
    Task<AppSettings> LoadAsync(CancellationToken token = default);
    Task SaveAsync(AppSettings settings, CancellationToken token = default);
}
public sealed record RecoveryOperation(string Id, string Source, string Target, string? Temporary, string? Backup, string Stage);
public interface IReaderStateStore
{
    Task<ReadingState?> GetAsync(BookId book, CancellationToken token = default);
    Task SaveAsync(ReadingState state, CancellationToken token = default);
    Task<IReadOnlyList<ReadingState>> HistoryAsync(CancellationToken token = default);
    Task<IReadOnlyList<Bookmark>> BookmarksAsync(CancellationToken token = default);
    Task SaveBookmarkAsync(Bookmark bookmark, CancellationToken token = default);
    Task DeleteBookmarkAsync(string id, CancellationToken token = default);
    Task RecordOperationAsync(RecoveryOperation operation, CancellationToken token = default);
    Task RemoveOperationAsync(string id, CancellationToken token = default);
    Task<IReadOnlyList<RecoveryOperation>> RecoveriesAsync(CancellationToken token = default);
}
public sealed record ReaderSnapshot(long Generation, SourceIndex? Index, ReadingAnchor? Anchor, ContentId? Selection,
    ReaderOptions Options, bool Loading, string? Error)
{
    public PageDescriptor? Current => Index?.Pages.FirstOrDefault(p => p.Id == Anchor?.Content);
    public PageDescriptor? ActionTarget => Options.Mode == ReaderMode.Masonry
        ? Index?.Pages.FirstOrDefault(p => p.Id == Selection) : Current;
}
public interface IReaderSession : IAsyncDisposable
{
    ReaderSnapshot Snapshot { get; }
    IContentSource? Source { get; }
    event Action<ReaderSnapshot>? Changed;
    Task OpenAsync(OpenRequest request, CancellationToken token = default);
    Task NavigateAsync(int direction, bool onePage = false);
    Task LocateAsync(ReadingAnchor anchor, bool select = false);
    Task SetOptionsAsync(ReaderOptions options);
    Task ReportSizeAsync(ContentId content, PixelSize size, long generation);
    Task RefreshAsync(ContentId? prefer = null);
    Task FlushAsync();
}
public enum ConflictChoice { Cancel, Overwrite }
public enum FileActionKind { Move, Copy, Rename, Trash }
public sealed record FileActionResult(bool Success, string Source, string? Target, string? Error = null, ContentId? Content = null);
public interface IPlatformService
{
    Task RevealAsync(string path, CancellationToken token = default);
    Task TrashAsync(string path, CancellationToken token = default);
}
public interface IFileActionService
{
    bool CanUndo { get; }
    bool CanRedo { get; }
    int Capacity { get; set; }
    Task<FileActionResult> ExecuteAsync(PageDescriptor page, FileActionKind action, string? target,
        ConflictChoice conflict = ConflictChoice.Cancel, CancellationToken token = default);
    Task<FileActionResult> UndoAsync(ConflictChoice conflict = ConflictChoice.Cancel, CancellationToken token = default);
    Task<FileActionResult> RedoAsync(ConflictChoice conflict = ConflictChoice.Cancel, CancellationToken token = default);
}
public interface IDestinationFolderService
{
    IReadOnlyList<string> Managed { get; }
    IReadOnlyList<string> Children { get; }
    Task RefreshAsync(string? directory, bool force, CancellationToken token = default);
    Task<string> CreateChildAsync(string directory, string name, CancellationToken token = default);
}
public interface IFolderNavigator
{
    Task<IReadOnlyList<string>> ChildrenAsync(string directory, CancellationToken token = default);
}
public sealed record PathMapping(string WindowsPrefix, string MacPrefix);
public sealed record ImportBook(string Path, string? Page, ReaderOptions Options, DateTimeOffset Access);
public sealed record ImportBookmark(string Id, string? Parent, int Order, string Name, string? Path, string? Page, ReaderOptions Options);
public sealed record ImportPlan(string Id, string Source, IReadOnlyList<ImportBook> Books,
    IReadOnlyList<ImportBookmark> Bookmarks, AppSettings Settings, IReadOnlyList<string> Warnings);
public interface ILegacyImporter
{
    Task<ImportPlan> PlanImportAsync(string path, IReadOnlyList<PathMapping> mappings, CancellationToken token = default);
    Task ApplyAsync(ImportPlan plan, CancellationToken token = default);
}
