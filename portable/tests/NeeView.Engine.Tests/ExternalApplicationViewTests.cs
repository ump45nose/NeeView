using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using NeeView.Backends;
using NeeView.MacOS.ViewModels;
using NeeView.MacOS.Views;
namespace NeeView.Engine.Tests;

/// <summary>正式菜单、原应用集合与父设置事务；系统能力使用Fake。</summary>
public sealed class ExternalApplicationViewTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    [AvaloniaFact]
    public void CollectionEditorKeepsIndependentDraftAndOriginalOrder()
    {
        var source = new ExternalAppCollection([new ExternalApp { Name = "甲", Command = "probe1" }, new ExternalApp { Name = "乙", Command = "probe2" }]);
        var dialog = new ExternalAppDialog(source); dialog.Show();
        try
        {
            var model = dialog.Model; model.SelectedItem = model.Items[1]; model.Move(-1); model.SelectedItem.Name = "修改后的乙";
            model.Add(); model.SelectedItem!.Name = "丙"; model.Remove();
            Assert.Equal(["甲", "乙"], source.Select(app => app.Name)); Assert.Equal(["修改后的乙", "甲"], model.ToCollection().Select(app => app.Name));
            dialog.UpdateLayout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            if (Environment.GetEnvironmentVariable("NEEVIEW_EXTERNAL_SCREENSHOT") is { } path) { using var frame = dialog.CaptureRenderedFrame(); frame!.Save(path, Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default); }
            dialog.Close(); Assert.Equal(["甲", "乙"], source.Select(app => app.Name));
        }
        finally { dialog.Close(); }
    }
    [AvaloniaFact]
    public async Task IndexZeroShowsOriginalChoiceAndSelectedItemUsesSameBusiness()
    {
        using var f = new Fixture(); var state = new SaveData(f.State); await state.LoadAsync(Token); var op = f.Operation(state); using var images = new BitmapFactory(new MagickImageDecoder());
        var platform = new ExternalApplicationTests.Platform(); op.AttachExternalApplications(platform, "/tmp/NeeView");
        Config.Current.System.ExternalAppCollection = new([new ExternalApp { Name = "甲", Command = "probe1" }, new ExternalApp { Name = "乙", Command = "probe2" }]);
        var window = new MainWindow(); window.Bind(new(op, new(op), state), images, platform); window.Show();
        try
        {
            await window.OpenAsync(f.Images); var book = op.Book; var page = book!.CurrentPage;
            await window.ExecuteAsync("OpenExternalAppAs"); Dispatcher.UIThread.RunJobs();
            var menu = (ContextMenu)typeof(MainWindow).GetField("_externalMenu", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
            Assert.True(menu.IsOpen); Assert.Empty(platform.Requests); var choice = Assert.IsType<MenuItem>(menu.Items[1]); Assert.Equal("乙", choice.Header);
            choice.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); await WaitAsync(() => platform.Requests.Count == 1);
            Assert.Equal("probe2", platform.Requests.Single().Command); Assert.Same(book, op.Book); Assert.Same(page, book.CurrentPage);
        }
        finally { await window.PrepareShutdownAsync(); window.Close(); }
    }
    [AvaloniaFact]
    public async Task ParentSettingsCancellationAndFailedSaveKeepDraftForRetry()
    {
        using var f = new Fixture(); var state = new SaveData(f.State); await state.LoadAsync(Token); var op = f.Operation(state); using var images = new BitmapFactory(new MagickImageDecoder());
        var model = new ReaderWorkspaceViewModel(op, new(op), state); var window = new MainWindow(); window.Bind(model, images, new ExternalApplicationTests.Platform()); window.Show();
        try
        {
            var field = typeof(SettingsWindow).GetField("_externalApplicationsDraft", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var canceled = new SettingsWindow(model); canceled.Show(window); field.SetValue(canceled, new ExternalAppCollection([new ExternalApp { Name = "取消" }])); canceled.Close();
            Assert.Null(Config.Current.System.ExternalAppCollection.Single().Name);
            var settings = new SettingsWindow(model); settings.Show(window); field.SetValue(settings, new ExternalAppCollection([new ExternalApp { Name = "已编辑", Command = "probe" }]));
            var blocker = Path.Combine(f.State, "UserSetting.json.tmp"); Directory.CreateDirectory(blocker);
            try
            {
                settings.FindControl<Button>("SaveSettings")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await WaitAsync(() => settings.FindControl<TextBlock>("Message")!.Text?.StartsWith("保存失败") == true);
                Assert.Null(Config.Current.System.ExternalAppCollection.Single().Name);
                Assert.Equal("已编辑", Assert.IsType<ExternalAppCollection>(field.GetValue(settings)).Single().Name);
            }
            finally { Directory.Delete(blocker); }
            settings.FindControl<Button>("SaveSettings")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await WaitAsync(() => !settings.IsVisible);
            Assert.Equal("已编辑", Config.Current.System.ExternalAppCollection.Single().Name);
        }
        finally { await window.PrepareShutdownAsync(); window.Close(); }
    }
    private static async Task WaitAsync(Func<bool> condition)
    { for (int i = 0; i < 200 && !condition(); i++) { Dispatcher.UIThread.RunJobs(); await Task.Delay(10, Token); } Assert.True(condition()); }
}
