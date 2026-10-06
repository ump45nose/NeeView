using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using NeeView.Backends;
using NeeView.MacOS.ViewModels;
using NeeView.MacOS.Views;
namespace NeeView.Engine.Tests;

/// <summary>原PDF配置/三种尺寸/来源/JSON/目录关系，结合真实窗口与唯一图像工厂；系统像素由native专项另验。</summary>
public sealed class PdfMigrationTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private class Renderer : IPdfRenderer
    {
        public List<Size> Requests { get; } = [];
        public PdfDocumentInfo Inspect(string path, CancellationToken token)
        { token.ThrowIfCancellationRequested(); return new([new(200, 300), new(300, 200)], [new("Chapter", 0, [new("Child", 1, [])])], default); }
        public virtual DecodedImageLease Render(string path, int page, Size target, CancellationToken token)
        {
            token.ThrowIfCancellationRequested(); Requests.Add(target); var size = new Size((int)target.Width, (int)target.Height);
            var pixels = new byte[checked((int)size.Width * (int)size.Height * 4)];
            for (var i = 0; i < pixels.Length; i += 4) { pixels[i] = 40; pixels[i + 1] = 110; pixels[i + 2] = 200; pixels[i + 3] = 255; }
            return new(size, pixels);
        }
    }
    private static string Pdf(Fixture f) { var path = Path.Combine(f.Root, "原文.pdf"); File.WriteAllText(path, "test provider"); return path; }
    private sealed class BlockingRenderer : Renderer
    {
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal ManualResetEventSlim Continue { get; } = new();
        internal DecodedImageLease? Result { get; private set; }
        public override DecodedImageLease Render(string path, int page, Size target, CancellationToken token)
        { Entered.TrySetResult(); Continue.Wait(); return Result = base.Render(path, page, target, CancellationToken.None); }
    }
    [Fact]
    public async Task ConcurrentExportLengthGeneratesOnlyOnePng()
    {
        using var f = new Fixture(); Config.SetCurrent(new()); var renderer = new BlockingRenderer();
        await using var source = await new ArchiveFactory(pdfRenderer: renderer).OpenAsync(Pdf(f), Token);
        await using var stream = await source.OpenEntryAsync((await source.GetEntriesAsync(Token))[0], Token);
        var first = Task.Run(() => stream.Length, Token);
        try
        {
            await renderer.Entered.Task.WaitAsync(Token);
            var repeated = Enumerable.Range(0, 8).Select(_ => Task.Run(() => stream.Length, Token)).ToArray();
            renderer.Continue.Set(); var length = await first.WaitAsync(Token);
            Assert.All(await Task.WhenAll(repeated).WaitAsync(Token), value => Assert.Equal(length, value));
            Assert.Single(renderer.Requests); Assert.NotNull(renderer.Result); Assert.Empty(renderer.Result.Pixels);
        }
        finally { renderer.Continue.Set(); await first; renderer.Continue.Dispose(); }
    }
    [Fact]
    public async Task DisposeWaitsForActiveExportAndRejectsLaterReads()
    {
        using var f = new Fixture(); Config.SetCurrent(new()); var renderer = new BlockingRenderer();
        await using var source = await new ArchiveFactory(pdfRenderer: renderer).OpenAsync(Pdf(f), Token);
        await using var stream = await source.OpenEntryAsync((await source.GetEntriesAsync(Token))[0], Token);
        var read = Task.Run(() => stream.Read(new byte[8], 0, 8), Token);
        try
        {
            await renderer.Entered.Task.WaitAsync(Token);
            var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var close = Task.Run(() => { started.SetResult(); stream.Dispose(); }, Token);
            await started.Task.WaitAsync(Token); Assert.False(close.IsCompleted);
            renderer.Continue.Set(); Assert.Equal(8, await read.WaitAsync(Token)); await close.WaitAsync(Token);
            Assert.Single(renderer.Requests); Assert.NotNull(renderer.Result); Assert.Empty(renderer.Result.Pixels);
            Assert.False(stream.CanRead); Assert.Throws<ObjectDisposedException>(() => stream.ReadByte());
        }
        finally { renderer.Continue.Set(); await read; renderer.Continue.Dispose(); }
    }
    [Fact]
    public async Task CancelledRenderKeepsSourceUntilActualCompletionAndReleasesLatePixels()
    {
        using var f = new Fixture(); Config.SetCurrent(new()); var renderer = new BlockingRenderer();
        var source = await new ArchiveFactory(pdfRenderer: renderer).OpenAsync(Pdf(f), Token); var entry = (await source.GetEntriesAsync(Token))[0];
        await using var stream = await source.OpenEntryAsync(entry, Token); using var cancel = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var render = new MagickImageDecoder().DecodeAsync(stream, new(100, 150, true), cancel.Token);
        try
        {
            await renderer.Entered.Task.WaitAsync(Token); cancel.Cancel(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => render);
            var close = source.DisposeAsync().AsTask(); Assert.False(close.IsCompleted); renderer.Continue.Set(); await close.WaitAsync(Token);
            Assert.NotNull(renderer.Result); Assert.Empty(renderer.Result.Pixels);
        }
        finally { renderer.Continue.Set(); await source.DisposeAsync(); renderer.Continue.Dispose(); }
    }
    [Fact]
    public void OriginalDefaultsLimitsAndThreeSizingRulesRemainSeparate()
    {
        Config.SetCurrent(new()); var source = new Size(200, 300);
        Assert.Equal(new Size(1920, 1080), Config.Current.Archive.Pdf.RenderSize); Assert.True(Config.Current.Archive.Pdf.IsEnabled);
        Assert.Equal(new Size(4096, 4096), Config.Current.Performance.MaximumSize); Assert.False(Config.Current.Performance.IsLimitSourceSize);
        Assert.Equal(source, PdfArchiveProfile.GetDisplaySize(source)); Assert.Equal(new Size(720, 1080), PdfArchiveProfile.GetExportSize(source));
        Assert.Equal(new Size(720, 1080), PdfArchiveProfile.GetRenderSize(source, new(100, 150), false));
        Assert.Equal(new Size(100, 150), PdfArchiveProfile.GetRenderSize(source, new(100, 150), true));
        Assert.Equal(new Size(5000, 8000), PdfArchiveProfile.GetExportSize(new(5000, 8000)));
        Assert.Equal(new Size(4096, 2048), PdfArchiveProfile.GetRenderSize(new(300, 150), new(12000, 6000), false));
        Config.Current.Performance.IsLimitSourceSize = true; Assert.Equal(new Size(4096, 2048), PdfArchiveProfile.GetDisplaySize(new(8000, 4000)));
        Config.Current.Archive.Pdf.RenderSize = new(1, 2); Assert.Equal(new Size(256, 256), Config.Current.Archive.Pdf.RenderSize);
    }
    [Theory]
    [InlineData("\"2400,1600\"")]
    [InlineData("{\"Width\":2400,\"Height\":1600}")]
    public async Task OriginalSizeJsonAndUnknownPdfPerformanceFieldsSurviveDifferenceSave(string size)
    {
        using var f = new Fixture(); Directory.CreateDirectory(f.State);
        var path = Path.Combine(f.State, "UserSetting.json");
        await File.WriteAllTextAsync(path, "{\"Config\":{\"Archive\":{\"Pdf\":{\"RenderSize\":" + size + ",\"CustomField\":42}},\"Performance\":{\"MaximumSize\":\"8192,4096\",\"NativeCache\":77}}}", Token);
        var state = new SaveData(f.State); await state.LoadAsync(Token); Assert.Equal(new Size(2400, 1600), Config.Current.Archive.Pdf.RenderSize);
        Assert.Equal(new Size(8192, 4096), Config.Current.Performance.MaximumSize); await state.SaveAsync(null, Token);
        var json = JsonNode.Parse(await File.ReadAllTextAsync(path, Token))!;
        Assert.Equal("2400,1600", json["Config"]!["Archive"]!["Pdf"]!["RenderSize"]!.GetValue<string>());
        Assert.Equal(42, json["Config"]!["Archive"]!["Pdf"]!["CustomField"]!.GetValue<int>()); Assert.Equal(77, json["Config"]!["Performance"]!["NativeCache"]!.GetValue<int>());
        await state.LoadAsync(Token); Assert.Equal(new Size(2400, 1600), Config.Current.Archive.Pdf.RenderSize);
    }
    [Theory]
    [InlineData("\"bad\"")]
    [InlineData("\"NaN,1080\"")]
    [InlineData("{\"Width\":100}")]
    public void InvalidKnownSizeCannotSilentlyReplaceOldProfile(string raw)
    { Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<PdfArchiveConfig>("{\"RenderSize\":" + raw + "}")); }
    [Fact]
    public async Task ProbeHasNoRasterWorkDisplayAndExportUseOneDecoderAndCache()
    {
        using var f = new Fixture(); Config.SetCurrent(new()); var renderer = new Renderer(); var factory = new ArchiveFactory(pdfRenderer: renderer);
        await using var archive = await factory.OpenAsync(Pdf(f), Token); var entry = (await archive.GetEntriesAsync(Token))[0];
        Assert.Equal("001.png", entry.EntryName); Assert.Equal(0, entry.Length); await using var stream = await archive.OpenEntryAsync(entry, Token);
        var decoder = new MagickImageDecoder(); Assert.Equal(new Size(200, 300), (await decoder.ProbeAsync(stream, Token)).Size); Assert.Empty(renderer.Requests);
        var page = new Page(entry); using var cache = new BitmapFactory(decoder);
        using (var first = await cache.GetAsync(page, new(100, 150, true), Token)) Assert.Equal(100 * 150 * 4, first.Image.ByteCount);
        using (var repeated = await cache.GetAsync(page, new(100, 150, true), Token)) Assert.Equal(new Size(100, 150), repeated.Image.Size);
        Assert.Single(renderer.Requests);
        await using var realizer = new ArchiveEntryRealizer(); await using var file = await realizer.ExtractAsync(entry, 1024 * 1024, Token);
        Assert.True(File.Exists(file.Path)); await using var png = File.OpenRead(file.Path);
        Assert.Equal(new Size(720, 1080), (await decoder.ProbeAsync(png, Token)).Size); Assert.Equal(2, renderer.Requests.Count);
    }
    [Fact]
    public async Task DisabledOrMissingBackendGivesClearFailureAndSourceEntryCannotBeDeleted()
    {
        using var f = new Fixture(); Config.SetCurrent(new()); var path = Pdf(f);
        await Assert.ThrowsAsync<NotSupportedException>(() => new ArchiveFactory().OpenAsync(path, Token));
        var factory = new ArchiveFactory(pdfRenderer: new Renderer()); Config.Current.Archive.Pdf.IsEnabled = false;
        await Assert.ThrowsAsync<NotSupportedException>(() => factory.OpenAsync(path, Token)); Config.Current.Archive.Pdf.IsEnabled = true;
        var source = await factory.OpenAsync(path, Token); var entries = await source.GetEntriesAsync(Token);
        Assert.False(source.CanDelete(entries)); Assert.All(entries, e => Assert.Null(e.FilePath));
        await source.DisposeAsync(); await Assert.ThrowsAsync<ObjectDisposedException>(() => source.OpenEntryAsync(entries[0], Token));
    }
    [Theory]
    [InlineData(ArchiveEntryCollectionMode.CurrentDirectory, 1)]
    [InlineData(ArchiveEntryCollectionMode.IncludeSubDirectories, 1)]
    [InlineData(ArchiveEntryCollectionMode.IncludeSubArchives, 2)]
    public async Task PdfInsideZipUsesOriginalCollectionModesAndExplicitPageLocation(ArchiveEntryCollectionMode mode, int pages)
    {
        using var f = new Fixture(); var outer = Path.Combine(f.Root, "pdf.cbz"); using (var zip = ZipFile.Open(outer, ZipArchiveMode.Create)) zip.CreateEntryFromFile(Pdf(f), "inside.pdf");
        var state = new SaveData(f.State); await state.LoadAsync(Token); Config.Current.System.ArchiveRecursiveMode = mode;
        var factory = new ArchiveFactory(pdfRenderer: new Renderer()); await using var operation = new BookOperation(factory, new MagickImageDecoder(), state);
        await operation.OpenAsync(outer, Token); Assert.Null(operation.Error); Assert.Equal(pages, operation.Book!.Pages.Count);
        var nested = Path.Combine(outer, "inside.pdf", "002.png"); await operation.OpenAsync(nested, Token); Assert.Null(operation.Error);
        Assert.Equal("002.png", operation.Book!.CurrentPage!.EntryName); Assert.Equal(outer, operation.Book.Source.RootArchivePath);
        Assert.True(await factory.ExistsAsync(nested, Token)); Assert.False(await factory.ExistsAsync(nested.Replace("002.png", "003.png"), Token));
    }
    [Fact]
    public async Task DirectoryAndPdfReadRestoreKeepOriginalMementoAndOutlineTargetsAcrossSort()
    {
        using var f = new Fixture(); var path = Pdf(f); var state = new SaveData(f.State); await state.LoadAsync(Token);
        var factory = new ArchiveFactory(pdfRenderer: new Renderer());
        await using (var operation = new BookOperation(factory, new MagickImageDecoder(), state))
        {
            Assert.Contains(await factory.ListBooksAsync(f.Root, Token), i => i.Path == path);
            await operation.OpenAsync(path, Token); Assert.Null(operation.Error); await operation.JumpAsync(1); await operation.SaveAsync();
            var source = operation.Book!.Pages.SourcePages; var tree = BookTableOfContents.Create(source, Token, operation.Book.Source);
            Assert.Same(source[0], Assert.Single(tree.Children).Page); Assert.Same(source[1], Assert.Single(tree.Children[0].Children).Page);
        }
        await state.LoadAsync(Token); await using var restored = new BookOperation(factory, new MagickImageDecoder(), state); await restored.RestoreLastAsync(Token);
        Assert.Null(restored.Error); Assert.Equal(path, restored.Book!.Path); Assert.Equal("002.png", restored.Book.CurrentPage!.EntryName);
    }
    [AvaloniaFact]
    public async Task FormalPdfSettingsRemainSearchableCancelRollbackAndRetryUseOriginalJson()
    {
        using var f = new Fixture(); var state = new SaveData(f.State); await state.LoadAsync(Token); await state.SaveAsync(null, Token);
        await using var operation = f.Operation(state); var model = new ReaderWorkspaceViewModel(operation, new(operation), state);
        var settings = new SettingsWindow(model); settings.Show();
        try
        {
            settings.FindControl<ListBox>("SettingsNavigation")!.SelectedIndex = 9; Pump(settings); Assert.True(settings.FindControl<ScrollViewer>("ArchiveSettings")!.IsVisible);
            settings.FindControl<NumericUpDown>("PdfWidth")!.Value = 2400; settings.FindControl<CheckBox>("PdfLimitSource")!.IsChecked = true;
            Assert.Equal(new Size(1920, 1080), Config.Current.Archive.Pdf.RenderSize);
            if (Environment.GetEnvironmentVariable("NEEVIEW_P5_PDF_SETTINGS_SCREENSHOT") is { } path)
            { using var frame = settings.CaptureRenderedFrame(); Assert.NotNull(frame); frame!.Save(path, Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default); }
            var blocked = Path.Combine(f.State, "UserSetting.json.tmp"); Directory.CreateDirectory(blocked);
            try { Click(settings, "SaveSettings"); await Wait(() => settings.FindControl<TextBlock>("Message")!.Text?.StartsWith("保存失败：") == true); Assert.Equal(new Size(1920, 1080), Config.Current.Archive.Pdf.RenderSize); Assert.Equal(2400m, settings.FindControl<NumericUpDown>("PdfWidth")!.Value); }
            finally { Directory.Delete(blocked); }
            Click(settings, "SaveSettings"); await Wait(() => settings.WasSaved); await state.LoadAsync(Token);
            Assert.Equal(new Size(2400, 1080), Config.Current.Archive.Pdf.RenderSize); Assert.True(Config.Current.Performance.IsLimitSourceSize);
        }
        finally { settings.Close(); model.Detach(); }
    }
    [AvaloniaFact]
    public async Task PdfSearchReusesOriginalGridDraftAndCancelKeepsSettingsBytes()
    {
        using var f = new Fixture(); var state = new SaveData(f.State); await state.LoadAsync(Token); await state.SaveAsync(null, Token);
        var before = await File.ReadAllBytesAsync(Path.Combine(f.State, "UserSetting.json"), Token);
        await using var operation = f.Operation(state); var model = new ReaderWorkspaceViewModel(operation, new(operation), state);
        var settings = new SettingsWindow(model); settings.Show();
        try
        {
            var width = settings.FindControl<NumericUpDown>("PdfWidth")!; var parent = width.Parent; var context = width.DataContext; width.Value = 2800;
            var search = Assert.IsType<NavigationSearchViewModel>(settings.FindControl<StackPanel>("SettingsSearchInput")!.DataContext);
            search.Keyword = "PDF"; await search.SearchAsync(); Pump(settings);
            Assert.NotSame(parent, width.Parent); Assert.Same(context, width.DataContext); Assert.Equal(2800m, width.Value);
            search.Keyword = ""; await search.SearchAsync(); Pump(settings); Assert.Same(parent, width.Parent); Assert.Equal(2800m, width.Value);
            Click(settings, "CancelSettings"); Assert.False(settings.WasSaved); Assert.Equal(new Size(1920, 1080), Config.Current.Archive.Pdf.RenderSize);
            Assert.Equal(before, await File.ReadAllBytesAsync(Path.Combine(f.State, "UserSetting.json"), Token));
        }
        finally { settings.Close(); model.Detach(); }
    }
    [AvaloniaFact]
    public async Task FormalPdfViewerUsesExistingPageFramesAndShutdownReleasesCache()
    {
        using var f = new Fixture(); var state = new SaveData(f.State); await state.LoadAsync(Token);
        var operation = new BookOperation(new ArchiveFactory(pdfRenderer: new Renderer()), new MagickImageDecoder(), state);
        var images = new BitmapFactory(new MagickImageDecoder()); var model = new ReaderWorkspaceViewModel(operation, new(operation), state);
        var window = new MainWindow(); window.Bind(model, images, new NoPlatform()); window.Show();
        try
        {
            await window.OpenAsync(Pdf(f)); await window.Viewer.RefreshAsync(); Pump(window); Assert.Null(operation.Error); Assert.Equal(2, operation.Book!.Pages.Count);
            if (Environment.GetEnvironmentVariable("NEEVIEW_P5_PDF_VIEWER_SCREENSHOT") is { } path)
            { using var frame = window.CaptureRenderedFrame(); Assert.NotNull(frame); frame!.Save(path, Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default); }
        }
        finally { await window.PrepareShutdownAsync(); window.Close(); }
    }
    private sealed class NoPlatform : IPlatformService
    {
        public Task RevealAsync(string path, CancellationToken token = default) => throw new NotSupportedException();
        public Task TrashAsync(string path, CancellationToken token = default) => throw new NotSupportedException();
    }
    private static void Pump(Window window) { Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); }
    private static void Click(SettingsWindow window, string name) => window.FindControl<Button>(name)!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static async Task Wait(Func<bool> done)
    { for (var i = 0; i < 300 && !done(); i++) { Dispatcher.UIThread.RunJobs(); await Task.Delay(10, Token); } Assert.True(done()); }
}
