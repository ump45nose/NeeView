// Copyright (c) NeeLaboratory. MIT；原PageFrameTransform/Accessor的数值部分，动画留在表现层。
using CommunityToolkit.Mvvm.ComponentModel;
namespace NeeView.PageFrames;

/// <summary>原页面变换数据及变化事件；不包含WPF/Avalonia变换对象。</summary>
public sealed class PageFrameTransform : ObservableObject
{
    private double _scale = 1, _angle;
    private Vector _point;
    private bool _horizontal, _vertical;
    public double Scale => _scale;
    public double Angle => _angle;
    public Vector Point => _point;
    public bool IsFlipHorizontal => _horizontal;
    public bool IsFlipVertical => _vertical;
    public event EventHandler<TransformChangedEventArgs>? TransformChanged;
    /// <summary>更新原缩放数据；表现端处理显示插值。</summary>
    public void SetScale(double value, TimeSpan span, TransformTrigger trigger = TransformTrigger.None)
    { if (SetProperty(ref _scale, value, nameof(Scale))) TransformChanged?.Invoke(this, new(TransformAction.Scale)); }
    /// <summary>更新原旋转角度。</summary>
    public void SetAngle(double value, TimeSpan span)
    { if (SetProperty(ref _angle, value, nameof(Angle))) TransformChanged?.Invoke(this, new(TransformAction.Angle)); }
    /// <summary>保存DIP坐标；设备像素舍入由绘制端完成。</summary>
    public void SetPoint(Vector value, TimeSpan span)
    { if (SetProperty(ref _point, value, nameof(Point))) TransformChanged?.Invoke(this, new(TransformAction.Point)); }
    /// <summary>更新原水平翻转。</summary>
    public void SetFlipHorizontal(bool value, TimeSpan span)
    { if (SetProperty(ref _horizontal, value, nameof(IsFlipHorizontal))) TransformChanged?.Invoke(this, new(TransformAction.FlipHorizontal)); }
    /// <summary>更新原垂直翻转。</summary>
    public void SetFlipVertical(bool value, TimeSpan span)
    { if (SetProperty(ref _vertical, value, nameof(IsFlipVertical))) TransformChanged?.Invoke(this, new(TransformAction.FlipVertical)); }
    /// <summary>按原mask清除页面变换，不改书籍BaseScale。</summary>
    public void Clear(TransformMask mask = TransformMask.All)
    {
        if (mask.HasFlag(TransformMask.Flip)) { SetFlipHorizontal(false, TimeSpan.Zero); SetFlipVertical(false, TimeSpan.Zero); }
        if (mask.HasFlag(TransformMask.Scale)) SetScale(1, TimeSpan.Zero);
        if (mask.HasFlag(TransformMask.Angle)) SetAngle(0, TimeSpan.Zero);
        if (mask.HasFlag(TransformMask.Point)) SetPoint(default, TimeSpan.Zero);
    }
}
public enum TransformAction { Scale, Angle, Point, FlipHorizontal, FlipVertical }
public enum TransformTrigger { None, Clear }
public sealed class TransformChangedEventArgs(TransformAction action) : EventArgs { public TransformAction Action => action; }

/// <summary>保留原共享/页面数据选择；没有控件、动画或订阅资源。</summary>
public sealed class PageFrameTransformAccessor(PageFrameTransformMap map, PageFrameTransform source)
{
    private PageFrameTransform ScaleSource => map.IsScaleLocked ? map.Share : source;
    private PageFrameTransform AngleSource => map.IsAngleLocked ? map.Share : source;
    private PageFrameTransform FlipSource => map.IsFlipLocked ? map.Share : source;
    public double Scale => ScaleSource.Scale;
    public double Angle => AngleSource.Angle;
    public Vector Point => source.Point;
    public bool IsFlipHorizontal => FlipSource.IsFlipHorizontal;
    public bool IsFlipVertical => FlipSource.IsFlipVertical;
    /// <summary>缩放按锁定状态写共享或当前页。</summary>
    public void SetScale(double value) => ScaleSource.SetScale(value, TimeSpan.Zero);
    /// <summary>角度按锁定状态写共享或当前页。</summary>
    public void SetAngle(double value) => AngleSource.SetAngle(value, TimeSpan.Zero);
    /// <summary>位置始终属于当前页。</summary>
    public void SetPoint(Vector value) => source.SetPoint(value, TimeSpan.Zero);
    /// <summary>翻转按锁定状态写共享或当前页。</summary>
    public void SetFlipHorizontal(bool value) => FlipSource.SetFlipHorizontal(value, TimeSpan.Zero);
    /// <summary>翻转按锁定状态写共享或当前页。</summary>
    public void SetFlipVertical(bool value) => FlipSource.SetFlipVertical(value, TimeSpan.Zero);
}
