using System.Globalization;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using NeeView;
using NeeView.PageFrames;
using CoreSize = NeeView.Size;
using CoreBitmapFactory = NeeView.BitmapFactory;
namespace NeeView.MacOS.Views;

/// <summary>原 MainView 的绘制适配；页框与分割规则由迁入的 Engine 计算。</summary>
public sealed class ReaderView : Control, IDisposable
{
    private sealed class Display(Bitmap bitmap, BitmapLease lease) : IDisposable
    {
        private int _references = 1;
        public Bitmap Bitmap { get; } = bitmap;
        public Display Retain() { ++_references; return this; }
        /// <summary>同一显示资源可被当前帧和短暂退出帧共用，字节预算只登记一次。</summary>
        public void Dispose() { if (--_references != 0) return; Bitmap.Dispose(); lease.Dispose(); }
    }
    private sealed record FrameVisual((PageFrameElement Source, Avalonia.Rect Target)[] Targets, Matrix Matrix, NeeView.Rect Bounds,
        Dictionary<Page, Display> Images, Dictionary<Page, string> Errors) : IDisposable
    { public void Dispose() { foreach(var image in Images.Values) image.Dispose(); Images.Clear(); } }
    private readonly ReaderMotionPresenter _motion = new();
    private readonly DispatcherTimer _motionTimer = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private FrameVisual? _outgoing;
    private bool _awaitingTransition;
    private Avalonia.Vector _incomingOffset;
    private TimeSpan _pageDuration;
    private PageMoveType _pageType;
    public bool IsMotionActive => _motion.IsActive || _awaitingTransition;
    public int TransitionDisplayCount => _outgoing?.Images.Count ?? 0;
    private readonly Dictionary<Page, Display> _images = [];
    private readonly Dictionary<Page, string> _pageErrors = [];
    private BookOperation? _operation;
    private CoreBitmapFactory? _factory;
    private CancellationTokenSource? _request;
    private int _revision;
    private PageFrames.PageFrame? _frame;
    private readonly ReaderTransformPresenter _transform = new();
    private double _zoom { get => _transform.Scale; set => _transform.Scale = value; }
    private Avalonia.Vector _pan { get => _transform.Pan; set => _transform.Pan = value; }
    public double TransformScale => _zoom;
    public double TransformAngle => _transform.Angle;
    public bool IsFlipHorizontal => _transform.IsFlipHorizontal;
    public bool IsFlipVertical => _transform.IsFlipVertical;
    private Point? _pointer;
    private Point? _pressed;
    private Avalonia.Vector _initialPan;
    private bool _dragged;
    private bool _bookCardPressed;
    private string? _pendingClick;
    private readonly HashSet<MouseButton> _suppressedButtons = [];
    private readonly MouseSequenceBuilder _sequence = new();
    private bool _sequenceActive;
    private string _sequenceHint = "";
    public Func<bool>? CanStartMouseSequence { get; set; }
    public Func<MouseSequence, bool>? TryMouseSequenceRequested { get; set; }
    public Func<MouseSequence, string?>? MouseSequenceText { get; set; }
    public string MouseSequenceHint => _sequenceHint;
    private bool _disposed;
    private Book? _displayBook;
    private PageRange? _displayRange;
    private readonly PageFrames.PageFrameScrollControl _scrollControl = new();
    private readonly ScrollLock _scrollLock = new();
    private readonly SemaphoreSlim _scrollGate = new(1);
    private string? _loadError;
    /// <summary>同步裁决是否命中命令；宿主异步执行，命中后阻止普通点击/拖动。</summary>
    public Func<string, bool>? TryGestureRequested { get; set; }
    public event EventHandler<Page>? ChildBookRequested;
    public event EventHandler? DisplayCompleted;
    public int DisplayCount => _images.Count;
    /// <summary>当前页的资源结果；空封面是正常状态，错误不会遮蔽同帧其他页。</summary>
    public string? GetPageError(Page page) => _pageErrors.GetValueOrDefault(page);

