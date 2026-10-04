using Avalonia;
using Avalonia.Media;
using NeeView;
using NeeView.PageFrames;
namespace NeeView.MacOS.Views;

/// <summary>唯一查看器的原全景容器适配；像素仍归ReaderView/BitmapFactory，不建立第二显示缓存。</summary>
public sealed partial class ReaderView
{
    private PageFramePanorama? _panorama;
    private (Book Book, PagePosition Position, Avalonia.Vector Pan, double Scale, double Angle, bool FlipX, bool FlipY)? _panoramaRecenter;
    private bool _reportingPanorama;
    private bool IsPanorama => _operation?.BrowseMode == BrowseLayoutMode.Panorama;
    internal IReadOnlyList<PanoramaFrame> PanoramaFrames => _panorama?.Frames ?? [];

    /// <summary>只计算当前帧及邻近轻量容器；来源/图像需求由既有Refresh管理。</summary>
    private void RebuildPanorama()
    {
        if (!IsPanorama || _frame is null || _operation?.Book is not { } book || _operation.Context is not { } context || Bounds.Width <= 0 || Bounds.Height <= 0)
        { _panorama = null; return; }
        _panorama = new(book.Pages, _frame, context, new(-Bounds.Width / 2 - _pan.X, -Bounds.Height / 2 - _pan.Y, Bounds.Width, Bounds.Height),
            _transform.BaseScale * _zoom, _transform.Angle, book.IsIndexing);
    }
    /// <summary>每帧保留自身自动旋转及双页几何，共享当前表现变换；FrameSpace在显示DIP中应用一次。</summary>
    private Matrix PanoramaMatrix(PanoramaFrame placement)
    {
        var center = new Avalonia.Vector(placement.Bounds.X + placement.Bounds.Width / 2, placement.Bounds.Y + placement.Bounds.Height / 2);
        return Matrix.CreateScale(_transform.BaseScale, _transform.BaseScale)
            * Matrix.CreateRotation(placement.Frame.Angle * Math.PI / 180)
            * Matrix.CreateScale(_transform.IsFlipHorizontal ? -_zoom : _zoom, _transform.IsFlipVertical ? -_zoom : _zoom)
            * Matrix.CreateRotation(_transform.Angle * Math.PI / 180)
            * Matrix.CreateTranslation(Bounds.Width / 2 + _pan.X + center.X, Bounds.Height / 2 + _pan.Y + center.Y)
            * Matrix.CreateTranslation(_motion.GetPanOffset());
    }
    /// <summary>可见和一视口邻区共享原缓存；普通分页仍只申请当前原帧。</summary>
    private Page[] GetDemandSources()
    {
        RebuildPanorama();
        return (_panorama is null ? _frame?.Elements ?? [] : _panorama.Frames.SelectMany(f => f.Frame.Elements))
            .Where(e => !e.IsDummy).Select(e => e.Page).Distinct().Take(128).ToArray();
    }
    /// <summary>PagesAsOne选择原全景窗口矩形，否则仅选中帧；拖动限幅使用完整容器区域。</summary>
    private NeeView.Rect GetMotionBounds()
    {
        RebuildPanorama();
        if (_panorama is null) return GetContentRect();
        var r = _panorama.ContentRect;
        return new(r.X + Bounds.Width / 2 + _pan.X, r.Y + Bounds.Height / 2 + _pan.Y, r.Width, r.Height);
    }
    private NeeView.Rect GetScrollContent(bool pagesAsOne) => IsPanorama && pagesAsOne ? GetMotionBounds() : GetContentRect();

    /// <summary>原全景滚动选择距中心最近的帧；重设参考帧时补偿坐标，不把画面拉回页起点。</summary>
    private async Task ReportPanoramaAnchorAsync()
    {
        if (_disposed || !IsPanorama || IsMotionActive || _reportingPanorama || _operation?.Book is not { } book || _frame is null) return;
        RebuildPanorama();
        var placement = _panorama?.FindNearest(-_pan.X, -_pan.Y);
        if (placement is null || placement.Frame.FrameRange == _frame.FrameRange) return;
        _reportingPanorama = true;
        int direction = placement.Frame.FrameRange.Min > _frame.FrameRange.Min ? 1 : -1;
        var position = placement.Frame.FrameRange.Top(direction);
        if (_operation.Context!.IsLoopPage) position = new BookContext(book.Pages).NormalizePosition(position);
        _panoramaRecenter = (book, position, _pan + new Avalonia.Vector(placement.Bounds.X + placement.Bounds.Width / 2,
            placement.Bounds.Y + placement.Bounds.Height / 2), _zoom, _transform.Angle, _transform.IsFlipHorizontal, _transform.IsFlipVertical);
        try
        {
            await _operation.ReportPanoramaPositionAsync(book, position, direction);
            // 无缝循环可能回到同一个Page/Part；业务无需重复登记，表现参考坐标仍要换原点。
            if (_operation.Position == position) { SynchronizeFrame(); RestorePanoramaReference(); }
            await RefreshAsync();
        }
        catch (Exception ex) { System.Diagnostics.Trace.WriteLine("Panorama anchor: " + ex.GetType().Name); }
        finally { _reportingPanorama = false; }
    }
    /// <summary>滚动回报沿原导航锁提交后，在新帧上恢复同一显示坐标及变换。</summary>
    private bool RestorePanoramaReference()
    {
        if (_panoramaRecenter is not { } state || _operation is null) return false;
        if (!ReferenceEquals(state.Book, _operation.Book)) { _panoramaRecenter = null; return false; }
        if (state.Position != _operation.Position) return false;
        _panoramaRecenter = null; _zoom = state.Scale; _transform.Angle = state.Angle;
        _transform.IsFlipHorizontal = state.FlipX; _transform.IsFlipVertical = state.FlipY;
        _pan = state.Pan; return true;
    }
    private void DrawPanorama(DrawingContext context)
    {
        RebuildPanorama();
        foreach (var placement in _panorama?.Frames ?? [])
            DrawFrame(context, ReaderTransformPresenter.GetTargets(placement.Frame), PanoramaMatrix(placement), _images, _pageErrors, 1);
    }
}
