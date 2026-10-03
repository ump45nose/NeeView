using System.Globalization;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using NeeView;
using CoreSize = NeeView.Size;
using CoreBitmapFactory = NeeView.BitmapFactory;
namespace NeeView.MacOS.Views;

/// <summary>原 MainView 的绘制适配；页框与分割规则由迁入的 Engine 计算。</summary>
public sealed class ReaderView : Control, IDisposable
{
    private sealed record Display(Bitmap Bitmap, BitmapLease Lease) : IDisposable
    {
        /// <summary>先释放显示资源，再归还缓存租约。</summary>
        public void Dispose() { Bitmap.Dispose(); Lease.Dispose(); }
    }
    private readonly Dictionary<Page, Display> _images = [];
    private readonly Dictionary<Page, string> _pageErrors = [];
    private BookOperation? _operation;
    private CoreBitmapFactory? _factory;
    private CancellationTokenSource? _request;
    private int _revision;
    private PageFrames.PageFrame? _frame;
    private double _zoom = 1;
    private Avalonia.Vector _pan;
    private Point? _pressed;
    private Avalonia.Vector _initialPan;
    private bool _dragged;
    private bool _bookCardPressed;
    private bool _disposed;
    private Book? _displayBook;
    private PageRange? _displayRange;
    private readonly PageFrames.PageFrameScrollControl _scrollControl = new();
    private readonly SemaphoreSlim _scrollGate = new(1);
    private string? _loadError;
    public event EventHandler<string>? GestureRequested;
    public event EventHandler<Page>? ChildBookRequested;
    public event EventHandler? DisplayCompleted;
    public int DisplayCount => _images.Count;
    /// <summary>当前页的资源结果；空封面是正常状态，错误不会遮蔽同帧其他页。</summary>
    public string? GetPageError(Page page) => _pageErrors.GetValueOrDefault(page);

