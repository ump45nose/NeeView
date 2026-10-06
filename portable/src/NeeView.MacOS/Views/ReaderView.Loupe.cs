// Copyright (c) NeeLaboratory. Adapted from MouseInputLoupe/LoupeDragTransformContext.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using NeeView;
using NeeView.PageFrames;
namespace NeeView.MacOS.Views;

/// <summary>原 Loupe 的独立表现变换；普通缩放/平移及阅读位置继续归现有查看器。</summary>
public sealed partial class ReaderView
{
    private LoupeContext? _loupe;
    private NeeView.Vector _loupeBase, _loupeDelta;
    private Point _loupeLastPointer;
    private Book? _loupeBook;
    private PageRange? _loupeRange;
    private BrowseLayoutMode _loupeMode;
    private IDisposable? _relativePointer;
    private int _loupeGeneration;
    private bool _loupeRefreshRequested;
    private IPlatformInput? _loupeInput;
    private Func<nint>? _loupeWindow;
    internal Task LoupePending { get; private set; } = Task.CompletedTask;
    public bool IsLoupeEnabled => _loupe?.IsEnabled == true;
    public double LoupeScale => _loupe?.Scale ?? Config.Current.Loupe.DefaultScale;
    public Avalonia.Vector LoupePan => IsLoupeEnabled ? new(LoupePoint.X, LoupePoint.Y) : default;
    internal double LoupeFixedScale => IsLoupeEnabled ? _loupe!.GetFixedScale(GetLoupeOriginalScale()) : 1;
    private NeeView.Vector LoupePoint => LoupeTransform.GetPoint(_loupeBase, _loupeDelta, Config.Current.Loupe.Speed, TopLevel.GetTopLevel(this)?.RenderScaling ?? 1);
    public event EventHandler? LoupeChanged;

