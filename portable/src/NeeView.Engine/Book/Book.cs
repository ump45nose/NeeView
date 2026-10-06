// Copyright (c) NeeLaboratory. 原源码关系与业务规则沿用仓库 MIT；P1子集适配见 docs/source-migration.json。
namespace NeeView;

/// <summary>原 Book 的 P1 适配：来源、页面、当前页和设置；媒体/标记在后续阶段迁入。</summary>
public sealed class Book(Archive source, List<Page> pages, BookSettingConfig setting) : IAsyncDisposable
{
    public bool IsNew { get; init; } = true;
    private BookMementoControl? _mementoControl;
    public BookMementoControl MementoControl => _mementoControl ??= new(this);
    public Archive Source { get; } = source;
    public PageSortModeClass PageSortModeClass => Source.IsPlaylist ? PageSortModeClass.WithEntry : PageSortModeClass.Normal;
    public BookAddress BookAddress { get; } = BookAddress.Create(source);
    public ArchiveEntryCollection? Entries { get; init; }
    public string Path => Source.Path;
    public BookPageCollection Pages { get; } = new(pages);
    /// <summary>页面顺序提交代次；展示端按此刷新列表，普通翻页不重建条目集合。</summary>
    public long PageOrderVersion { get; private set; }
    /// <summary>来源索引仍在补齐时，末端不是实际页尾；失败保留已提交页面并记录错误。</summary>
    public bool IsIndexing { get; internal set; }
    public string? IndexError { get; internal set; }
    public BookSettingConfig Setting { get; } = setting;
    public PageSortMode EffectiveSortMode { get; private set; }
    public int SortSeed { get; internal set; }
    private IReadOnlyList<Page> _currentPages = Array.Empty<Page>();
    /// <summary>原当前页集合按选中范围索引顺序保存；双页文件操作不以视觉左右推断阅读顺序。</summary>
    public IReadOnlyList<Page> CurrentPages => _currentPages;
    public Page? CurrentPage { get => _currentPages.FirstOrDefault(); internal set => SetCurrentPages(value is null ? [] : [value]); }
    /// <summary>提交原阅读控制已确定的页集合，页面身份继续来自唯一来源索引。</summary>
    internal void SetCurrentPages(IEnumerable<Page> pages) => _currentPages = pages.ToArray();
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
        var mode = BookSourceFactory.ValidatePageSortMode(Setting.SortMode, Source);
        UpdatePrefix();
        var result = BookPageSort.Sort(PageSearchProfile.Search(Pages.SearchKeyword, Pages.SourcePages, token), mode, SortSeed, token);
        ApplySort(result);
    }
    /// <summary>来源补齐后更新原Smart名称公共目录，不重新扫描或解码。</summary>
    internal void UpdatePrefix() { var prefix = BookTableOfContents.GetPagesPrefix(Pages.SourcePages); foreach (var page in Pages.SourcePages) page.Prefix = prefix; }
    /// <summary>锁内提交后台生成的原排序结果；保持Page引用及同一页面集合。</summary>
    internal void ApplySort(BookPageSortResult result)
    {
        EffectiveSortMode = result.SortMode; Pages.SortMode = result.SortMode; SortSeed = result.SortSeed; Pages.Clear(); Pages.AddRange(result.Pages);
        for (int i = 0; i < Pages.Count; i++) Pages[i].Index = i;
        PageOrderVersion++;
    }
    /// <summary>书籍拥有来源，关闭后释放归档句柄。</summary>
    public ValueTask DisposeAsync() => Entries?.DisposeAsync() ?? Source.DisposeAsync();
}