    /// <summary>表现层持有序列进度与焦点取消，不拥有命令业务。</summary>
    public ReaderView()
    {
        LostFocus += (_, _) => CancelMouseSequence();
        _motionTimer.Tick += (_, _) =>
        {
            if (!_motion.IsPageActive && !_awaitingTransition) ReleaseOutgoing();
            if (!_motion.IsActive) _motionTimer.Stop();
            InvalidateVisual();
        };
        _sequence.GestureProgressed += (_, e) => { _sequenceHint = e.Sequence.IsEmpty ? "" : (MouseSequenceText?.Invoke(e.Sequence) is { } text ? text + "\n" : "") + e.Sequence.GetDisplayString(); InvalidateVisual(); };
    }
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
        SynchronizeFrame(); _loadError = null;
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
            if (_awaitingTransition)
            {
                _awaitingTransition = false; _motion.BeginPage(_incomingOffset, _pageType, _pageDuration);
                if (_motion.IsActive) _motionTimer.Start(); else ReleaseOutgoing();
            }
            if (Config.Current.Mouse.IsHoverScroll && _pointer is { } hover) HoverScroll(hover, true);
            DisplayCompleted?.Invoke(this, EventArgs.Empty);
            if (_operation.Book is { } book && _frame is { } frame)
            {
                var next = frame.FrameRange.Next().Index;
                if (next < book.Pages.Count) await PrefetchAsync(book.Pages[next], request.Token);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (revision == _revision) { ReleaseOutgoing(); _loadError = ex.Message; InvalidateVisual(); } }
        finally { if (ReferenceEquals(_request, request)) _request = null; request.Dispose(); }
    }
    /// <summary>命令与排队中的UI刷新先同步同一帧，避免首图未绘制时丢失手工变换。</summary>
    private void SynchronizeFrame()
    {
        if (_operation is null || _disposed) return;
        _operation.SetViewport(new CoreSize(Bounds.Width, Bounds.Height), TopLevel.GetTopLevel(this)?.RenderScaling ?? 1);
        var nextFrame = _operation.Frame;
        bool pageChanged = !ReferenceEquals(_displayBook, _operation.Book) || _displayRange != nextFrame?.FrameRange;
        if (pageChanged)
        {
            _pan += _motion.CancelPan(); ReleaseOutgoing();
            if (ReferenceEquals(_displayBook, _operation.Book) && _frame is not null && nextFrame is not null
                && _operation.Context?.PageChangeDuration > TimeSpan.Zero && _images.Count > 0)
            {
                var pages = _transform.GetTargets().Where(t => !t.Source.IsDummy).Select(t => t.Source.Page).Distinct().ToHashSet();
                _outgoing = new(_transform.GetTargets().ToArray(), _transform.GetMatrix(), GetContentRect(),
                    _images.Where(p => pages.Contains(p.Key)).ToDictionary(p => p.Key, p => p.Value.Retain()), new(_pageErrors));
            }
        }
        _frame = nextFrame;
        if (!pageChanged && _outgoing is not null && _transform.Viewport != new CoreSize(Bounds.Width, Bounds.Height)) ReleaseOutgoing();
        _transform.Viewport = new(Bounds.Width, Bounds.Height); _transform.DeviceScale = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;
        var transformChanged = _transform.Synchronize(_operation.Book, _frame);
        if (transformChanged || !ReferenceEquals(_displayBook, _operation.Book) || _displayRange != _frame?.FrameRange)
        {
            CancelMouseSequence();
            _displayBook = _operation.Book; _displayRange = _frame?.FrameRange;
            AlignPageOrigin(_operation.MoveDirection);
            _scrollLock.SetLock(Config.Current.View.MovementConstraint.IsLockStart);
            if (_outgoing is { } previous && _operation.Context is { } context)
            {
                var rect = GetContentRect(); var old = previous.Bounds;
                var located = PageFrameContainerLayout.Layout(new(-old.Width/2,-old.Height/2,old.Width,old.Height),new(rect.Width,rect.Height),context,_operation.MoveDirection);
                _incomingOffset = new(located.X+located.Width/2+old.X+old.Width/2-rect.X-rect.Width/2,
                    located.Y+located.Height/2+old.Y+old.Height/2-rect.Y-rect.Height/2);
                _pageDuration=context.PageChangeDuration; _pageType=context.PageChangeType; _awaitingTransition=true;
            }
        }
    }
    /// <summary>按设备像素请求图像，128 像素分桶减少窗口小幅调整造成的缓存重复。</summary>
    private DecodeRequest GetRequest(Page page)
    {
        var size = page.Content.PageDataSource.Size;
        var scale = _transform.PixelScale * (TopLevel.GetTopLevel(this)?.RenderScaling ?? 1);
        return new(Math.Max(128, (int)Math.Ceiling(Math.Min(size.Width, size.Width * scale) / 128) * 128), Math.Max(128, (int)Math.Ceiling(Math.Min(size.Height, size.Height * scale) / 128) * 128));
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
        { DrawText(context, _operation?.Book is null ? "NeeView\n打开图片、目录或 ZIP / CBZ" : "这个来源没有可阅读的图片", new(30,30)); return; }
        using var clip=context.PushClip(new Avalonia.Rect(Bounds.Size));
        var motion=_motion.GetPageState();
        if (_outgoing is { } old)
            DrawFrame(context,old.Targets,old.Matrix * Matrix.CreateTranslation(motion.Outgoing),old.Images,old.Errors,_awaitingTransition ? 1 : motion.OutgoingOpacity);
        if (!_awaitingTransition)
            DrawFrame(context,_transform.GetTargets(),GetRenderedMatrix(),_images,_pageErrors,motion.IncomingOpacity);
        if (_sequenceHint.Length>0) { context.FillRectangle(new SolidColorBrush(Color.FromArgb(210,24,24,24)),new Avalonia.Rect(12,12,220,58)); DrawText(context,_sequenceHint,new(24,20)); }
        if (_loadError is not null) DrawText(context,_loadError,new(12,12));
    }
    /// <summary>同一绘制函数显示当前/退出帧；快照复用真实Bitmap与租约，不复制像素。</summary>
    private void DrawFrame(DrawingContext context,IEnumerable<(PageFrameElement Source,Avalonia.Rect Target)> targets,Matrix matrix,
        Dictionary<Page,Display> images,Dictionary<Page,string> errors,double opacity)
    {
        if(opacity<=0) return;
        using var alpha=context.PushOpacity(opacity); using var transform=context.PushTransform(matrix);
        foreach(var(source,target) in targets)
        {
            if(source.IsDummy) context.FillRectangle(Brushes.White,target);
            else if(source.Page.PageType.IsFolder()) ArchivePageRenderer.Draw(this,context,source.Page,target,images.GetValueOrDefault(source.Page)?.Bitmap,errors.GetValueOrDefault(source.Page)??"正在加载…");
            else if(images.TryGetValue(source.Page,out var image))
            {
                var crop=source.ViewSizeCalculator.GetViewBox(); var pixels=image.Bitmap.PixelSize;
                context.DrawImage(image.Bitmap,new Avalonia.Rect(crop.X*pixels.Width,crop.Y*pixels.Height,crop.Width*pixels.Width,crop.Height*pixels.Height),target);
            }
            else { context.FillRectangle(new SolidColorBrush(Color.Parse("#202020")),target); DrawText(context,source.Page.Content.Error??errors.GetValueOrDefault(source.Page)??"正在加载…",target.TopLeft+new Avalonia.Vector(12,12)); }
        }
    }
    /// <summary>命中与绘制共享插值矩阵，动画期间不按终点点击旧画面。</summary>
    private Matrix GetRenderedMatrix() => _transform.GetMatrix() * Matrix.CreateTranslation(_motion.GetPanOffset()+_motion.GetPageState().Incoming);
    private void ReleaseOutgoing()
    { _outgoing?.Dispose(); _outgoing=null; _awaitingTransition=false; _motion.CancelPage(); }
    /// <summary>原取消滚动保留当前可见点；直接操作关闭退出帧并释放旧资源。</summary>
    private void StopMotion()
    { _pan+=_motion.CancelPan(); ReleaseOutgoing(); _motionTimer.Stop(); }
    /// <summary>逻辑目标先提交，表现从当前显示点插值；零时长立即落地。</summary>
    private void AnimatePan(Avalonia.Vector from,TimeSpan duration,bool linear=false)
    { _motion.BeginPan(from,_pan,duration,linear); if(_motion.IsActive) _motionTimer.Start(); InvalidateVisual(); }
    private TimeSpan ScrollDuration => _operation?.Context?.ScrollDuration ?? TimeSpan.Zero;
    /// <summary>错误和空书籍保持可操作，文本不改变来源索引。</summary>
    private static void DrawText(DrawingContext context, string text, Point position) => context.DrawText(new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface("sans-serif"), 13, Brushes.LightGray), position);
    /// <summary>围绕指针位置缩放，像素需求随缩放更新。</summary>
    public async Task ZoomAsync(double factor, Point? pointer = null)
    {
        StopMotion(); SynchronizeFrame();
        var origin = pointer ?? new Point(Bounds.Width / 2, Bounds.Height / 2);
        var old = _zoom; _zoom *= factor; var ratio = _zoom / old;
        _scrollLock.Unlock();
        var relative = origin - new Point(Bounds.Width / 2, Bounds.Height / 2);
        _pan = _pan * ratio + relative * (1 - ratio);
        ClampPan();
        await RefreshAsync();
    }
    /// <summary>重置手工缩放及平移，100% 由引擎 StretchMode.None 与设备比例决定。</summary>
    public void ResetTransform() { StopMotion(); _transform.Reset(); AlignPageOrigin(_operation?.MoveDirection ?? 1); InvalidateVisual(); }
    /// <summary>原缩放参数及中心补偿；BaseScale写回书籍，手工Scale只写变换图。</summary>
    public async Task ScaleAsync(int direction, ViewScaleCommandParameter parameter, bool baseScale = false)
    {
        SynchronizeFrame();
        if (_frame is null || _operation?.Book is not { } book) return;
        StopMotion();
        var center = _transform.GetCenter(Config.Current.View.ScaleCenter, _pointer, true);
        if (!baseScale) { await ZoomAsync(ViewTransformMath.Scale(_zoom, direction, parameter) / _zoom, center); return; }
        var start = book.Setting.BaseScale; if (!double.IsFinite(start) || start <= 0) start = 1;
        var scale = ViewTransformMath.Scale(start, direction, parameter);
        var relative = center - new Point(Bounds.Width / 2, Bounds.Height / 2); var oldPan = _pan;
        await _operation.ApplySettingAsync(setting => setting.BaseScale = scale);
        // 设置异步重建仍在同一书籍时才应用旧中心，切书不接受晚到平移。
        if (!ReferenceEquals(book, _operation.Book)) return;
        if (Config.Current.View.IsBaseScaleEnabled) _pan = oldPan * (scale / start) + relative * (1 - scale / start);
        _scrollLock.Unlock();
        ClampPan(); await RefreshAsync();
    }
    /// <summary>原角度步长/频率、中心补偿；可选Stretch不改变翻转及基准缩放。</summary>
    public async Task RotateAsync(int direction, ViewRotateCommandParameter parameter)
    {
        SynchronizeFrame();
        if (_frame is null) return;
        StopMotion();
        var center = _transform.GetCenter(Config.Current.View.RotateCenter, _pointer);
        var start = _transform.Angle; var angle = ViewTransformMath.Rotate(start, direction * parameter.Angle, Config.Current.View.AngleFrequency);
        var view = new Point(Bounds.Width / 2, Bounds.Height / 2); var v = view + _pan - center;
        var rotated = ViewTransformMath.RotateVector(new(v.X, v.Y), angle - start);
        _transform.Angle = angle; _pan = center - view + new Avalonia.Vector(rotated.X, rotated.Y);
        _scrollLock.Unlock();
        if (parameter.IsStretch) { StopMotion(); _transform.Stretch(); AlignPageOrigin(_operation?.MoveDirection ?? 1); }
        ClampPan(); await RefreshAsync();
    }
    /// <summary>原屏幕轴翻转：翻转角度及内容中心，使旋转后仍按水平/垂直轴工作。</summary>
    public void Flip(bool horizontal, bool state)
    {
        if (_frame is null || (horizontal ? IsFlipHorizontal : IsFlipVertical) == state) return;
        StopMotion();
        var center = _transform.GetCenter(Config.Current.View.FlipCenter, _pointer);
        var view = new Point(Bounds.Width / 2, Bounds.Height / 2); var offset = view + _pan - center;
        if (horizontal) { _transform.IsFlipHorizontal = state; _transform.Angle = -ViewTransformMath.NormalizeAngle(_transform.Angle); offset = new(-offset.X, offset.Y); }
        else { _transform.IsFlipVertical = state; _transform.Angle = 90 - ViewTransformMath.NormalizeAngle(_transform.Angle + 90); offset = new(offset.X, -offset.Y); }
        _pan = center - view + offset; _scrollLock.Unlock(); ClampPan(); InvalidateVisual();
    }
    /// <summary>原适配操作重新计算手工Scale，并回到配置页起点。</summary>
    public async Task StretchAsync()
    { StopMotion(); _transform.Stretch(); AlignPageOrigin(_operation?.MoveDirection ?? 1); ClampPan(); await RefreshAsync(); }
    /// <summary>原预置滚动，不经过翻页命令且可强制对齐小图。</summary>
    public void ScrollToPreset(ViewPresetScrollCommandParameter parameter)
    {
        if (_frame is null || Config.Current.Mouse.IsHoverScroll) return;
        var from=_pan+_motion.GetPanOffset();
        var delta = new DragArea(new(0, 0, Bounds.Width, Bounds.Height), GetContentRect()).SnapAlignment(parameter.Horizontal, parameter.Vertical, parameter.IsSnap);
        _pan += new Avalonia.Vector(delta.X, delta.Y); AnimatePan(from,ScrollDuration);
    }
    /// <summary>原纯NType到终端不翻页；与滚动翻页共用同一限流算法。</summary>
    public void ScrollNType(int direction, ViewScrollNTypeCommandParameter parameter)
    {
        if (_frame is null || Config.Current.Mouse.IsHoverScroll || _operation?.Context is not { } context) return;
        var result = _scrollControl.ScrollToNext(context, GetContentRect(), new(0, 0, Bounds.Width, Bounds.Height), direction, parameter);
        if (result is not null && !result.IsTerminated) { var from=_pan+_motion.GetPanOffset(); ApplyPan(new(result.Vector.X,result.Vector.Y)); AnimatePan(from,ScrollDuration); }
    }
    /// <summary>高精度滚动平移；分页导航由输入映射处理。</summary>
    public void Pan(Avalonia.Vector delta) { StopMotion(); ApplyPan(delta); }
    private void ApplyPan(Avalonia.Vector delta)
    {
        if (_frame is null) return;
        var move = new NeeView.Vector(delta.X, delta.Y);
        if (Config.Current.View.MovementConstraint.IsLimited)
        {
            var rect = GetContentRect(); var viewport = new NeeView.Rect(0, 0, Bounds.Width, Bounds.Height);
            _scrollLock.Update(rect, viewport); move = _scrollLock.Limit(move);
            move = new ScrollAreaLimit(rect, viewport).GetLimitContentMove(move);
        }
        _pan += new Avalonia.Vector(move.X, move.Y); InvalidateVisual();
    }
    /// <summary>按原四向滚动先移动指定轴，到边界后按阅读方向尝试跨轴。</summary>
    public void ScrollView(string command, ViewScrollCommandParameter parameter)
    {
        if (_frame is null || Config.Current.Mouse.IsHoverScroll) return;
        var from=_pan+_motion.GetPanOffset();
        var dx = Bounds.Width * parameter.Scroll; var dy = Bounds.Height * parameter.Scroll;
        var readDirection = _operation?.Context?.ReadOrder == PageReadOrder.LeftToRight ? 1 : -1;
        var horizontal = command is "ViewScrollLeft" or "ViewScrollRight";
        var sign = command is "ViewScrollLeft" or "ViewScrollUp" ? 1 : -1;
        var old = _pan; ApplyPan(horizontal ? new(dx * sign, 0) : new(0, dy * sign));
        if (parameter.AllowCrossScroll && (horizontal ? old.X == _pan.X : old.Y == _pan.Y))
            ApplyPan(horizontal ? new(0, dy * sign * readDirection) : new(dx * sign * readDirection, 0));
        AnimatePan(from,ScrollDuration);
    }
    /// <summary>原Hover：指针相对视口中心，超出尺寸乘灵敏度并限定半幅。</summary>
    private void HoverScroll(Point point,bool immediate=false)
    {
        if(_frame is null || Bounds.Width<=0 || Bounds.Height<=0) return;
        var from=_pan+_motion.GetPanOffset(); var rect=GetContentRect(); var c=Config.Current.Mouse;
        _pan=new(Math.Max(rect.Width-Bounds.Width,0)*Math.Clamp((point.X-Bounds.Width/2)/Bounds.Width*-c.HoverScrollSensitivity,-.5,.5),
            Math.Max(rect.Height-Bounds.Height,0)*Math.Clamp((point.Y-Bounds.Height/2)/Bounds.Height*-c.HoverScrollSensitivity,-.5,.5));
        AnimatePan(from,immediate?TimeSpan.Zero:PageFrameContext.SafeDuration(c.HoverScrollDuration));
    }
    /// <summary>原连续轮滚仅无修饰/按钮优先，横轴方向及120单位沿原Windows delta。</summary>
    public bool TryWheelScroll(PointerWheelEventArgs e)
    {
        var c=Config.Current.Mouse;
        if(!c.IsMouseWheelScrollEnabled || e.KeyModifiers!=KeyModifiers.None || MouseGestureSource.HeldButtons(e.GetCurrentPoint(this).Properties).Any()) return false;
        var from=_pan+_motion.GetPanOffset(); ApplyPan(new(-e.Delta.X*120*c.MouseWheelScrollSensitivity,e.Delta.Y*120*c.MouseWheelScrollSensitivity));
        AnimatePan(from,PageFrameContext.SafeDuration(c.MouseWheelScrollDuration),true); return true;
    }
    /// <summary>组合滚轮消费当前按下动作，释放时不追加普通点击。</summary>
    public void SuppressPendingClick(PointerPointProperties properties)
    { CancelMouseSequence(); _suppressedButtons.UnionWith(MouseGestureSource.HeldButtons(properties)); _pendingClick = null; _pressed = null; }
    /// <summary>沿用原 DragArea.SnapView，精确滚动和拖动不允许把图片完全推出视口。</summary>
    private void ClampPan()
    {
        if (_frame is null || Bounds.Width <= 0 || Bounds.Height <= 0) return;
        if (Config.Current.View.MovementConstraint < MovementConstraint.Snap) return;
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
            var result = Config.Current.Mouse.IsHoverScroll ? new PageFrames.ScrollResult(default, default) : _scrollControl.ScrollToNext(context, GetContentRect(), new(0, 0, Bounds.Width, Bounds.Height), direction, parameter);
            if (result is null) return;
            if (!result.IsTerminated) { var from=_pan+_motion.GetPanOffset(); ApplyPan(new(result.Vector.X,result.Vector.Y)); AnimatePan(from,ScrollDuration); return; }
            // 导航仍走 BookOperation 的原帧范围；切书后不能应用旧命令的原点。
            var book = _operation.Book; var position = _operation.Position;
            await _operation.MoveAsync(direction);
            if (_disposed || !ReferenceEquals(book, _operation.Book) || position == _operation.Position) return;
            await RefreshAsync();
        }
        finally { _scrollGate.Release(); }
    }
    /// <summary>将原页框实际绘制尺寸转为轴对齐矩形，含双页间距、分割及旋转。</summary>
    public NeeView.Rect GetContentRect() => _transform.GetContentRect();
    /// <summary>新帧按阅读与移动方向进入起点；普通刷新/手工缩放保留当前平移。</summary>
    private void AlignPageOrigin(int direction)
    {
        _transform.AlignOrigin(_operation?.Context?.ReadOrder ?? PageReadOrder.RightToLeft, direction);
    }
    /// <summary>导航器将图像内指定位置移到视口中心；页框及旋转仍由原引擎计算。</summary>
    public void Navigate(Point point)
    {
        StopMotion();
        if (_frame is null || _operation?.Book?.CurrentPage is not { } page) return;
        foreach (var (source, target) in _transform.GetTargets())
        {
            if (!source.IsDummy && ReferenceEquals(source.Page, page))
            {
                // 导航器展示主图片原图；双页需定位到该页，分割页先转换到当前裁剪区。
                var crop = source.ViewSizeCalculator.GetViewBox();
                var x = Math.Clamp((point.X - crop.X) / crop.Width, 0, 1);
                var y = Math.Clamp((point.Y - crop.Y) / crop.Height, 0, 1);
                var offset = _transform.GetMatrix(false).Transform(new Point(target.X + target.Width * x, target.Y + target.Height * y));
                _pan = new(-offset.X, -offset.Y);
                InvalidateVisual(); return;
            }
        }
    }
    /// <summary>视口变化后只请求可见帧，避免扫描目录。</summary>
    protected override async void OnSizeChanged(SizeChangedEventArgs e) { base.OnSizeChanged(e); await RefreshAsync(); }
    /// <summary>按下记录拖动起点，释放时才确认是否为翻页点击。</summary>
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e); Focus(); _pointer = e.GetPosition(this);
        var properties = e.GetCurrentPoint(this).Properties;
        var button = MouseGestureSource.ChangedButton(properties);
        if (_sequenceActive && button == MouseButton.Left && !_sequence.IsEmpty)
        { CompleteMouseSequence(properties, true); e.Handled = true; return; }
        // 所有按钮都持有本次输入，避免移出控件后的释放丢失；捕获转移会取消待确认动作。
        if (button != MouseButton.None) e.Pointer.Capture(this);
        var action = MouseGestureSource.ClickAction(button, e.ClickCount);
        var gesture = action is null ? null : MouseGestureSource.Create(action, e.KeyModifiers, properties, button);
        // 原扩展组合/双击优先在按下阶段匹配，不能随后再触发第一次普通释放。
        if (gesture is not null && (gesture.Contains('+') || e.ClickCount >= 2 || button is MouseButton.XButton1 or MouseButton.XButton2)
            && TryGestureRequested?.Invoke(gesture) == true)
        {
            CancelMouseSequence(); _suppressedButtons.UnionWith(MouseGestureSource.HeldButtons(properties));
            _pendingClick = null; _pressed = null; _dragged = false; e.Handled = true; return;
        }
        if (button == MouseButton.Left && GetBookPageAt(e.GetPosition(this)) is { } page)
        {
            _bookCardPressed = true; e.Handled = true;
            if (button == MouseButton.Left && e.ClickCount >= 2) ChildBookRequested?.Invoke(this, page);
            return;
        }
        _pendingClick = gesture;
        if (button == MouseButton.Right && e.ClickCount == 1 && e.KeyModifiers == KeyModifiers.None
            && MouseGestureSource.HeldButtons(properties).SequenceEqual([MouseButton.Right]) && Config.Current.Mouse.IsGestureEnabled && CanStartMouseSequence?.Invoke() != false)
        { _sequenceActive = true; _sequence.Reset(new(_pointer!.Value.X, _pointer.Value.Y)); }
        if (button == MouseButton.Left) { StopMotion(); _pressed = e.GetPosition(this); _initialPan = _pan; _dragged = false; }
    }
    /// <summary>拖动大图只修改表现变换。</summary>
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e); _pointer = e.GetPosition(this);
        // Avalonia在已有按钮按下时把额外按钮变化送入PointerMoved；按更新种类补原ChangedButton事件。
        var properties = e.GetCurrentPoint(this).Properties;
        var changed = MouseGestureSource.ChangedButton(properties);
        if (_sequenceActive && changed == MouseButton.Left && !_sequence.IsEmpty)
        { CompleteMouseSequence(properties, true); e.Handled = true; return; }
        if (changed != MouseButton.None && MouseGestureSource.ClickAction(changed, 1) is { } action)
        {
            var gesture = MouseGestureSource.Create(action, e.KeyModifiers, properties, changed);
            if (TryGestureRequested?.Invoke(gesture) == true)
            { SuppressPendingClick(properties); _dragged = false; e.Handled = true; }
            return;
        }
        if (_sequenceActive)
        {
            if (!Config.Current.Mouse.IsGestureEnabled || CanStartMouseSequence?.Invoke() == false) { CancelMouseSequence(); _pendingClick = null; return; }
            _sequence.Move(new(_pointer!.Value.X, _pointer.Value.Y));
            if (!_sequence.IsEmpty) { _pendingClick = null; e.Handled = true; }
            return;
        }
        if (_pressed is not { } start)
        { if (Config.Current.Mouse.IsHoverScroll && !MouseGestureSource.HeldButtons(properties).Any() && CanStartMouseSequence?.Invoke()!=false) HoverScroll(_pointer!.Value); return; }
        var position = e.GetPosition(this); var delta = new Avalonia.Vector(position.X - start.X, position.Y - start.Y);
        if (delta.Length > 4) _dragged = true;
        if (_dragged) Pan(_initialPan + delta - _pan);
    }
    /// <summary>未拖动的左右点击送入原快捷键映射。</summary>
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_sequenceActive && !_sequence.IsEmpty)
        { CompleteMouseSequence(e.GetCurrentPoint(this).Properties, false); e.Handled = true; }
        else CancelMouseSequence(false);
        var suppressed = _suppressedButtons.Remove(e.InitialPressMouseButton);
        if (!suppressed && !_dragged && !_bookCardPressed && new Avalonia.Rect(Bounds.Size).Contains(e.GetPosition(this)) && _pendingClick is { } gesture)
            e.Handled |= TryGestureRequested?.Invoke(gesture) == true;
        _pressed = null; _dragged = false; _bookCardPressed = false; _pendingClick = null;
        if (!MouseGestureSource.HeldButtons(e.GetCurrentPoint(this).Properties).Any()) e.Pointer.Capture(null);
    }
    /// <summary>捕获丢失取消未确认点击/拖动，旧指针不作用于新控件。</summary>
    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    { base.OnPointerCaptureLost(e); CancelMouseSequence(); _pressed = null; _dragged = false; _pendingClick = null; _bookCardPressed = false; _suppressedButtons.Clear(); }
    /// <summary>释放或已有方向时左键终止；未知序列也不能退化为右击翻页。</summary>
    private void CompleteMouseSequence(PointerPointProperties properties, bool click)
    {
        if (click) _sequence.AddClick();
        var sequence = _sequence.ToMouseSequence(); var enabled = Config.Current.Mouse.IsGestureEnabled && CanStartMouseSequence?.Invoke() != false;
        SuppressPendingClick(properties);
        if (enabled) TryMouseSequenceRequested?.Invoke(sequence);
    }
    /// <summary>捕获/焦点/书籍变化与Escape取消方向序列；不执行命令。</summary>
    public bool CancelMouseSequence(bool cancelClick = true)
    {
        bool active = _sequenceActive; if (active && cancelClick) _pendingClick = null; _sequenceActive = false; _sequence.Reset(default); return active;
    }
    /// <summary>按原封面按钮区域命中书籍页，单击不翻页，双击打开实际命中项。</summary>
    private Page? GetBookPageAt(Point point)
    {
        if (_frame is null) return null;
        if (!GetRenderedMatrix().TryInvert(out var inverse)) return null;
        var local = inverse.Transform(point);
        foreach (var (source, target) in _transform.GetTargets())
        {
            if (!source.IsDummy && source.Page.PageType.IsFolder() && ArchivePageRenderer.CoverArea(target).Contains(local)) return source.Page;
        }
        return null;
    }
    /// <summary>释放当前需求和所有显示租约，晚到结果按 revision 拒绝。</summary>
    public void Dispose()
    {
        if (_disposed) return; CancelMouseSequence(); StopMotion(); _disposed = true; ++_revision; _request?.Cancel(); _request = null;
        foreach (var item in _images.Values) item.Dispose(); _images.Clear(); _pageErrors.Clear();
        _transform.Dispose();
    }
}
