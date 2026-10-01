using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using NeeView.Application;
using NeeView.Core;

namespace NeeView.Desktop;

/// <summary>虚拟化查看器：单一绘制控件，图像资源仅覆盖可见和邻近区域。</summary>
public sealed class ReaderView : Control, IDisposable
{
    private sealed class Display(DecodedImageLease lease, WriteableBitmap bitmap, ContentVersion version) : IDisposable
    {
        public DecodedImageLease Lease = lease; public WriteableBitmap Bitmap = bitmap;
        public ContentVersion Version = version;
        public void Dispose() { Bitmap.Dispose(); Lease.Dispose(); }
    }
    private readonly IReaderSession _session;
    private readonly IImageRequestScheduler _scheduler;
    private readonly IImageDecoder _decoder;
    private readonly Dictionary<ContentId, Display> _images = [];
    private readonly Dictionary<ContentId, CancellationTokenSource> _requests = [];
    private readonly Dictionary<ContentId, string> _errors = [];
    private LayoutSnapshot _layout = new([], 0);
    private ReaderSnapshot _snapshot;
    private double _top;
    private double _viewportHeight = 600;
    private double _viewportWidth = 800;
    private bool _disposed;
    private ReadingAnchor? _observed;
    public event Action<double>? ScrollRequested;
    public LayoutSnapshot Layout => _layout;
    public ReaderView(IReaderSession session, IImageRequestScheduler scheduler, IImageDecoder decoder)
    {
        _session = session; _scheduler = scheduler; _decoder = decoder; _snapshot = session.Snapshot;
        Focusable = true; ClipToBounds = true;
        PointerPressed += OnPointerPressed;
        DoubleTapped += async (_, _) => { if (_snapshot.Anchor is { } anchor) { await session.SetOptionsAsync(_snapshot.Options with { Mode = ReaderMode.Paged }); await session.LocateAsync(anchor); } };
        Pinch += (_, e) => { _ = session.SetOptionsAsync(_snapshot.Options with { Zoom = Math.Clamp(_snapshot.Options.Zoom * e.Scale, 0.1, 8) }); e.Handled = true; };
    }
    /// <summary>更新快照；切书取消旧需求并释放显示资源。</summary>
    public void SetSnapshot(ReaderSnapshot snapshot)
    {
        var generationChanged = snapshot.Generation != _snapshot.Generation;
        var anchorChanged = snapshot.Anchor != _snapshot.Anchor;
        var geometryChanged = generationChanged || snapshot.Options != _snapshot.Options
            || !ReferenceEquals(snapshot.Index?.Pages, _snapshot.Index?.Pages);
        if (generationChanged)
        {
            foreach (var request in _requests.Values) request.Cancel(); _requests.Clear();
            foreach (var display in _images.Values) display.Dispose(); _images.Clear(); _errors.Clear();
            _observed = null;
        }
        var modeChanged = snapshot.Options.Mode != _snapshot.Options.Mode;
        _snapshot = snapshot;
        if (geometryChanged || snapshot.Options.Mode == ReaderMode.Paged && anchorChanged) Rebuild(true);
        else
        {
            if (modeChanged || anchorChanged && snapshot.Anchor != _observed) ScrollRequested?.Invoke(_layout.RestoreY(snapshot.Anchor));
            UpdateDemands(); InvalidateVisual();
        }
    }
    /// <summary>输入视口尺寸和滚动量，更新可见需求并回报内容锚点。</summary>
    public void SetViewport(double width, double height, double top, bool observe = true)
    {
        var changed = Math.Abs(width - _viewportWidth) > 0.5 || Math.Abs(height - _viewportHeight) > 0.5;
        _viewportWidth = Math.Max(1, width); _viewportHeight = Math.Max(1, height); _top = Math.Max(0, top);
        if (changed) Rebuild(true); else UpdateDemands();
        if (observe && _snapshot.Options.Mode != ReaderMode.Paged)
        {
            var item = _layout.Visible(_top, _top + _viewportHeight).OrderBy(i => i.Bounds.Y).FirstOrDefault(i => i.Bounds.Bottom > _top);
            if (item is not null)
            {
                _observed = new(item.Page.Id, item.Part, Math.Clamp((_top - item.Bounds.Y) / item.Bounds.Height, 0, 1));
                _ = _session.LocateAsync(_observed);
            }
        }
        InvalidateVisual();
    }
    /// <summary>按模式重新计算几何；通过锚点补偿尺寸晚到和窗口变化。</summary>
    private void Rebuild(bool restore)
    {
        ILayoutStrategy strategy = _snapshot.Options.Mode switch { ReaderMode.Continuous => new ContinuousLayout(), ReaderMode.Masonry => new MasonryLayout(), _ => new PagedLayout() };
        var input = new LayoutInput(_snapshot.Index?.Pages ?? [], _snapshot.Options, _viewportWidth, _viewportHeight,
            _snapshot.Anchor, TopLevel.GetTopLevel(this)?.RenderScaling ?? 1);
        _layout = strategy.Calculate(input); Height = Math.Max(_viewportHeight, _layout.Height);
        Width = Math.Max(_viewportWidth, _layout.Items.Select(i => i.Bounds.X + i.Bounds.Width).DefaultIfEmpty(_viewportWidth).Max());
        if (restore && _snapshot.Options.Mode != ReaderMode.Paged)
        {
            _top = Math.Clamp(_layout.RestoreY(_snapshot.Anchor), 0, Math.Max(0, _layout.Height - _viewportHeight));
            ScrollRequested?.Invoke(_top);
        }
        else if (restore) { _top = 0; ScrollRequested?.Invoke(0); }
        UpdateDemands(); InvalidateVisual();
    }
    /// <summary>更新可见和邻近资源；脱离范围的租约立即归还。</summary>
    private void UpdateDemands()
    {
        if (_disposed || _session.Source is not { } source) return;
        var demand = _layout.Visible(Math.Max(0, _top - _viewportHeight * 0.5), _top + _viewportHeight * 1.5).ToArray();
        var needed = demand.Select(i => i.Page.Id).ToHashSet();
        foreach (var id in _requests.Keys.Where(id => !needed.Contains(id)).ToArray()) { _requests[id].Cancel(); _requests.Remove(id); }
        foreach (var id in _images.Keys.Where(id => !needed.Contains(id)).ToArray()) { _images[id].Dispose(); _images.Remove(id); }
        foreach (var item in demand)
        {
            if (_images.TryGetValue(item.Page.Id, out var existing) && existing.Version != item.Page.Version)
            { existing.Dispose(); _images.Remove(item.Page.Id); _errors.Remove(item.Page.Id); }
            if (_images.ContainsKey(item.Page.Id) || _requests.ContainsKey(item.Page.Id) || _errors.ContainsKey(item.Page.Id)) continue;
            var cancellation = new CancellationTokenSource(); _requests.Add(item.Page.Id, cancellation);
            var priority = item.Page.Id == _snapshot.Anchor?.Content ? ImagePriority.Current
                : item.Bounds.Intersects(_top, _top + _viewportHeight) ? ImagePriority.Visible : ImagePriority.Prefetch;
            _ = LoadAsync(source, item, priority, _snapshot.Generation, cancellation);
        }
    }
    /// <summary>后台探测/解码，代次检查后在 UI 线程创建显示资源。</summary>
    private async Task LoadAsync(IContentSource source, LayoutItem item, ImagePriority priority, long generation, CancellationTokenSource cancellation)
    {
        DecodedImageLease? lease = null;
        try
        {
            if (item.Page.Size is null)
            {
                await using var stream = await source.OpenReadAsync(item.Page, cancellation.Token);
                var info = await _decoder.ProbeAsync(stream, cancellation.Token);
                await _session.ReportSizeAsync(item.Page.Id, info.Size, generation);
            }
            var scale = TopLevel.GetTopLevel(this)?.RenderScaling ?? 2;
            var width = Math.Clamp((int)Math.Ceiling(item.Bounds.Width * scale * (item.Divided ? 2 : 1)), 128, 8192);
            lease = await _scheduler.RequestAsync(source, new(item.Page, width, 16384), priority, cancellation.Token);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (_disposed || generation != _snapshot.Generation || cancellation.IsCancellationRequested) return;
                var bitmap = new WriteableBitmap(new(lease.Size.Width, lease.Size.Height), new(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
                using (var framebuffer = bitmap.Lock())
                {
                    var bytes = lease.Pixels.ToArray();
                    for (var row = 0; row < lease.Size.Height; row++)
                        Marshal.Copy(bytes, row * lease.Stride, framebuffer.Address + row * framebuffer.RowBytes, lease.Stride);
                }
                _images[item.Page.Id] = new(lease, bitmap, item.Page.Version); lease = null; InvalidateVisual();
            });
        }
        catch (OperationCanceledException) { }
        catch (Exception error)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            { if (!_disposed && generation == _snapshot.Generation && !cancellation.IsCancellationRequested) { _errors[item.Page.Id] = error.Message; InvalidateVisual(); } });
        }
        finally
        {
            lease?.Dispose();
            await Dispatcher.UIThread.InvokeAsync(() =>
            { if (_requests.TryGetValue(item.Page.Id, out var active) && active == cancellation) _requests.Remove(item.Page.Id); });
            cancellation.Dispose();
        }
    }
    /// <summary>绘制视口中的图像/错误占位，保留损坏条目的导航身份。</summary>
    public override void Render(DrawingContext context)
    {
        base.Render(context); context.FillRectangle(new SolidColorBrush(Color.Parse("#15181D")), new Rect(Bounds.Size));
        foreach (var item in _layout.Visible(_top, _top + _viewportHeight))
        {
            var bounds = new Rect(item.Bounds.X, item.Bounds.Y, item.Bounds.Width, item.Bounds.Height);
            if (_images.TryGetValue(item.Page.Id, out var display))
            {
                var src = new Rect(0, 0, display.Lease.Size.Width, display.Lease.Size.Height);
                if (item.Divided)
                {
                    var firstRight = _snapshot.Options.Direction == ReadDirection.RightToLeft;
                    var right = item.Part == 0 ? firstRight : !firstRight;
                    if (Math.Abs(_snapshot.Options.Rotation) % 180 == 90)
                    {
                        var top = right == ((_snapshot.Options.Rotation % 360 + 360) % 360 == 90);
                        src = new(0, top ? 0 : src.Height / 2, src.Width, src.Height / 2);
                    }
                    else src = new(right ? src.Width / 2 : 0, 0, src.Width / 2, src.Height);
                }
                if (_snapshot.Options.Rotation % 360 == 0) context.DrawImage(display.Bitmap, src, bounds);
                else
                {
                    var center = bounds.Center;
                    var swapped = Math.Abs(_snapshot.Options.Rotation) % 180 == 90;
                    var target = swapped ? new Rect(center.X - bounds.Height / 2, center.Y - bounds.Width / 2, bounds.Height, bounds.Width) : bounds;
                    using var transform = context.PushTransform(Matrix.CreateTranslation(-center.X, -center.Y)
                        * Matrix.CreateRotation(_snapshot.Options.Rotation * Math.PI / 180) * Matrix.CreateTranslation(center.X, center.Y));
                    context.DrawImage(display.Bitmap, src, target);
                }
            }
            else
            {
                context.FillRectangle(new SolidColorBrush(Color.Parse("#262C35")), bounds);
                var text = new FormattedText(_errors.TryGetValue(item.Page.Id, out var error) ? $"{item.Page.Name}\n{error}" : item.Page.Name,
                    System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Typeface.Default, 13, Brushes.LightGray);
                context.DrawText(text, bounds.TopLeft + new Vector(8, 8));
            }
            if (item.Page.Id == _snapshot.Selection) context.DrawRectangle(null, new Pen(Brushes.DodgerBlue, 3), bounds);
        }
    }
    /// <summary>点击明确选择操作对象；在瀑布流中滚动不改变选择。</summary>
    private async void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        Focus(); var point = e.GetPosition(this);
        var item = _layout.Visible(_top, _top + _viewportHeight).FirstOrDefault(i => new Rect(i.Bounds.X, i.Bounds.Y, i.Bounds.Width, i.Bounds.Height).Contains(point));
        if (item is not null) await _session.LocateAsync(new(item.Page.Id, item.Part), true);
    }
    /// <summary>取消需求并释放全部显示租约。</summary>
    public void Dispose()
    {
        _disposed = true; foreach (var request in _requests.Values) request.Cancel(); _requests.Clear();
        foreach (var display in _images.Values) display.Dispose(); _images.Clear();
    }
}
