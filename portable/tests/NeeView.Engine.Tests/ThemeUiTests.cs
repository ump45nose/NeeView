using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using NeeView.MacOS.ViewModels;
using NeeView.MacOS.Views;
namespace NeeView.Engine.Tests;

/// <summary>正式窗口、配置事务与原主题资源；后台Headless不证明真实系统外观事件。</summary>
public sealed class ThemeUiTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private sealed class NoPlatform : IPlatformService
    {
        public Task RevealAsync(string path, CancellationToken token = default) => throw new NotSupportedException();
        public Task TrashAsync(string path, CancellationToken token = default) => throw new NotSupportedException();
    }
    private sealed class CountingDecoder : IImageDecoder
    {
        private readonly NeeView.Backends.MagickImageDecoder _inner = new();
        public int Decodes;
        public Task<ImageInfo> ProbeAsync(Stream stream, CancellationToken token) => _inner.ProbeAsync(stream, token);
        public Task<DecodedImageLease> DecodeAsync(Stream stream, DecodeRequest request, CancellationToken token)
        { Interlocked.Increment(ref Decodes); return _inner.DecodeAsync(stream, request, token); }
    }
    /// <summary>原主题实际更新已打开的主/浮/设置/菜单及下拉，保留书籍身份和解码数量。</summary>
    [AvaloniaFact]
    public async Task RuntimePaletteFlowsToExistingWindowsWithoutReloadingBook()
    {
        using var f = new Fixture(); var state = new SaveData(f.State); await state.LoadAsync(Token);
        var decoder = new CountingDecoder(); var operation = new BookOperation(new NeeView.Backends.ArchiveFactory(), decoder, state);
        var images = new BitmapFactory(decoder); var model = new ReaderWorkspaceViewModel(operation, new CommandTable(operation), state);
        var window = new MainWindow(); window.Bind(model, images, new NoPlatform()); window.Show();
        var presenter = new ThemePresenter(Application.Current!, Config.Current.Theme); window.AttachTheme(presenter);
        var floating = new FloatingPanelWindow("InformationPanel", "信息"); floating.ContentHost.Child = new TextBox { Text = "浮动面板" }; floating.Show(window);
        var settings = new SettingsWindow(model); settings.Show(window); settings.FindControl<ListBox>("SettingsNavigation")!.SelectedIndex = 7;
        var combo = settings.FindControl<ComboBox>("ThemeChoice")!;
        var menu = new ContextMenu { Items = { new MenuItem { Header = "主题验证", Items = { new MenuItem { Header = "子菜单" } } } } };
        menu.Open(window);
        try
        {
            await window.OpenAsync(f.Images); await window.Viewer.RefreshAsync(); await Wait(() => images.GetDiagnostics().PendingRequests == 0); Pump(window);
            // 页列表/导航器有200ms需求防抖，计数基线必须等真实后台需求稳定后再采集。
            await WaitForImagesIdle(images, decoder);
            var book = operation.Book; var page = book!.CurrentPage; var position = operation.Position; var decoded = decoder.Decodes; var refreshes = 0;
            model.Refreshed += (_, _) => refreshes++;
            Config.Current.Theme.ThemeType = new(ThemeType.Light); await presenter.RefreshAsync(); Pump(window); Pump(settings); Pump(floating); menu.UpdateLayout();
            Assert.Equal(ThemeVariant.Light, Application.Current!.RequestedThemeVariant);
            Assert.Equal("#FFFFFFFF", BrushColor(window.Background)); Assert.Equal("#FF202020", ThemeProfileTools.LoadFromContent("DarkTheme.json").GetColor("Panel.Background", 1).ToString());
            Assert.Equal("#FFFFFFFF", BrushColor(floating.Background)); Assert.Equal("#FFFFFFFF", BrushColor(settings.Background));
            Assert.Equal(presenter.Current!.Profile.GetColor("Menu.Background", 1).ToString(), BrushColor(menu.Background));
            Assert.Equal(presenter.Current.Profile.GetColor("Button.Foreground", 1).ToString(), BrushColor(combo.Foreground));
            combo.IsDropDownOpen = true; Pump(settings); Assert.True(combo.IsDropDownOpen); Save(window, "light-main"); Save(settings, "light-settings"); Save(floating, "light-floating");
            combo.IsDropDownOpen = false; menu.Close();
            Directory.CreateDirectory(Config.Current.Theme.CustomThemeFolder);
            await File.WriteAllTextAsync(Path.Combine(Config.Current.Theme.CustomThemeFolder, "custom.json"), "{\"BasedOn\":\"themes://LightTheme.json\",\"Colors\":{\"Control.Accent\":\"Orange\",\"Panel.Background\":\"#FFEEDDCC\",\"SideBar.Background\":\"#FFC8CACB\"}}", Token);
            Config.Current.Theme.ThemeType = new(ThemeType.Custom, "custom.json"); await presenter.RefreshAsync(); Pump(window);
            Assert.Null(presenter.Current!.Error); Assert.Equal("#FFFFA500", BrushColor((IBrush)Application.Current.Resources["Control.Accent"]!));
            Assert.Same(book, operation.Book); Assert.Same(page, operation.Book!.CurrentPage); Assert.Equal(position, operation.Position); Assert.Equal(0, refreshes); Assert.Equal(decoded, decoder.Decodes);
            Save(window, "custom-main");
        }
        finally { menu.Close(); settings.Close(); floating.Close(); await window.PrepareShutdownAsync(); window.Close(); await ResetPalette(); }
    }
    /// <summary>原设置入口取消、失败及重试；只有实际提交成功后宿主应用主题。</summary>
    [AvaloniaFact]
    public async Task SettingsThemeCancelFailureRetryUsesOnlyExistingTransaction()
    {
        using var f = new Fixture(); var state = new SaveData(f.State); await state.LoadAsync(Token);
        var operation = f.Operation(state); var model = new ReaderWorkspaceViewModel(operation, new CommandTable(operation), state);
        var window = new MainWindow(); window.Bind(model, new BitmapFactory(new NeeView.Backends.MagickImageDecoder()), new NoPlatform()); window.Show();
        var presenter = new ThemePresenter(Application.Current!, Config.Current.Theme); window.AttachTheme(presenter); await presenter.RefreshAsync();
        try
        {
            await window.OpenAsync(f.Images); Pump(window); await state.SaveAsync(null, Token); var branch = Config.Current.Theme;
            var open = window.ExecuteAsync("OpenOptionsWindow"); await Wait(() => window.OwnedWindows.OfType<SettingsWindow>().Any());
            var cancel = window.OwnedWindows.OfType<SettingsWindow>().Single(); var canceledDraft = Draft(cancel); Select(canceledDraft, ThemeType.Light);
            cancel.FindControl<Button>("CancelSettings")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await open;
            Assert.Equal(ThemeType.Dark, branch.ThemeType.Type); Assert.Equal(ThemeType.Dark, presenter.Current!.EffectiveType);
            var editing = window.ExecuteAsync("OpenOptionsWindow"); await Wait(() => window.OwnedWindows.OfType<SettingsWindow>().Any());
            var settings = window.OwnedWindows.OfType<SettingsWindow>().Single(); var draft = Draft(settings); Select(draft, ThemeType.Light);
            settings.FindControl<ListBox>("SettingsNavigation")!.SelectedIndex = 7; Pump(settings);
            await state.SynchronizeWritesAsync(); var blocked = Path.Combine(f.State, "UserSetting.json.tmp"); Directory.CreateDirectory(blocked);
            try
            {
                settings.FindControl<Button>("SaveSettings")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await Wait(() => settings.FindControl<TextBlock>("Message")!.Text?.StartsWith("保存失败：") == true);
                Assert.False(settings.WasSaved); Assert.Same(branch, Config.Current.Theme); Assert.Equal(ThemeType.Dark, branch.ThemeType.Type); Assert.Equal(ThemeType.Dark, presenter.Current!.EffectiveType); Assert.Equal(ThemeType.Light, draft.Selected.Source.Type);
            }
            finally { Directory.Delete(blocked); }
            settings.FindControl<Button>("SaveSettings")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await editing;
            Assert.True(settings.WasSaved); Assert.Equal(ThemeType.Light, Config.Current.Theme.ThemeType.Type); Assert.Equal(ThemeType.Light, presenter.Current!.EffectiveType);
            var book = operation.Book; await state.LoadAsync(Token); Assert.Equal(ThemeType.Light, Config.Current.Theme.ThemeType.Type); Assert.Same(book, operation.Book);
        }
        finally { foreach (var owned in window.OwnedWindows.ToArray()) owned.Close(); await window.PrepareShutdownAsync(); window.Close(); await ResetPalette(); }
    }
    /// <summary>扫描最新目录/缺失项/关闭均只改变草稿，来源配置保持不变。</summary>
    [AvaloniaFact]
    public async Task DraftRejectsOldScansAndKeepsMissingCustomSelection()
    {
        using var f = new Fixture(); var first = Path.Combine(f.Root, "first"); var second = Path.Combine(f.Root, "second"); Directory.CreateDirectory(first); Directory.CreateDirectory(second);
        File.WriteAllText(Path.Combine(first, "old.json"), "{}"); File.WriteAllText(Path.Combine(second, "new.JSON"), "{}");
        var config = new ThemeConfig { ThemeType = new(ThemeType.Custom, "missing.json"), CustomThemeFolder = first };
        using var draft = new ThemeSettingsViewModel(config); var old = draft.RefreshAsync(); draft.Folder = second; var latest = draft.RefreshAsync(); await Task.WhenAll(old, latest);
        Assert.Equal("missing.json", draft.Selected.Source.FileName); Assert.Contains(draft.Items, i => i.Source.FileName == "new.JSON"); Assert.DoesNotContain(draft.Items, i => i.Source.FileName == "old.json");
        Assert.Equal(first, config.CustomThemeFolder); Assert.Equal("missing.json", config.ThemeType.FileName);
        var items = draft.Items; draft.Dispose(); draft.Folder = first; await draft.RefreshAsync(); Assert.Same(items, draft.Items);
    }
    /// <summary>快速切换、缺失主题回退及关闭后的晚到结果不能替换新资源。</summary>
    [AvaloniaFact]
    public async Task PresenterRejectsOldLoadsAndDisposedResults()
    {
        var config = new ThemeConfig(); using var presenter = new ThemePresenter(Application.Current!, config); var applied = 0; presenter.Applied += _ => applied++;
        try
        {
            var dark = presenter.RefreshAsync(); config.ThemeType = new(ThemeType.Light); var light = presenter.RefreshAsync(); await Task.WhenAll(dark, light);
            Assert.Equal(ThemeType.Light, presenter.Current!.EffectiveType); Assert.Equal(1, applied);
            config.ThemeType = new(ThemeType.Custom, "missing.json"); await presenter.RefreshAsync(); Assert.NotNull(presenter.Current!.Error); Assert.Equal(ThemeType.Custom, config.ThemeType.Type);
            var before = presenter.Current; var late = presenter.RefreshAsync(); presenter.Dispose(); await late; await presenter.RefreshAsync(); Assert.Same(before, presenter.Current);
        }
        finally { await ResetPalette(); }
    }
    /// <summary>原透明按钮主题使用透明背景和原前景，只以accent/danger作边框。</summary>
    [AvaloniaFact]
    public async Task TransparentButtonSpecialResourcesKeepOriginalRule()
    {
        using var presenter = new ThemePresenter(Application.Current!, new ThemeConfig());
        try
        {
            var profile = ThemeProfileTools.LoadFromContent("LightTheme.json"); profile.Colors["Button.Background"] = ThemeColor.Parse("Transparent"); profile.Colors["Button.Foreground"] = ThemeColor.Parse("Red");
            presenter.Apply(new(profile.Validate(), ThemeType.Custom, null));
            Assert.Equal("#00FFFFFF", BrushColor((IBrush)Application.Current!.Resources["Button.Accent.Background"]!));
            Assert.Equal("#FFFF0000", BrushColor((IBrush)Application.Current.Resources["Button.Danger.Foreground"]!));
            Assert.Equal("#FFE81123", BrushColor((IBrush)Application.Current.Resources["Button.Danger.Border"]!));
        }
        finally { await ResetPalette(); }
    }
    private static ThemeSettingsViewModel Draft(SettingsWindow settings) => (ThemeSettingsViewModel)settings.FindControl<ScrollViewer>("ThemeSettings")!.DataContext!;
    private static void Select(ThemeSettingsViewModel draft, ThemeType type) => draft.Selected = draft.Items.Single(i => i.Source.Type == type);
    private static string BrushColor(IBrush? brush)
    { var c = Assert.IsAssignableFrom<ISolidColorBrush>(brush).Color; return $"#{c.A:X2}{c.R:X2}{c.G:X2}{c.B:X2}"; }
    private static void Pump(Window window) { Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); }
    private static async Task WaitForImagesIdle(BitmapFactory images, CountingDecoder decoder)
    {
        var last = decoder.Decodes; var stable = 0;
        for (var i = 0; i < 150 && stable < 15; i++)
        {
            Dispatcher.UIThread.RunJobs(); await Task.Delay(20, Token);
            stable = decoder.Decodes == last && images.GetDiagnostics().PendingRequests == 0 ? stable + 1 : 0; last = decoder.Decodes;
        }
        Assert.Equal(15, stable);
    }
    /// <summary>只等实际事务和绑定，不用真实系统焦点或固定长等待。</summary>
    private static async Task Wait(Func<bool> done)
    { for (var i = 0; i < 150 && !done(); i++) { Dispatcher.UIThread.RunJobs(); await Task.Delay(20, Token); } Assert.True(done()); }
    /// <summary>每个用例恢复完整原Dark资源表，避免污染后续共享Headless应用。</summary>
    private static async Task ResetPalette()
    { using var reset = new ThemePresenter(Application.Current!, new ThemeConfig()); await reset.RefreshAsync(); }
    private static void Save(Window window, string suffix)
    {
        if (Environment.GetEnvironmentVariable("NEEVIEW_P5_THEME_SCREENSHOT_PREFIX") is not { } prefix) return;
        if (!Path.IsPathFullyQualified(prefix)) throw new InvalidOperationException("主题截图必须使用绝对路径。");
        Directory.CreateDirectory(Path.GetDirectoryName(prefix)!); window.UpdateLayout();
        using var frame = window.CaptureRenderedFrame(); Assert.NotNull(frame); frame!.Save(prefix + "-" + suffix + ".png", PngBitmapEncoderOptions.Default);
    }
}
