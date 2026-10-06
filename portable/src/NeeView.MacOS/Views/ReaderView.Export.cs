// Copyright (c) NeeLaboratory. 原ViewImageExporter，替换VisualBrush/WPF位图，复用唯一帧绘制。
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using NeeView.PageFrames;
namespace NeeView.MacOS.Views;

public sealed partial class ReaderView
{
    private bool _exportOriginalSize;
    /// <summary>捕获原选中页框，不截图窗口；导出当前变换或原始页框尺寸，不含导航/错误/过渡UI。</summary>
    /// <param name="frame">导航锁保护的原页框。</param><param name="options">独立导出草稿。</param>
    /// <param name="stream">调用方持有的文件/ZIP条目流。</param><param name="token">准备及原生编码前后取消。</param>
    public async Task ExportViewAsync(PageFrame frame, IExportImageParameter options, Stream stream, CancellationToken token)
    {
        await Dispatcher.UIThread.InvokeAsync(() => ExportViewCoreAsync(frame, options, stream, token));
    }
    private async Task ExportViewCoreAsync(PageFrame frame, IExportImageParameter options, Stream stream, CancellationToken token)
    {
        Dispatcher.UIThread.VerifyAccess(); token.ThrowIfCancellationRequested(); ImageEffectRenderer.EnsureExportSupported();
        if (IsBrowsing || _disposed) throw new NotSupportedException("视图导出请先切回分页或原帧全景。");
        if (options.IsOriginalSize)
        {
            var raw = frame.GetRawContentSize();
            ValidateExportSize(raw.Width, raw.Height);
        }
        // 原尺寸输出不能把按窗口降采样的显示图放大冒充原分辨率。仍复用唯一图片工厂及预算。
        _exportOriginalSize = options.IsOriginalSize;
        try { await RefreshAsync(); }
        finally { _exportOriginalSize = false; }
        token.ThrowIfCancellationRequested();
        if (_disposed || _frame?.FrameRange != frame.FrameRange || _operation?.Frame?.FrameRange != frame.FrameRange)
            throw new OperationCanceledException("导出页框已改变。", token);
        // 同一PageRange在刷新时可能按最新视口重算比例；尺寸与targets必须来自同一份页框。
        frame = _frame!;
        var targets = _transform.GetTargets().ToArray();
        foreach (var (element, _) in targets)
            if (!element.IsDummy && element.Page.IsImage && (!_images.ContainsKey(element.Page) || _pageErrors.ContainsKey(element.Page)))
                throw new IOException("导出图片未完成加载：" + element.Page.EntryName + " " + _pageErrors.GetValueOrDefault(element.Page));
        if (options.HasBackground && _background is not null) await _background.Pending.WaitAsync(token);
        var matrix = _transform.GetMatrix(false);
        var size = frame.StretchedSize;
        var sourceRect = new Avalonia.Rect(-size.Width / 2, -size.Height / 2, size.Width, size.Height);
        var outputRect = sourceRect.TransformToAABB(matrix);
        // 原IsOriginalSize属于View模式：恢复raw页框尺寸、取消外部变换，页内分割仍保持。
        if (options.IsOriginalSize)
        {
            var raw = frame.GetRawContentSize();
            if (size.Width <= 0 || size.Height <= 0) throw new IOException("导出页框尺寸无效。");
            var scale = Math.Min(raw.Width / size.Width, raw.Height / size.Height);
            // 原离屏Rectangle启用LayoutRounding/SnapsToDevicePixels。把原几何一次归入输出像素，
            // 避免先缩小到窗口再逆矩阵放大时，Skia float舍入使nearest采样偏移一列。
            targets = targets.Select(t => (t.Item1, new Avalonia.Rect(
                Math.Round(t.Item2.X * scale + raw.Width / 2), Math.Round(t.Item2.Y * scale + raw.Height / 2),
                Math.Round(t.Item2.Width * scale), Math.Round(t.Item2.Height * scale)))).ToArray();
            matrix = Matrix.Identity; outputRect = new Avalonia.Rect(0, 0, raw.Width, raw.Height);
        }
        ValidateExportSize(outputRect.Width, outputRect.Height);
        var pixels = new PixelSize((int)Math.Ceiling(outputRect.Width), (int)Math.Ceiling(outputRect.Height));
        using var bitmap = new RenderTargetBitmap(pixels, new(96, 96));
        var result = new ImageEffectRenderResult();
        using (var context = bitmap.CreateDrawingContext())
        {
            if (options.HasBackground) _background?.Render(context, CurrentContentColor, new Avalonia.Size(pixels.Width, pixels.Height));
            var transform = matrix * Matrix.CreateTranslation(-outputRect.X, -outputRect.Y);
            var sampling = options.IsDotKeep ? BitmapInterpolationMode.None : options.IsOriginalSize ? BitmapInterpolationMode.HighQuality : (BitmapInterpolationMode?)null;
            if (!ImageEffectRenderer.DrawScene(context, new Avalonia.Rect(0, 0, pixels.Width, pixels.Height),
                canvas => RecordFrame(canvas, targets, transform, _images, [], 1, sampling, true), true, result))
                DrawFrame(context, targets, transform, _images, [], 1, sampling, immediate: true, result: result);
        }
        // Custom的异常由框架捕获；显式结果保证失败不编码成无效果导出文件。
        result.ThrowIfFailed();
        token.ThrowIfCancellationRequested();
        // 离屏位图独立持有直到编码结束，逐帧释放；非seek ZIP流通过编码器流式写入。
        await Task.Run(() =>
        {
            token.ThrowIfCancellationRequested();
            bitmap.Save(stream, options.FileFormat == BitmapImageFormat.Png ? PngBitmapEncoderOptions.Default : new JpegBitmapEncoderOptions { Quality = Math.Clamp(options.QualityLevel, 5, 100) });
            token.ThrowIfCancellationRequested();
        }, token);
    }
    /// <summary>原尺寸在解码前校验；输出按向上取整后的真实像素计入工作预算。</summary>
    private static void ValidateExportSize(double width, double height)
    {
        if (!double.IsFinite(width) || !double.IsFinite(height) || width < 1 || height < 1
            || width > 32768 || height > 32768 || Math.Ceiling(width) * Math.Ceiling(height) * 4 > 128L * 1024 * 1024)
            throw new NotSupportedException("导出画布超过128MiB工作预算，请降低缩放或关闭原始尺寸。");
    }
}
