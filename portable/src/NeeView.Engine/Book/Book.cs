// Copyright (c) NeeLaboratory. 原源码关系与业务规则沿用仓库 MIT；P1子集适配见 docs/source-migration.json。
namespace NeeView;

/// <summary>原 Book 的 P1 适配：来源、页面、当前页和设置；媒体/标记在后续阶段迁入。</summary>
public sealed class Book(Archive source, List<Page> pages, BookSettingConfig setting) : IAsyncDisposable
{
    public Archive Source { get; } = source;
    public string Path => Source.Path;
    public BookPageCollection Pages { get; } = new(pages);
    public BookSettingConfig Setting { get; } = setting;
    public PageSortMode EffectiveSortMode { get; private set; }
    public int SortSeed { get; internal set; }
    public Page? CurrentPage { get; internal set; }
    private BookPageMarker? _marker;
    public BookPageMarker Marker => _marker ??= new(this);

    /// <summary>来自原 Book.CreateMemento；保持各字段与 Path/Page/Props 的关系。</summary>
    public BookMemento CreateMemento() => new()
    {
        Path = Path,
        Page = CurrentPage?.EntryName ?? "",
        PageMode = Setting.PageMode,
        BookReadOrder = Setting.BookReadOrder,
        IsSupportedDividePage = Setting.IsSupportedDividePage,
        IsSupportedSingleFirstPage = Setting.IsSupportedSingleFirstPage,
        IsSupportedSingleLastPage = Setting.IsSupportedSingleLastPage,
        IsSupportedWidePage = Setting.IsSupportedWidePage,
        IsRecursiveFolder = Setting.IsRecursiveFolder,
        SortMode = EffectiveSortMode,
        SortSeed = SortSeed,
        AutoRotate = Setting.AutoRotate,
        BaseScale = Setting.BaseScale,
        EffectProfileId = Setting.EffectProfileId
    };
    /// <summary>按原 BookPageSort 排序并保持当前 Page 对象。</summary>
    public void Sort(CancellationToken token)
    {
        // 原 BookSourceFactory.ValidatePageSortMode：普通来源不使用播放列表的注册顺序。
        var mode = Setting.SortMode.IsEntryCategory()
            ? (Setting.SortMode.IsDescending() ? PageSortMode.FileNameDescending : PageSortMode.FileName)
            : Setting.SortMode;
        var result = BookPageSort.Sort(Pages, mode, SortSeed, token);
        EffectiveSortMode = result.SortMode; Pages.SortMode = result.SortMode; SortSeed = result.SortSeed; Pages.Clear(); Pages.AddRange(result.Pages);
        for (int i = 0; i < Pages.Count; i++) Pages[i].Index = i;
    }
    /// <summary>书籍拥有来源，关闭后释放归档句柄。</summary>
    public ValueTask DisposeAsync() => Source.DisposeAsync();
}
