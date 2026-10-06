using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
namespace NeeView.MacOS.Views;
public sealed partial class MainWindow
{
    private readonly DispatcherTimer _slideShowTimer = new() { Interval = TimeSpan.FromMilliseconds(40) };
    private Task _slideShowTick = Task.CompletedTask;
    private bool _resumeSlideShowOnCloseFailure;
    /// <summary>时钟、进度条和输入反馈是表现；导航与媒体等待由原SlideShow控制。</summary>
    private void InitializeSlideShow()
    {
        var show = _model!.Operation.SlideShow;
        show.Played += SlideShow_Played; Viewer.DisplayCompleted += SlideShow_Displayed;
        _slideShowTimer.Tick += (_, _) => { if (_slideShowTick.IsCompleted) _slideShowTick = TickSlideShowAsync(); };
        AddHandler(PointerPressedEvent, (_, _) => NotifySlideShowInput(SlideShowTimerResetGesture.InputAction, true), RoutingStrategies.Tunnel, true);
        AddHandler(PointerWheelChangedEvent, (_, _) => NotifySlideShowInput(SlideShowTimerResetGesture.InputAction, true), RoutingStrategies.Tunnel, true);
        AddHandler(PointerMovedEvent, (_, _) => NotifySlideShowInput(SlideShowTimerResetGesture.MouseMove, false), RoutingStrategies.Tunnel, true);
    }
    private void NotifySlideShowInput(SlideShowTimerResetGesture gesture, bool cancelScroll)
    {
        if (_preparing || _closedPrepared || _model?.Operation.SlideShow.IsPlaying != true) return;
        _model.Operation.SlideShow.NotifyInput(gesture);
        if (cancelScroll) Viewer.CancelSlideShowScroll();
    }
    private void SlideShow_Played(object? sender, SlideShowPlayedEventArgs e)
    {
        if (!Dispatcher.UIThread.CheckAccess()) { Dispatcher.UIThread.Post(() => SlideShow_Played(sender, e)); return; }
        if (_preparing || _closedPrepared || _model?.Operation.SlideShow is not { IsPlaying: true, IsPaused: false }) { _slideShowTimer.Stop(); Viewer.CancelSlideShowScroll(); }
        else _slideShowTimer.Start();
        UpdateSlideShowPresentation(true);
    }
    private void SlideShow_Displayed(object? sender, EventArgs args)
    {
        if (_preparing || _closedPrepared || _model is null) return;
        var show = _model.Operation.SlideShow;
        if (show.NotifyDisplayed()) BeginSlideShowAutoScroll();
    }
    private void UpdateSlideShowPresentation(bool refreshMenu = false)
    {
        var show = _model?.Operation.SlideShow; var progress = this.FindControl<SimpleProgressBar>("SlideShowTimer")!;
        progress.IsVisible = show?.IsPlaying == true && Config.Current.SlideShow.IsTimerVisible;
        progress.Value = show?.Progress ?? 0;
        if (refreshMenu && !_preparing && !_closedPrepared)
            MenuPresenter.RefreshChecks(this.FindControl<Menu>("MenuBar")!, GetCommandCheck);
    }
    /// <summary>原FirstLoader启动选项入口，不由普通打开/切书重复自动启动。</summary>
    public void StartSlideShow()
    {
        if (_preparing || _closedPrepared || _model is null) return;
        _model.Operation.SlideShow.Play();
        BeginSlideShowAutoScroll();
    }
    /// <summary>沿原页框上下文读取滚动时长，损坏配置不会在窗口层溢出。</summary>
    private void BeginSlideShowAutoScroll()
    {
        if (_model?.Operation.Context is { IsAutoScroll: true } context)
            Viewer.BeginSlideShowScroll(context.AutoScrollDuration);
    }
    /// <summary>单个在途回报，使用同一宿主路由；不抢焦点、不批量补翻页。</summary>
    internal async Task TickSlideShowAsync()
    {
        if (_preparing || _closedPrepared || _model is null) return;
        try
        {
            await _model.Operation.SlideShow.TickAsync(DefaultInputScheme.IsKnownCommand, name =>
            {
                if (!IsCommandImplemented(name)) throw new NotSupportedException($"幻灯片命令 {name} 尚未迁移，请在设置中选择已支持的命令。");
                return IsCommandAvailable(name);
            }, name => ExecuteAsync(name, true));
        }
        catch (Exception ex) { _model.Operation.SlideShow.Stop(); ShowError("幻灯播放失败：" + ex.Message); }
        UpdateSlideShowPresentation();
    }
    private void StopSlideShowForClose()
    {
        _resumeSlideShowOnCloseFailure = _model?.Operation.SlideShow.IsPlaying == true;
        _model?.Operation.SlideShow.Stop(); _slideShowTimer.Stop();
    }
    private void CompleteSlideShowClose(bool success)
    {
        if (success)
        {
            if (_model is not null) _model.Operation.SlideShow.Played -= SlideShow_Played;
            Viewer.DisplayCompleted -= SlideShow_Displayed;
        }
        else if (_resumeSlideShowOnCloseFailure)
        {
            _model?.Operation.SlideShow.Play();
            BeginSlideShowAutoScroll();
        }
    }
}
