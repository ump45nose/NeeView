using System.Linq;
namespace NeeView;
public sealed class BookAccessor(ScriptAccessContext context)
{
    public string? Path => context.Read(() => context.Operation.Book?.Path);
    public long Size => context.Read(() => context.Operation.Book?.Source.CreateBookEntry().Length ?? 0);
    public DateTime LastWriteTime => context.Read(() => context.Operation.Book?.Source.CreateBookEntry().LastWriteTime ?? DateTime.MinValue);
    public DateTime CreationTime => context.Read(() => context.Operation.Book?.Source.CreateBookEntry().CreationTime ?? DateTime.MinValue);
    public bool IsMedia => context.Read(() => context.Operation.Book?.IsMedia == true);
    public bool IsNew => context.Read(() => context.Operation.Book?.IsNew == true);
    public bool IsBookmarked { get => context.Read(() => Path is { } p && context.State.IsBookmark(p)); set => context.Run(() => context.Operation.Book is { } b && context.State.IsBookmark(b.Path) != value ? context.State.ToggleBookmarkAsync(b, context.Token) : Task.CompletedTask); }
    public BookConfigAccessor Config => new(context);
    public PageAccessor[] Pages => context.Read(() => context.Operation.Book?.Pages.Select(p => new PageAccessor(context, p)).ToArray() ?? []);
    public ViewPageAccessor[] ViewPages => context.Read(() => context.Operation.Frame?.Elements.Where(e => !e.IsDummy).Select(e => new ViewPageAccessor(context, e.Page)).ToArray() ?? []);
    public void Wait() => context.Run(() => context.WaitForDisplayAsync(context.Token));
    [Obsolete("no used")] public int PageSize => context.Diagnostics.Throw<int>(new NotSupportedException("使用nv.Book.ViewPages.length。"));
    [Obsolete("no used")] public int ViewPageSize => context.Diagnostics.Throw<int>(new NotSupportedException("使用nv.Book.Pages.length。"));
    [Obsolete("no used")] public PageAccessor? Page(int index) => context.Diagnostics.Throw<PageAccessor>(new NotSupportedException("使用nv.Book.Pages/ViewPages数组。"));
    [Obsolete("no used")] public PageAccessor? ViewPage(int index) => context.Diagnostics.Throw<PageAccessor>(new NotSupportedException("使用nv.Book.Pages/ViewPages数组。"));
}
