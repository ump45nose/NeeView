using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using NeeView.MacOS.ViewModels;
using NeeView.MacOS.Views;
namespace NeeView.Engine.Tests;

/// <summary>正式字体表单、实际已打开控件和原事务，后台Headless不代替真机字体/Retina验收。</summary>
public sealed class FontsUiTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private static FontEnvironment EnvironmentValues => new(FontManager.Current.DefaultFontFamily.Name, 12, 13);
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
    [AvaloniaFact]
    public void DraftPreservesMissingNameAndOriginalOutOfRangeScales()
    {
        var source = new FontsConfig { DefaultFontName = "System UI", FontNameRaw = "Removed Family", FontScale = 2.37567, MenuFontScale = .875 };
        var draft = new FontSettingsViewModel(source, ["System UI"]);
        Assert.Contains("Removed Family", draft.FontNames); Assert.Equal(237.567m, draft.FontPercent); Assert.Equal(237.567m, draft.FontMaximum); Assert.Equal(87.5m, draft.MenuMinimum);
        draft.FontPercent = 150; Assert.Equal(2.37567, source.FontScale); draft.Apply(source); Assert.Equal(1.5, source.FontScale); Assert.Equal("Removed Family", source.FontNameRaw);
        var casing = new FontSettingsViewModel(new FontsConfig { FontNameRaw = "same font" }, ["Same Font"]);
        Assert.Contains("same font", casing.FontNames); Assert.DoesNotContain("Same Font", casing.FontNames);
        casing.FontName = null!; Assert.Equal("same font", casing.FontName);
    }
    /// <summary>真实Numeric绑定不裁掉原范围外比例，禁用ClearType保持配置。</summary>
    [AvaloniaFact]
    public async Task FormalFontFormKeepsImportedValuesOnUnrelatedSave()
    {
        using var f = new Fixture(); var state = new SaveData(f.State); await state.LoadAsync(Token);
        Config.Current.Fonts.FontScale = 2.37567; Config.Current.Fonts.MenuFontScale = .875; Config.Current.Fonts.FontNameRaw = "Missing Family"; Config.Current.Fonts.IsClearTypeEnabled = false;
        await using var operation = f.Operation(state); var model = new ReaderWorkspaceViewModel(operation, new CommandTable(operation), state);
        var window = new SettingsWindow(model); window.Show();
        try
        {
            window.FindControl<ListBox>("SettingsNavigation")!.SelectedIndex = 8; Pump(window);
            Assert.Equal(237.567m, window.FindControl<NumericUpDown>("FontPercent")!.Value); Assert.Equal(87.5m, window.FindControl<NumericUpDown>("MenuFontPercent")!.Value);
            Assert.False(window.FindControl<CheckBox>("ClearTypeFont")!.IsEnabled); Assert.False(window.FindControl<CheckBox>("ClearTypeFont")!.IsChecked);
            Click(window, "SaveSettings"); await Wait(() => window.WasSaved);
            Assert.Equal(2.37567, Config.Current.Fonts.FontScale); Assert.Equal(.875, Config.Current.Fonts.MenuFontScale); Assert.Equal("Missing Family", Config.Current.Fonts.FontNameRaw); Assert.False(Config.Current.Fonts.IsClearTypeEnabled);
        }
        finally { window.Close(); model.Detach(); }
    }
    /// <summary>主窗口真实设置入口取消/保存失败/重试，成功后才应用字体资源。</summary>
    [AvaloniaFact]
    public async Task FormalSettingsCancelFailureAndRetryApplyOnlyCommittedFonts()
    {
        using var f = new Fixture(); var state = new SaveData(f.State); await state.LoadAsync(Token);
        var operation = f.Operation(state); var model = new ReaderWorkspaceViewModel(operation, new CommandTable(operation), state);
        var window = new MainWindow(); window.Bind(model, new BitmapFactory(new NeeView.Backends.MagickImageDecoder()), new NoPlatform());
        window.AttachFonts(new(Application.Current!, Config.Current.Fonts, EnvironmentValues)); window.Show();
        try
        {
            await window.OpenAsync(f.Images); await state.SaveAsync(null, Token); var branch = Config.Current.Fonts;
            var open = window.ExecuteAsync("OpenOptionsWindow"); await Wait(() => window.OwnedWindows.OfType<SettingsWindow>().Any());
            var cancel = window.OwnedWindows.OfType<SettingsWindow>().Single(); cancel.FindControl<ListBox>("SettingsNavigation")!.SelectedIndex = 8; Draft(cancel).FontPercent = 175;
            Click(cancel, "CancelSettings"); await open; Assert.Equal(1.25, branch.FontScale); Assert.Equal(15, window.FontSize);
            var editing = window.ExecuteAsync("OpenOptionsWindow"); await Wait(() => window.OwnedWindows.OfType<SettingsWindow>().Any());
            var settings = window.OwnedWindows.OfType<SettingsWindow>().Single(); settings.FindControl<ListBox>("SettingsNavigation")!.SelectedIndex = 8;
            var draft = Draft(settings); draft.FontPercent = 175; draft.MenuPercent = 150; draft.TreePercent = 150; draft.PanelPercent = 200;
            await state.SynchronizeWritesAsync(); var blocked = Path.Combine(f.State, "UserSetting.json.tmp"); Directory.CreateDirectory(blocked);
            try
            {
                Click(settings, "SaveSettings"); await Wait(() => settings.FindControl<TextBlock>("Message")!.Text?.StartsWith("保存失败：") == true);
                Assert.False(settings.WasSaved); Assert.Same(branch, Config.Current.Fonts); Assert.Equal(1.25, branch.FontScale); Assert.Equal(15, window.FontSize); Assert.Equal(175m, draft.FontPercent);
            }
            finally { Directory.Delete(blocked); }
            Click(settings, "SaveSettings"); await editing; Pump(window);
            Assert.True(settings.WasSaved); Assert.Equal(1.75, branch.FontScale); Assert.Equal(21, window.FontSize);
            await new SaveData(f.State).LoadAsync(Token); Assert.Equal(1.75, Config.Current.Fonts.FontScale); Assert.Equal(2, Config.Current.Fonts.PanelFontScale);
        }
        finally { foreach (var owned in window.OwnedWindows.ToArray()) owned.Close(); await window.PrepareShutdownAsync(); window.Close(); ResetFonts(); }
    }
    /// <summary>字体角色更新真实主/浮/设置/树/列表/菜单；正常小图的相同规格无额外解码。</summary>
    [AvaloniaFact]
    public async Task FontRolesUpdateOpenControlsAndKeepCurrentBook()
    {
        using var f = new Fixture(); var state = new SaveData(f.State); await state.LoadAsync(Token);
        var decoder = new CountingDecoder(); var operation = new BookOperation(new NeeView.Backends.ArchiveFactory(), decoder, state); var images = new BitmapFactory(decoder);
        var model = new ReaderWorkspaceViewModel(operation, new CommandTable(operation), state);
        var window = new MainWindow(); window.Bind(model, images, new NoPlatform()); var presenter = new FontPresenter(Application.Current!, Config.Current.Fonts, EnvironmentValues); window.AttachFonts(presenter); window.Show();
        var profile = PanelListItemProfile.Create(PanelListItemStyle.Content); profile.IsTextWrapped = true;
        var item = new PanelListItemView(PanelListItemStyle.Content, profile, null) { DataContext = new BookmarkNode { Name = "字体测试：长名称保持两行完整显示", Path = "synthetic" } };
        var tree = new TreeView { Items = { new TreeViewItem { Header = "目录字体" } } };
        var floating = new FloatingPanelWindow("InformationPanel", "字体测试"); floating.ContentHost.Child = new StackPanel { Children = { tree, item, new TextBox { Text = "默认字体文本" } } }; floating.Show(window);
        var settings = new SettingsWindow(model); settings.Show(window); settings.FindControl<ListBox>("SettingsNavigation")!.SelectedIndex = 8;
        var menuItem = new MenuItem { Header = "字体验证", Items = { new MenuItem { Header = "子菜单" } } }; var menu = new ContextMenu { Items = { menuItem } }; menu.Open(window);
        try
        {
            await window.OpenAsync(f.Images); await window.Viewer.RefreshAsync(); await ImagesIdle(images, decoder);
            var book = operation.Book; var page = book!.CurrentPage; var position = operation.Position; var decodes = decoder.Decodes; var refreshed = 0; model.Refreshed += (_, _) => refreshed++;
            var config = Config.Current.Fonts; config.FontScale = 1.5; config.MenuFontScale = 1.75; config.FolderTreeFontScale = 1.5; config.PanelFontScale = 2;
            presenter.Apply(); Pump(window); Pump(settings); Pump(floating); menu.UpdateLayout();
            Assert.Equal(18, window.FontSize); Assert.Equal(18, settings.FontSize); Assert.Equal(18, floating.FontSize);
            Assert.Equal(22.75, menu.FontSize); Assert.Equal(22.75, menuItem.FontSize); Assert.Equal(22.75, window.FindControl<Menu>("MenuBar")!.FontSize);
            Assert.Equal(18, tree.FontSize); Assert.Equal(24, item.FontSize); Assert.Equal(24, item.FindControl<TextBlock>("ItemName")!.FontSize);
            Assert.Equal(67.2, item.FindControl<TextBlock>("ItemName")!.MaxHeight, 5);
            Assert.Equal(EnvironmentValues.DefaultFontName, window.FontFamily.Name); Assert.Same(book, operation.Book); Assert.Same(page, operation.Book!.CurrentPage); Assert.Equal(position, operation.Position); Assert.Equal(0, refreshed);
            await ImagesIdle(images, decoder); Assert.Equal(decodes, decoder.Decodes);
            Save(window, "main"); Save(settings, "form"); Save(floating, "floating");
            config.FontNameRaw = "NeeView-Missing-Test-Font"; presenter.Apply(); Assert.NotNull(presenter.FallbackMessage); Assert.Equal("NeeView-Missing-Test-Font", config.FontNameRaw);
            Assert.Equal(EnvironmentValues.DefaultFontName, window.FontFamily.Name);
            var installed = FontManager.Current.SystemFonts.First().Name; config.FontNameRaw = installed; presenter.Apply();
            Assert.Null(presenter.FallbackMessage); Assert.Equal(installed, window.FontFamily.Name); Assert.Equal(installed, settings.FontFamily.Name);
            var before = Application.Current!.Resources["DefaultFontSize"]; config.FontScale = double.PositiveInfinity;
            try { Assert.Throws<ArgumentOutOfRangeException>(() => presenter.Apply()); Assert.Equal(before, Application.Current.Resources["DefaultFontSize"]); }
            finally { config.FontScale = 1.5; }
        }
        finally { menu.Close(); settings.Close(); floating.Close(); await window.PrepareShutdownAsync(); window.Close(); ResetFonts(); }
    }
    private static FontSettingsViewModel Draft(SettingsWindow settings) => (FontSettingsViewModel)settings.FindControl<ScrollViewer>("FontSettings")!.DataContext!;
    private static void Click(Window window, string name) => window.FindControl<Button>(name)!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static void Pump(Window window) { Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); }
    private static async Task Wait(Func<bool> done)
    { for (var i = 0; i < 150 && !done(); i++) { Dispatcher.UIThread.RunJobs(); await Task.Delay(20, Token); } Assert.True(done()); }
    private static async Task ImagesIdle(BitmapFactory images, CountingDecoder decoder)
    {
        var last = decoder.Decodes; var stable = 0;
        for (var i = 0; i < 150 && stable < 15; i++)
        { Dispatcher.UIThread.RunJobs(); await Task.Delay(20, Token); stable = decoder.Decodes == last && images.GetDiagnostics().PendingRequests == 0 ? stable + 1 : 0; last = decoder.Decodes; }
        Assert.Equal(15, stable);
    }
    private static void ResetFonts() => new FontPresenter(Application.Current!, new FontsConfig(), EnvironmentValues).Apply();
    private static void Save(Window window, string suffix)
    {
        if (Environment.GetEnvironmentVariable("NEEVIEW_P5_FONTS_SCREENSHOT_PREFIX") is not { } prefix) return;
        if (!Path.IsPathFullyQualified(prefix)) throw new InvalidOperationException("字体截图必须为绝对路径。");
        Directory.CreateDirectory(Path.GetDirectoryName(prefix)!); Pump(window);
        using var frame = window.CaptureRenderedFrame(); Assert.NotNull(frame); frame!.Save(prefix + "-" + suffix + ".png", PngBitmapEncoderOptions.Default);
    }
}
