using System.Text.Json.Nodes;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using NeeView.Backends;
using NeeView.MacOS.Views;
using NeeView.PageFrames;
namespace NeeView.Engine.Tests;

/// <summary>原周期、输入、媒体EOS、命令路由、页尾及唯一JSON/正式视图闭环。</summary>
public sealed class SlideShowTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    [Fact]
    public async Task TimerResetsOnActualDisplayAndInputButNotOnSamePageRefresh()
    {
        using var f = new Fixture(); var state = new SaveData(f.State); await state.LoadAsync(Token);
        await using var op = f.Operation(state); await op.OpenAsync(f.Images, Token);
        double clock = 0; using var show = new SlideShow(op, () => clock); var commands = new List<string>();
        show.Play(); Assert.True(show.NotifyDisplayed()); clock = 4;
        Assert.False(show.NotifyDisplayed()); show.NotifyInput(SlideShowTimerResetGesture.InputAction);
        clock = 8; await Tick(); Assert.Empty(commands); clock = 9; await Tick(); Assert.Equal(["NextPage"], commands);
        clock = 10; show.NotifyInput(SlideShowTimerResetGesture.MouseMove); clock = 14; await Tick(); Assert.Equal(2, commands.Count);
        show.Suspend(); clock = 100; await Tick(); Assert.Equal(2, commands.Count); show.Resume();
        clock = 104.9; await Tick(); Assert.Equal(2, commands.Count); clock = 105; await Tick(); Assert.Equal(3, commands.Count);
        show.Stop(); clock = 1000; await Tick(); Assert.Equal(3, commands.Count); Assert.Equal(0, show.Progress);
        Task Tick() => show.TickAsync(_ => true, _ => true, name => { commands.Add(name); return Task.CompletedTask; });
    }
    [Fact]
    public async Task PrioritizeTimeUsesOriginalScheduleAndNeverBatchesCatchup()
    {
        using var f = new Fixture(); var state = new SaveData(f.State); await state.LoadAsync(Token);
        await using var op = f.Operation(state); await op.OpenAsync(f.Images, Token);
        Config.Current.SlideShow.IsPrioritizeTime = true;
        double clock = 0; using var show = new SlideShow(op, () => clock); int calls = 0; show.Play();
        clock = 4; show.NotifyInput(SlideShowTimerResetGesture.InputAction);
        clock = 5; await Tick(); Assert.Equal(1, calls); Assert.Equal(5000, show.Interval);
        clock = 22; await Tick(); Assert.Equal(2, calls); Assert.Equal(1, show.Interval, .00001);
        await Tick(); Assert.Equal(2, calls); clock += .002; await Tick(); Assert.Equal(3, calls);
        Task Tick() => show.TickAsync(_ => true, _ => true, _ => { calls++; return Task.CompletedTask; });
    }
    [Fact]
    public async Task ProgressUsesCompensatedPlayedIntervalAfterDelayedCommand()
    {
        using var f = new Fixture(); var state = new SaveData(f.State); await state.LoadAsync(Token);
        await using var op = f.Operation(state); await op.OpenAsync(f.Images, Token);
        Config.Current.SlideShow.IsPrioritizeTime = true;
        double clock = 0; using var show = new SlideShow(op, () => clock); show.Play(); clock = 5;
        await show.TickAsync(_ => true, _ => true, _ => { clock = 7; return Task.CompletedTask; });
        // 原 MainView 的 Played 动画每次从1到0，时长为补偿后的下一周期。
        Assert.Equal(3000, show.Interval); Assert.Equal(1, show.Progress);
        clock = 8; Assert.Equal(2.0 / 3, show.Progress, 6);
        Config.Current.SlideShow.SlideShowInterval = double.MaxValue;
        Assert.Equal(TimeSpan.Zero, op.Context!.AutoScrollDuration);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WaitForFirstMediaCycleHandlesBothRepeatModesAndIgnoresLatePlayer(bool repeat)
    {
        using var f = new Fixture(); var state = new SaveData(f.State); await state.LoadAsync(Token);
        await using var op = f.Operation(state); await op.OpenAsync(f.Images, Token);
        Config.Current.SlideShow.IsWaitAnimation = true;
        using var player = new AnimatedMediaPlayer(new(new(2, 2), AnimatedImageType.Gif, [TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(4)], 0)) { IsRepeat = repeat };
        op.CurrentMediaPlayer = player; player.Play(); double clock = 0; int calls = 0;
        using var show = new SlideShow(op, () => clock); show.Play(); clock = 5; await Tick(); Assert.True(show.IsWaitingAnimation);
        player.Pause(); clock = 100; await Tick(); Assert.Equal(0, calls);
        player.Play(); player.Advance(TimeSpan.FromSeconds(8)); await Tick(); Assert.Equal(1, calls); Assert.False(show.IsWaitingAnimation);
        using var replacement = new AnimatedMediaPlayer(player.Info); op.CurrentMediaPlayer = replacement;
        clock += 5; await Tick(); Assert.True(show.IsWaitingAnimation);
        op.CurrentMediaPlayer = null; await Tick(); Assert.False(show.IsWaitingAnimation);
        replacement.Play(); replacement.Advance(TimeSpan.FromSeconds(20)); await Tick(); Assert.Equal(1, calls);
        Task Tick() => show.TickAsync(_ => true, _ => true, _ => { calls++; return Task.CompletedTask; });
    }
    [Fact]
    public async Task TickIsSerialAndStoppedLateCommandCannotRestartTimer()
    {
        using var f = new Fixture(); var state = new SaveData(f.State); await state.LoadAsync(Token);
        await using var op = f.Operation(state); await op.OpenAsync(f.Images, Token);
        double clock = 0; using var show = new SlideShow(op, () => clock); show.Play(); clock = 5;
        var entered = new TaskCompletionSource(); var finish = new TaskCompletionSource(); int calls = 0;
        async Task Execute(string _) { calls++; entered.SetResult(); await finish.Task; }
        var pending = show.TickAsync(_ => true, _ => true, Execute); await entered.Task;
        await show.TickAsync(_ => true, _ => true, Execute); Assert.Equal(1, calls);
        show.Stop(); finish.SetResult(); await pending; Assert.False(show.IsPlaying);
        show.Play(); clock = 9; await show.TickAsync(_ => true, _ => true, _ => { calls++; return Task.CompletedTask; }); Assert.Equal(1, calls);
    }
    [Fact]
    public async Task UnknownCommandFallsBackWhileUnavailableKnownCommandDoesNotNavigate()
    {
        using var f = new Fixture(); var state = new SaveData(f.State); await state.LoadAsync(Token);
        await using var op = f.Operation(state); await op.OpenAsync(f.Images, Token);
        Config.Current.SlideShow.NextPageCommandName = "FutureInvalid";
        double clock = 0; using var show = new SlideShow(op, () => clock); show.Play(); clock = 5;
        await show.TickAsync(DefaultInputScheme.IsKnownCommand, _ => true, name => { Assert.Equal("NextPage", name); return op.MoveAsync(1); });
        Assert.Equal("NextPage", Config.Current.SlideShow.NextPageCommandName); Assert.Equal(1, op.Position.Index);
        var position = op.Position; clock = 10;
        await show.TickAsync(_ => true, _ => false, _ => throw new Exception("不可用命令不应执行")); Assert.Equal(position, op.Position);
    }
    [Theory]
    [InlineData(PageEndAction.Loop, 0, true)]
    [InlineData(PageEndAction.None, 4, false)]
    [InlineData(PageEndAction.SeamlessLoop, 0, true)]
    public async Task SlideShowOverridesBookTerminalRule(PageEndAction end, int expectedIndex, bool playing)
    {
        using var f = new Fixture(); var state = new SaveData(f.State); await state.LoadAsync(Token);
        await using var op = f.Operation(state); await op.OpenAsync(f.Images, Token); await op.JumpAsync(4);
        Config.Current.Book.PageEndAction = PageEndAction.None; Config.Current.SlideShow.PageEndAction = end;
        op.SlideShow.Play(); Assert.Equal(PageMoveType.Fade, op.Context!.PageChangeType); Assert.Equal(TimeSpan.FromSeconds(.5), op.Context.PageChangeDuration);
        await op.MoveAsync(1); Assert.Equal(expectedIndex, op.Position.Index); Assert.Equal(playing, op.SlideShow.IsPlaying);
        op.SlideShow.Stop(); Assert.Equal(Config.Current.View.PageMoveType, op.Context.PageChangeType);
    }
    [Fact]
    public async Task TerminalDialogSuspendsAndCloseUnloadDetachMediaWait()
    {
        using var f = new Fixture(); var state = new SaveData(f.State); await state.LoadAsync(Token);
        await using var op = f.Operation(state); await op.OpenAsync(f.Images, Token); await op.JumpAsync(4);
        Config.Current.SlideShow.PageEndAction = PageEndAction.Dialog; op.SlideShow.Play();
        op.PageEndDialogAsync = (_, _) => { Assert.True(op.SlideShow.IsPaused); return Task.FromResult(PageEndAction.Loop); };
        await op.MoveAsync(1); Assert.Equal(0, op.Position.Index); Assert.False(op.SlideShow.IsPaused); Assert.True(op.SlideShow.IsPlaying);
        await op.UnloadAsync(Token); Assert.False(op.SlideShow.IsPlaying);
    }
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task LegacyAliasesRetireWithoutResurrectingOrDroppingUnknownFields(bool loop)
    {
        using var f = new Fixture(); Directory.CreateDirectory(f.State);
        File.WriteAllText(Path.Combine(f.State, "UserSetting.json"), new JsonObject { ["Config"] = new JsonObject { ["SlideShow"] = new JsonObject { ["IsSlideShowByLoop"] = loop, ["IsCancelSlideByMouseMove"] = false, ["Future"] = 7 } } }.ToJsonString());
        var state = new SaveData(f.State); await state.LoadAsync(Token);
        Assert.Equal(loop ? PageEndAction.Loop : PageEndAction.None, Config.Current.SlideShow.PageEndAction);
        Assert.Equal(SlideShowTimerResetGesture.MouseMove, Config.Current.SlideShow.TimerResetGesture);
        Config.Current.SlideShow.TimerResetGesture = SlideShowTimerResetGesture.InputAction; Config.Current.SlideShow.PageEndAction = PageEndAction.Loop;
        await state.SaveAsync(null, Token); await new SaveData(f.State).LoadAsync(Token);
        Assert.Equal(PageEndAction.Loop, Config.Current.SlideShow.PageEndAction); Assert.Equal(SlideShowTimerResetGesture.InputAction, Config.Current.SlideShow.TimerResetGesture);
        var raw = JsonNode.Parse(File.ReadAllText(Path.Combine(f.State, "UserSetting.json")))!;
        Assert.Equal(7, raw["Config"]!["SlideShow"]!["Future"]!.GetValue<int>()); Assert.Null(raw["Config"]!["SlideShow"]!["IsSlideShowByLoop"]);
        Assert.NotNull(raw["MacImportedLegacyConfigFields"]!["SlideShow"]!["IsCancelSlideByMouseMove"]);
    }
    [Fact]
    public async Task DefaultsPrecisionAndSettingsFailurePreserveOriginalJson()
    {
        using var f = new Fixture(); var state = new SaveData(f.State); await state.LoadAsync(Token); await state.SaveAsync(null, Token);
        Assert.Null(JsonNode.Parse(File.ReadAllText(Path.Combine(f.State, "UserSetting.json")))!["Config"]?["SlideShow"]);
        await using var op = f.Operation(state); var blocked = Path.Combine(f.State, "UserSetting.json.tmp"); Directory.CreateDirectory(blocked);
        try
        {
            await Assert.ThrowsAnyAsync<Exception>(() => op.ApplyOptionsAsync(() => { Config.Current.SlideShow.SlideShowInterval = .123456; Config.Current.SlideShow.IsWaitAnimation = true; Config.Current.StartUp.IsAutoPlaySlideShow = true; }, (-1, TimeSpan.Zero)));
            Assert.Equal(5, Config.Current.SlideShow.SlideShowInterval); Assert.False(Config.Current.SlideShow.IsWaitAnimation); Assert.False(Config.Current.StartUp.IsAutoPlaySlideShow);
        }
        finally { Directory.Delete(blocked); }
        await op.ApplyOptionsAsync(() => Config.Current.SlideShow.SlideShowInterval = .123456, (-1, TimeSpan.Zero)); await new SaveData(f.State).LoadAsync(Token);
        Assert.Equal(.12346, Config.Current.SlideShow.SlideShowInterval);
    }
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FormalMenuTimerAndReaderFollowDirectoryAndZip(bool zip)
    {
        using var f = new Fixture(); var state = new SaveData(f.State); await state.LoadAsync(Token);
        var op = f.Operation(state); var factory = new BitmapFactory(new MagickImageDecoder()); var window = new MainWindow(); window.Bind(new(op, new(op), state), factory, new Platform()); window.Show();
        try
        {
            await window.OpenAsync(zip ? f.Zip : f.Images); await window.Viewer.RefreshAsync(); Config.Current.SlideShow.SlideShowInterval = .15; Config.Current.SlideShow.IsTimerVisible = true; Config.Current.SlideShow.PageMoveDuration = 0;
            await window.ExecuteAsync("ToggleSlideShow", true); Assert.True(op.SlideShow.IsPlaying); Assert.True(window.FindControl<SimpleProgressBar>("SlideShowTimer")!.IsVisible);
            await Wait(() => op.Position.Index >= 1); await window.ExecuteAsync("ToggleSlideShow", true); Assert.False(op.SlideShow.IsPlaying);
            var position = op.Position; await Task.Delay(250, Token); Dispatcher.UIThread.RunJobs(); Assert.Equal(position, op.Position);
            await window.Viewer.RefreshAsync();
            if (Environment.GetEnvironmentVariable("NEEVIEW_P5_SLIDESHOW_SCREENSHOT") is { } path) { using var image = window.CaptureRenderedFrame(); image!.Save(path, Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default); }
            Config.Current.SlideShow.SlideShowInterval = 5; await window.Viewer.ZoomAsync(3); window.StartSlideShow();
            Assert.True(window.Viewer.IsMotionActive); window.Viewer.CancelSlideShowScroll(); Assert.False(window.Viewer.IsMotionActive);
            if (!zip && Environment.GetEnvironmentVariable("NEEVIEW_P5_SLIDESHOW_PROGRESS_SCREENSHOT") is { } progressPath)
            {
                await window.TickSlideShowAsync(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
                var timer = window.FindControl<SimpleProgressBar>("SlideShowTimer")!;
                Assert.True(timer.IsVisible); Assert.NotNull(timer.Foreground); Assert.InRange(timer.Value, .1, 1); Assert.Equal(4, timer.Bounds.Height);
                await Task.Delay(60, Token); Dispatcher.UIThread.RunJobs();
                using var image = window.CaptureRenderedFrame(); image!.Save(progressPath, Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
            }
        }
        finally { await window.PrepareShutdownAsync(); window.Close(); }
        Assert.False(op.SlideShow.IsPlaying); await Wait(() => factory.ByteCount == 0);
    }
    [AvaloniaFact]
    public async Task FailedShutdownRestoresPlayingAndAutoScrollThenSuccessfulRetryReleases()
    {
        using var f = new Fixture(); var state = new SaveData(f.State); await state.LoadAsync(Token);
        var op = f.Operation(state); var factory = new BitmapFactory(new MagickImageDecoder()); var window = new MainWindow();
        window.Bind(new(op, new(op), state), factory, new Platform()); window.Show();
        var blocked = Path.Combine(f.State, "UserSetting.json.tmp");
        try
        {
            await window.OpenAsync(f.Images); await window.Viewer.RefreshAsync(); await window.Viewer.ZoomAsync(3);
            window.StartSlideShow(); Assert.True(window.Viewer.IsMotionActive); Directory.CreateDirectory(blocked);
            await Assert.ThrowsAnyAsync<Exception>(() => window.PrepareShutdownAsync());
            Assert.True(op.SlideShow.IsPlaying); Assert.True(window.Viewer.IsMotionActive);
            Assert.True(window.IsVisible); Assert.NotNull(op.Book);
            Directory.Delete(blocked); await window.PrepareShutdownAsync(); Assert.False(op.SlideShow.IsPlaying);
        }
        finally
        {
            if (Directory.Exists(blocked)) Directory.Delete(blocked);
            await window.PrepareShutdownAsync(); window.Close();
        }
        await Wait(() => factory.ByteCount == 0);
    }
    [AvaloniaFact]
    public async Task FormalSettingsDraftCancelSaveSearchAndFixedToggleAreIndependent()
    {
        using var f = new Fixture(); var state = new SaveData(f.State); await state.LoadAsync(Token);
        var op = f.Operation(state); var window = new MainWindow(); window.Bind(new(op, new(op), state), new(new MagickImageDecoder()), new Platform()); window.Show();
        try
        {
            var settings = new SettingsWindow(new(op, new(op), state)); settings.Show(window); settings.FindControl<ListBox>("SettingsNavigation")!.SelectedIndex = 10;
            settings.FindControl<NumericUpDown>("SlideShowInterval")!.Value = 2; settings.Close(); Assert.Equal(5, Config.Current.SlideShow.SlideShowInterval);
            settings = new SettingsWindow(new(op, new(op), state)); settings.Show(window); settings.FindControl<ListBox>("SettingsNavigation")!.SelectedIndex = 10;
            settings.FindControl<NumericUpDown>("SlideShowInterval")!.Value = 2; settings.FindControl<CheckBox>("SlideShowWaitAnimation")!.IsChecked = true;
            if (Environment.GetEnvironmentVariable("NEEVIEW_P5_SLIDESHOW_SETTINGS_SCREENSHOT") is { } path) { Dispatcher.UIThread.RunJobs(); settings.UpdateLayout(); using var image = settings.CaptureRenderedFrame(); image!.Save(path, Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default); }
            settings.FindControl<Button>("SaveSettings")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Wait(() => !settings.IsVisible);
            Assert.True(settings.WasSaved); Assert.Equal(2, Config.Current.SlideShow.SlideShowInterval); Assert.True(Config.Current.SlideShow.IsWaitAnimation);
            state.SetCommandParameter("ToggleSlideShow", new ToggleCommandParameter { ToggleMode = ToggleMode.Off });
            await window.ExecuteAsync("ToggleSlideShow", true); Assert.True(op.SlideShow.IsPlaying);
            await window.ExecuteAsync("ToggleSlideShow"); Assert.False(op.SlideShow.IsPlaying);
            await new SaveData(f.State).LoadAsync(Token); Assert.Equal(2, Config.Current.SlideShow.SlideShowInterval);
        }
        finally { await window.PrepareShutdownAsync(); window.Close(); }
    }
    private static async Task Wait(Func<bool> done)
    { for (int i = 0; i < 400 && !done(); i++) { Dispatcher.UIThread.RunJobs(); await Task.Delay(10, Token); } Assert.True(done()); }
    private sealed class Platform : IPlatformService
    { public Task RevealAsync(string path, CancellationToken token = default) => Task.CompletedTask; public Task TrashAsync(string path, CancellationToken token = default) => Task.CompletedTask; }
}