    /// <summary>由窗口装配唯一平台输入，不引用具体 AppKit 后端。</summary>
    public void AttachLoupeInput(IPlatformInput? input, Func<nint>? window)
    { StopLoupe(false); _loupeInput = input; _loupeWindow = window; }
    /// <summary>启用时固定初始基点；退出只归还 Loupe 变换和捕获。</summary>
    public void SetLoupe(bool enabled)
    {
        if (_disposed || enabled == IsLoupeEnabled) return;
        if (!enabled) { StopLoupe(true); return; }
        if (_operation?.Book is null) return;
        SynchronizeFrame(); StopMotion(); CancelMouseSequence(); _browse?.CaptureLost();
        if (_loupe is null || !ReferenceEquals(_loupe.Config, Config.Current.Loupe)) _loupe = new(Config.Current.Loupe);
        if (Config.Current.Loupe.IsResetByRestart) _loupe.Reset();
        _loupe.IsEnabled = true; _loupeBook = _operation.Book; _loupeRange = _operation.Frame?.FrameRange; _loupeMode = _operation.BrowseMode;
        _loupeLastPointer = _pointer ?? new Point(Bounds.Width / 2, Bounds.Height / 2);
        _loupeDelta = default;
        _loupeBase = LoupeTransform.GetBasePoint(new(_loupeLastPointer.X-Bounds.Width/2, _loupeLastPointer.Y-Bounds.Height/2), LoupeFixedScale, Config.Current.Loupe.IsLoupeCenter);
        Focus();
        var generation = ++_loupeGeneration;
        try
        {
            if (_loupeInput is not null && _loupeWindow?.Invoke() is { } window && window != 0)
            {
                var lease = _loupeInput.BeginRelativePointer(window, (x,y) => { if (generation == _loupeGeneration && IsLoupeEnabled) LoupePanBy(new(x,y)); }, () =>
                {
                    void Released() { if (generation == _loupeGeneration) StopLoupe(true); }
                    if (Dispatcher.UIThread.CheckAccess()) Released(); else Dispatcher.UIThread.Post(Released);
                })
                    ?? throw new InvalidOperationException("无法取得当前窗口的放大镜鼠标输入。");
                if (generation == _loupeGeneration && IsLoupeEnabled) _relativePointer = lease;
                else { lease.Dispose(); return; }
            }
        }
        catch { StopLoupe(false); throw; }
        LoupeChanged?.Invoke(this, EventArgs.Empty); QueueLoupeRefresh();
    }
    public void ToggleLoupe() => SetLoupe(!IsLoupeEnabled);
    /// <summary>原线性步进及上下限；未启用时与原命令一样不执行。</summary>
    public void LoupeZoom(int direction)
    {
        if (!IsLoupeEnabled || direction == 0) return;
        if (direction > 0) _loupe!.ZoomIn(); else _loupe!.ZoomOut();
        LoupeChanged?.Invoke(this, EventArgs.Empty); QueueLoupeRefresh();
    }
    /// <summary>原 Last-First 累计输入；不是普通拖动，也不修改内容平移。</summary>
    public void LoupePanBy(Avalonia.Vector delta)
    {
        if (!IsLoupeEnabled || !double.IsFinite(delta.X) || !double.IsFinite(delta.Y)) return;
        _loupeDelta = new(_loupeDelta.X+delta.X, _loupeDelta.Y+delta.Y);
        InvalidateVisual();
        if (IsPanorama || IsBrowsing) QueueLoupeRefresh();
    }
    /// <summary>归还捕获；倍率保留以便下次开启，原 ResetByRestart 才重置它。</summary>
    private void StopLoupe(bool refresh)
    {
        var active = IsLoupeEnabled; ++_loupeGeneration;
        if (_loupe is not null) _loupe.IsEnabled = false;
        _relativePointer?.Dispose(); _relativePointer = null;
        _loupeBase = _loupeDelta = default; _loupeBook = null; _loupeRange = null;
        if (!active) return;
        InvalidateVisual(); LoupeChanged?.Invoke(this, EventArgs.Empty);
        if (refresh && !_disposed) QueueLoupeRefresh();
    }
    /// <summary>原范围变化关闭规则；换书、卸载或模式变更总是归还原生捕获。</summary>
    private void SynchronizeLoupe()
    {
        if (!IsLoupeEnabled || _operation is null) return;
        if (_operation.IsLoading || !ReferenceEquals(_loupeBook, _operation.Book) || _loupeMode != _operation.BrowseMode
            || !ReferenceEquals(_loupe!.Config, Config.Current.Loupe)) { StopLoupe(false); return; }
        var range = _operation.Frame?.FrameRange;
        if (_loupeRange != range && Config.Current.Loupe.IsResetByPageChanged && !IsPanorama) { StopLoupe(false); return; }
        _loupeRange = range;
    }
    /// <summary>垂直滚轮优先改变 Loupe；横轴和禁用设置交还原输入绑定。</summary>
    internal bool TryLoupeWheel(double x, double y)
    {
        if (!IsLoupeEnabled || !Config.Current.Loupe.IsWheelScalingEnabled || y == 0 || Math.Abs(x) > Math.Abs(y)) return false;
        LoupeZoom(y > 0 ? 1 : -1); return true;
    }
    /// <summary>仅查看器的无修饰 Escape 退出，不抢文本/菜单/对话框输入。</summary>
    internal bool TryLoupeEscape(Key key, KeyModifiers modifiers)
    { if (key != Key.Escape || modifiers != KeyModifiers.None || !IsLoupeEnabled || !Config.Current.Loupe.IsEscapeKeyEnabled) return false; SetLoupe(false); return true; }
    private void QueueLoupeRefresh()
    {
        _loupeRefreshRequested = true;
        if (LoupePending.IsCompleted) LoupePending = RefreshLoupeAsync();
    }
    /// <summary>高频相对移动只保留最新需求；现有 revision 继续裁决像素，只有一个刷新循环。</summary>
    private async Task RefreshLoupeAsync()
    {
        await Task.Yield();
        try
        {
            while (_loupeRefreshRequested && !_disposed)
            {
                await Task.Delay(16); _loupeRefreshRequested = false;
                await RefreshAsync();
            }
        }
        catch (Exception ex) { System.Diagnostics.Trace.WriteLine("Loupe refresh: " + ex.GetType().Name); }
    }

    /// <summary>原主元素×帧×手工倍率×设备比例；自定义尺寸/分割仍采用原元素 Scale。</summary>
    private double GetLoupeOriginalScale()
    {
        if (IsBrowsing) return _browse?.GetOriginalScale() ?? 1;
        var element = _frame?.Elements.Where(e=>!e.IsDummy).OrderBy(e=>e.Page.Index).FirstOrDefault();
        return (element?.Scale ?? 1) * (_frame?.Scale ?? 1) * _transform.BaseScale * _zoom * (TopLevel.GetTopLevel(this)?.RenderScaling ?? 1);
    }
    /// <summary>原平移后缩放，围绕画布中心；静帧作用于内容，动态全景作用于整画布。</summary>
    internal Matrix LoupeMatrix => !IsLoupeEnabled ? Matrix.Identity :
        Matrix.CreateTranslation(-Bounds.Width/2, -Bounds.Height/2) * Matrix.CreateTranslation(LoupePan)
        * Matrix.CreateScale(LoupeFixedScale, LoupeFixedScale) * Matrix.CreateTranslation(Bounds.Width/2, Bounds.Height/2);
    private Matrix ApplyLoupe(Matrix matrix) => matrix * LoupeMatrix;
    internal Avalonia.Rect LoupeVisibleRect => LoupeMatrix.TryInvert(out var inverse)
        ? new Avalonia.Rect(Bounds.Size).TransformToAABB(inverse) : new Avalonia.Rect(Bounds.Size);
    private void DrawLoupeInfo(Avalonia.Media.DrawingContext context)
    {
        if (IsLoupeEnabled && Config.Current.Loupe.IsVisibleLoupeInfo)
            DrawText(context, $"{LoupeScale:0.#####}×", new(12, Bounds.Height-30));
    }
}
