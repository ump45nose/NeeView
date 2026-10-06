// Copyright (c) NeeLaboratory. 原 CanvasBackgroundSource/BrushSource 的 Avalonia 表现适配。
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using NeeView;
namespace NeeView.MacOS.Views;

/// <summary>仅拥有背景显示租约/刷；读取沿 Engine，正文、布局和主题独立。</summary>
internal sealed class CanvasBackgroundPresenter(Control owner, BookOperation operation, BitmapFactory factory) : IDisposable
{
    private CancellationTokenSource? _cancellation;
    private string? _path;
    private Bitmap? _bitmap;
    private BitmapLease? _lease;
    private bool _failed, _disposed;
    private Task _work = Task.CompletedTask;
    private DrawingBrush? _checker;
    private (BackgroundType Type, double Scale)? _checkerKey;
    public Task Pending => _work;
    /// <summary>改变背景需求仅取消旧等待者；原生晚到结果自行释放，不阻塞正文打开。</summary>
    public void Refresh()
    {
        if (_disposed) return;
        var config = Config.Current.Background;
        string? path = config.BackgroundType == BackgroundType.Custom && config.CustomBackground.Type != BrushType.SolidColor
            ? config.CustomBackground.ImageFileName : null;
        if (string.IsNullOrEmpty(path)) path = null;
        if (_path == path) { owner.InvalidateVisual(); return; }
        _cancellation?.Cancel(); _path = path; _failed = false;
        _bitmap?.Dispose(); _bitmap = null; _lease?.Dispose(); _lease = null;
        if (path is not null) { var cancel = _cancellation = new CancellationTokenSource(); _work = LoadAsync(path, cancel); }
        owner.InvalidateVisual();
    }
    /// <summary>显示缓冲先释放，像素租约后释放；统一计入原工厂主预算。</summary>
    private async Task LoadAsync(string path, CancellationTokenSource cancel)
    {
        BitmapLease? lease = null; Bitmap? bitmap = null;
        try
        {
            lease = await operation.LoadBackgroundAsync(factory, path, cancel.Token);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                // UI发布点再次校验需求；线程切换期间的关闭/切背景不能接收晚到结果。
                if (_disposed || cancel.IsCancellationRequested || !ReferenceEquals(_cancellation, cancel)) return;
                var image = lease.Image; var pinned = GCHandle.Alloc(image.Pixels, GCHandleType.Pinned);
                try { bitmap = new(PixelFormat.Bgra8888, AlphaFormat.Premul, pinned.AddrOfPinnedObject(), new((int)image.Size.Width, (int)image.Size.Height), new(96, 96), image.Stride); }
                finally { pinned.Free(); }
                lease.RegisterDisplayBytes(checked((long)bitmap.PixelSize.Width * bitmap.PixelSize.Height * 4));
                _bitmap = bitmap; bitmap = null; _lease = lease; lease = null; owner.InvalidateVisual();
            });
        }
        catch (OperationCanceledException) { }
        catch { await Dispatcher.UIThread.InvokeAsync(() => { if (!_disposed && !cancel.IsCancellationRequested && ReferenceEquals(_cancellation, cancel)) { _failed = true; owner.InvalidateVisual(); } }); }
        finally
        {
            bitmap?.Dispose(); lease?.Dispose();
            if (ReferenceEquals(_cancellation, cancel)) _cancellation = null;
            cancel.Dispose();
        }
    }
    /// <summary>原底刷与前景刷分开；棋盘/平铺以设备像素为周期，其他模式按视口伸展。</summary>
    public void Render(DrawingContext context, ThemeRgba contentColor, Avalonia.Size? exportSize = null)
    {
        var config = Config.Current.Background; var bounds = new Avalonia.Rect(exportSize ?? owner.Bounds.Size);
        IBrush? back = config.BackgroundType switch
        { BackgroundType.White => Brushes.White, BackgroundType.Auto => Solid(contentColor), BackgroundType.Check => null,
          BackgroundType.Custom => Solid(config.CustomBackground.Color), _ => Brushes.Black };
        if (back is not null) context.FillRectangle(back, bounds);
        double scale = TopLevel.GetTopLevel(owner)?.RenderScaling ?? 1;
        if (config.BackgroundType is BackgroundType.Check or BackgroundType.CheckDark)
        {
            var key = (config.BackgroundType, scale);
            if (_checkerKey != key)
            {
                _checkerKey = key; _checker = config.BackgroundType == BackgroundType.Check
                    ? Checker(Color.Parse("#FFF8F8F8"), Color.Parse("#FFF0F0F0"), 16 / scale)
                    : Checker(Color.Parse("#FF1C1C1C"), Color.Parse("#FF181818"), 16 / scale);
            }
            context.FillRectangle(_checker!, bounds);
        }
        else if (config.BackgroundType == BackgroundType.Custom && config.CustomBackground.Type != BrushType.SolidColor)
        {
            if (_failed) context.FillRectangle(Brushes.LightGray, bounds);
            else if (_bitmap is { } bitmap)
            {
                var brush = new ImageBrush(bitmap) { Stretch = config.CustomBackground.Type switch
                    { BrushType.ImageFill => Stretch.Fill, BrushType.ImageUniformToFill => Stretch.UniformToFill, _ => Stretch.Uniform } };
                if (config.CustomBackground.Type == BrushType.ImageTile)
                {
                    brush.Stretch = Stretch.Fill; brush.TileMode = TileMode.Tile; brush.AlignmentX = AlignmentX.Left; brush.AlignmentY = AlignmentY.Top;
                    var size = _lease!.Image.SourceSize;
                    brush.DestinationRect = new(0, 0, size.Width / scale, size.Height / scale, RelativeUnit.Absolute);
                }
                context.FillRectangle(brush, bounds);
            }
        }
    }
    /// <summary>原 SolidColor 底刷亮度决定文字；格子非纯色背景使用黑字。</summary>
    public static IBrush Foreground(ThemeRgba contentColor)
    {
        var config = Config.Current.Background;
        if (config.BackgroundType == BackgroundType.Check) return Brushes.Black;
        var color = config.BackgroundType switch { BackgroundType.White => ThemeRgba.Parse("White"), BackgroundType.Auto => contentColor,
            BackgroundType.Custom => config.CustomBackground.Color, _ => ThemeRgba.Parse("Black") };
        return color.R * .299 + color.G * .587 + color.B * .114 < 128 ? Brushes.White : Brushes.Black;
    }
    internal static SolidColorBrush Solid(ThemeRgba color) => new(ToColor(color));
    internal static Color ToColor(ThemeRgba color) => Color.FromArgb(color.A, color.R, color.G, color.B);
    /// <summary>原8×8绘图拉伸到16×16周期；两块对角格，不按页面数生成控件。</summary>
    internal static DrawingBrush Checker(Color a, Color b, double period)
    {
        var drawing = new DrawingGroup();
        drawing.Children.Add(new GeometryDrawing { Brush = new SolidColorBrush(a), Geometry = new RectangleGeometry(new(0, 0, 8, 8)) });
        drawing.Children.Add(new GeometryDrawing { Brush = new SolidColorBrush(b), Geometry = new RectangleGeometry(new(0, 0, 4, 4)) });
        drawing.Children.Add(new GeometryDrawing { Brush = new SolidColorBrush(b), Geometry = new RectangleGeometry(new(4, 4, 4, 4)) });
        return new(drawing) { DestinationRect = new(0, 0, period, period, RelativeUnit.Absolute), Stretch = Stretch.Fill, TileMode = TileMode.Tile };
    }
    public void Dispose()
    {
        if (_disposed) return; _disposed = true; _cancellation?.Cancel();
        _bitmap?.Dispose(); _bitmap = null; _lease?.Dispose(); _lease = null;
    }
}