    /// <summary>只装配业务和像素边界；没有解码器或文件系统依赖。</summary>
    public void Attach(BookOperation operation, CoreBitmapFactory factory)
    {
        _operation = operation; _factory = factory; Focusable = true;
    }
    /// <summary>状态变化触发当前可见帧需求；版本隔离旧解码结果。</summary>
    public async Task RefreshAsync()
    {
        if (_disposed || _operation is null || _factory is null) return;
        var revision = ++_revision; _request?.Cancel(); var request = new CancellationTokenSource(); _request = request;
        _operation.SetViewport(new CoreSize(Bounds.Width, Bounds.Height), TopLevel.GetTopLevel(this)?.RenderScaling ?? 1);
        _frame = _operation.Frame; _loadError = null;
        if (!ReferenceEquals(_displayBook, _operation.Book) || _displayRange != _frame?.FrameRange)
        {
            _displayBook = _operation.Book; _displayRange = _frame?.FrameRange;
            AlignPageOrigin(_operation.MoveDirection);
        }
        ClampPan();
        var sources = _frame?.Elements.Where(e => !e.IsDummy).Select(e => e.Page).Distinct().ToArray() ?? [];
        foreach (var page in _images.Keys.Except(sources).ToArray()) { _images[page].Dispose(); _images.Remove(page); }
        foreach (var page in _pageErrors.Keys.Except(sources).ToArray()) _pageErrors.Remove(page);
        InvalidateVisual();
        try
        {
            foreach (var page in sources)
            {
                BitmapLease lease;
                try { lease = await _factory.GetAsync(page, GetRequest(page), request.Token); }
                catch (EmptyArchivePageException) { if (revision == _revision) _pageErrors[page] = ""; continue; }
                catch (Exception ex) when (ex is not OperationCanceledException)
                { if (revision == _revision) _pageErrors[page] = ex.Message; continue; }
                if (_disposed || revision != _revision) { lease.Dispose(); return; }
                var pixels = lease.Image;
                var handle = GCHandle.Alloc(pixels.Pixels, GCHandleType.Pinned);
                Bitmap? bitmap = null;
                try
                {
                    bitmap = new Bitmap(PixelFormat.Bgra8888, AlphaFormat.Premul, handle.AddrOfPinnedObject(), new PixelSize((int)pixels.Size.Width, (int)pixels.Size.Height), new Avalonia.Vector(96, 96), pixels.Stride);
                    lease.RegisterDisplayBytes(checked((long)bitmap.PixelSize.Width * bitmap.PixelSize.Height * 4));
                }
                catch { bitmap?.Dispose(); lease.Dispose(); throw; }
                finally { handle.Free(); }
                if (_images.Remove(page, out var old)) old.Dispose();
                _pageErrors.Remove(page); _images[page] = new(bitmap, lease); InvalidateVisual();
            }
            if (_disposed || revision != _revision || request.IsCancellationRequested) return;
            InvalidateVisual();
            DisplayCompleted?.Invoke(this, EventArgs.Empty);
            if (_operation.Book is { } book && _frame is { } frame)
            {
                var next = frame.FrameRange.Next().Index;
                if (next < book.Pages.Count) await PrefetchAsync(book.Pages[next], request.Token);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (revision == _revision) { _loadError = ex.Message; InvalidateVisual(); } }
        finally { if (ReferenceEquals(_request, request)) _request = null; request.Dispose(); }
    }
    /// <summary>按设备像素请求图像，128 像素分桶减少窗口小幅调整造成的缓存重复。</summary>
    private DecodeRequest GetRequest(Page page)
    {
        var size = page.Content.PageDataSource.Size;
        var scale = (_frame?.Scale ?? 1) * _zoom * (TopLevel.GetTopLevel(this)?.RenderScaling ?? 1);
        return new(Math.Max(128, (int)Math.Ceiling(size.Width * scale / 128) * 128), Math.Max(128, (int)Math.Ceiling(size.Height * scale / 128) * 128));
    }
    /// <summary>邻页预取只有一个背景槽，取消或损坏不影响当前图。</summary>
    private async Task PrefetchAsync(Page page, CancellationToken token)
    {
        try { using var lease = await _factory!.GetAsync(page, GetRequest(page), token, true); }
        catch { /* 预取错误由实际访问时显示；不覆盖当前书籍状态。 */ }
    }
    /// <summary>绘制原页框的方向、空白页、缩放与归一化裁剪区域。</summary>
    public override void Render(DrawingContext context)
    {
        base.Render(context); context.FillRectangle(Brushes.Black, new Avalonia.Rect(Bounds.Size));
        if (_frame is null)
        {
            DrawText(context, _operation?.Book is null ? "NeeView\n打开图片、目录或 ZIP / CBZ" : "这个来源没有可阅读的图片", new(30, 30)); return;
        }
        var sources = _frame.GetDirectedSources().ToArray();
        var scale = _frame.Scale * _zoom;
        var width = sources.Sum(e => e.Width) * scale + _frame.TotalSpan * _zoom;
        var height = sources.Max(e => e.Height) * scale;
        using var clip = context.PushClip(new Avalonia.Rect(Bounds.Size));
        var center = new Point(Bounds.Width / 2 + _pan.X, Bounds.Height / 2 + _pan.Y);
        using var rotate = context.PushTransform(Matrix.CreateTranslation(-center.X, -center.Y) * Matrix.CreateRotation(_frame.Angle * Math.PI / 180) * Matrix.CreateTranslation(center.X, center.Y));
        double left = center.X - width / 2;
        foreach (var source in sources)
        {
            var target = new Avalonia.Rect(left, center.Y - source.Height * scale / 2, source.Width * scale, source.Height * scale);
            if (source.IsDummy) context.FillRectangle(Brushes.White, target);
            else if (source.Page.PageType.IsFolder())
                ArchivePageRenderer.Draw(this, context, source.Page, target, _images.GetValueOrDefault(source.Page)?.Bitmap,
                    _pageErrors.GetValueOrDefault(source.Page) ?? "正在加载…");
            else if (_images.TryGetValue(source.Page, out var image))
            {
                var crop = source.ViewSizeCalculator.GetViewBox();
                var pixels = image.Bitmap.PixelSize;
                context.DrawImage(image.Bitmap, new Avalonia.Rect(crop.X * pixels.Width, crop.Y * pixels.Height, crop.Width * pixels.Width, crop.Height * pixels.Height), target);
            }
            else
            {
                context.FillRectangle(new SolidColorBrush(Color.Parse("#202020")), target);
                DrawText(context, source.Page.Content.Error ?? _pageErrors.GetValueOrDefault(source.Page) ?? _loadError ?? "正在加载…", target.TopLeft + new Avalonia.Vector(12, 12));
            }
            left += target.Width + _frame.Span * _zoom;
        }
        if (_loadError is not null) DrawText(context, _loadError, new(12, 12));
    }
    /// <summary>错误和空书籍保持可操作，文本不改变来源索引。</summary>
    private static void DrawText(DrawingContext context, string text, Point position) => context.DrawText(new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface("sans-serif"), 13, Brushes.LightGray), position);
    /// <summary>围绕指针位置缩放，像素需求随缩放更新。</summary>
    public async Task ZoomAsync(double factor, Point? pointer = null)
    {
        var origin = pointer ?? new Point(Bounds.Width / 2, Bounds.Height / 2);
        var old = _zoom; _zoom = Math.Clamp(_zoom * factor, .1, 16); var ratio = _zoom / old;
        var relative = origin - new Point(Bounds.Width / 2, Bounds.Height / 2);
        _pan = _pan * ratio + relative * (1 - ratio);
        ClampPan();
        await RefreshAsync();
    }
    /// <summary>重置手工缩放及平移，100% 由引擎 StretchMode.None 与设备比例决定。</summary>
    public void ResetTransform() { _zoom = 1; _pan = default; InvalidateVisual(); }
    /// <summary>高精度滚动平移；分页导航由输入映射处理。</summary>
    public void Pan(Avalonia.Vector delta) { _pan += delta; ClampPan(); InvalidateVisual(); }
    /// <summary>沿用原 DragArea.SnapView，精确滚动和拖动不允许把图片完全推出视口。</summary>
    private void ClampPan()
    {
        if (_frame is null || Bounds.Width <= 0 || Bounds.Height <= 0) return;
        var delta = new DragArea(new(0, 0, Bounds.Width, Bounds.Height), GetContentRect()).SnapView(true);
        _pan += new Avalonia.Vector(delta.X, delta.Y);
    }
    /// <summary>原 ScrollToNextFrame：先应用 NScroll 位移，终止后才进入原帧导航。</summary>
    /// <param name="direction">下一帧为 1，上一帧为 -1。</param>
    /// <param name="parameter">原滚动类型、步幅、终端容差与换行停顿。</param>
    public async Task ScrollToNextFrameAsync(int direction, ScrollPageCommandParameter parameter)
    {
        await _scrollGate.WaitAsync();
        try
        {
            if (_disposed || _frame is null || _operation?.Context is not { } context || _operation.IsLoading || Bounds.Width <= 0 || Bounds.Height <= 0) return;
            var result = _scrollControl.ScrollToNext(context, GetContentRect(), new(0, 0, Bounds.Width, Bounds.Height), direction, parameter);
            if (result is null) return;
            if (!result.IsTerminated) { Pan(new(result.Vector.X, result.Vector.Y)); return; }
            // 导航仍走 BookOperation 的原帧范围；切书后不能应用旧命令的原点。
            var book = _operation.Book; var position = _operation.Position;
            await _operation.MoveAsync(direction);
            if (_disposed || !ReferenceEquals(book, _operation.Book) || position == _operation.Position) return;
            await RefreshAsync();
        }
        finally { _scrollGate.Release(); }
    }
    /// <summary>将原页框实际绘制尺寸转为轴对齐矩形，含双页间距、分割及旋转。</summary>
    public NeeView.Rect GetContentRect()
    {
        if (_frame is null) return default;
        var sources = _frame.GetDirectedSources().ToArray(); var scale = _frame.Scale * _zoom;
        var size = new CoreSize(sources.Sum(e => e.Width) * scale + _frame.TotalSpan * _zoom, sources.Max(e => e.Height) * scale);
        var rotated = PageFrames.GeometryMath.RotateSize(size, _frame.Angle);
        return new((Bounds.Width - rotated.Width) / 2 + _pan.X, (Bounds.Height - rotated.Height) / 2 + _pan.Y, rotated.Width, rotated.Height);
    }
    /// <summary>新帧按阅读与移动方向进入起点；普通刷新/手工缩放保留当前平移。</summary>
    private void AlignPageOrigin(int direction)
    {
        _pan = default;
        var rect = GetContentRect();
        var x = Math.Max(0, rect.Width - Bounds.Width) / 2; var y = Math.Max(0, rect.Height - Bounds.Height) / 2;
        var readDirection = _operation?.Context?.ReadOrder.ToSign() ?? 1;
        _pan = new(direction * readDirection > 0 ? x : -x, direction > 0 ? y : -y);
    }
    /// <summary>导航器将图像内指定位置移到视口中心；页框及旋转仍由原引擎计算。</summary>
    public void Navigate(Point point)
    {
        if (_frame is null || _operation?.Book?.CurrentPage is not { } page) return;
        var sources = _frame.GetDirectedSources().ToArray();
        var scale = _frame.Scale * _zoom;
        var width = sources.Sum(e => e.Width) * scale + _frame.TotalSpan * _zoom;
        double left = -width / 2;
        foreach (var source in sources)
        {
            if (!source.IsDummy && ReferenceEquals(source.Page, page))
            {
                // 导航器展示主图片原图；双页需定位到该页，分割页先转换到当前裁剪区。
                var crop = source.ViewSizeCalculator.GetViewBox();
                var x = Math.Clamp((point.X - crop.X) / crop.Width, 0, 1);
                var y = Math.Clamp((point.Y - crop.Y) / crop.Height, 0, 1);
                var offset = new Avalonia.Vector(left + source.Width * scale * x, source.Height * scale * (y - .5));
                var angle = _frame.Angle * Math.PI / 180;
                _pan = new(-offset.X * Math.Cos(angle) + offset.Y * Math.Sin(angle), -offset.X * Math.Sin(angle) - offset.Y * Math.Cos(angle));
                InvalidateVisual(); return;
            }
            left += source.Width * scale + _frame.Span * _zoom;
        }
    }
    /// <summary>视口变化后只请求可见帧，避免扫描目录。</summary>
    protected override async void OnSizeChanged(SizeChangedEventArgs e) { base.OnSizeChanged(e); await RefreshAsync(); }
    /// <summary>按下记录拖动起点，释放时才确认是否为翻页点击。</summary>
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e); Focus();
        if (GetBookPageAt(e.GetPosition(this)) is { } page)
        {
            _bookCardPressed = true; e.Handled = true;
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed && e.ClickCount == 2) ChildBookRequested?.Invoke(this, page);
            return;
        }
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) { _pressed = e.GetPosition(this); _initialPan = _pan; _dragged = false; e.Pointer.Capture(this); }
    }
    /// <summary>拖动大图只修改表现变换。</summary>
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_pressed is not { } start) return;
        var position = e.GetPosition(this); var delta = new Avalonia.Vector(position.X - start.X, position.Y - start.Y);
        if (delta.Length > 4) _dragged = true;
        if (_dragged) { _pan = _initialPan + delta; ClampPan(); InvalidateVisual(); }
    }
    /// <summary>未拖动的左右点击送入原快捷键映射。</summary>
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e); e.Pointer.Capture(null);
        if (!_dragged && !_bookCardPressed)
        {
            var gesture = e.InitialPressMouseButton switch { MouseButton.Left => "LeftClick", MouseButton.Right => "RightClick", MouseButton.Middle => "MiddleClick", _ => null };
            if (gesture is not null) GestureRequested?.Invoke(this, gesture);
        }
        _pressed = null; _dragged = false; _bookCardPressed = false;
    }
    /// <summary>按原封面按钮区域命中书籍页，单击不翻页，双击打开实际命中项。</summary>
    private Page? GetBookPageAt(Point point)
    {
        if (_frame is null) return null;
        var center = new Point(Bounds.Width / 2 + _pan.X, Bounds.Height / 2 + _pan.Y);
        var angle = -_frame.Angle * Math.PI / 180; var relative = point - center;
        var local = center + new Avalonia.Vector(relative.X * Math.Cos(angle) - relative.Y * Math.Sin(angle), relative.X * Math.Sin(angle) + relative.Y * Math.Cos(angle));
        var sources = _frame.GetDirectedSources().ToArray(); var scale = _frame.Scale * _zoom;
        double left = center.X - (sources.Sum(e => e.Width) * scale + _frame.TotalSpan * _zoom) / 2;
        foreach (var source in sources)
        {
            var target = new Avalonia.Rect(left, center.Y - source.Height * scale / 2, source.Width * scale, source.Height * scale);
            if (!source.IsDummy && source.Page.PageType.IsFolder() && ArchivePageRenderer.CoverArea(target).Contains(local)) return source.Page;
            left += target.Width + _frame.Span * _zoom;
        }
        return null;
    }
    /// <summary>释放当前需求和所有显示租约，晚到结果按 revision 拒绝。</summary>
    public void Dispose()
    {
        if (_disposed) return; _disposed = true; ++_revision; _request?.Cancel(); _request = null;
        foreach (var item in _images.Values) item.Dispose(); _images.Clear(); _pageErrors.Clear();
    }
}
