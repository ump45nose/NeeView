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
    private bool _disposed;
    private string? _loadError;
    public event EventHandler<string>? GestureRequested;
    public event EventHandler? DisplayCompleted;

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
        var sources = _frame?.Elements.Where(e => !e.IsDummy).Select(e => e.Page).Distinct().ToArray() ?? [];
        foreach (var page in _images.Keys.Except(sources).ToArray()) { _images[page].Dispose(); _images.Remove(page); }
        InvalidateVisual();
        try
        {
            foreach (var page in sources)
            {
                var lease = await _factory.GetAsync(page, GetRequest(page), request.Token);
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
                _images[page] = new(bitmap, lease); InvalidateVisual();
            }
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
            else if (_images.TryGetValue(source.Page, out var image))
            {
                var crop = source.ViewSizeCalculator.GetViewBox();
                var pixels = image.Bitmap.PixelSize;
                context.DrawImage(image.Bitmap, new Avalonia.Rect(crop.X * pixels.Width, crop.Y * pixels.Height, crop.Width * pixels.Width, crop.Height * pixels.Height), target);
            }
            else
            {
                context.FillRectangle(new SolidColorBrush(Color.Parse("#202020")), target);
                DrawText(context, source.Page.Content.Error ?? _loadError ?? "正在加载…", target.TopLeft + new Avalonia.Vector(12, 12));
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
        await RefreshAsync();
    }
    /// <summary>重置手工缩放及平移，100% 由引擎 StretchMode.None 与设备比例决定。</summary>
    public void ResetTransform() { _zoom = 1; _pan = default; InvalidateVisual(); }
    /// <summary>高精度滚动平移；分页导航由输入映射处理。</summary>
    public void Pan(Avalonia.Vector delta) { _pan += delta; InvalidateVisual(); }
    /// <summary>视口变化后只请求可见帧，避免扫描目录。</summary>
    protected override async void OnSizeChanged(SizeChangedEventArgs e) { base.OnSizeChanged(e); await RefreshAsync(); }
    /// <summary>按下记录拖动起点，释放时才确认是否为翻页点击。</summary>
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e); Focus();
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) { _pressed = e.GetPosition(this); _initialPan = _pan; _dragged = false; e.Pointer.Capture(this); }
    }
    /// <summary>拖动大图只修改表现变换。</summary>
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_pressed is not { } start) return;
        var position = e.GetPosition(this); var delta = new Avalonia.Vector(position.X - start.X, position.Y - start.Y);
        if (delta.Length > 4) _dragged = true;
        if (_dragged) { _pan = _initialPan + delta; InvalidateVisual(); }
    }
    /// <summary>未拖动的左右点击送入原快捷键映射。</summary>
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e); e.Pointer.Capture(null);
        if (!_dragged) GestureRequested?.Invoke(this, e.InitialPressMouseButton == MouseButton.Right ? "RightClick" : "LeftClick");
        _pressed = null; _dragged = false;
    }
    /// <summary>释放当前需求和所有显示租约，晚到结果按 revision 拒绝。</summary>
    public void Dispose()
    {
        if (_disposed) return; _disposed = true; ++_revision; _request?.Cancel(); _request = null;
        foreach (var item in _images.Values) item.Dispose(); _images.Clear();
    }
}
