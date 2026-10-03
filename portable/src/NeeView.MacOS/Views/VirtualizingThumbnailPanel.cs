using System.Collections.Specialized;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.VisualTree;
using NeeView.MacOS.ViewModels;
namespace NeeView.MacOS.Views;

/// <summary>原虚拟缩略图网格的Avalonia适配；只有可见行、邻行和焦点容器实例化。</summary>
public sealed class VirtualizingThumbnailPanel : VirtualizingPanel
{
    private sealed record Realized(Control Control, object? RecycleKey, bool Own);
    private readonly Dictionary<int, Realized> _realized = [];
    private readonly Dictionary<object, Stack<Control>> _pool = [];
    private Avalonia.Rect _viewport;
    private Avalonia.Rect[] _cells = [];
    private double _width, _height;
    private int _columns = 1;
    private bool _dirty = true;
    private IScrollAnchorProvider? _anchors;
    public double CellWidth { get; init; } = 144;
    public double CellHeight { get; init; } = 174;
    public int RealizedCount => _realized.Count;
    public int Columns => _columns;
    /// <summary>有效视口由ScrollViewer发布，滚动只重新计算可见范围。</summary>
    public VirtualizingThumbnailPanel() => EffectiveViewportChanged += ViewportChanged;
    private void ViewportChanged(object? sender, EffectiveViewportChangedEventArgs e)
    { if (_viewport != e.EffectiveViewport) { _viewport = e.EffectiveViewport; InvalidateMeasure(); } }
    /// <summary>将可见容器登记给原生物理滚动锚点，宽度重排不凭数组下标猜滚动位置。</summary>
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    { base.OnAttachedToVisualTree(e); _anchors = this.FindAncestorOfType<IScrollAnchorProvider>(); }
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    { foreach (var item in _realized.Values) _anchors?.UnregisterAnchorCandidate(item.Control); _anchors = null; base.OnDetachedFromVisualTree(e); }
    /// <summary>宽度变化按原固定项目大小换列；日期分组在新行开始，不产生可选择的假条目。</summary>
    private void BuildCells(double width)
    {
        _width = width; _columns = Math.Max(1, (int)(width / Math.Max(24, CellWidth))); _cells = new Avalonia.Rect[Items.Count];
        double y = 0; int column = 0;
        for (int i = 0; i < Items.Count; i++)
        {
            bool header = Items[i] is HistoryRow { HasGroupHeader: true };
            if (header && column > 0) { y += CellHeight; column = 0; }
            double headerHeight = header ? 28 : 0;
            if (header) y += headerHeight;
            _cells[i] = new(column * CellWidth, y - headerHeight, Math.Min(CellWidth, width), CellHeight + headerHeight);
            if (++column == _columns) { column = 0; y += CellHeight; }
        }
        _height = y + (column > 0 ? CellHeight : 0); _dirty = false;
    }
    /// <summary>布局元数据不创建控件；二分定位首行，滚动成本与可见行数有关。</summary>
    protected override Avalonia.Size MeasureOverride(Avalonia.Size availableSize)
    {
        var width = double.IsFinite(availableSize.Width) ? Math.Max(1, availableSize.Width) : Math.Max(CellWidth, Bounds.Width);
        if (_dirty || Math.Abs(width - _width) > .1) BuildCells(width);
        var viewport = _viewport.Height > 0 && double.IsFinite(_viewport.Height) ? _viewport : new Avalonia.Rect(0, 0, width, 1);
        double top = Math.Max(0, viewport.Top - CellHeight), bottom = viewport.Bottom + CellHeight;
        int low = 0, high = _cells.Length;
        while (low < high) { int mid = (low + high) / 2; if (_cells[mid].Bottom < top) low = mid + 1; else high = mid; }
        var keep = new HashSet<int>();
        for (int i = low; i < _cells.Length && _cells[i].Top <= bottom; i++) { keep.Add(i); Ensure(i).Measure(_cells[i].Size); }
        foreach (var pair in _realized.ToArray())
            if (!keep.Contains(pair.Key) && !pair.Value.Control.IsKeyboardFocusWithin && !pair.Value.Own) Recycle(pair.Key);
        return new(width, _height);
    }
    /// <summary>容器始终使用完整内容坐标，物理ScrollViewer负责平移。</summary>
    protected override Avalonia.Size ArrangeOverride(Avalonia.Size finalSize)
    {
        foreach (var pair in _realized) if (pair.Key < _cells.Length)
        {
            pair.Value.Control.Arrange(_cells[pair.Key]);
            if (_viewport.Intersects(_cells[pair.Key])) _anchors?.RegisterAnchorCandidate(pair.Value.Control);
            else _anchors?.UnregisterAnchorCandidate(pair.Value.Control);
        }
        return finalSize;
    }
    /// <summary>通过唯一ItemContainerGenerator准备选择/模板，回收池不超过曾可见的容器数。</summary>
    private Control Ensure(int index)
    {
        if (_realized.TryGetValue(index, out var realized)) return realized.Control;
        var generator = ItemContainerGenerator!; var item = Items[index]; bool own = !generator.NeedsContainer(item, index, out var key);
        var control = own ? (Control)item! : key is not null && _pool.TryGetValue(key, out var pool) && pool.Count > 0 ? pool.Pop() : generator.CreateContainer(item, index, key);
        generator.PrepareItemContainer(control, item, index); AddInternalChild(control); generator.ItemContainerPrepared(control, item, index);
        _realized[index] = new(control, key, own); return control;
    }
    /// <summary>移出视觉树立即结束封面需求；清除选择绑定后按兼容key回收。</summary>
    private void Recycle(int index)
    {
        var item = _realized[index]; _realized.Remove(index); _anchors?.UnregisterAnchorCandidate(item.Control); RemoveInternalChild(item.Control);
        if (!item.Own) ItemContainerGenerator!.ClearItemContainer(item.Control);
        if (!item.Own && item.RecycleKey is { } key) { if (!_pool.TryGetValue(key, out var pool)) _pool[key] = pool = new(); pool.Push(item.Control); }
    }
    protected override void OnItemsChanged(IReadOnlyList<object?> items, NotifyCollectionChangedEventArgs e)
    { foreach (var index in _realized.Keys.ToArray()) Recycle(index); _dirty = true; InvalidateMeasure(); }
    protected override Control? ContainerFromIndex(int index) => _realized.GetValueOrDefault(index)?.Control;
    protected override int IndexFromContainer(Control container) => _realized.FirstOrDefault(e => ReferenceEquals(e.Value.Control, container)).Value is null ? -1 : _realized.First(e => ReferenceEquals(e.Value.Control, container)).Key;
    protected override IEnumerable<Control> GetRealizedContainers() => _realized.Values.Select(e => e.Control);
    /// <summary>直接定位离屏项，只实现目标容器；不逐行创建全部中间项。</summary>
    protected override Control? ScrollIntoView(int index)
    {
        if (index < 0 || index >= Items.Count || ItemsControl is null) return null;
        if (_dirty) BuildCells(Math.Max(1, Bounds.Width));
        var control = Ensure(index); control.Measure(_cells[index].Size); control.Arrange(_cells[index]); control.BringIntoView(); InvalidateMeasure(); return control;
    }
    /// <summary>保留原网格左右换行、上下同列及Home/End语义，不接管Enter打开业务。</summary>
    protected override IInputElement? GetControl(NavigationDirection direction, IInputElement? from, bool wrap)
    {
        if (Items.Count == 0) return null;
        var source = from as Control; var container = source as ListBoxItem ?? source?.GetVisualAncestors().OfType<ListBoxItem>().FirstOrDefault();
        int index = container is not null ? IndexFromContainer(container) : -1;
        int target = direction switch
        { NavigationDirection.First => 0, NavigationDirection.Last => Items.Count - 1,
          NavigationDirection.Left or NavigationDirection.Previous => index - 1, NavigationDirection.Right or NavigationDirection.Next => index + 1,
          NavigationDirection.Up => index - _columns, NavigationDirection.Down => index + _columns, _ => index };
        if (index < 0 && direction != NavigationDirection.Last) target = 0;
        if (wrap) target = (target % Items.Count + Items.Count) % Items.Count;
        return target >= 0 && target < Items.Count ? ScrollIntoView(target) : container;
    }
}
