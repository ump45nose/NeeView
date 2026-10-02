using System.Globalization;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using NeeView;
namespace NeeView.MacOS.Views;

/// <summary>原胶片条的显示适配，只解码可见窗口；导航器复用单图缩略展示。</summary>
public sealed class ThumbnailView : Control, IDisposable
{
    private sealed record Display(Bitmap Bitmap, BitmapLease Lease) : IDisposable
    {
        /// <summary>先释放显示缓冲，再归还受字节预算保护的像素。</summary>
        public void Dispose() { Bitmap.Dispose(); Lease.Dispose(); }
    }
    private readonly Dictionary<Page, Display> _images = [];
    private BookOperation? _operation;
    private BitmapFactory? _factory;
    private CancellationTokenSource? _request;
    private int _revision;
    private int _start;
    private double _horizontalWheel;
    private bool _disposed;
    public bool IsNavigator { get; set; }
    public int DisplayCount => _images.Count;
    public event EventHandler<int>? PageRequested;
    public event EventHandler<Point>? NavigateRequested;
    private double CellWidth => Math.Clamp(Config.Current.FilmStrip.ImageWidth, 32, 512) + 12;
    private bool Reverse => _operation?.Book?.Setting.BookReadOrder == PageReadOrder.RightToLeft;

    /// <summary>接入原页面集合和共享加载工厂，控件不读取文件。</summary>
    public void Attach(BookOperation operation, BitmapFactory factory) { _operation = operation; _factory = factory; Focusable = true; }
    /// <summary>按阅读方向将胶片条位置转换为原 Page.Index。</summary>
    private int PageIndex(int slot) => Reverse ? (_operation?.Book?.Pages.Count ?? 0) - slot - 1 : slot;
    /// <summary>保持当前选择可见，尺寸改变只重算可见窗口。</summary>
    public async Task RefreshAsync(bool keepSelectionVisible = true)
    {
        if (_disposed || _operation is null || _factory is null) return;
        var revision = ++_revision; _request?.Cancel(); var request = new CancellationTokenSource(); _request = request;
        var book = _operation.Book;
        var count = Math.Max(1, (int)Math.Ceiling(Bounds.Width / CellWidth));
        var selected = book?.CurrentPage?.Index ?? 0;
        var slot = Reverse ? (book?.Pages.Count ?? 1) - selected - 1 : selected;
        if (keepSelectionVisible && Config.Current.FilmStrip.IsSelectedCenter) _start = Math.Max(0, slot - count / 2);
        else if (keepSelectionVisible && (slot < _start || slot >= _start + count)) _start = Math.Max(0, slot - count / 2);
        _start = Math.Clamp(_start, 0, Math.Max(0, (book?.Pages.Count ?? 0) - count));
        var pages = book is null || !IsVisible || Bounds.Width <= 0 ? [] : IsNavigator
            ? book.CurrentPage is { } current ? new[] { current } : []
            : Enumerable.Range(_start, Math.Min(count, book.Pages.Count - _start)).Select(i => book.Pages[PageIndex(i)]).ToArray();
        foreach (var page in _images.Keys.Except(pages).ToArray()) { _images[page].Dispose(); _images.Remove(page); }
        InvalidateVisual();
        try
        {
            var scale = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;
            var size = IsNavigator ? Math.Max(Bounds.Width, Bounds.Height) : CellWidth;
            // 缩略规格固定分桶，缩放与翻页不会请求原图；后台共用工厂的单槽。
            int target = Math.Clamp((int)Math.Ceiling(size * scale / 32) * 32, 64, 1024);
            foreach (var page in pages)
            {
                var lease = await _factory.GetAsync(page, new(target, target, true), request.Token, true);
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
                if (_images.Remove(page, out var old)) old.Dispose();
                _images[page] = new(bitmap, lease); InvalidateVisual();
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { System.Diagnostics.Trace.WriteLine("缩略图：" + ex.Message); }
        finally { if (ReferenceEquals(_request, request)) _request = null; request.Dispose(); }
    }
    /// <summary>绘制可见缩略图与选择框，没有按书籍总页数创建控件。</summary>
    public override void Render(DrawingContext context)
    {
        base.Render(context); context.FillRectangle(new SolidColorBrush(Color.Parse("#202020")), new Avalonia.Rect(Bounds.Size));
        if (_operation?.Book is not { } book) return;
        using var clip = context.PushClip(new Avalonia.Rect(Bounds.Size));
        var pages = IsNavigator ? book.CurrentPage is { } selected ? new[] { selected } : []
            : Enumerable.Range(_start, Math.Min(Math.Max(1, (int)Math.Ceiling(Bounds.Width / CellWidth)), Math.Max(0, book.Pages.Count - _start))).Select(i => book.Pages[PageIndex(i)]).ToArray();
        for (int i = 0; i < pages.Length; i++)
        {
            var page = pages[i];
            var cell = IsNavigator ? new Avalonia.Rect(4, 4, Math.Max(1, Bounds.Width - 8), Math.Max(1, Bounds.Height - 8)) : new Avalonia.Rect(i * CellWidth + 4, 4, CellWidth - 8, Math.Max(1, Bounds.Height - 8));
            if (_images.TryGetValue(page, out var image))
            {
                var size = image.Bitmap.Size; var scale = Math.Min(cell.Width / size.Width, cell.Height / size.Height);
                var target = new Avalonia.Rect(cell.Center.X - size.Width * scale / 2, cell.Center.Y - size.Height * scale / 2, size.Width * scale, size.Height * scale);
                context.DrawImage(image.Bitmap, target);
            }
            if (ReferenceEquals(page, book.CurrentPage)) context.DrawRectangle(null, new Pen(Brushes.DodgerBlue, 2), cell);
            if (!IsNavigator && Config.Current.FilmStrip.IsVisibleNumber)
                context.DrawText(new FormattedText((page.Index + 1).ToString(), CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface("sans-serif"), 11, Brushes.White), cell.TopLeft);
        }
    }
    /// <summary>点击胶片条按原索引定位；导航器返回图像内归一化位置。</summary>
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (_operation?.Book is not { } book || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        e.Handled = true; Focus(); var point = e.GetPosition(this);
        if (IsNavigator && book.CurrentPage is { } page && _images.TryGetValue(page, out var image))
        {
            var scale = Math.Min((Bounds.Width - 8) / image.Bitmap.Size.Width, (Bounds.Height - 8) / image.Bitmap.Size.Height);
            var width = image.Bitmap.Size.Width * scale; var height = image.Bitmap.Size.Height * scale;
            // 导航器留白不属于图像，避免点击留白把查看器意外定位到边缘。
            if (!new Avalonia.Rect((Bounds.Width - width) / 2, (Bounds.Height - height) / 2, width, height).Contains(point)) return;
            NavigateRequested?.Invoke(this, new(Math.Clamp((point.X - (Bounds.Width - width) / 2) / width, 0, 1), Math.Clamp((point.Y - (Bounds.Height - height) / 2) / height, 0, 1)));
        }
        else if (!IsNavigator)
        {
            int index = PageIndex(_start + (int)(point.X / CellWidth));
            if (index >= 0 && index < book.Pages.Count) PageRequested?.Invoke(this, index);
        }
    }
    /// <summary>滚轮选择原序列中的邻页；高精度水平滚动只移动胶片条窗口。</summary>
    protected override async void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (IsNavigator || _operation?.Book is not { } book) return;
        e.Handled = true;
        if (Math.Abs(e.Delta.X) > 0)
        {
            // 框架滚轮增量为逻辑步数；累积小数，手动浏览期间不被选中居中逻辑拉回。
            _horizontalWheel -= e.Delta.X; int steps = (int)_horizontalWheel; _horizontalWheel -= steps;
            _start = Math.Clamp(_start + steps, 0, Math.Max(0, book.Pages.Count - 1));
            if (steps != 0) await RefreshAsync(false);
        }
        else if (e.Delta.Y != 0) PageRequested?.Invoke(this, Math.Clamp((book.CurrentPage?.Index ?? 0) - Math.Sign(e.Delta.Y), 0, Math.Max(0, book.Pages.Count - 1)));
    }
    /// <summary>只在实际视口改变时补齐可见图像。</summary>
    protected override async void OnSizeChanged(SizeChangedEventArgs e) { base.OnSizeChanged(e); await RefreshAsync(); }
    /// <summary>隐藏时取消需求并释放显示租约，重新显示后恢复可见窗口。</summary>
    protected override async void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsVisibleProperty) await RefreshAsync();
    }
    /// <summary>关闭拒绝晚到请求，释放全部显示资源。</summary>
    public void Dispose()
    {
        if (_disposed) return; _disposed = true; ++_revision; _request?.Cancel();
        foreach (var image in _images.Values) image.Dispose(); _images.Clear();
    }
}
