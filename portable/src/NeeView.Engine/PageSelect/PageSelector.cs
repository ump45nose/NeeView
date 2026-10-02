// Copyright (c) NeeLaboratory. 适配原 PageSelector/FilmStrip/PageSlider 的选择与布局算法，展示由 MacOS 承担。
namespace NeeView;

/// <summary>原临时选择，与正文页框分离；胶片条和滑条共用一个实例。</summary>
public sealed class PageSelector
{
    public Book? Book { get; private set; }
    public int SelectedIndex { get; private set; }
    public int MaxIndex => Math.Max((Book?.Pages.Count ?? 0) - 1, 0);
    public Page? SelectedItem => Book is { } book && SelectedIndex >= 0 && SelectedIndex < book.Pages.Count ? book.Pages[SelectedIndex] : null;
    public event EventHandler? SelectionChanged;
    /// <summary>正文选中范围变化时按原最小页索引刷新选择；不会提交另一次正文导航。</summary>
    public void Synchronize(Book? book, int index)
    { Book = book; SelectedIndex = Math.Clamp(index, 0, MaxIndex); SelectionChanged?.Invoke(this, EventArgs.Empty); }
    /// <summary>沿用原 SetSelectedIndex，只发布临时选择；返回是否发生实际改变。</summary>
    public bool SetSelectedIndex(object? sender, int value, bool raiseChangedEvent)
    {
        value = Math.Clamp(value, 0, MaxIndex); if (value == SelectedIndex) return false;
        SelectedIndex = value; if (raiseChangedEvent) SelectionChanged?.Invoke(sender, EventArgs.Empty); return true;
    }
}

/// <summary>原 FilmStrip 的索引、选择及请求顺序；控件/像素/防抖放在展示资源入口。</summary>
public sealed class FilmStrip(PageSelector pageSelector)
{
    public PageSelector PageSelector { get; } = pageSelector;
    public bool IsSliderDirectionReversed => Config.Current.Slider.SliderDirection switch
    { SliderDirection.RightToLeft => true, SliderDirection.SyncBookReadDirection => PageSelector.Book?.Setting.BookReadOrder == PageReadOrder.RightToLeft, _ => false };
    public int SelectedIndex => GetIndexWithDirectionReverse(PageSelector.SelectedIndex);
    /// <summary>原双向索引转换；负值仍代表无选择。</summary>
    public int GetIndexWithDirectionReverse(int value) => Math.Max(-1, IsSliderDirectionReversed ? PageSelector.MaxIndex - value : value);
    /// <summary>原可视顺序选择移动，与原书籍页索引方向分开。</summary>
    public void MoveSelectedIndex(int delta, bool isDirectionReverse = false)
    {
        if (PageSelector.Book?.Pages.Count is not > 0 || delta == 0) return;
        if (isDirectionReverse) delta = -delta;
        var index = Math.Clamp(SelectedIndex + delta, 0, PageSelector.MaxIndex);
        PageSelector.SetSelectedIndex(this, GetIndexWithDirectionReverse(index), true);
    }
    /// <summary>迁入原 RequestThumbnail 的方向反转、两项余量和中央优先顺序。</summary>
    public Page[] RequestThumbnail(int start, int count, int margin, int direction)
    {
        if (PageSelector.Book is not { } book || count <= 0) return [];
        if (IsSliderDirectionReversed) { start = PageSelector.MaxIndex - (start + count - 1); direction = -direction; }
        int center = start + count / 2;
        return Enumerable.Range(start - margin, Math.Max(0, count + margin * 2 - 1)).Where(i => i >= 0 && i < book.Pages.Count)
            .Select(i => book.Pages[i]).OrderBy(p => Math.Abs(p.Index - center)).ToArray();
    }
    /// <summary>原首尾居中补偿等价为允许负的显示偏移；普通模式仅保证完整可见。</summary>
    public static double ScrollIntoView(int index, int count, double itemWidth, double viewWidth, double offset, bool centered)
    {
        if (index < 0 || count <= 0 || itemWidth <= 0 || viewWidth <= 0) return 0;
        if (centered) return Math.Floor(itemWidth * index + itemWidth * .5 - viewWidth * .5);
        var x0 = itemWidth * index; var x1 = x0 + itemWidth;
        if (offset + viewWidth < x1) offset = Math.Max(x0 - (viewWidth - itemWidth), 0);
        if (x0 < offset) offset = x0;
        return Math.Clamp(offset, 0, Math.Max(0, itemWidth * count - viewWidth));
    }
    /// <summary>原 PageSlider.GetFixedIndex：双页静态对齐、首尾优先及同步模式步长。</summary>
    public int GetFixedSliderIndex(int value, int viewPageCount)
    {
        var book = PageSelector.Book;
        if (book is null || book.Setting.PageMode != PageMode.WidePage || value == PageSelector.SelectedIndex || value == 0 || value == PageSelector.MaxIndex && book.Setting.IsSupportedSingleLastPage) return value;
        value = BookTools.WidwPageAlignment(value, book.Setting);
        if (!Config.Current.Slider.IsSyncPageMode) return value;
        var baseIndex = PageSelector.SelectedIndex;
        if (viewPageCount < 2) baseIndex = Math.Min(PageSelector.MaxIndex, baseIndex + (value > baseIndex ? 1 : 0));
        else if (Math.Abs(value - baseIndex) < 2) return baseIndex;
        var delta = value - baseIndex; return baseIndex + delta - delta % 2;
    }
}
/// <summary>原 BookTools 中实际使用的静态双页对齐算法，保留特殊拼写。</summary>
public static class BookTools
{
    /// <summary>首页单独时奇数对齐，否则偶数对齐；非静态双页原值不变。</summary>
    public static int WidwPageAlignment(int value, BookSettingConfig setting) => !Config.Current.Book.IsStaticWidePage ? value
        : setting.IsSupportedSingleFirstPage ? ((value - 1) & ~1) + 1 : value & ~1;
}
