using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using NeeView.Backends;
using NeeView.MacOS.ViewModels;
using NeeView.MacOS.Views;
namespace NeeView.Engine.Tests;

/// <summary>封面/启动设置走唯一草稿和原JSON，取消与失败不改变运行配置。</summary>
public sealed class CoverStartupSettingsTests
{
    private sealed class Platform : IPlatformService
    {
        public Task RevealAsync(string path, CancellationToken token = default) => Task.CompletedTask;
        public Task TrashAsync(string path, CancellationToken token = default) => Task.CompletedTask;
    }

    [AvaloniaFact]
    public async Task CoverAndStartupDraftCancelFailureAndRetryPreserveOriginalState()
    {
        using var fixture = new Fixture(); var state = new SaveData(fixture.State);
        await state.LoadAsync(TestContext.Current.CancellationToken);
        Assert.False(Config.Current.StartUp.IsOpenLastBook); // 固定原版默认，不改Windows源码。
        var operation = fixture.Operation(state); var model = new ReaderWorkspaceViewModel(operation, new(operation), state);
        var images = new BitmapFactory(new MagickImageDecoder()); var window = new MainWindow(); window.Bind(model, images, new Platform()); window.Show();
        var blocker = Path.Combine(fixture.State, "UserSetting.json.tmp");
        try
        {
            await window.OpenAsync(fixture.Images); await operation.SaveAsync();
            var last = state.GetLastBook(); Assert.NotNull(last);
            var originalRegex = Config.Current.Book.BookThumbnailRegex;
            var canceled = new SettingsWindow(model); canceled.Show(window);
            canceled.FindControl<TextBox>("BookThumbnailRegex")!.Text = "cancel";
            canceled.FindControl<CheckBox>("OpenLastBook")!.IsChecked = true; canceled.Close();
            Assert.Equal(originalRegex, Config.Current.Book.BookThumbnailRegex); Assert.False(Config.Current.StartUp.IsOpenLastBook);
            var settings = new SettingsWindow(model); settings.Show(window);
            settings.FindControl<TextBox>("BookThumbnailRegex")!.Text = "^cover\\.png$";
            settings.FindControl<NumericUpDown>("BookThumbnailDepth")!.Value = 6; // 原模型不人为截断到4。
            settings.FindControl<CheckBox>("OpenLastBook")!.IsChecked = true;
            await state.SynchronizeWritesAsync();
            Directory.CreateDirectory(blocker);
            settings.FindControl<Button>("SaveSettings")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await UntilAsync(() => settings.FindControl<TextBlock>("Message")!.Text?.StartsWith("保存失败：") == true);
            Assert.Equal(originalRegex, Config.Current.Book.BookThumbnailRegex); Assert.False(Config.Current.StartUp.IsOpenLastBook);
            Directory.Delete(blocker);
            settings.FindControl<Button>("SaveSettings")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await UntilAsync(() => settings.WasSaved);
            Assert.True(Config.Current.StartUp.IsOpenLastBook); Assert.Equal(6, Config.Current.Book.BookThumbnailDepth);
            Assert.Equal("^cover\\.png$", Config.Current.Book.BookThumbnailRegex); Assert.Equal(last!.Path, state.GetLastBook()!.Path);
            Assert.Contains("IsOpenLastBook", File.ReadAllText(Path.Combine(fixture.State, "UserSetting.json")));
        }
        finally { if (Directory.Exists(blocker)) Directory.Delete(blocker); await window.PrepareShutdownAsync(); window.Close(); }
    }
    private static async Task UntilAsync(Func<bool> ready)
    {
        for (int n = 0; n < 200; n++) { Dispatcher.UIThread.RunJobs(); if (ready()) return; await Task.Delay(10, TestContext.Current.CancellationToken); }
        Assert.True(ready());
    }
}
