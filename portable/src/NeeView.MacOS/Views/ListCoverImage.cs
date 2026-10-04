using System.Runtime.InteropServices;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.VisualTree;
namespace NeeView.MacOS.Views;

/// <summary>可见列表封面的像素/显示所有者；视图不打开文件，也不保留离屏Bitmap。</summary>
public sealed class ListCoverImage : Control
{
    public static readonly StyledProperty<string?> SourceProperty = AvaloniaProperty.Register<ListCoverImage, string?>(nameof(Source));
    public string? Source { get => GetValue(SourceProperty); set => SetValue(SourceProperty, value); }
    public static readonly StyledProperty<Page?> PageSourceProperty = AvaloniaProperty.Register<ListCoverImage, Page?>(nameof(PageSource));
    /// <summary>页面列表直接租用当前书的原Page，归档条目不重新按路径打开另一来源。</summary>
    public Page? PageSource { get => GetValue(PageSourceProperty); set => SetValue(PageSourceProperty, value); }
    public Func<string, DecodeRequest, CancellationToken, Task<BitmapLease>>? LoadCoverAsync { get; init; }
    public Func<Page, DecodeRequest, CancellationToken, Task<BitmapLease>>? LoadPageAsync { get; init; }
    public PanelListItemProfile Profile { get; init; } = PanelListItemProfile.Create(PanelListItemStyle.Content);
    public string Placeholder { get; set; } = "▱";
    public IBrush IconBrush { get; set; } = Brushes.LightGray;
    private Bitmap? _bitmap;
    private BitmapLease? _lease;
    private CancellationTokenSource? _request;
    private int _revision;
    private bool _inViewport;
    private string? _loadedPath;
    private Page? _loadedPage;
    private int _target;
    private readonly List<Visual> _ancestors = [];
    public bool HasImage => _bitmap is not null;
    public string? Error { get; private set; }
    public Task Loading { get; private set; } = Task.CompletedTask;
    /// <summary>视口交集来自Avalonia，预实现邻行不读取封面。</summary>
    public ListCoverImage()
    { EffectiveViewportChanged += ViewportChanged; }
    /// <summary>Avalonia有效可见事件为内部API；仅订阅本控件的祖先显隐，隐藏立即释放，不做全局扫描。</summary>
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        foreach (var visual in this.GetVisualAncestors()) { _ancestors.Add(visual); visual.PropertyChanged += AncestorChanged; }
        Refresh();
    }
    private void AncestorChanged(object? sender, AvaloniaPropertyChangedEventArgs e) { if (e.Property == IsVisibleProperty) Refresh(); }
    private void ViewportChanged(object? sender, EffectiveViewportChangedEventArgs e)
    { _inViewport = e.EffectiveViewport.Intersects(new Avalonia.Rect(Bounds.Size)); Refresh(); }
    /// <summary>可见规格变化重提需求，原生晚到结果只能归还租约。</summary>
    public void Refresh()
    {
        bool visible = _inViewport && IsVisible && _ancestors.All(visual => visual.IsVisible) && TopLevel.GetTopLevel(this) is not null && Bounds.Width > 0 && Bounds.Height > 0;
        var scale = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;
        int target = Math.Clamp((int)Math.Ceiling(Math.Max(Bounds.Width, Bounds.Height) * scale / 32) * 32, 32, 1024);
        var path = visible ? Source : null;
        var page = visible ? PageSource : null;
        if (path == _loadedPath && ReferenceEquals(page, _loadedPage) && target == _target) return;
        ++_revision; _request?.Cancel(); _request = null; ClearImage(); Error = null; _loadedPath = path; _loadedPage = page; _target = target;
        if (page is null ? path is null || LoadCoverAsync is null : LoadPageAsync is null) { Loading = Task.CompletedTask; return; }
        var request = new CancellationTokenSource(); _request = request; Loading = LoadAsync(path, page, target, _revision, request);
    }
    /// <summary>滚动防抖后交给唯一工厂；创建显示缓冲后登记实际字节。</summary>
    private async Task LoadAsync(string? path, Page? page, int target, int revision, CancellationTokenSource request)
    {
        BitmapLease? lease = null; Bitmap? bitmap = null;
        try
        {
            await Task.Delay(150, request.Token);
            lease = page is null ? await LoadCoverAsync!(path!, new(target, target, true), request.Token) : await LoadPageAsync!(page, new(target, target, true), request.Token);
            if (revision != _revision || request.IsCancellationRequested) return;
            var pixels = lease.Image; var pin = GCHandle.Alloc(pixels.Pixels, GCHandleType.Pinned);
            try { bitmap = new(PixelFormat.Bgra8888, AlphaFormat.Premul, pin.AddrOfPinnedObject(), new((int)pixels.Size.Width, (int)pixels.Size.Height), new(96, 96), pixels.Stride); }
            finally { pin.Free(); }
            lease.RegisterDisplayBytes(checked((long)bitmap.PixelSize.Width * bitmap.PixelSize.Height * 4));
            if (revision != _revision || request.IsCancellationRequested) return;
            _bitmap = bitmap; _lease = lease; bitmap = null; lease = null;
            if (Profile.IsImagePopupEnabled) ToolTip.SetTip(this, new Image { Source = _bitmap, MaxWidth = 256, MaxHeight = 256, Stretch = Stretch.Uniform });
            InvalidateVisual();
        }
        catch (OperationCanceledException) { }
        catch (EmptyArchivePageException) { }
        catch (Exception ex) { if (revision == _revision) { Error = ex.Message; ToolTip.SetTip(this, Error); InvalidateVisual(); } }
        finally { bitmap?.Dispose(); lease?.Dispose(); if (ReferenceEquals(_request, request)) _request = null; request.Dispose(); }
    }
    /// <summary>原Original底对齐、Square填充及Banner上部60%裁剪归表现，不改变解码内容。</summary>
    public override void Render(DrawingContext context)
    {
        base.Render(context); var area = new Avalonia.Rect(Bounds.Size); using var clip = context.PushClip(area);
        if (_bitmap is null)
        {
            context.DrawText(new FormattedText(Error is null ? Placeholder : "!", CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface("sans-serif"), 24, IconBrush), area.Center - new Avalonia.Vector(12, 14));
            return;
        }
        var source = new Avalonia.Rect(_bitmap.Size);
        if (Profile.ImageShape == PanelListItemImageShape.Banner) source = new(0, 0, source.Width, source.Height * .6);
        var scale = Profile.ImageShape == PanelListItemImageShape.Original ? Math.Min(area.Width / source.Width, area.Height / source.Height) : Math.Max(area.Width / source.Width, area.Height / source.Height);
        var width = source.Width * scale; var height = source.Height * scale;
        var y = Profile.ImageShape switch { PanelListItemImageShape.Original => area.Height - height, PanelListItemImageShape.Banner => (area.Height - height) / 2, _ => 0 };
        context.DrawImage(_bitmap, source, new Avalonia.Rect((area.Width - width) / 2, y, width, height));
        if (Profile.IsTagVisible && Profile.IsIconOverlay)
            context.DrawText(new FormattedText(Placeholder, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface("sans-serif"), 14, IconBrush), new Avalonia.Point(2, Math.Max(0, area.Height - 18)));
    }
    /// <summary>先关闭引用显示图的弹层，再释放Bitmap、最后归还像素租约。</summary>
    private void ClearImage() { ToolTip.SetIsOpen(this, false); ToolTip.SetTip(this, null); _bitmap?.Dispose(); _bitmap = null; _lease?.Dispose(); _lease = null; InvalidateVisual(); }
    protected override void OnSizeChanged(SizeChangedEventArgs e) { base.OnSizeChanged(e); Refresh(); }
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    { base.OnPropertyChanged(change); if (change.Property == SourceProperty || change.Property == PageSourceProperty || change.Property == IsVisibleProperty) Refresh(); }
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    { base.OnDetachedFromVisualTree(e); foreach (var visual in _ancestors) visual.PropertyChanged -= AncestorChanged; _ancestors.Clear(); _inViewport = false; ++_revision; _request?.Cancel(); _loadedPath = null; _loadedPage = null; ClearImage(); }
}
