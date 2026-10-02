using System.Globalization;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;
using NeeView;
namespace NeeView.MacOS.Views;

/// <summary>原胶片条的显示适配；选择/索引规则来自 Engine，导航器复用单图资源。</summary>
public sealed class ThumbnailView : Control, IDisposable
{
    private sealed record Display(Bitmap Bitmap, BitmapLease Lease) : IDisposable
    {
        /// <summary>先释放显示缓冲，再归还受预算保护的像素租约。</summary>
        public void Dispose() { Bitmap.Dispose(); Lease.Dispose(); }
    }
    private readonly Dictionary<Page, Display> _images = [];
    private BookOperation? _operation;
    private BitmapFactory? _factory;
    private CancellationTokenSource? _request, _details;
    private int _revision, _targetSize;
    private Page[] _requestedPages = [];
    private Task _loadTask = Task.CompletedTask;
    private double _offset, _wheel;
    private Page? _hoverPage;
    private Book? _displayBook;
    private bool _disposed;
    public bool IsNavigator { get; set; }
    public int DisplayCount => _images.Count;
    public double HorizontalOffset => _offset;
    public string? DetailText { get; private set; }
    public event EventHandler<int>? PageRequested;
    public event EventHandler<int>? FrameMoveRequested;
    public event EventHandler<PointerWheelEventArgs>? GlobalWheelRequested;
    public event EventHandler<Point>? NavigateRequested;
    private double CellWidth => Config.Current.FilmStrip.ImageWidth + 12;
    private FilmStrip? Model => _operation?.FilmStrip;

