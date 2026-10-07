using Avalonia;
using NeeView;
using NeeView.PageFrames;
namespace NeeView.MacOS.Views;

/// <summary>原变换数据的表现适配；统一绘制、导航及命中矩阵，不拥有读取或图像资源。</summary>
internal sealed class ReaderTransformPresenter : IShareTransformContext, IDisposable
{
    private Book? _book;
    private PageFrameTransformMap? _map;
    private PageFrameTransformKey? _key;
    private PageFrameTransformAccessor? _current;
    private bool _tracking;
    private NeeView.Size _lastViewport;
    public PageFrame? Frame { get; private set; }
    public double Scale { get => _current?.Scale ?? 1; set { if (double.IsFinite(value) && value > 0) _current?.SetScale(value); } }
    public double Angle { get => _current?.Angle ?? 0; set => _current?.SetAngle(value); }
    public bool IsFlipHorizontal { get => _current?.IsFlipHorizontal ?? false; set => _current?.SetFlipHorizontal(value); }
    public bool IsFlipVertical { get => _current?.IsFlipVertical ?? false; set => _current?.SetFlipVertical(value); }
    public Avalonia.Vector Pan { get => new(_current?.Point.X ?? 0, _current?.Point.Y ?? 0); set => _current?.SetPoint(new(value.X, value.Y)); }
    public double BaseScale => Config.Current.View.IsBaseScaleEnabled && _book?.Setting.BaseScale is > 0 and var value && double.IsFinite(value) ? value : 1;
    public double PixelScale => (Frame?.Scale ?? 1) * BaseScale * Scale;
    public bool IsFlipLocked => Config.Current.View.IsKeepFlip;
    public bool IsScaleLocked => Config.Current.View.IsKeepScale;
    public bool IsAngleLocked => Config.Current.View.IsKeepAngle;
    public bool IsKeepAngleBooks => Config.Current.View.IsKeepAngleBooks;
    public bool IsKeepFlipBooks => Config.Current.View.IsKeepFlipBooks;
    public bool IsKeepScaleBooks => Config.Current.View.IsKeepScaleBooks;
    public double ShareAngle { get; set; }
    public bool ShareFlipHorizontal { get; set; }
    public bool ShareFlipVertical { get; set; }
    public double ShareScale { get; set; } = 1;

