using System.Text.Json;
using System.Text.Json.Nodes;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using ImageMagick;
using NeeView.Backends;
using NeeView.MacOS.ViewModels;
using NeeView.MacOS.Views;
namespace NeeView.Engine.Tests;

/// <summary>原背景/透明页/nearest的隔离合成像素、生命周期与JSON回归。</summary>
public sealed class BackgroundPresentationTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private sealed class Fixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "NeeView-P5-Background-" + Guid.NewGuid().ToString("N"));
        public string State => Path.Combine(Root, "Profile");
        public string Images => Path.Combine(Root, "Images");
        public string Background => Path.Combine(Root, "background.png");
        public Fixture()
        {
            Directory.CreateDirectory(State); Directory.CreateDirectory(Images);
            using var image = new MagickImage(MagickColors.Transparent, 20, 40); image.Write(Path.Combine(Images, "001.png"));
            using var tile = new MagickImage(MagickColors.Blue, 2, 2);
            using (var pixels = tile.GetPixels()) pixels.SetPixel(0, 0, new byte[] { 255, 0, 0, 255 });
            tile.Write(Background);
        }
        public async Task<SaveData> Load() { var data = new SaveData(State); await data.LoadAsync(Token); return data; }
        public void Dispose() => Directory.Delete(Root, true);
    }
    private sealed class Decoder : IImageDecoder
    {
        private readonly MagickImageDecoder _inner = new();
        public int Decodes;
        public int MainDecodes;
        public bool Hold;
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Released { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<ImageInfo> ProbeAsync(Stream stream, CancellationToken token) => _inner.ProbeAsync(stream, token);
        public async Task<DecodedImageLease> DecodeAsync(Stream stream, DecodeRequest request, CancellationToken token)
        {
            Interlocked.Increment(ref Decodes); Entered.TrySetResult();
            if (!request.IsThumbnail) Interlocked.Increment(ref MainDecodes);
            if (Hold) await Released.Task; // 原生无法即时取消的合成对照。
            return await _inner.DecodeAsync(stream, request, token);
        }
    }
    private sealed class Platform : IPlatformService
    {
        public Task RevealAsync(string path, CancellationToken token = default) => Task.CompletedTask;
        public Task TrashAsync(string path, CancellationToken token = default) => throw new NotSupportedException();
    }
    private static IEnumerable<MenuItem> MenuItems(IEnumerable<object?> items)
    {
        foreach (var item in items.OfType<MenuItem>())
        { yield return item; foreach (var child in MenuItems(item.Items)) yield return child; }
    }
    private static void Pump(Window window) { Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); }
    private static async Task Wait(Func<bool> ready)
    {
        for (int i = 0; i < 500 && !ready(); i++) { await Task.Delay(10, Token); Dispatcher.UIThread.RunJobs(); }
        Assert.True(ready());
    }
    private static Color Pixel(Window window, int x, int y)
    {
        Pump(window); using var bitmap = window.CaptureRenderedFrame()!; using var stream = new MemoryStream();
        bitmap.Save(stream, PngBitmapEncoderOptions.Default); stream.Position = 0; using var image = new MagickImage(stream);
        using var pixels = image.GetPixels(); var color = pixels.GetPixel(x, y).ToColor()!;
        return Color.FromArgb(color.A, color.R, color.G, color.B);
    }
    private sealed class Probe : Control
    {
        public CanvasBackgroundPresenter? Background;
        public Action<DrawingContext>? Draw;
        public override void Render(DrawingContext context)
        { Background?.Render(context, ThemeRgba.Parse("Red")); Draw?.Invoke(context); }
    }
    [Fact]
    public async Task OriginalColorsDefaultsUnknownFieldsDifferenceAndReloadRoundTrip()
    {
        using var f = new Fixture();
        var raw = new JsonObject { ["Version"] = 4630, ["Config"] = new JsonObject { ["Background"] = new JsonObject
            { ["BackgroundType"] = 5, ["CustomBackground"] = new JsonObject { ["Type"] = 1, ["Color"] = "#80203040", ["ImageFileName"] = f.Background, ["Future"] = 17 },
              ["PageBackgroundColor"] = "#7F102030", ["IsPageBackgroundChecker"] = true, ["Future"] = "keep" },
            ["ImageDotKeep"] = new JsonObject { ["IsEnabled"] = true, ["Threshold"] = .8, ["Future"] = 19 } } };
        File.WriteAllText(Path.Combine(f.State, "UserSetting.json"), raw.ToJsonString());
        var state = await f.Load(); await using var operation = new BookOperation(new ArchiveFactory(), new MagickImageDecoder(), state);
        Assert.Equal(BackgroundType.Custom, Config.Current.Background.BackgroundType); Assert.Equal(128, Config.Current.Background.CustomBackground.Color.A);
        await operation.SaveAllAsync(Token); raw = JsonNode.Parse(File.ReadAllText(Path.Combine(f.State, "UserSetting.json")))!.AsObject();
        Assert.Equal("#80203040", raw["Config"]!["Background"]!["CustomBackground"]!["Color"]!.GetValue<string>());
        Assert.Equal("#7F102030", raw["Config"]!["Background"]!["PageBackgroundColor"]!.GetValue<string>());
        Assert.Equal(17, raw["Config"]!["Background"]!["CustomBackground"]!["Future"]!.GetValue<int>());
        Assert.Equal("keep", raw["Config"]!["Background"]!["Future"]!.GetValue<string>());
        Assert.Equal(19, raw["Config"]!["ImageDotKeep"]!["Future"]!.GetValue<int>());
        var branch = Config.Current.Background; branch.BackgroundType = BackgroundType.White; await operation.ReloadSettingAsync(Token);
        Assert.Same(branch, Config.Current.Background); Assert.Equal(BackgroundType.Custom, branch.BackgroundType);
        Assert.Equal(.8, Config.Current.ImageDotKeep.Threshold);
        Assert.Equal(BackgroundType.Black, new BackgroundConfig().BackgroundType); Assert.Equal(0, new BackgroundConfig().PageBackgroundColor.A);
        Assert.Equal(ThemeRgba.Parse("LightGray"), new BrushSource().Color); Assert.False(new ImageDotKeepConfig().IsEnabled);
    }
    [Theory]
    [InlineData("LightGray", "#FFD3D3D3")]
    [InlineData(" Transparent ", "#00FFFFFF")]
    [InlineData("red", "#FFFF0000")]
    [InlineData("#123", "#FF112233")]
    [InlineData("#8123", "#88112233")]
    [InlineData("#102030", "#FF102030")]
    [InlineData("sc#0.5,1,0,0", "#80FF0000")]
    public void OriginalColorStringsRoundTripAlpha(string input, string expected)
    {
        var color = JsonSerializer.Deserialize<ThemeRgba>(JsonSerializer.Serialize(input));
        Assert.Equal(expected, color.ToString()); Assert.Equal(JsonSerializer.Serialize(expected), JsonSerializer.Serialize(color));
    }
    [Theory]
    [InlineData(-1.0)]
    [InlineData(0.0)]
    [InlineData(1e100)]
    [InlineData(-1e100)]
    public void UnrelatedDraftSavePreservesOriginalFiniteThreshold(double threshold)
    {
        var background = new BackgroundConfig { BackgroundType = (BackgroundType)71 };
        background.CustomBackground.Type = (BrushType)72;
        var dot = new ImageDotKeepConfig { Threshold = threshold }; var draft = new BackgroundSettingsViewModel(background, dot);
        draft.Color = "Red"; draft.Apply(background, dot);
        Assert.Equal(threshold, dot.Threshold); Assert.Equal((BackgroundType)71, background.BackgroundType);
        Assert.Equal((BrushType)72, background.CustomBackground.Type);
        draft.Threshold = .8m; draft.Apply(background, dot); Assert.Equal(.8, dot.Threshold);
    }
    [Fact]
    public async Task UnknownNumericModesAndDefaultTrimmingSurviveSaveAndFailedReload()
    {
        using var f = new Fixture(); File.WriteAllText(Path.Combine(f.State, "UserSetting.json"), """
            {"Config":{"Background":{"BackgroundType":71,"CustomBackground":{"Type":72,"Future":17},"Future":19},"ImageDotKeep":{"Future":20}}}
            """);
        var state = await f.Load(); await using var operation = new BookOperation(new ArchiveFactory(), new MagickImageDecoder(), state);
        await operation.SaveAllAsync(Token); var raw = JsonNode.Parse(File.ReadAllText(Path.Combine(f.State, "UserSetting.json")))!;
        Assert.Equal(71, raw["Config"]!["Background"]!["BackgroundType"]!.GetValue<int>());
        Assert.Equal(72, raw["Config"]!["Background"]!["CustomBackground"]!["Type"]!.GetValue<int>());
        Assert.Null(raw["Config"]!["Background"]!["PageBackgroundColor"]);
        Assert.Null(raw["Config"]!["ImageDotKeep"]!["Threshold"]);
        var branch = Config.Current.Background; var saved = File.ReadAllText(Path.Combine(f.State, "UserSetting.json"));
        File.WriteAllText(Path.Combine(f.State, "UserSetting.json"), """{"Config":{"Background":{"PageBackgroundColor":"sc#NaN,0,0"}}}""");
        await Assert.ThrowsAnyAsync<Exception>(() => operation.ReloadSettingAsync(Token));
        Assert.Same(branch, Config.Current.Background); Assert.Equal((BackgroundType)71, branch.BackgroundType);
        await operation.SaveAllAsync(Token);
        Assert.Equal(saved, File.ReadAllText(Path.Combine(f.State, "UserSetting.json")));
    }
    [Theory]
    [InlineData(100, 100, 1, true)]
    [InlineData(99, 99, 1, true)]
    [InlineData(99, 98, 1, false)]
    [InlineData(49, 49, .5, true)]
    [InlineData(200, 98, 1, false)]
    public void OriginalNearestUsesBothAxesAndOnePixelMargin(double width, double height, double threshold, bool expected)
    {
        var dot = new ImageDotKeepConfig { IsEnabled = true, Threshold = threshold };
        Assert.Equal(expected, dot.IsImageDotKeep(new(width, height), new(100, 100)));
        dot.IsEnabled = false; Assert.False(dot.IsImageDotKeep(new(width, height), new(100, 100)));
    }
    [AvaloniaFact]
    public async Task BackgroundCommandsPreserveBookPageFrameAndPixelsAndRollbackFailure()
    {
        using var f = new Fixture(); var state = await f.Load(); var decoder = new Decoder(); var factory = new BitmapFactory(decoder);
        var operation = new BookOperation(new ArchiveFactory(), decoder, state); var model = new ReaderWorkspaceViewModel(operation, new(operation), state);
        var window = new MainWindow(); window.Bind(model, factory, new Platform()); window.Show();
        try
        {
            await window.OpenAsync(f.Images); Pump(window); await window.Viewer.RefreshAsync();
            await Wait(() => factory.GetDiagnostics().PendingRequests == 0); Pump(window); await window.Viewer.RefreshAsync();
            var book = operation.Book!; var frame = operation.Frame; var page = book.CurrentPage; var decodes = decoder.MainDecodes; var bitmapCount = window.Viewer.BitmapCreationCount; int refreshed = 0;
            model.Refreshed += (_, _) => refreshed++;
            foreach (var type in Enum.GetValues<BackgroundType>())
            {
                await window.ExecuteAsync("SetBackground" + type); Pump(window); await window.Viewer.RefreshAppearanceAsync();
                Assert.Equal(type, Config.Current.Background.BackgroundType);
                Assert.True(MenuItems(window.FindControl<Menu>("MenuBar")!.Items).Single(item => item.Tag as string == "SetBackground" + type).IsChecked);
            }
            await window.ExecuteAsync("ToggleBackground"); Assert.Equal(BackgroundType.Black, Config.Current.Background.BackgroundType);
            Assert.Same(book, operation.Book); Assert.Same(page, book.CurrentPage); Assert.Equal(frame!.FrameRange, operation.Frame!.FrameRange);
            // 面板的200ms缩略防抖与背景命令独立；这里只核验正文不会重解码。
            Assert.Equal(decodes, decoder.MainDecodes);
            Assert.Equal(bitmapCount, window.Viewer.BitmapCreationCount);
            Assert.Equal(0, refreshed);
            state.SetCommandParameter("ToggleNearestNeighbor", new ToggleCommandParameter { ToggleMode = ToggleMode.On });
            await window.ExecuteAsync("ToggleNearestNeighbor"); Assert.True(Config.Current.ImageDotKeep.IsEnabled);
            await window.ExecuteAsync("ToggleNearestNeighbor", true); Assert.False(Config.Current.ImageDotKeep.IsEnabled);
            File.Move(Path.Combine(f.State, "UserSetting.json"), Path.Combine(f.State, "settings-backup.json")); Directory.CreateDirectory(Path.Combine(f.State, "UserSetting.json"));
            try { await Assert.ThrowsAnyAsync<Exception>(() => operation.SetBackgroundAsync(BackgroundType.White)); Assert.Equal(BackgroundType.Black, Config.Current.Background.BackgroundType); }
            finally { Directory.Delete(Path.Combine(f.State, "UserSetting.json")); }
        }
        finally { await window.PrepareShutdownAsync(); window.Close(); factory.Dispose(); }
    }
    [AvaloniaTheory]
    [InlineData(BackgroundType.Black, "#FF000000")]
    [InlineData(BackgroundType.White, "#FFFFFFFF")]
    [InlineData(BackgroundType.Auto, "#FFFF0000")]
    [InlineData(BackgroundType.Custom, "#FF00FF00")]
    public async Task CanvasSolidAndAutomaticFirstPixelDrawActualColors(BackgroundType type, string expected)
    {
        using var f = new Fixture(); var state = await f.Load(); await using var operation = new BookOperation(new ArchiveFactory(), new MagickImageDecoder(), state);
        using var factory = new BitmapFactory(new MagickImageDecoder()); var probe = new Probe(); using var background = new CanvasBackgroundPresenter(probe, operation, factory);
        probe.Background = background; var window = new Window { Width = 64, Height = 64, Content = probe }; window.Show();
        try { Config.Current.Background.BackgroundType = type; Config.Current.Background.CustomBackground.Color = ThemeRgba.Parse("Lime"); background.Refresh(); Assert.Equal(Color.Parse(expected), Pixel(window, 24, 24)); }
        finally { window.Close(); }
    }
    [AvaloniaTheory]
    [InlineData(BackgroundType.Check)]
    [InlineData(BackgroundType.CheckDark)]
    public async Task CanvasCheckerKeepsOriginalSixteenPixelPeriod(BackgroundType type)
    {
        using var f = new Fixture(); var state = await f.Load(); await using var operation = new BookOperation(new ArchiveFactory(), new MagickImageDecoder(), state);
        using var factory = new BitmapFactory(new MagickImageDecoder()); var probe = new Probe(); using var background = new CanvasBackgroundPresenter(probe, operation, factory);
        probe.Background = background; var window = new Window { Width = 64, Height = 64, Content = probe }; window.Show();
        try
        {
            Config.Current.Background.BackgroundType = type; background.Refresh();
            var a = Pixel(window, 2, 2); Assert.Equal(a, Pixel(window, 18, 2)); Assert.NotEqual(a, Pixel(window, 10, 2));
            Assert.Equal(8, CanvasBackgroundPresenter.Checker(Colors.White, Colors.Gray, 16 / 2.0).DestinationRect.Rect.Width);
        }
        finally { window.Close(); }
    }
    [AvaloniaTheory]
    [InlineData(BrushType.ImageTile)]
    [InlineData(BrushType.ImageFill)]
    [InlineData(BrushType.ImageUniform)]
    [InlineData(BrushType.ImageUniformToFill)]
    public async Task CustomImageUsesSharedLeasesAndOriginalBrushModes(BrushType type)
    {
        using var f = new Fixture(); var state = await f.Load(); var decoder = new Decoder(); await using var operation = new BookOperation(new ArchiveFactory(), decoder, state);
        using var factory = new BitmapFactory(decoder); var probe = new Probe(); using var background = new CanvasBackgroundPresenter(probe, operation, factory);
        probe.Background = background; var window = new Window { Width = 64, Height = 64, Content = probe }; window.Show();
        try
        {
            Config.Current.Background.BackgroundType = BackgroundType.Custom; Config.Current.Background.CustomBackground.Type = type;
            Config.Current.Background.CustomBackground.ImageFileName = f.Background; background.Refresh(); await background.Pending;
            Assert.Equal(1, decoder.Decodes); Assert.True(factory.GetDiagnostics().DisplayBytes > 0);
            Assert.NotEqual(Colors.LightGray, Pixel(window, 24, 24));
            if (type == BrushType.ImageTile) Assert.Equal(Pixel(window, 4, 4), Pixel(window, 6, 4));
            Config.Current.Background.BackgroundType = BackgroundType.Black; background.Refresh(); Assert.Equal(0, factory.GetDiagnostics().DisplayBytes);
            Config.Current.Background.BackgroundType = BackgroundType.Custom; Config.Current.Background.CustomBackground.ImageFileName = Path.Combine(f.Root, "missing.png"); background.Refresh(); await background.Pending;
            Assert.Equal(Colors.LightGray, Pixel(window, 24, 24));
            Config.Current.Background.CustomBackground.ImageFileName = null; background.Refresh(); Assert.Equal(0, factory.GetDiagnostics().Leases);
        }
        finally { window.Close(); }
    }
    [AvaloniaFact]
    public async Task TransparentPageCheckerAndNearestUseActualScopedDraw()
    {
        using var f = new Fixture(); await f.Load(); var probe = new Probe(); var window = new Window { Width = 64, Height = 64, Content = probe }; window.Show();
        try
        {
            Config.Current.Background.PageBackgroundColor = ThemeRgba.Parse("#FF00FF00");
            probe.Draw = context => { context.FillRectangle(Avalonia.Media.Brushes.Black, new Avalonia.Rect(0, 0, 64, 64)); ReaderImageRenderer.PageBackground(context, new(0, 0, 64, 64)); };
            probe.InvalidateVisual(); Assert.Equal(Colors.Lime, Pixel(window, 20, 20)); Assert.Equal(Colors.Black, Pixel(window, 0, 0));
            Config.Current.Background.IsPageBackgroundChecker = true; probe.InvalidateVisual(); Assert.NotEqual(Pixel(window, 2, 2), Pixel(window, 10, 2));
            Config.Current.Background.PageBackgroundColor = ThemeRgba.Parse("Transparent"); probe.InvalidateVisual(); Assert.Equal(Colors.Black, Pixel(window, 20, 20));
            Config.Current.ImageDotKeep.IsEnabled = true;
            Assert.Equal(BitmapInterpolationMode.None, ReaderImageRenderer.Interpolation(probe, new(0, 0, 10, 10), new(0, 0, 10, 10), Matrix.Identity));
            Assert.Equal(BitmapInterpolationMode.HighQuality, ReaderImageRenderer.Interpolation(probe, new(0, 0, 10, 10), new(0, 0, 4, 4), Matrix.Identity));
            Assert.Equal(BitmapInterpolationMode.None, ReaderImageRenderer.Interpolation(probe, new(0, 0, 10, 10), new(0, 0, 4, 4), Matrix.CreateScale(3, 3)));
        }
        finally { window.Close(); }
    }
    [AvaloniaFact]
    public async Task CancelledNativeBackgroundNeverPublishesOrLeaksDisplay()
    {
        using var f = new Fixture(); var state = await f.Load(); var decoder = new Decoder { Hold = true }; await using var operation = new BookOperation(new ArchiveFactory(), decoder, state);
        using var factory = new BitmapFactory(decoder); var probe = new Probe(); var background = new CanvasBackgroundPresenter(probe, operation, factory);
        Config.Current.Background.BackgroundType = BackgroundType.Custom; Config.Current.Background.CustomBackground.Type = BrushType.ImageFill;
        Config.Current.Background.CustomBackground.ImageFileName = f.Background; background.Refresh();
        await decoder.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5), Token); background.Dispose(); decoder.Released.TrySetResult();
        await background.Pending.WaitAsync(TimeSpan.FromSeconds(5), Token); await Wait(() => factory.GetDiagnostics().DecodeSlotsInUse == 0);
        Assert.Equal(0, factory.GetDiagnostics().DisplayBytes); Assert.Equal(0, factory.GetDiagnostics().Leases);
    }
    [Fact]
    public async Task AutomaticBackgroundSamplesBeforeThumbnailResize()
    {
        using var f = new Fixture(); var decoder = new MagickImageDecoder(); await using var stream = File.OpenRead(f.Background);
        using var image = await decoder.DecodeAsync(stream, new(1, 1), Token);
        Assert.Equal(new NeeView.Size(2, 2), image.SourceSize); Assert.Equal(ThemeRgba.Parse("Red"), image.Color);
    }
    [Fact]
    public async Task AutomaticBackgroundRetainsStraightRgbWhileDisplayPixelsArePremultiplied()
    {
        using var source = new MagickImage(MagickColors.Transparent, 2, 2);
        using (var pixels = source.GetPixels()) pixels.SetPixel(0, 0, new byte[] { 255, 0, 0, 128 });
        using var stream = new MemoryStream(); source.Write(stream, MagickFormat.Png); stream.Position = 0;
        using var image = await new MagickImageDecoder().DecodeAsync(stream, new(2, 2), Token);
        Assert.Equal(ThemeRgba.Parse("Red"), image.Color); Assert.Equal(128, image.Pixels[2]); Assert.Equal(128, image.Pixels[3]);
    }
    [AvaloniaFact]
    public async Task SettingsDraftCancelFailureRetryAndSearchKeepOriginalConfig()
    {
        using var f = new Fixture(); var state = await f.Load(); var operation = new BookOperation(new ArchiveFactory(), new MagickImageDecoder(), state);
        var model = new ReaderWorkspaceViewModel(operation, new(operation), state); var window = new MainWindow(); window.Bind(model, new BitmapFactory(new MagickImageDecoder()), new Platform()); window.Show();
        try
        {
            var settings = new SettingsWindow(model); settings.Show(window); settings.FindControl<ListBox>("SettingsNavigation")!.SelectedIndex = 11;
            var draft = (BackgroundSettingsViewModel)settings.FindControl<ScrollViewer>("BackgroundSettings")!.DataContext!;
            Config.Current.System.IsIncrementalSearchEnabled = false;
            var field = settings.FindControl<TextBox>("CustomBackgroundColor")!; var parent = field.Parent;
            var search = (NavigationSearchViewModel)typeof(SettingsWindow).GetField("_settingsSearch", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(settings)!;
            search.Keyword = "背景"; Assert.True(await search.SearchAsync()); Pump(settings);
            Assert.NotSame(parent, field.Parent); field.Text = "#80112233"; Pump(settings); Assert.Equal("#80112233", draft.Color);
            search.Keyword = ""; Assert.True(await search.SearchAsync()); Pump(settings); Assert.Same(parent, field.Parent);
            draft.Background = BackgroundType.Custom; draft.Color = "#80203040"; draft.PageColor = "#7F708090"; draft.Nearest = true; draft.Threshold = .8m;
            Assert.Equal(BackgroundType.Black, Config.Current.Background.BackgroundType); settings.Close(); Assert.False(settings.WasSaved);
            settings = new SettingsWindow(model); settings.Show(window); settings.FindControl<ListBox>("SettingsNavigation")!.SelectedIndex = 11;
            draft = (BackgroundSettingsViewModel)settings.FindControl<ScrollViewer>("BackgroundSettings")!.DataContext!;
            draft.Color = "invalid-color"; settings.FindControl<Button>("SaveSettings")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Wait(() => settings.FindControl<TextBlock>("Message")!.Text?.StartsWith("保存失败：") == true);
            Assert.False(settings.WasSaved); Assert.Equal(ThemeRgba.Parse("LightGray"), Config.Current.Background.CustomBackground.Color);
            draft.Color = "#80203040"; draft.Background = BackgroundType.Custom; draft.Nearest = true; draft.Threshold = .8m;
            settings.FindControl<Button>("SaveSettings")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Wait(() => settings.WasSaved);
            Assert.Equal(128, Config.Current.Background.CustomBackground.Color.A); Assert.Equal(.8, Config.Current.ImageDotKeep.Threshold);
            if (Environment.GetEnvironmentVariable("NEEVIEW_P5_BACKGROUND_SCREENSHOT") is { } path)
            {
                settings = new SettingsWindow(model); settings.Show(window); settings.FindControl<ListBox>("SettingsNavigation")!.SelectedIndex = 11; Pump(settings);
                using var capture = settings.CaptureRenderedFrame(); capture!.Save(path, PngBitmapEncoderOptions.Default); settings.Close();
            }
        }
        finally { foreach (var owned in window.OwnedWindows.ToArray()) owned.Close(); await window.PrepareShutdownAsync(); window.Close(); }
    }
}