    /// <summary>装配原选择状态和共享工厂；不访问具体文件或解码后端。</summary>
    public void Attach(BookOperation operation, BitmapFactory factory)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_operation is not null) _operation.PageSelector.SelectionChanged -= Selection_Changed;
        // 控件重绑也必须结束旧请求和租约，不能让旧工厂晚到结果混入新操作实例。
        ++_revision; _request?.Cancel(); ClearDetail();
        foreach (var image in _images.Values) image.Dispose(); _images.Clear();
        _requestedPages = []; _targetSize = 0; _loadTask = Task.CompletedTask; _displayBook = null; _offset = 0;
        _operation = operation; _factory = factory; Focusable = true;
        if (!IsNavigator) operation.PageSelector.SelectionChanged += Selection_Changed;
    }
    /// <summary>临时选择只更新胶片条，不请求正文图像；后台回报切 UI 线程。</summary>
    private void Selection_Changed(object? sender, EventArgs e)
    {
        if (Dispatcher.UIThread.CheckAccess()) _ = RefreshAsync();
        else Dispatcher.UIThread.Post(() => { if (!_disposed) _ = RefreshAsync(); });
    }
    /// <summary>按原可视顺序将槽索引转换为 Page.Index。</summary>
    private int PageIndex(int slot) => Model?.GetIndexWithDirectionReverse(slot) ?? slot;
    /// <summary>计算实际可见槽；负偏移用于原首尾居中补偿，不产生整书控件。</summary>
    private (int Start, int Count) VisibleRange()
    {
        int total = _operation?.Book?.Pages.Count ?? 0;
        int start = Math.Clamp((int)Math.Floor(_offset / CellWidth), 0, total);
        int end = Math.Clamp((int)Math.Ceiling((_offset + Bounds.Width) / CellWidth), start, total);
        return (start, end - start);
    }
    /// <summary>原 200ms 请求防抖及序列去重；同一可见窗口复用未完成的资源任务。</summary>
    /// <param name="keepSelectionVisible">选择改变时保持可见，手动滚动时保留浏览位置。</param>
    public Task RefreshAsync(bool keepSelectionVisible = true)
    {
        if (_disposed || _operation is null || _factory is null) return Task.CompletedTask;
        var book = _operation.Book;
        if (!ReferenceEquals(book, _displayBook)) { _displayBook = book; ClearDetail(); }
        if (!Config.Current.FilmStrip.IsDetailPopupEnabled) ClearDetail();
        bool visible = book is not null && IsEffectivelyVisible && TopLevel.GetTopLevel(this) is not null && Bounds.Width > 0;
        if (!IsNavigator && visible && keepSelectionVisible)
            _offset = FilmStrip.ScrollIntoView(Model!.SelectedIndex, book!.Pages.Count, CellWidth, Bounds.Width, _offset, Config.Current.FilmStrip.IsSelectedCenter);
        var range = VisibleRange();
        var pages = !visible ? [] : IsNavigator ? book!.CurrentPage is { } current ? new[] { current } : []
            : Model!.RequestThumbnail(range.Start, range.Count, 2, 0);
        var scale = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;
        int target = Math.Clamp((int)Math.Ceiling((IsNavigator ? Math.Max(Bounds.Width, Bounds.Height) : CellWidth) * scale / 32) * 32, 64, 1024);
        InvalidateVisual();
        if (target == _targetSize && _requestedPages.SequenceEqual(pages)) return _loadTask;
        int revision = ++_revision; _request?.Cancel(); var request = new CancellationTokenSource(); _request = request;
        _requestedPages = pages;
        foreach (var page in _images.Keys.Where(p => target != _targetSize || !pages.Contains(p)).ToArray()) { _images[page].Dispose(); _images.Remove(page); }
        _targetSize = target;
        if (pages.Length == 0) { ClearDetail(); _request = null; request.Dispose(); _loadTask = Task.CompletedTask; return _loadTask; }
        _loadTask = LoadAsync(pages, target, request, revision); return _loadTask;
    }
    /// <summary>仅提交中心优先的可见窗口及邻近余量，取消或晚到结果归还资源。</summary>
    private async Task LoadAsync(Page[] pages, int target, CancellationTokenSource request, int revision)
    {
        try
        {
            if (!IsNavigator) await Task.Delay(200, request.Token);
            foreach (var page in pages)
            {
                if (_images.ContainsKey(page)) continue;
                var lease = await _factory!.GetAsync(page, new(target, target, true), request.Token, true);
                if (_disposed || revision != _revision) { lease.Dispose(); return; }
                var pixels = lease.Image; var pin = GCHandle.Alloc(pixels.Pixels, GCHandleType.Pinned);
                Bitmap? bitmap = null;
                try
                {
                    bitmap = new Bitmap(PixelFormat.Bgra8888, AlphaFormat.Premul, pin.AddrOfPinnedObject(), new((int)pixels.Size.Width, (int)pixels.Size.Height), new(96, 96), pixels.Stride);
                    lease.RegisterDisplayBytes(checked((long)bitmap.PixelSize.Width * bitmap.PixelSize.Height * 4));
                }
                catch { bitmap?.Dispose(); lease.Dispose(); throw; }
                finally { pin.Free(); }
                if (_disposed || revision != _revision || request.IsCancellationRequested) { bitmap.Dispose(); lease.Dispose(); return; }
                _images[page] = new(bitmap, lease); InvalidateVisual();
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (revision == _revision) _requestedPages = []; System.Diagnostics.Trace.WriteLine("缩略图：" + ex.Message); }
        finally { if (ReferenceEquals(_request, request)) _request = null; request.Dispose(); }
    }
    /// <summary>绘制临时选择和真实显示范围，两个状态使用不同边框，不改变正文阅读位置。</summary>
    public override void Render(DrawingContext context)
    {
        base.Render(context); context.FillRectangle(new SolidColorBrush(Color.Parse("#202020")), new Avalonia.Rect(Bounds.Size));
        if (_operation?.Book is not { } book) return;
        using var clip = context.PushClip(new Avalonia.Rect(Bounds.Size));
        var range = VisibleRange();
        var pages = IsNavigator ? book.CurrentPage is { } current ? new[] { current } : []
            : Enumerable.Range(range.Start, range.Count).Select(i => book.Pages[PageIndex(i)]).ToArray();
        for (int i = 0; i < pages.Length; i++)
        {
            var page = pages[i];
            var cell = IsNavigator ? new Avalonia.Rect(4, 4, Math.Max(1, Bounds.Width - 8), Math.Max(1, Bounds.Height - 8))
                : new Avalonia.Rect((range.Start + i) * CellWidth - _offset + 4, 4, CellWidth - 8, Math.Max(1, Bounds.Height - 8));
            if (_images.TryGetValue(page, out var image))
            {
                var size = image.Bitmap.Size; var scale = Math.Min(cell.Width / size.Width, cell.Height / size.Height);
                context.DrawImage(image.Bitmap, new Avalonia.Rect(cell.Center.X - size.Width * scale / 2, cell.Center.Y - size.Height * scale / 2, size.Width * scale, size.Height * scale));
            }
            if (_operation.Frame?.Elements.Any(e => !e.IsDummy && ReferenceEquals(e.Page, page)) == true)
                context.DrawRectangle(null, new Pen(Brushes.LightGray, 1), cell);
            if (IsNavigator || page.Index == _operation.PageSelector.SelectedIndex) context.DrawRectangle(null, new Pen(Brushes.DodgerBlue, 2), cell);
            if (!IsNavigator && Config.Current.FilmStrip.IsVisibleNumber)
                context.DrawText(new FormattedText((page.Index + 1).ToString(), CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface("sans-serif"), 11, Brushes.White), cell.TopLeft);
        }
    }
    /// <summary>命中有效图像槽，居中留白不能转换成首尾页。</summary>
    private Page? GetPageAt(Point point)
    {
        if (_operation?.Book is not { } book || point.X < 0 || point.X >= Bounds.Width) return null;
        int slot = (int)Math.Floor((point.X + _offset) / CellWidth);
        return slot >= 0 && slot < book.Pages.Count ? book.Pages[PageIndex(slot)] : null;
    }
    /// <summary>无修饰键左击确认原条目；导航器只处理图像内位置。</summary>
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (_operation?.Book is not { } book || e.KeyModifiers != KeyModifiers.None || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        e.Handled = true; Focus(); var point = e.GetPosition(this);
        if (IsNavigator && book.CurrentPage is { } page && _images.TryGetValue(page, out var image))
        {
            var scale = Math.Min((Bounds.Width - 8) / image.Bitmap.Size.Width, (Bounds.Height - 8) / image.Bitmap.Size.Height);
            var width = image.Bitmap.Size.Width * scale; var height = image.Bitmap.Size.Height * scale;
            if (!new Avalonia.Rect((Bounds.Width - width) / 2, (Bounds.Height - height) / 2, width, height).Contains(point)) return;
            NavigateRequested?.Invoke(this, new(Math.Clamp((point.X - (Bounds.Width - width) / 2) / width, 0, 1), Math.Clamp((point.Y - (Bounds.Height - height) / 2) / height, 0, 1)));
        }
        else if (!IsNavigator && GetPageAt(point) is { } selected)
        { _operation.PageSelector.SetSelectedIndex(this, selected.Index, true); PageRequested?.Invoke(this, selected.Index); }
    }
    /// <summary>处理原胶片条作用域：左右选择，回车确认，上下隔离，Escape 恢复正文选择。</summary>
    public bool HandleSelectionKey(KeyEventArgs e)
    {
        if (IsNavigator || _operation is null || e.KeyModifiers != KeyModifiers.None) return false;
        switch (e.Key)
        {
            case Key.Left: Model!.MoveSelectedIndex(-1); break;
            case Key.Right: Model!.MoveSelectedIndex(1); break;
            case Key.Enter: PageRequested?.Invoke(this, _operation.PageSelector.SelectedIndex); break;
            case Key.Escape: _operation.PageSelector.Synchronize(_operation.Book, _operation.Frame?.FrameRange.Min.Index ?? 0); break;
            case Key.Up: case Key.Down: break;
            default: return false;
        }
        e.Handled = true; return true;
    }
    /// <summary>原三种滚轮模式；水平滚动只浏览窗口，MoveSelection 不直接跳页。</summary>
    protected override async void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (IsNavigator || _operation?.Book is not { } book) return;
        e.Handled = true;
        if (Math.Abs(e.Delta.X) > 0)
        {
            _offset = Math.Clamp(_offset - e.Delta.X * 24, 0, Math.Max(0, book.Pages.Count * CellWidth - Bounds.Width));
            await RefreshAsync(false); return;
        }
        if (Config.Current.FilmStrip.MouseWheelAction == FilmStripMouseWheelAction.CommandDependent) { GlobalWheelRequested?.Invoke(this, e); return; }
        _wheel += e.Delta.Y; int steps = (int)_wheel; _wheel -= steps;
        if (steps == 0) return;
        if (Config.Current.FilmStrip.MouseWheelAction == FilmStripMouseWheelAction.MoveSelection) Model!.MoveSelectedIndex(-steps, Model.IsSliderDirectionReversed);
        else for (int i = 0; i < Math.Abs(steps); i++) FrameMoveRequested?.Invoke(this, -Math.Sign(steps));
    }
    /// <summary>详情按当前悬停条目获取真实尺寸；取消和切书拒绝旧元数据结果。</summary>
    protected override async void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var page = !IsNavigator && Config.Current.FilmStrip.IsDetailPopupEnabled ? GetPageAt(e.GetPosition(this)) : null;
        if (ReferenceEquals(page, _hoverPage)) return;
        ClearDetail(); _hoverPage = page; if (page is null || _operation is null) return;
        var pending = new CancellationTokenSource(); _details = pending;
        try
        {
            SetDetail(page);
            if (!page.Content.HasSize && await _operation.GetPageInformationAsync(page.Index, pending.Token) is { } known && ReferenceEquals(known, page) && !pending.IsCancellationRequested) SetDetail(page);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { System.Diagnostics.Trace.WriteLine("缩略详情：" + ex.Message); }
        finally { if (ReferenceEquals(_details, pending)) _details = null; pending.Dispose(); }
    }
    /// <summary>原详情包含条目名、页码、尺寸及字节数，不通过 UI 读取文件。</summary>
    private void SetDetail(Page page)
    {
        DetailText = $"{page.EntryName}\n{page.Index + 1} / {_operation?.Book?.Pages.Count}\n" + (page.Content.HasSize ? $"{page.Content.PageDataSource.Size.Width:0} × {page.Content.PageDataSource.Size.Height:0}\n" : "") + $"{page.ArchiveEntry.Length:N0} 字节";
        ToolTip.SetTip(this, DetailText);
    }
    /// <summary>离开或隐藏取消元数据需求并关闭详情，防止租约释放后仍显示旧内容。</summary>
    private void ClearDetail() { _details?.Cancel(); _hoverPage = null; DetailText = null; ToolTip.SetIsOpen(this, false); ToolTip.SetTip(this, null); }
    /// <summary>离开胶片条时清理详情，不改变临时选择。</summary>
    protected override void OnPointerExited(PointerEventArgs e) { base.OnPointerExited(e); ClearDetail(); }
    /// <summary>视口变化只更新可见窗口。</summary>
    protected override void OnSizeChanged(SizeChangedEventArgs e) { base.OnSizeChanged(e); _ = RefreshAsync(); }
    /// <summary>隐藏时取消并释放资源。</summary>
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change) { base.OnPropertyChanged(change); if (change.Property == IsVisibleProperty) _ = RefreshAsync(); }
    /// <summary>停靠移出视觉树后不持有缩略资源。</summary>
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e) { base.OnDetachedFromVisualTree(e); _ = RefreshAsync(); }
    /// <summary>关闭解除选择订阅，取消需求并释放全部显示资源。</summary>
    public void Dispose()
    {
        if (_disposed) return; _disposed = true; ++_revision; _request?.Cancel(); ClearDetail();
        if (_operation is not null) _operation.PageSelector.SelectionChanged -= Selection_Changed;
        foreach (var image in _images.Values) image.Dispose(); _images.Clear();
    }
}