    /// <summary>原按Page/Part保存变换；切书只在锁定且Books开关开启时继承共享值。</summary>
    public bool Synchronize(Book? book, PageFrame? frame)
    {
        var previousFrame = Frame;
        var oldStretch = previousFrame is null ? 1 : CalcStretch(previousFrame, _lastViewport);
        var resize = _lastViewport != Viewport;
        var firstLayout = (_lastViewport.Width <= 0 || _lastViewport.Height <= 0) && Viewport.Width > 0 && Viewport.Height > 0;
        var stretchRate = oldStretch > 0 ? Scale / oldStretch : 1;
        bool bookChanged = !ReferenceEquals(_book, book);
        if (bookChanged) { _map?.Dispose(); _map = book is null ? null : new(this); _book = book; _key = null; }
        Frame = frame;
        if (_map is null || frame is null) { _current = null; return bookChanged; }
        _map.IsScaleLocked = IsScaleLocked; _map.IsAngleLocked = IsAngleLocked; _map.IsFlipLocked = IsFlipLocked;
        var key = PageFrameTransformTool.CreateKey(frame); var changed = bookChanged || key != _key;
        if (changed)
        {
            var source = _map.ElementAt(key);
            if (!Config.Current.View.IsKeepPageTransform) source.Clear();
            _key = key; _current = _map.CreateAccessor(key);
            if (!IsScaleLocked && !Config.Current.View.IsKeepPageTransform) Stretch();
        }
        var tracking = Config.Current.View.IsScaleStretchTracking && !IsScaleLocked;
        if (tracking != _tracking)
        {
            if (tracking) Stretch(); else Scale = 1;
            _tracking = tracking;
        }
        else if (tracking && resize && !changed)
        {
            // 原StoreStretchScaleRate/CorrectStretchScale：保留相对适配比例，不能吞掉手工缩放。
            Scale = CalcStretch(frame, Viewport) * stretchRate;
        }
        _lastViewport = Viewport;
        // 启动前打开会先收到零尺寸视口；首次真实布局必须重新定位，不能保留该临时原点。
        return changed || firstLayout;
    }
    /// <summary>原重置清除整个书内变换图，不改变BaseScale。</summary>
    public void Reset() => _map?.Clear();
    /// <summary>原Stretch使用已适配的帧尺寸和手工角度，基准缩放独立保留。</summary>
    public void Stretch()
    {
        if (Frame is null || _book is null) return;
        // Frame.Scale已经按Retina设备比例应用原始大小，手工Scale不能再重复一次设备换算。
        Scale = CalcStretch(Frame, Viewport);
    }
    /// <summary>按原已适配帧尺寸计算手工拉伸倍率，供显式适配与窗口跟随共用。</summary>
    private double CalcStretch(PageFrame frame, NeeView.Size viewport)
    {
        if (_book is null || viewport.Width <= 0 || viewport.Height <= 0) return 1;
        var context = new PageFrameContext(_book.Setting, Config.Current) { CanvasSize = viewport, DeviceScale = DeviceScale };
        return Config.Current.View.StretchMode == PageStretchMode.None ? 1 : new ContentSizeCalculator(context).CalcModeStretchScale(frame.Size, Angle);
    }
    public NeeView.Size Viewport { get; set; }
    public double DeviceScale { get; set; } = 1;
    /// <summary>帧原点坐标，包含双页间隔，外部变换一次性作用于整帧。</summary>
    public IEnumerable<(PageFrameElement Source, Avalonia.Rect Target)> GetTargets()
    {
        return Frame is null ? [] : GetTargets(Frame);
    }
    /// <summary>原帧的局部目标几何；全景邻帧也复用同一双页排列/裁剪入口。</summary>
    public static IEnumerable<(PageFrameElement Source, Avalonia.Rect Target)> GetTargets(PageFrame frame)
    {
        var sources = frame.GetDirectedSources().ToArray();
        var width = sources.Sum(e => e.Width) * frame.Scale + frame.TotalSpan;
        double left = -width / 2;
        foreach (var source in sources)
        {
            var rect = new Avalonia.Rect(left, -source.Height * frame.Scale / 2, source.Width * frame.Scale, source.Height * frame.Scale);
            yield return (source, rect); left += rect.Width + frame.Span;
        }
    }
    /// <summary>原顺序：BaseScale、自动旋转、翻转、手工缩放/旋转，再平移到视口。</summary>
    public Matrix GetMatrix(bool translated = true) => Matrix.CreateScale(BaseScale, BaseScale)
        * Matrix.CreateRotation((Frame?.Angle ?? 0) * Math.PI / 180)
        * Matrix.CreateScale(IsFlipHorizontal ? -Scale : Scale, IsFlipVertical ? -Scale : Scale)
        * Matrix.CreateRotation(Angle * Math.PI / 180)
        * (translated ? Matrix.CreateTranslation(Viewport.Width / 2 + Pan.X, Viewport.Height / 2 + Pan.Y) : Matrix.Identity);
    /// <summary>静止且设备像素1:1时对齐最终图像边界；不修改原页框、缩放或逻辑平移。</summary>
    /// <param name="matrix">绘制和命中共用的最终矩阵。</param>
    /// <param name="targets">真实页面的局部目标矩形。</param>
    /// <param name="origin">查看器在顶层中的原点，避免侧栏及工具栏的半像素偏移。</param>
    /// <param name="deviceScale">顶层的实际设备比例。</param>
    /// <param name="pixelScale">一个原图像素对应的DIP大小。</param>
    /// <param name="isMoving">动画期间保持连续插值，不吸附。</param>
    /// <returns>所有页边界可同时对齐时的矩阵，否则保留原矩阵。</returns>
    internal static Matrix AlignDevicePixels(Matrix matrix, IEnumerable<Avalonia.Rect> targets, Point origin,
        double deviceScale, double pixelScale, bool isMoving)
    {
        const double epsilon = .000001;
        if (isMoving || !double.IsFinite(deviceScale) || deviceScale <= 0
            || Math.Abs(pixelScale * deviceScale - 1) > epsilon
            || Math.Abs(matrix.M12) > epsilon || Math.Abs(matrix.M21) > epsilon) return matrix;
        Avalonia.Vector? correction = null;
        foreach (var target in targets)
        {
            var bounds = target.TransformToAABB(matrix);
            var x = (origin.X + bounds.X) * deviceScale;
            var y = (origin.Y + bounds.Y) * deviceScale;
            var width = bounds.Width * deviceScale; var height = bounds.Height * deviceScale;
            if (!double.IsFinite(x) || !double.IsFinite(y) || !double.IsFinite(width) || !double.IsFinite(height)
                || Math.Abs(width - Math.Round(width)) > epsilon || Math.Abs(height - Math.Round(height)) > epsilon) return matrix;
            var delta = new Avalonia.Vector((Math.Floor(x + .5) - x) / deviceScale, (Math.Floor(y + .5) - y) / deviceScale);
            // 不同奇偶尺寸的双页可能不能共用吸附量；保持原页组几何，不能为清晰度改变排列。
            if (correction is { } previous && (Math.Abs(previous.X - delta.X) > epsilon || Math.Abs(previous.Y - delta.Y) > epsilon)) return matrix;
            correction = delta;
        }
        return correction is { } offset ? matrix * Matrix.CreateTranslation(offset) : matrix;
    }
    /// <summary>根据唯一矩阵计算显示包围盒，滚动不使用另一套旋转尺寸。</summary>
    public NeeView.Rect GetContentRect()
    {
        if (Frame is null) return default;
        var bounds = new Avalonia.Rect(-Frame.StretchedSize.Width / 2, -Frame.StretchedSize.Height / 2, Frame.StretchedSize.Width, Frame.StretchedSize.Height).TransformToAABB(GetMatrix());
        return new(bounds.X, bounds.Y, bounds.Width, bounds.Height);
    }
    /// <summary>按原参考视口计算拉伸包围尺寸，保留页框/基准缩放/旋转，不修改用户变换或解码。</summary>
    public NeeView.Size GetReferenceStretchSize(NeeView.Size reference)
    {
        if (Frame is null) return default;
        if (reference.Width <= 0 || reference.Height <= 0) reference = Viewport;
        var scale = BaseScale * CalcStretch(Frame, reference);
        return GeometryMath.RotateSize(new(Frame.StretchedSize.Width * scale, Frame.StretchedSize.Height * scale), Frame.Angle + Angle);
    }
    /// <summary>原中心策略，Auto按当前内容相对视口的比例逐轴求中心。</summary>
    public Point GetCenter(DragControlCenter mode, Point? pointer = null, bool allowAuto = false)
    {
        var view = new Point(Viewport.Width / 2, Viewport.Height / 2); var content = view + Pan;
        if (mode == DragControlCenter.Target) return content;
        if (mode == DragControlCenter.Cursor) return pointer ?? view;
        if (mode != DragControlCenter.Auto || !allowAuto) return view;
        var rect = GetContentRect();
        double rateX = Math.Abs(Viewport.Width - rect.Width) > .01 ? (content.X - rect.Width * .5) / (Viewport.Width - rect.Width) : _autoX;
        double rateY = Math.Abs(Viewport.Height - rect.Height) > .01 ? (content.Y - rect.Height * .5) / (Viewport.Height - rect.Height) : _autoY;
        _autoX = rateX; _autoY = rateY; return new(Viewport.Width * rateX, Viewport.Height * rateY);
    }
    private double _autoX = .5, _autoY = .5;
    /// <summary>原页起点设置，同时初始化Auto中心比率。</summary>
    public void AlignOrigin(PageReadOrder readOrder, int direction)
    {
        Pan = default; var rect = GetContentRect(); var c = Config.Current.View;
        var horizontalCenter = rect.Width / (Viewport.Width + .01) <= c.ViewOriginCenterRatio;
        var verticalCenter = rect.Height / (Viewport.Height + .01) <= c.ViewOriginCenterRatio;
        var dir = readOrder.ToSign() * direction;
        int horizontal = c.ViewHorizontalOrigin switch
        {
            ViewHorizontalOrigin.Left or ViewHorizontalOrigin.CenterOrLeft => 1,
            ViewHorizontalOrigin.Right or ViewHorizontalOrigin.CenterOrRight => -1,
            ViewHorizontalOrigin.DirectionDependent or ViewHorizontalOrigin.CenterOrDirectionDependent => dir < 0 ? -1 : 1,
            ViewHorizontalOrigin.BindingDirectionDependent or ViewHorizontalOrigin.CenterOrBindingDirectionDependent => readOrder == PageReadOrder.LeftToRight ? 1 : -1,
            _ => 0
        };
        int vertical = c.ViewVerticalOrigin switch
        { ViewVerticalOrigin.Top or ViewVerticalOrigin.CenterOrTop => 1, ViewVerticalOrigin.Bottom or ViewVerticalOrigin.CenterOrBottom => -1,
            ViewVerticalOrigin.DirectionDependent or ViewVerticalOrigin.CenterOrDirectionDependent => direction < 0 ? -1 : 1, _ => 0 };
        if (c.ViewHorizontalOrigin.IsCenter && horizontalCenter) horizontal = 0;
        if (c.ViewVerticalOrigin.IsCenter && verticalCenter) vertical = 0;
        Pan = new((rect.Width - Viewport.Width) * horizontal / 2, (rect.Height - Viewport.Height) * vertical / 2);
        _autoX = horizontal > 0 ? 1 : horizontal < 0 ? 0 : .5; _autoY = vertical > 0 ? 0 : vertical < 0 ? 1 : .5;
    }
    /// <summary>释放当前书内变换图和页面引用。</summary>
    public void Dispose() { _map?.Dispose(); _map = null; _current = null; _book = null; Frame = null; }
}
