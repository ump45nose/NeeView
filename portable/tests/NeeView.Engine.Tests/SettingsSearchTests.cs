using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using NeeView.MacOS.ViewModels;
using NeeView.MacOS.Views;
namespace NeeView.Engine.Tests;

/// <summary>原设置搜索语法、正式表单编辑/取消/回滚及窗口历史生命周期。</summary>
public sealed class SettingsSearchTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    [Theory]
    [InlineData("阅读", "page,wide")]
    [InlineData("阅读 /not 宽图", "page")]
    [InlineData("阅读 宽图", "wide")]
    [InlineData("主题 /or 宽图", "theme,wide")]
    [InlineData("/re (宽图|主题)", "wide,theme")]
    [InlineData("none", "")]
    public void OriginalStructuredTextSearchKeepsSectionAndStableTargets(string keyword, string targets)
    {
        using var index = new SettingsSearchIndex([new("page", "阅读", "分页", "模式"), new("wide", "阅读", "分页", "宽图"), new("theme", "主题", "配色", "目录")]);
        Assert.Equal(targets.Split(',', StringSplitOptions.RemoveEmptyEntries), index.Search(keyword, Token).Select(i => i.Target));
    }
    [Theory]
    [InlineData("/rating /gt 3")]
    [InlineData("/re [")]
    public void InvalidSyntaxDoesNotSilentlyMatchEverySetting(string keyword)
    {
        using var index = new SettingsSearchIndex([new("one", "阅读", "", "模式")]);
        Assert.ThrowsAny<Exception>(() => index.Search(keyword, Token));
    }
    [Fact]
    public void CancellationIsPropagatedWithoutChangingIndex()
    {
        using var index = new SettingsSearchIndex([new("one", "阅读", "", "模式")]);
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => index.Search("阅读", cancel.Token)); Assert.Single(index.Items);
    }
    [AvaloniaFact]
    public async Task EveryFormFieldAndAllOriginalCommandsAreIndexedIncludingDisabledCapabilities()
    {
        using var f = new Fixture(); var state = new SaveData(f.State); await state.LoadAsync(Token); Config.Current.System.IsIncrementalSearchEnabled = false;
        await using var operation = f.Operation(state); var model = new ReaderWorkspaceViewModel(operation, new(operation), state);
        var settings = new SettingsWindow(model); settings.Show();
        try
        {
            var fields = PageRoots(settings).SelectMany(r => r.GetLogicalDescendants().OfType<Control>())
                .Where(c => c.Name is not null && c is CheckBox or ComboBox or NumericUpDown or TextBox or Button).Select(c => c.Name).ToArray();
            var index = Presenter(settings).Index;
            Assert.All(fields, name => Assert.Contains(index.Items, i => i.Target == name));
            Assert.Equal(model.Commands.Definitions.Count(), index.Items.Count(i => i.Target.StartsWith("command:")));
            Assert.Equal(index.Items.Count, index.Items.Select(i => i.Target).Distinct().Count());
            Assert.Contains(index.Search("ClearType", Token), i => i.Target == "ClearTypeFont");
            Assert.Contains(index.Search("尚未迁移", Token), i => i.Target.StartsWith("command:"));
            Assert.Contains(index.Search("旋转角度", Token), i => i.Target.StartsWith("command:"));
            await Query(settings, "缩放 /or 旋转 /or 宽图"); Save(settings, "fields");
            await Query(settings, "MoveToDestinationFolder9"); Save(settings, "command");
            var row = settings.FindControl<StackPanel>("SettingsSearchResults")!.GetLogicalDescendants().OfType<TextBox>().First();
            row.Text = "Meta+F9"; Pump(settings);
            Assert.Equal("Meta+F9", Inputs(settings).Single(i => i.Name == "MoveToDestinationFolder9").Value);
            Click(settings, "SettingsSearchClear"); Assert.Same(Inputs(settings), settings.FindControl<ListBox>("InputList")!.ItemsSource);
            Assert.False(settings.FindControl<ScrollViewer>("SettingsSearchPage")!.IsVisible);
            await Query(settings, "没有任何匹配123"); Assert.Equal("没有匹配的设置", settings.FindControl<TextBlock>("SettingsSearchSummary")!.Text);
        }
        finally { settings.Close(); model.Detach(); }
    }
    [AvaloniaFact]
    public async Task SearchReusesBoundDraftsRestoresParentsAndCancelKeepsJsonUntouched()
    {
        using var f = new Fixture(); var state = new SaveData(f.State); await state.LoadAsync(Token); Config.Current.System.IsIncrementalSearchEnabled = false;
        await using var operation = f.Operation(state); var model = new ReaderWorkspaceViewModel(operation, new(operation), state);
        await state.SaveAsync(null, Token); var before = await File.ReadAllBytesAsync(Path.Combine(f.State, "UserSetting.json"), Token);
        var settings = new SettingsWindow(model); settings.Show();
        try
        {
            settings.FindControl<ListBox>("SettingsNavigation")!.SelectedIndex = 8;
            var font = settings.FindControl<NumericUpDown>("FontPercent")!; var parent = (Panel)font.Parent!; var order = parent.Children.ToArray(); var source = font.DataContext;
            font.Value = 175;
            var themeFolder = settings.FindControl<TextBox>("ThemeFolder")!; themeFolder.Text = Path.Combine(f.Root, "草稿");
            var history = settings.FindControl<CheckBox>("SaveHistory")!; history.IsChecked = false;
            for (var i = 0; i < 3; i++)
            {
                await Query(settings, "字体 /or 主题 /or 历史记录");
                Assert.Same(font, settings.FindControl<NumericUpDown>("FontPercent")); Assert.NotSame(parent, font.Parent); Assert.Same(source, font.DataContext);
                Assert.Equal(175m, font.Value); Assert.Equal(Path.Combine(f.Root, "草稿"), themeFolder.Text); Assert.False(history.IsChecked);
                font.Value = 180; Click(settings, "SettingsSearchClear");
                Assert.Same(parent, font.Parent); Assert.Equal(order, parent.Children); Assert.Same(source, font.DataContext); Assert.Equal(180m, font.Value); font.Value = 175;
            }
            await Query(settings, "字体"); settings.FindControl<ListBox>("SettingsNavigation")!.SelectedIndex = 4; Pump(settings);
            Assert.Same(parent, font.Parent); Assert.True(settings.FindControl<ScrollViewer>("HistorySettings")!.IsVisible);
            Click(settings, "CancelSettings"); Assert.False(settings.WasSaved); Assert.Equal(1.25, Config.Current.Fonts.FontScale);
            Assert.Equal(before, await File.ReadAllBytesAsync(Path.Combine(f.State, "UserSetting.json"), Token));
        }
        finally { settings.Close(); model.Detach(); }
    }
    [AvaloniaFact]
    public async Task SavingFromResultsRollsBackOnFailureAndRetriesSameFontAndHistoryDrafts()
    {
        using var f = new Fixture(); var state = new SaveData(f.State); await state.LoadAsync(Token); Config.Current.System.IsIncrementalSearchEnabled = false;
        await using var operation = f.Operation(state); var model = new ReaderWorkspaceViewModel(operation, new(operation), state);
        var settings = new SettingsWindow(model); settings.Show();
        try
        {
            await Query(settings, "字体 /or 历史记录 /or 宽图");
            settings.FindControl<NumericUpDown>("FontPercent")!.Value = 175; settings.FindControl<CheckBox>("SaveHistory")!.IsChecked = false;
            settings.FindControl<CheckBox>("Wide")!.IsChecked = false; Pump(settings);
            await state.SynchronizeWritesAsync(); var blocked = Path.Combine(f.State, "UserSetting.json.tmp"); Directory.CreateDirectory(blocked);
            try
            {
                Click(settings, "SaveSettings"); await Wait(() => settings.FindControl<TextBlock>("Message")!.Text?.StartsWith("保存失败：") == true);
                Assert.False(settings.WasSaved); Assert.Equal(1.25, Config.Current.Fonts.FontScale); Assert.True(Config.Current.History.IsSaveHistory);
                Assert.Equal(175m, settings.FindControl<NumericUpDown>("FontPercent")!.Value); Assert.False(settings.FindControl<CheckBox>("SaveHistory")!.IsChecked);
            }
            finally { Directory.Delete(blocked); }
            Click(settings, "SaveSettings"); await Wait(() => settings.WasSaved);
            Assert.Equal(1.75, Config.Current.Fonts.FontScale); Assert.False(Config.Current.History.IsSaveHistory); Assert.False(Config.Current.BookSetting.IsSupportedWidePage);
            await new SaveData(f.State).LoadAsync(Token); Assert.Equal(1.75, Config.Current.Fonts.FontScale); Assert.False(Config.Current.History.IsSaveHistory);
        }
        finally { settings.Close(); model.Detach(); }
    }
    [AvaloniaFact]
    public async Task ConfirmationHistoryIsWindowLocalAndInvalidQueryKeepsLastValidResult()
    {
        using var f = new Fixture(); var state = new SaveData(f.State); await state.LoadAsync(Token); Config.Current.System.IsIncrementalSearchEnabled = false; Config.Current.System.SearchHistorySize = 2;
        await using var operation = f.Operation(state); var model = new ReaderWorkspaceViewModel(operation, new(operation), state);
        var settings = new SettingsWindow(model); settings.Show();
        try
        {
            await state.SaveAsync(null, Token); var before = await File.ReadAllBytesAsync(Path.Combine(f.State, "History.json"), Token);
            var search = Search(settings); settings.FindControl<TextBox>("SettingsSearchBox")!.Text = "字体"; Pump(settings);
            Assert.Equal("字体", search.Keyword); Assert.False(settings.FindControl<ScrollViewer>("SettingsSearchPage")!.IsVisible); Assert.Empty(search.History);
            Assert.True(await search.SearchAsync()); Assert.Equal("字体", Assert.Single(search.History));
            var font = settings.FindControl<NumericUpDown>("FontPercent")!; var parent = font.Parent;
            search.Keyword = "/re ["; Assert.False(await search.SearchAsync()); Assert.NotNull(search.Error); Assert.Same(parent, font.Parent); Assert.Single(search.History);
            await Query(settings, "主题"); await Query(settings, "字体"); await Query(settings, "宽图"); Assert.Equal(new[] { "宽图", "字体" }, search.History);
            await search.RemoveHistoryAsync("宽图"); Assert.Equal("字体", Assert.Single(search.History));
            Assert.Equal(before, await File.ReadAllBytesAsync(Path.Combine(f.State, "History.json"), Token));
            settings.Close(); var reopened = new SettingsWindow(model); reopened.Show(); Assert.Empty(Search(reopened).History); reopened.Close();
        }
        finally { settings.Close(); model.Detach(); }
    }
    [AvaloniaFact]
    public async Task ParameterDialogFromSearchUsesSharedDraftAndCancelDoesNotApplyIt()
    {
        using var f = new Fixture(); var state = new SaveData(f.State); await state.LoadAsync(Token); Config.Current.System.IsIncrementalSearchEnabled = false;
        await using var operation = f.Operation(state); var model = new ReaderWorkspaceViewModel(operation, new(operation), state);
        var original = state.GetCommandParameter<ViewScaleCommandParameter>("ViewScaleUp").Scale;
        var settings = new SettingsWindow(model, _ => true); settings.Show();
        try
        {
            await Query(settings, "ViewScaleUp");
            var parameter = settings.FindControl<StackPanel>("SettingsSearchResults")!.GetLogicalDescendants().OfType<Button>().Single(b => Equals(b.Content, "参数…"));
            parameter.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Wait(() => settings.OwnedWindows.OfType<CommandParameterWindow>().Any());
            var dialog = settings.OwnedWindows.OfType<CommandParameterWindow>().Single(); dialog.FindControl<NumericUpDown>("Scale")!.Value = .75m;
            Click(dialog, "CancelParameter"); await Wait(() => !settings.OwnedWindows.Any());
            parameter.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Wait(() => settings.OwnedWindows.OfType<CommandParameterWindow>().Any());
            dialog = settings.OwnedWindows.OfType<CommandParameterWindow>().Single(); Assert.Equal((decimal)original, dialog.FindControl<NumericUpDown>("Scale")!.Value);
            dialog.FindControl<NumericUpDown>("Scale")!.Value = .25m; Click(dialog, "ApplyParameter"); await Wait(() => !settings.OwnedWindows.Any());
            Assert.Equal(original, state.GetCommandParameter<ViewScaleCommandParameter>("ViewScaleUp").Scale);
            Click(settings, "SaveSettings"); await Wait(() => settings.WasSaved);
            Assert.Equal(.25, state.GetCommandParameter<ViewScaleCommandParameter>("ViewScaleUp").Scale);
            Assert.Equal(.25, state.GetCommandParameter<ViewScaleCommandParameter>("ViewScaleDown").Scale);
        }
        finally { foreach (var child in settings.OwnedWindows.ToArray()) child.Close(); settings.Close(); model.Detach(); }
    }
    [AvaloniaFact]
    public async Task ClearingTextAndHistoryButtonsUseSameWindowSearch()
    {
        using var f = new Fixture(); var state = new SaveData(f.State); await state.LoadAsync(Token); Config.Current.System.IsIncrementalSearchEnabled = false;
        await using var operation = f.Operation(state); var model = new ReaderWorkspaceViewModel(operation, new(operation), state);
        var settings = new SettingsWindow(model); settings.Show();
        try
        {
            settings.FindControl<ListBox>("SettingsNavigation")!.SelectedIndex = 8;
            await Query(settings, "字体"); settings.FindControl<TextBox>("SettingsSearchBox")!.Text = ""; Pump(settings);
            Assert.True(settings.FindControl<ScrollViewer>("FontSettings")!.IsVisible); Assert.False(settings.FindControl<ScrollViewer>("SettingsSearchPage")!.IsVisible);
            Click(settings, "SettingsSearchHistory"); var menu = settings.FindControl<Button>("SettingsSearchHistory")!.ContextMenu!;
            var entry = Assert.IsType<MenuItem>(Assert.Single(menu.Items));
            var delete = Assert.IsType<StackPanel>(entry.Header).Children.OfType<Button>().Single(); delete.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Empty(Search(settings).History); Assert.False(menu.IsOpen);
        }
        finally { settings.FindControl<Button>("SettingsSearchHistory")!.ContextMenu?.Close(); settings.Close(); model.Detach(); }
    }
    [AvaloniaFact]
    public async Task IncrementalSearchMergesInputAndClosingCancelsPendingResult()
    {
        using var f = new Fixture(); var state = new SaveData(f.State); await state.LoadAsync(Token); Config.Current.System.IsIncrementalSearchEnabled = true;
        await using var operation = f.Operation(state); var model = new ReaderWorkspaceViewModel(operation, new(operation), state);
        var settings = new SettingsWindow(model); settings.Show();
        try
        {
            var search = Search(settings); search.Keyword = "字体"; search.Keyword = "宽图";
            await Wait(() => settings.FindControl<ScrollViewer>("SettingsSearchPage")!.IsVisible);
            Assert.DoesNotContain(settings.FindControl<StackPanel>("SettingsSearchResults")!.GetLogicalDescendants().OfType<Control>(), c => c.Name == "FontPercent"); Assert.Empty(search.History);
            search.Keyword = "字体"; settings.Close(); await Task.Delay(600, Token); Dispatcher.UIThread.RunJobs();
            Assert.Same(settings.FindControl<ScrollViewer>("FontSettings")!.Content, settings.FindControl<NumericUpDown>("FontPercent")!.Parent);
        }
        finally { settings.Close(); model.Detach(); }
    }
    private static SettingsSearchPresenter Presenter(SettingsWindow w) => (SettingsSearchPresenter)typeof(SettingsWindow).GetField("_settingsSearchPresenter", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(w)!;
    private static NavigationSearchViewModel Search(SettingsWindow w) => (NavigationSearchViewModel)typeof(SettingsWindow).GetField("_settingsSearch", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(w)!;
    private static IReadOnlyList<ShortcutEdit> Inputs(SettingsWindow w) => (IReadOnlyList<ShortcutEdit>)typeof(SettingsWindow).GetField("_inputs", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(w)!;
    private static IEnumerable<Control> PageRoots(SettingsWindow w) => new[] { "ReadingSettings", "InputSettings", "FilmSettings", "AutoHideSettings", "HistorySettings", "NavigationSettings", "FileSettings", "ThemeSettings", "FontSettings" }.Select(n => w.FindControl<Control>(n)!);
    private static async Task Query(SettingsWindow w, string text) { var search = Search(w); search.Keyword = text; Assert.True(await search.SearchAsync()); Pump(w); }
    private static void Click(Window w, string name) { w.FindControl<Button>(name)!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump(w); }
    private static void Pump(Window w) { Dispatcher.UIThread.RunJobs(); w.UpdateLayout(); }
    private static async Task Wait(Func<bool> done) { for (var i = 0; i < 150 && !done(); i++) { Dispatcher.UIThread.RunJobs(); await Task.Delay(20, Token); } Assert.True(done()); }
    private static void Save(Window w, string suffix)
    {
        if (Environment.GetEnvironmentVariable("NEEVIEW_P5_SETTINGS_SEARCH_SCREENSHOT_PREFIX") is not { } prefix) return;
        if (!Path.IsPathFullyQualified(prefix)) throw new InvalidOperationException("截图必须为绝对路径。");
        Directory.CreateDirectory(Path.GetDirectoryName(prefix)!); Pump(w); using var frame = w.CaptureRenderedFrame(); Assert.NotNull(frame);
        frame!.Save(prefix + "-" + suffix + ".png", Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
    }
}
