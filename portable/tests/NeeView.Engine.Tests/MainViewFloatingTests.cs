using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using NeeView.Backends;
using NeeView.MacOS.ViewModels;
using NeeView.MacOS.Views;
using NeeView.Windows;
namespace NeeView.Engine.Tests;

/// <summary>真实 XAML 中央查看器宿主、借用面板和保存失败回归；不激活桌面应用。</summary>
public sealed class MainViewFloatingTests
{
    private sealed class Platform : IPlatformService
    {
        public Task RevealAsync(string path, CancellationToken token = default) => Task.CompletedTask;
        public Task TrashAsync(string path, CancellationToken token = default) => Task.CompletedTask;
    }
    private static async Task<(SaveData State, ReaderWorkspaceViewModel Model, MainWindow Window)> OpenAsync(Fixture f)
    {
        var state = new SaveData(f.State); await state.LoadAsync(TestContext.Current.CancellationToken);
        var op = f.Operation(state); var model = new ReaderWorkspaceViewModel(op, new(op), state);
        var window = new MainWindow(); window.Bind(model, new(new MagickImageDecoder()), new Platform()); window.Show();
        await window.OpenAsync(f.Images); await PageListThumbnailTests.SettleAsync(window);
        return (state, model, window);
    }
    [AvaloniaFact]
    public async Task FloatDockKeepsUniqueReaderBookPageListAndOriginalLayout()
    {
        using var f = new Fixture(); var (state, model, window) = await OpenAsync(f);
        try
        {
            var reader = window.Viewer; var book = model.Operation.Book; var list = window.FindControl<ListBox>("PageList")!;
            model.ShowPanel("PageListPanel"); list.SelectedItems!.Add(model.Pages[2]);
            var selection = list.SelectedItems.Cast<object>().ToArray(); var layout = model.Layout;
            await window.ExecuteAsync("ToggleMainViewFloating"); var floating = Assert.IsType<MainViewWindow>(window.FloatingMainView);
            Assert.Same(reader, window.Viewer); Assert.Same(book, model.Operation.Book);
            Assert.Same(floating, TopLevel.GetTopLevel(reader)); Assert.Same(window, TopLevel.GetTopLevel(list));
            Assert.Equal(selection, list.SelectedItems.Cast<object>().ToArray()); Assert.Same(layout, model.Layout);
            Assert.False(WindowInteraction.HasDialog(window));
            var timer = window.FindControl<SimpleProgressBar>("SlideShowTimer")!; Assert.Same(floating, TopLevel.GetTopLevel(timer));
            await window.ExecuteAsync("NextPage"); await PageListThumbnailTests.SettleAsync(window); Assert.Equal(1, model.Operation.Position.Index);
            floating.Close(); Assert.Equal(WindowState.Minimized, floating.WindowState); Assert.Same(floating, window.FloatingMainView);
            await window.ExecuteAsync("NextPage"); await PageListThumbnailTests.SettleAsync(window); Assert.Equal(WindowState.Normal, floating.WindowState);
            await window.ExecuteAsync("ToggleMainViewFloating"); Assert.Null(window.FloatingMainView); Assert.Same(window, TopLevel.GetTopLevel(reader));
            Assert.Same(reader, window.Viewer); Assert.Same(book, model.Operation.Book); Assert.Same(window, TopLevel.GetTopLevel(list));
            Assert.Same(window, TopLevel.GetTopLevel(timer));
            await state.SaveAsync(book); Assert.False(Config.Current.MainView.IsFloating);
        }
        finally { await window.PrepareShutdownAsync(); window.Close(); }
    }
    [AvaloniaFact]
    public async Task PageListTemporaryBorrowRestoresItsExistingFloatingWindowAndSelection()
    {
        using var f = new Fixture(); var (_, model, window) = await OpenAsync(f);
        try
        {
            model.Layout.OpenWindow("PageListPanel"); Dispatcher.UIThread.RunJobs();
            var list = window.FindControl<ListBox>("PageList")!;
            Assert.IsType<FloatingPanelWindow>(TopLevel.GetTopLevel(list));
            var placement = model.Layout.Panels["PageListPanel"].WindowPlacement;
            await window.ExecuteAsync("ToggleMainViewFloating"); Assert.Same(window, TopLevel.GetTopLevel(list));
            Assert.Contains("PageListPanel", model.Layout.Windows);
            Config.Current.MainView.AlternativeContent = AlternativeContent.Blank; Dispatcher.UIThread.RunJobs();
            Assert.IsType<FloatingPanelWindow>(TopLevel.GetTopLevel(list));
            Config.Current.MainView.AlternativeContent = AlternativeContent.PageList; Dispatcher.UIThread.RunJobs();
            Assert.Same(window, TopLevel.GetTopLevel(list));
            Config.Current.MainView.IsFloatingEndWhenClosed = true; window.FloatingMainView!.Close(); Dispatcher.UIThread.RunJobs();
            Assert.Null(window.FloatingMainView); Assert.False(Config.Current.MainView.IsFloating);
            Assert.IsType<FloatingPanelWindow>(TopLevel.GetTopLevel(list));
            Assert.Equal(placement, model.Layout.Panels["PageListPanel"].WindowPlacement);
        }
        finally { await window.PrepareShutdownAsync(); window.Close(); }
    }
    [AvaloniaFact]
    public async Task FailedShutdownKeepsSameFloatingHostThenPersistsAndReopens()
    {
        using var f = new Fixture(); var (state, model, window) = await OpenAsync(f); var blocker = Path.Combine(f.State, "UserSetting.json.tmp");
        try
        {
            await window.ExecuteAsync("ToggleMainViewFloating"); var floating = window.FloatingMainView!;
            floating.Width = 650; floating.Height = 460; floating.UpdateLayout(); floating.Store();
            await state.SynchronizeWritesAsync(); Directory.CreateDirectory(blocker);
            await Assert.ThrowsAnyAsync<Exception>(() => window.PrepareShutdownAsync());
            Assert.Same(floating, window.FloatingMainView); Assert.True(floating.IsVisible); Assert.True(floating.IsEnabled);
            Assert.True(Config.Current.MainView.IsFloating); Assert.NotNull(model.Operation.Book);
            Directory.Delete(blocker); await window.PrepareShutdownAsync(); window.Close();
            var restored = new SaveData(f.State); await restored.LoadAsync(TestContext.Current.CancellationToken);
            Assert.True(Config.Current.MainView.IsFloating); Assert.True(Config.Current.MainView.WindowPlacement.IsValid());
            var op = f.Operation(restored); var reopened = new MainWindow(); var nextModel = new ReaderWorkspaceViewModel(op, new(op), restored);
            reopened.Bind(nextModel, new(new MagickImageDecoder()), new Platform()); reopened.Show();
            Assert.NotNull(reopened.FloatingMainView); Assert.Equal(WindowState.Minimized, reopened.FloatingMainView!.WindowState);
            await reopened.OpenAsync(f.Images); await PageListThumbnailTests.SettleAsync(reopened);
            Assert.Equal(WindowState.Normal, reopened.FloatingMainView.WindowState);
            await reopened.PrepareShutdownAsync(); reopened.Close();
        }
        finally { if (Directory.Exists(blocker)) Directory.Delete(blocker); await window.PrepareShutdownAsync(); window.Close(); }
    }
    [AvaloniaFact]
    public async Task ManualResizeUpdatesReferenceAndStretchUsesCurrentHostOnly()
    {
        using var f = new Fixture(); var (_, model, window) = await OpenAsync(f);
        try
        {
            await window.ExecuteAsync("ToggleMainViewFloating"); var floating = window.FloatingMainView!;
            var reader = window.Viewer; var book = model.Operation.Book; var ownerSize = window.ClientSize;
            floating.Width = 700; floating.Height = 500; floating.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            Assert.Equal(new NeeView.Size(700, 500), Config.Current.MainView.ReferenceSize);
            var reference = Config.Current.MainView.ReferenceSize;
            floating.IsReferenceSizeLocked = true;
            floating.Width = 680; floating.Height = 480; floating.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            floating.IsReferenceSizeLocked = false;
            Assert.Equal(reference, Config.Current.MainView.ReferenceSize);
            await window.ExecuteAsync("StretchWindow");
            Assert.Equal(ownerSize, window.ClientSize); Assert.Same(reader, window.Viewer); Assert.Same(book, model.Operation.Book);
            floating.WindowState = WindowState.Maximized;
            Assert.False(window.IsCommandAvailable("StretchWindow"));
        }
        finally { await window.PrepareShutdownAsync(); window.Close(); }
    }
    [AvaloniaFact]
    public async Task SettingsCancelAndFailedSaveRestoreSameOriginalMainViewConfig()
    {
        using var f = new Fixture(); var (state, model, window) = await OpenAsync(f); var blocker = Path.Combine(f.State, "UserSetting.json.tmp");
        try
        {
            var branch = Config.Current.MainView;
            var canceled = new SettingsWindow(model); canceled.Show(window); canceled.FindControl<CheckBox>("MainViewTopmost")!.IsChecked = true; canceled.Close();
            Assert.False(branch.IsTopmost);
            var settings = new SettingsWindow(model); settings.Show(window); settings.FindControl<CheckBox>("MainViewTopmost")!.IsChecked = true;
            settings.FindControl<ComboBox>("MainViewAlternative")!.SelectedIndex = 0;
            await state.SynchronizeWritesAsync(); Directory.CreateDirectory(blocker);
            settings.FindControl<Button>("SaveSettings")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            for (var i = 0; i < 200 && settings.FindControl<TextBlock>("Message")!.Text?.StartsWith("保存失败：") != true; i++) { Dispatcher.UIThread.RunJobs(); await Task.Delay(10, TestContext.Current.CancellationToken); }
            Assert.StartsWith("保存失败：", settings.FindControl<TextBlock>("Message")!.Text);
            Assert.Same(branch, Config.Current.MainView); Assert.False(branch.IsTopmost); Assert.Equal(AlternativeContent.PageList, branch.AlternativeContent);
            Directory.Delete(blocker); settings.Close();
        }
        finally { if (Directory.Exists(blocker)) Directory.Delete(blocker); await window.PrepareShutdownAsync(); window.Close(); }
    }
    [Fact]
    public async Task OriginalMainViewJsonPreservesUnknownFieldsAndDefaultDifference()
    {
        using var f = new Fixture(); Directory.CreateDirectory(f.State);
        File.WriteAllText(Path.Combine(f.State, "UserSetting.json"), "{\"Config\":{\"MainView\":{\"IsFloating\":true,\"IsTopmost\":true,\"ReferenceSize\":\"640,480\",\"WindowPlacement\":\"Normal,30,40,1280,960\",\"FutureField\":7}}}");
        var state = new SaveData(f.State); await state.LoadAsync(TestContext.Current.CancellationToken);
        Assert.True(Config.Current.MainView.IsFloating); Assert.Equal(new NeeView.Size(640, 480), Config.Current.MainView.ReferenceSize);
        Assert.Equal(1280, Config.Current.MainView.WindowPlacement.Width); await state.SaveAsync(null, TestContext.Current.CancellationToken);
        var raw = File.ReadAllText(Path.Combine(f.State, "UserSetting.json")); Assert.Contains("FutureField", raw); Assert.Contains("ReferenceSize", raw);
    }
}
