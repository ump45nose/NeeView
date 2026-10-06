using System.IO.Compression;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using ImageMagick;
using NeeView.Backends;
using NeeView.MacOS.Views;
using NeeView.MacOS.ViewModels;
namespace NeeView.Engine.Tests;

/// <summary>原条目字节/帧语义、真实导出图像、失败目标保持与关闭等待；全部使用隔离目录。</summary>
public sealed class ImageExportTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    public static bool HasImageSample => File.Exists(Environment.GetEnvironmentVariable("NEEVIEW_IMAGE_EXPORT_SAMPLE"));
    [AvaloniaFact(SkipUnless = nameof(HasImageSample), Skip = "需显式提供只读实图样本")]
    public async Task MountedImageSampleExportsOriginalBytesAndFullViewWithoutChangingSource()
    {
        using var f = new Fixture(); var sample = Environment.GetEnvironmentVariable("NEEVIEW_IMAGE_EXPORT_SAMPLE")!;
        var before = await File.ReadAllBytesAsync(sample, Token); var stamp = File.GetLastWriteTimeUtc(sample);
        var state = new SaveData(f.State); await state.LoadAsync(Token);
        var op = f.Operation(state); var window = Window(op, state, new(new MagickImageDecoder()));
        try
        {
            await window.OpenAsync(sample);
            await op.ApplySettingAsync(s => { s.PageMode = PageMode.SinglePage; s.IsSupportedDividePage = false; });
            var original = Path.Combine(f.Root, "original" + Path.GetExtension(sample));
            await op.ExportImagesAsync(new ExportImageParameter(), original, Token);
            var originalBytes = await File.ReadAllBytesAsync(original, Token); Assert.Equal(before, originalBytes);
            var view = Path.Combine(f.Root, "view.png");
            await op.ExportImagesAsync(new ExportImageParameter { Mode = ExportImageMode.View, IsOriginalSize = true }, view, Token);
            using var exported = new MagickImage(view); var expected = op.Frame!.GetRawContentSize();
            Assert.Equal((uint)Math.Ceiling(expected.Width), exported.Width); Assert.Equal((uint)Math.Ceiling(expected.Height), exported.Height);
            Assert.Equal(before, await File.ReadAllBytesAsync(sample, Token)); Assert.Equal(stamp, File.GetLastWriteTimeUtc(sample));
            if (Environment.GetEnvironmentVariable("NEEVIEW_ACCEPTANCE_PHASE") == "p5-image-export")
            {
                var result = new { scope = "用户指定资源的一张图片，源及Profile只读；输出仅在隔离临时目录并在结束后清理", width = exported.Width, height = exported.Height,
                    source_sha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(before)),
                    original_sha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(originalBytes)), view_bytes = new FileInfo(view).Length, source_unchanged = true };
                var path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../acceptance/p5-image-export-resource.json"));
                await File.WriteAllTextAsync(path, System.Text.Json.JsonSerializer.Serialize(result, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }) + "\n", Token);
            }
        }
        finally { await window.PrepareShutdownAsync(); window.Close(); }
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task OriginalCopiesFirstPageBytesOfSelectedDoubleFrame(bool zip)
    {
        using var f = new Fixture(); var state = new SaveData(f.State); await state.LoadAsync(Token);
        await using var op = f.Operation(state); await op.OpenAsync(zip ? f.Zip : f.Images, Token);
        await op.ApplySettingAsync(s => { s.PageMode = PageMode.WidePage; s.IsSupportedSingleFirstPage = false; s.BookReadOrder = PageReadOrder.RightToLeft; });
        await op.JumpAsync(2); var first = op.Frame!.Elements[0].Page; var old = op.Position;
        var output = Path.Combine(f.Root, "original.png"); await op.ExportImagesAsync(new ExportImageParameter(), output, Token);
        Assert.Equal(await File.ReadAllBytesAsync(Path.Combine(f.Images, first.EntryName), Token), await File.ReadAllBytesAsync(output, Token));
        Assert.Equal(old, op.Position); Assert.False(op.IsExporting);
    }
    [Theory]
    [InlineData(ExportBookType.Folder)] [InlineData(ExportBookType.Zip)]
    public async Task OriginalWholeBookPreservesEverySourceAndIndexNames(ExportBookType type)
    {
        using var f = new Fixture(); var state = new SaveData(f.State); await state.LoadAsync(Token);
        await using var op = f.Operation(state); await op.OpenAsync(f.Zip, Token); await op.JumpAsync(3); var old = op.Position;
        var target = Path.Combine(f.Root, type == ExportBookType.Zip ? "out.zip" : "out");
        var result = await op.ExportImagesAsync(new ExportBookParameter { BookType = type }, target, Token);
        Assert.Equal(5, result.Count); Assert.Equal(old, op.Position);
        if (type == ExportBookType.Zip)
        {
            using var zip = ZipFile.OpenRead(target); Assert.Equal(5, zip.Entries.Count);
            foreach (var entry in zip.Entries) { using var input = entry.Open(); using var bytes = new MemoryStream(); await input.CopyToAsync(bytes, Token); Assert.Equal(await File.ReadAllBytesAsync(Path.Combine(f.Images, entry.FullName), Token), bytes.ToArray()); }
        }
        else foreach (var path in Directory.GetFiles(target)) Assert.Equal(await File.ReadAllBytesAsync(Path.Combine(f.Images, Path.GetFileName(path)), Token), await File.ReadAllBytesAsync(path, Token));
    }
    [Theory]
    [InlineData(ExportOverwriteAnswer.Cancel)] [InlineData(ExportOverwriteAnswer.AddNumber)] [InlineData(ExportOverwriteAnswer.Replace)]
    public async Task ConfirmUsesRealAnswerAndPreservesOldTargetUntilSuccess(ExportOverwriteAnswer answer)
    {
        using var f = new Fixture(); var state = new SaveData(f.State); await state.LoadAsync(Token);
        await using var op = f.Operation(state); await op.OpenAsync(f.Images, Token); var target = Path.Combine(f.Root, "copy.png"); await File.WriteAllTextAsync(target, "old", Token);
        op.ConfirmExportOverwriteAsync = (path, token) => Task.FromResult(answer);
        if (answer == ExportOverwriteAnswer.Cancel) { await Assert.ThrowsAnyAsync<OperationCanceledException>(() => op.ExportImagesAsync(new ExportImageParameter(), target, Token)); Assert.Equal("old", await File.ReadAllTextAsync(target, Token)); }
        else
        {
            var result = await op.ExportImagesAsync(new ExportImageParameter(), target, Token);
            Assert.Equal(answer == ExportOverwriteAnswer.AddNumber ? Path.Combine(f.Root, "copy (1).png") : target, result.Path);
            Assert.Equal(await File.ReadAllBytesAsync(Path.Combine(f.Images, "001.png"), Token), await File.ReadAllBytesAsync(result.Path, Token));
            if (answer == ExportOverwriteAnswer.AddNumber) Assert.Equal("old", await File.ReadAllTextAsync(target, Token));
        }
        Assert.Empty(Directory.GetFiles(f.Root, ".neeview-export-*")); Assert.False(op.IsExporting);
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task PartialWriteFailureOrCancelKeepsExistingFileAndCleansTemporary(bool cancel)
    {
        using var f = new Fixture(); var target = Path.Combine(f.Root, "target.zip"); await File.WriteAllTextAsync(target, "old", Token);
        var action = ExportImageWriter.WriteFileAsync(target, ExportImageOverwriteMode.Confirm, (_, _) => Task.FromResult(ExportOverwriteAnswer.Replace), async (stream, ct) =>
        { await stream.WriteAsync(new byte[100], ct); if (cancel) throw new OperationCanceledException(ct); throw new IOException("bad entry"); }, Token);
        if (cancel) await Assert.ThrowsAnyAsync<OperationCanceledException>(() => action); else await Assert.ThrowsAsync<IOException>(() => action);
        Assert.Equal("old", await File.ReadAllTextAsync(target, Token)); Assert.Empty(Directory.GetFiles(f.Root, ".neeview-export-*"));
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task WholeFolderReportsCompletedCountAndKeepsCommittedFilesOnFailureOrCancel(bool cancel)
    {
        using var f = new Fixture(); var state = new SaveData(f.State); await state.LoadAsync(Token);
        await using var op = f.Operation(state); await op.OpenAsync(f.Images, Token);
        var target = Path.Combine(f.Root, "folder");
        var calls = 0;
        op.ViewImageExporter = new DelegateExporter(async (_, _, stream, token) =>
        {
            if (++calls > 1)
            {
                if (cancel) throw new OperationCanceledException(token);
                throw new IOException("simulated destination failure");
            }
            await stream.WriteAsync(new byte[] { 1, 2, 3 }, token);
        });
        var parameter = new ExportBookParameter { Mode = ExportImageMode.View, BookType = ExportBookType.Folder, OverwriteMode = ExportImageOverwriteMode.AddNumber,
            FileNameFormat0 = "same", FileNameFormat1 = "same", FileNameFormat2 = "same" };
        var error = await Assert.ThrowsAsync<ExportPartialException>(() => op.ExportImagesAsync(parameter, target, Token));
        Assert.Equal(1, error.CompletedCount); Assert.Equal(cancel, error.Canceled);
        Assert.True(File.Exists(Path.Combine(target, "same.png")));
        Assert.Empty(Directory.GetFiles(target, ".neeview-export-*")); Assert.False(op.IsExporting);
    }
    [Fact]
    public async Task WholeFolderOuterConfirmationIsIndependentFromPerFileAddNumberAndDisallow()
    {
        using var f = new Fixture(); var state = new SaveData(f.State); await state.LoadAsync(Token);
        await using var op = f.Operation(state); await op.OpenAsync(f.Images, Token);
        var target = Path.Combine(f.Root, "folder"); Directory.CreateDirectory(target); await File.WriteAllTextAsync(Path.Combine(target, "001.png"), "old", Token);
        var confirmed = new List<string>(); op.ConfirmExportOverwriteAsync = (path, token) => { confirmed.Add(path); return Task.FromResult(ExportOverwriteAnswer.Replace); };
        var add = new ExportBookParameter { BookType = ExportBookType.Folder, OverwriteMode = ExportImageOverwriteMode.AddNumber };
        var result = await op.ExportImagesAsync(add, target, Token);
        Assert.Equal(5, result.Count); Assert.Contains(Path.Combine(target, "001 (1).png"), Directory.GetFiles(target));
        Assert.Single(confirmed); Assert.Equal(Path.GetFullPath(target), confirmed[0]);

        var disallowTarget = Path.Combine(f.Root, "disallow"); Directory.CreateDirectory(disallowTarget);
        await File.WriteAllTextAsync(Path.Combine(disallowTarget, "001.png"), "old", Token);
        var disallow = new ExportBookParameter { BookType = ExportBookType.Folder, OverwriteMode = ExportImageOverwriteMode.Disallow };
        await Assert.ThrowsAsync<IOException>(() => op.ExportImagesAsync(disallow, disallowTarget, Token));
        Assert.Equal("old", await File.ReadAllTextAsync(Path.Combine(disallowTarget, "001.png"), Token));
    }
    [Theory]
    [InlineData("../evil.png")] [InlineData("/absolute.png")] [InlineData("dir/../../evil.png")] [InlineData("dir//image.png")]
    public void SourceTemplateCannotEscapeOutputRoot(string name) => Assert.Throws<IOException>(() => ExportImageParameterTools.ValidateRelativeFileName(name));
    [Fact]
    public async Task ExistingChildSymlinkDoesNotRedirectBookExport()
    {
        using var f = new Fixture(); var root = Path.Combine(f.Root, "out"); Directory.CreateDirectory(root); Directory.CreateSymbolicLink(Path.Combine(root, "linked"), f.Images);
        Assert.Throws<IOException>(() => ExportImageWriter.CheckChildLinks(root, Path.Combine(root, "linked/file.png")));
        Assert.Equal("safe\\name.png", ExportImageParameterTools.ValidateRelativeFileName("safe\\name.png")); await Task.CompletedTask;
    }
    [Fact]
    public async Task DirectTemplateChecksIntermediateLinksButKeepsOrdinarySubdirectories()
    {
        using var f = new Fixture(); var root = Path.Combine(f.Root, "out"); Directory.CreateDirectory(root);
        Directory.CreateSymbolicLink(Path.Combine(root, "linked"), f.Images);
        await Assert.ThrowsAsync<IOException>(() => ExportImageParameterTools.PrepareTargetAsync(root, "linked/file.png", Token));
        Assert.Equal(Path.Combine(root, "sub/file.png"), await ExportImageParameterTools.PrepareTargetAsync(root, "sub/file.png", Token));
        Assert.False(File.Exists(Path.Combine(f.Images, "file.png")));
    }
    [Fact]
    public async Task BrokenOriginalEntryKeepsExistingWholeZipAndCleansTemporary()
    {
        using var f = new Fixture(); var state = new SaveData(f.State); await state.LoadAsync(Token);
        await using var op = f.Operation(state); await op.OpenAsync(f.Images, Token);
        File.Delete(Path.Combine(f.Images, "003.png"));
        var output = Path.Combine(f.Root, "old.zip"); await File.WriteAllTextAsync(output, "old zip", Token);
        op.ConfirmExportOverwriteAsync = (_, _) => Task.FromResult(ExportOverwriteAnswer.Replace);
        await Assert.ThrowsAnyAsync<IOException>(() => op.ExportImagesAsync(new ExportBookParameter(), output, Token));
        Assert.Equal("old zip", await File.ReadAllTextAsync(output, Token));
        Assert.Empty(Directory.GetFiles(f.Root, ".neeview-export-*")); Assert.False(op.IsExporting);
    }
    private sealed class DelegateExporter(Func<NeeView.PageFrames.PageFrame, IExportImageParameter, Stream, CancellationToken, Task> write) : IViewImageExporter
    {
        public Task ExportViewAsync(NeeView.PageFrames.PageFrame frame, IExportImageParameter options, Stream stream, CancellationToken token)
            => write(frame, options, stream, token);
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task SwitchOrCloseWaitsForLateEncodingAndKeepsSourceUntilCleanup(bool close)
    {
        using var f = new Fixture(); var state = new SaveData(f.State); await state.LoadAsync(Token);
        await using var op = f.Operation(state); await op.OpenAsync(f.Images, Token);
        var book = op.Book!; var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        op.ViewImageExporter = new DelegateExporter(async (_, _, stream, _) =>
        {
            started.SetResult(); await release.Task;
            Assert.False(book.Source.IsDisposed);
            await stream.WriteAsync(new byte[] { 1, 2, 3 }); // 模拟不可立即取消的原生编码。
        });
        var output = Path.Combine(f.Root, "late.png");
        var export = op.ExportImagesAsync(new ExportImageParameter { Mode = ExportImageMode.View }, output, Token);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10), Token);
        Task next = close ? op.DisposeAsync().AsTask() : op.OpenAsync(f.Zip, Token);
        Assert.False(next.IsCompleted); Assert.False(book.Source.IsDisposed);
        release.SetResult();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => export);
        await next.WaitAsync(TimeSpan.FromSeconds(10), Token);
        Assert.True(book.Source.IsDisposed); Assert.False(File.Exists(output));
        Assert.Empty(Directory.GetFiles(f.Root, ".neeview-export-*"));
    }
    [Fact]
    public async Task WholeViewSeamlessLoopStopsOnOriginalFrameContainingLastPage()
    {
        using var f = new Fixture(); var state = new SaveData(f.State); await state.LoadAsync(Token);
        await using var op = f.Operation(state); await op.OpenAsync(f.Images, Token);
        await op.SetPageEndActionAsync(PageEndAction.SeamlessLoop);
        await op.ApplySettingAsync(s => { s.PageMode = PageMode.WidePage; s.IsSupportedSingleFirstPage = false; s.IsSupportedSingleLastPage = false; });
        var frames = new List<int[]>();
        op.ViewImageExporter = new DelegateExporter(async (frame, _, stream, token) =>
        { frames.Add(frame.Elements.Where(e => !e.IsDummy).Select(e => e.Page.Index).ToArray()); await stream.WriteAsync(new byte[] { 1 }, token); });
        var result = await op.ExportImagesAsync(new ExportBookParameter { Mode = ExportImageMode.View }, Path.Combine(f.Root, "loop.zip"), Token);
        Assert.Equal(3, result.Count); Assert.Contains(4, frames.Last());
    }
    private sealed class Platform : IPlatformService
    { public Task RevealAsync(string path, CancellationToken token = default) => throw new NotSupportedException(); public Task TrashAsync(string path, CancellationToken token = default) => throw new NotSupportedException(); }
    private static MainWindow Window(BookOperation op, SaveData state, BitmapFactory factory)
    { var w = new MainWindow(); w.Bind(new(op, new(op), state), factory, new Platform()); w.Show(); return w; }
    [AvaloniaFact]
    public async Task OriginalSizeViewLoadsFullResolutionRatherThanUpscalingWindowImage()
    {
        using var f = new Fixture(); var raw = new byte[1200 * 600 * 3];
        for (int i = 0; i < raw.Length / 3; i++) raw[i * 3 + (i % 2 == 0 ? 0 : 2)] = 255;
        using (var image = new MagickImage(raw, new MagickReadSettings { Width = 1200, Height = 600, Depth = 8, Format = MagickFormat.Rgb }))
            image.Write(Path.Combine(f.Images, "001.png"));
        var state = new SaveData(f.State); await state.LoadAsync(Token);
        var op = f.Operation(state); var window = Window(op, state, new(new MagickImageDecoder()));
        try
        {
            window.Width = 800; await window.OpenAsync(f.Images);
            await op.ApplySettingAsync(s => { s.PageMode = PageMode.SinglePage; s.IsSupportedDividePage = false; });
            var target = Path.Combine(f.Root, "full.png");
            await op.ExportImagesAsync(new ExportImageParameter { Mode = ExportImageMode.View, IsOriginalSize = true, IsDotKeep = true }, target, Token);
            using var exported = new MagickImage(target);
            Assert.Equal((uint)1200, exported.Width); Assert.Equal((uint)600, exported.Height);
            using var input = new MagickImage(Path.Combine(f.Images, "001.png")); using var original = input.GetPixels();
            using var pixels = exported.GetPixels();
            Assert.NotEqual(original.GetPixel(500, 300).GetChannel(0), original.GetPixel(501, 300).GetChannel(0));
            for (int x = 500; x <= 501; x++) for (uint channel = 0; channel < 3; channel++)
                Assert.True(original.GetPixel(x, 300).GetChannel(channel) == pixels.GetPixel(x, 300).GetChannel(channel),
                    $"x={x}, channel={channel}, input={string.Join(',', original.GetPixel(x, 300).ToArray())}, export={string.Join(',', pixels.GetPixel(x, 300).ToArray())}, firstExport={string.Join(',', pixels.GetPixel(0, 300).ToArray())}, frame={op.Frame!.StretchedSize}");
        }
        finally { await window.PrepareShutdownAsync(); window.Close(); }
    }
    [AvaloniaFact]
    public async Task DialogAutomaticallyPreviewsLatestDraftAndRejectsLateClosedResult()
    {
        using var f = new Fixture(); var pages = new ExportPageSource(f.Images, 1, [new(new PageNameSource(0, "001.png"))]);
        var model = new ExportImageViewModel(new ExportImageParameter(), pages, false);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var seen = new List<ExportImageMode>();
        var dialog = new ExportImageDialog(model);
        dialog.PreviewAsync = async (parameter, token) =>
        {
            seen.Add(parameter.Mode);
            if (seen.Count == 1) { entered.SetResult(); await release.Task; }
            return await File.ReadAllBytesAsync(Path.Combine(f.Images, "001.png"), Token);
        };
        dialog.Show();
        try
        {
            await entered.Task;
            model.Draft.Mode = ExportImageMode.View;
            Assert.Null(dialog.FindControl<Image>("PreviewImage")!.Source);
            release.SetResult(); await dialog.PendingPreview;
            Assert.Equal(new[] { ExportImageMode.Original, ExportImageMode.View }, seen);
            Assert.NotNull(dialog.FindControl<Image>("PreviewImage")!.Source);
            var late = new TaskCompletionSource<byte[]?>(TaskCreationOptions.RunContinuationsAsynchronously);
            dialog.PreviewAsync = (_, _) => late.Task;
            var pending = dialog.RefreshPreviewAsync(); await Task.Yield(); dialog.Close();
            Assert.False(pending.IsCompleted);
            late.SetResult(await File.ReadAllBytesAsync(Path.Combine(f.Images, "001.png"), Token));
            await pending;
            Assert.Null(dialog.FindControl<Image>("PreviewImage")!.Source);
            Assert.Equal("", model.Error);
        }
        finally { release.TrySetResult(); dialog.Close(); await dialog.PendingPreview; }
    }
    [AvaloniaTheory]
    [InlineData(false)] [InlineData(true)]
    public async Task OriginalDialogLayoutAndRenderedPreviewAreIndependentDrafts(bool book)
    {
        using var f = new Fixture(); var state = new SaveData(f.State); await state.LoadAsync(Token);
        var op = f.Operation(state); var window = Window(op, state, new(new MagickImageDecoder()));
        try
        {
            await window.OpenAsync(f.Zip); await window.Viewer.RefreshAsync();
            IExportImageParameter original = book ? Config.Current.Book.ExportBookParameter : Config.Current.Book.ExportImageParameter;
            var originalMode = original.Mode;
            var model = new ExportImageViewModel(original, op.GetExportPageSource()!, book);
            model.Draft.Mode = ExportImageMode.View;
            var dialog = new ExportImageDialog(model)
            {
                PreviewAsync = async (p, t) =>
                { using var s = new MemoryStream(); await window.Viewer.ExportViewAsync(op.Frame!, p, s, t); return s.ToArray(); }
            };
            dialog.Show(window); await dialog.PendingPreview; dialog.UpdateLayout();
            Assert.NotNull(dialog.FindControl<Image>("PreviewImage")!.Source); Assert.Equal(originalMode, original.Mode);
            Assert.Equal(800, dialog.Width); Assert.Equal(650, dialog.Height);
            if (Environment.GetEnvironmentVariable("NEEVIEW_ACCEPTANCE_PHASE") == "p5-image-export")
            {
                var path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../acceptance/p5-image-export-" + (book ? "book" : "image") + "-dialog.png"));
                using var bitmap = new RenderTargetBitmap(new PixelSize(800, 650)); bitmap.Render(dialog); bitmap.Save(path, PngBitmapEncoderOptions.Default);
            }
            dialog.Close(); await dialog.PendingPreview;
        }
        finally { await window.PrepareShutdownAsync(); window.Close(); }
    }
    [AvaloniaTheory]
    [InlineData(true, false)] [InlineData(false, false)] [InlineData(false, true)]
    public async Task ViewUsesSplitFramePixelsAndOriginalSizeCancelsRotation(bool originalSize, bool background)
    {
        using var f = new Fixture(); using (var image = new MagickImage(new MagickColor(200, 40, 80, 128), 600, 100)) image.Write(Path.Combine(f.Images, "001.png"));
        var state = new SaveData(f.State); await state.LoadAsync(Token); var op = f.Operation(state); var factory = new BitmapFactory(new MagickImageDecoder()); var window = Window(op, state, factory);
        try
        {
            await window.OpenAsync(Path.Combine(f.Images, "001.png")); await op.ApplySettingAsync(s => { s.PageMode = PageMode.SinglePage; s.IsSupportedDividePage = true; }); await window.Viewer.RefreshAsync();
            await window.Viewer.RotateAsync(1, new ViewRotateCommandParameter { Angle = 90 });
            Config.Current.Background.PageBackgroundColor = ThemeRgba.Parse("Transparent"); Config.Current.Background.BackgroundType = BackgroundType.Black;
            var target = Path.Combine(f.Root, "view.png"); var p = new ExportImageParameter { Mode = ExportImageMode.View, IsOriginalSize = originalSize, HasBackground = background };
            var result = await op.ExportImagesAsync(p, target, Token); using var exported = new MagickImage(target); Assert.Equal(1, result.Count);
            if (originalSize) { Assert.Equal((uint)300, exported.Width); Assert.Equal((uint)100, exported.Height); }
            else Assert.True(exported.Height > exported.Width);
            var pixel = exported.GetPixels().GetPixel((int)exported.Width / 2, (int)exported.Height / 2);
            if (background) Assert.Equal((byte)255, pixel.GetChannel(3)); else Assert.InRange((int)pixel.GetChannel(3), 127, 129);
        }
        finally { await window.PrepareShutdownAsync(); window.Close(); }
        Assert.Equal(0, factory.ByteCount);
    }
    [AvaloniaFact]
    public async Task WholeViewUsesOriginalDoubleFramesRestoresPositionAndWritesJpeg()
    {
        using var f = new Fixture(); var state = new SaveData(f.State); await state.LoadAsync(Token); var op = f.Operation(state); var window = Window(op, state, new(new MagickImageDecoder()));
        try
        {
            await window.OpenAsync(f.Zip); await op.ApplySettingAsync(s => { s.PageMode = PageMode.WidePage; s.IsSupportedSingleFirstPage = false; s.IsSupportedSingleLastPage = false; }); await op.JumpAsync(2); var old = op.Position;
            var p = new ExportBookParameter { Mode = ExportImageMode.View, FileFormat = BitmapImageFormat.Jpeg, IsOriginalSize = true };
            var target = Path.Combine(f.Root, "view.zip"); var result = await op.ExportImagesAsync(p, target, Token);
            Assert.Equal(3, result.Count); Assert.Equal(old, op.Position);
            using var zip = ZipFile.OpenRead(target); Assert.Equal(3, zip.Entries.Count);
            foreach (var entry in zip.Entries) { Assert.EndsWith(".jpg", entry.FullName); using var source = entry.Open(); using var image = new MagickImage(source); Assert.Equal(MagickFormat.Jpeg, image.Format); }
        }
        finally { await window.PrepareShutdownAsync(); window.Close(); }
    }
    [AvaloniaFact]
    public async Task DirectCommandUsesOriginalParameterAndMenuEntries()
    {
        using var f = new Fixture(); var state = new SaveData(f.State); await state.LoadAsync(Token); var op = f.Operation(state); var window = Window(op, state, new(new MagickImageDecoder()));
        try
        {
            await window.OpenAsync(f.Images); state.SetCommandParameter("ExportImage", new ExportImageCommandParameter { ExportFolder = f.Root });
            await window.ExecuteAsync("ExportImage"); Assert.True(File.Exists(Path.Combine(f.Root, "001.png")));
            foreach (var name in new[] { "ExportImage", "ExportImageAs", "ExportBookAs" }) Assert.True(window.IsCommandAvailable(name));
            var model = new ExportImageViewModel(Config.Current.Book.ExportBookParameter, op.GetExportPageSource()!, true); var dialog = new ExportImageDialog(model); dialog.Show(window); dialog.UpdateLayout();
            Assert.Equal(800, dialog.Width); Assert.DoesNotContain(ExportImageOverwriteMode.Confirm, model.OverwriteModes); dialog.Close();
        }
        finally { await window.PrepareShutdownAsync(); window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(PageFrameOrientation.Horizontal, false)]
    [InlineData(PageFrameOrientation.Vertical, false)]
    [InlineData(PageFrameOrientation.Horizontal, true)]
    [InlineData(PageFrameOrientation.Vertical, true)]
    public async Task PanoramaExportsSelectedFramesWithoutReenteringNavigationLock(PageFrameOrientation orientation, bool wholeBook)
    {
        using var f = new Fixture(); var state = new SaveData(f.State); await state.LoadAsync(Token);
        var op = f.Operation(state); var factory = new BitmapFactory(new MagickImageDecoder());
        var window = Window(op, state, factory);
        try
        {
            await window.OpenAsync(f.Zip);
            await op.SetBrowseModeAsync(BrowseLayoutMode.Panorama);
            await op.SetOrientationAsync(orientation);
            await op.ApplySettingAsync(s => { s.PageMode = PageMode.SinglePage; s.IsSupportedDividePage = false; });
            await op.JumpAsync(2); await window.Viewer.RefreshAsync(); var old = op.Position;
            Assert.True(window.Viewer.PanoramaFrames.Count > 1);
            IExportImageParameter options = wholeBook
                ? new ExportBookParameter { Mode = ExportImageMode.View, IsOriginalSize = true }
                : new ExportImageParameter { Mode = ExportImageMode.View, IsOriginalSize = true };
            var target = Path.Combine(f.Root, wholeBook ? "panorama.zip" : "panorama.png");
            // 导出持有原导航锁；全景的尺寸补齐不能再次请求同一锁。
            var result = await op.ExportImagesAsync(options, target, Token).WaitAsync(TimeSpan.FromSeconds(10), Token);
            Assert.Equal(wholeBook ? 5 : 1, result.Count); Assert.Equal(old, op.Position); Assert.False(op.IsExporting);
            if (wholeBook)
            {
                using var zip = ZipFile.OpenRead(target); Assert.Equal(5, zip.Entries.Count);
                foreach (var entry in zip.Entries) { using var stream = entry.Open(); using var image = new MagickImage(stream); Assert.True(image.Width > 0 && image.Height > 0); }
            }
            else { using var image = new MagickImage(target); Assert.Equal((uint)400, image.Width); Assert.Equal((uint)600, image.Height); }
            await op.MoveAsync(1); await window.Viewer.RefreshAsync(); Assert.Equal(3, op.Position.Index);
        }
        finally { await window.PrepareShutdownAsync(); window.Close(); }
        // 已取消的邻帧/面板原生请求仍可晚到，等实际租约归还；超时保持失败，不能只看缓存字典已清空。
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (factory.ByteCount != 0 && DateTime.UtcNow < deadline) await Task.Delay(10, Token);
        Assert.Equal(0, factory.ByteCount);
    }
}
