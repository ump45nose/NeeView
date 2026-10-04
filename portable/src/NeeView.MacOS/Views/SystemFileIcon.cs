using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
namespace NeeView.MacOS.Views;
/// <summary>可见树行系统图标，平台PNG适配独立于节点/主题；不扫描目录。</summary>
public sealed class SystemFileIcon : Control
{
    public static readonly StyledProperty<string?> PathProperty = AvaloniaProperty.Register<SystemFileIcon, string?>(nameof(Path));
    public static readonly AttachedProperty<Func<string, CancellationToken, Task<byte[]?>>?> LoaderProperty = AvaloniaProperty.RegisterAttached<SystemFileIcon, Control, Func<string, CancellationToken, Task<byte[]?>>?>("Loader", inherits: true);
    public string? Path { get => GetValue(PathProperty); set => SetValue(PathProperty, value); }
    private Bitmap? _icon;
    private CancellationTokenSource? _request;
    private bool _attached;
    private bool _inViewport;
    private string? _loadedPath;
    private readonly List<Visual> _ancestors = [];
    private long _revision;
    public SystemFileIcon() { EffectiveViewportChanged += (_, e) => { _inViewport = e.EffectiveViewport.Intersects(new Avalonia.Rect(Bounds.Size)); Refresh(); }; }
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    { base.OnPropertyChanged(change); if (change.Property == PathProperty || change.Property == LoaderProperty || change.Property == IsVisibleProperty) Refresh(); }
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e) { base.OnAttachedToVisualTree(e); _attached = true; foreach (var v in this.GetVisualAncestors()) { _ancestors.Add(v); v.PropertyChanged += AncestorChanged; } Refresh(); }
    private void AncestorChanged(object? sender, AvaloniaPropertyChangedEventArgs e) { if (e.Property == IsVisibleProperty) Refresh(); }
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e) { base.OnDetachedFromVisualTree(e); _attached = false; foreach (var v in _ancestors) v.PropertyChanged -= AncestorChanged; _ancestors.Clear(); _inViewport = false; Clear(); }
    private void Clear() { ++_revision; _request?.Cancel(); _request = null; _loadedPath = null; _icon?.Dispose(); _icon = null; }
    private async void Refresh()
    {
        if (!_attached || !_inViewport || !IsVisible || _ancestors.Any(v => !v.IsVisible) || Path is not { Length: > 0 } path || GetValue(LoaderProperty) is not { } load) { Clear(); InvalidateVisual(); return; }
        if (_loadedPath == path) return;
        Clear(); _loadedPath = path; InvalidateVisual();
        var request = new CancellationTokenSource(); _request = request; var revision = _revision;
        try
        {
            var data = await load(path, request.Token);
            if (!_attached || revision != _revision || request.IsCancellationRequested || data is null) return;
            using var stream = new MemoryStream(data, false); _icon = new Bitmap(stream); InvalidateVisual();
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested) { }
        catch (Exception ex) { System.Diagnostics.Trace.WriteLine("File icon: " + ex.GetType().Name); }
        finally { if (ReferenceEquals(_request, request)) _request = null; request.Dispose(); }
    }
    public override void Render(DrawingContext context)
    {
        if (_icon is not null) context.DrawImage(_icon, new(0, 0, _icon.Size.Width, _icon.Size.Height), new(0, 0, Bounds.Width, Bounds.Height));
        else context.DrawText(new FormattedText("▸", System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Typeface.Default, 14, Brushes.LightGray), new(0, 0));
    }
}
