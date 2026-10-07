// Copyright (c) NeeLaboratory. 原 SlideShow/SlideShowInput 的定时与等待规则，MIT 许可。
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
namespace NeeView;

/// <summary>原幻灯控制；单调时钟及可等待的命令替换 Timer/AppDispatcher，不持有窗口或图像。</summary>
public sealed class SlideShow : ObservableObject, IDisposable
{
    private readonly BookOperation _operation;
    private readonly Func<double> _clock;
    private bool _playing, _paused, _disposed, _ticking, _mediaReady;
    private double _start, _cycleStart, _due, _interval;
    private long _count, _epoch;
    private Book? _book, _displayBook;
    private PageRange? _displayRange;
    private IMediaPlayer? _waitingPlayer;
    public event EventHandler<SlideShowPlayedEventArgs>? Played;
    /// <param name="operation">唯一阅读控制，所有导航仍走其命令入口。</param>
    /// <param name="clock">单调秒数；测试可注入，不使用墙上时间。</param>
    public SlideShow(BookOperation operation, Func<double>? clock = null)
    {
        _operation = operation; _book = operation.Book;
        _clock = clock ?? (() => (double)Stopwatch.GetTimestamp() / Stopwatch.Frequency);
        operation.Changed += BookChanged;
    }
    public bool IsPlaying => _playing;
    public bool IsPaused => _paused;
    public bool IsPlayingAutoScroll => _playing && Config.Current.SlideShow.IsAutoScroll;
    public bool IsWaitingAnimation => _waitingPlayer is not null && !_mediaReady;
    public double Interval => _playing ? _interval * 1000 : Config.Current.SlideShow.SlideShowInterval * 1000;
    public double Progress => !_playing || _paused ? 0 : Math.Clamp(1 - (_clock() - _cycleStart) / _interval, 0, 1);

    /// <summary>原启动/停止不写JSON；再次启动清除临时挂起，停止撤销媒体等待。</summary>
    public void SetPlaying(bool value)
    {
        if (_disposed) return;
        if (value) _paused = false;
        if (_playing == value) return;
        _playing = value; _epoch++; ResetWaitAnimation();
        if (value) ResetTimerInner();
        OnPropertyChanged(nameof(IsPlaying)); OnPropertyChanged(nameof(IsPlayingAutoScroll)); Publish();
    }
    public void Play() => SetPlaying(true);
    public void Stop() => SetPlaying(false);
    /// <summary>原页尾对话框临时挂起，保留播放意图；恢复重新计时。</summary>
    public void Suspend() { if (_disposed) return; _paused = true; _epoch++; Publish(); }
    public void Resume() { if (_disposed) return; ResetTimer(); _paused = false; Publish(); }
    public void ResetTimer()
    {
        if (_disposed) return;
        _epoch++; ResetWaitAnimation();
        if (_playing) { ResetTimerInner(); Publish(); }
    }
    /// <summary>沿原输入优先级；优先时间模式不因键鼠操作重置。</summary>
    public void NotifyInput(SlideShowTimerResetGesture gesture)
    {
        var config = Config.Current.SlideShow;
        if (_playing && !config.IsPrioritizeTime && config.TimerResetGesture >= gesture) ResetTimer();
    }
    /// <summary>已显示的原页框变化才重置；同页资源刷新、seek或设置回显不重复延后。</summary>
    /// <returns>是否是新显示页框，供表现端启动原自动滚动。</returns>
    public bool NotifyDisplayed()
    {
        var book = _operation.Book; var range = _operation.Frame?.FrameRange;
        if (ReferenceEquals(book, _displayBook) && range == _displayRange) return false;
        _displayBook = book; _displayRange = range;
        if (_playing && !Config.Current.SlideShow.IsPrioritizeTime) ResetTimer();
        return true;
    }
    private void BookChanged(object? sender, EventArgs args)
    {
        var book = _operation.Book;
        if (book is null && !_operation.IsLoading) { _book = null; Stop(); return; }
        if (ReferenceEquals(book, _book)) return;
        _book = book;
        if (_playing && !Config.Current.SlideShow.IsPrioritizeTime) ResetTimer();
    }
    private void ResetTimerInner()
    { _start = _clock(); _count = 1; UpdateTimerInterval(); }
    private void UpdateTimerInterval()
    {
        var now = _clock(); var interval = Config.Current.SlideShow.SlideShowInterval;
        _interval = Config.Current.SlideShow.IsPrioritizeTime ? Math.Max(_start + _count * interval - now, .001) : interval;
        _cycleStart = now; _due = now + _interval;
    }
    /// <summary>每次回报最多执行一个原命令；异步未完成时不重入，不批量追赶页面。</summary>
    /// <param name="isKnown">原235命令表的存在判断，未知命令按原规则回退NextPage。</param>
    /// <param name="canExecute">原宿主实时能力及输入模式判断。</param>
    /// <param name="execute">唯一可等待的命令路由。</param>
    public async Task TickAsync(Func<string, bool> isKnown, Func<string, bool> canExecute, Func<string, Task> execute)
    {
        if (_disposed || !_playing || _paused || _ticking || _operation.IsLoading || _operation.Book is null) return;
        if (_waitingPlayer is { } waiting)
        {
            if (waiting.IsDisposed || !ReferenceEquals(waiting, _operation.CurrentMediaPlayer)) { ResetTimer(); return; }
            if (!_mediaReady) return;
            ResetWaitAnimation();
        }
        else
        {
            if (_clock() < _due) return;
            if (_operation.CurrentMediaPlayer is { IsDisposed: false, EndOfStreamCount: 0 } player
                && Config.Current.SlideShow.IsWaitAnimation && !Config.Current.SlideShow.IsPrioritizeTime)
            {
                _waitingPlayer = player; player.MediaEndOfStreamReached += MediaEndOfStream; return;
            }
        }
        var config = Config.Current.SlideShow; var command = config.NextPageCommandName;
        if (!isKnown(command)) { config.ResetNextPageCommandName(); command = config.NextPageCommandName; }
        var epoch = _epoch; _ticking = true;
        try
        {
            if (canExecute(command)) await execute(command);
            if (_disposed || !_playing || _paused || epoch != _epoch) return;
            _count++; UpdateTimerInterval(); Publish();
        }
        finally { _ticking = false; }
    }
    private void MediaEndOfStream(object? sender, EventArgs args)
    { if (ReferenceEquals(sender, _waitingPlayer) && !_disposed) _mediaReady = true; }
    private void ResetWaitAnimation()
    {
        if (_waitingPlayer is { } player) player.MediaEndOfStreamReached -= MediaEndOfStream;
        _waitingPlayer = null; _mediaReady = false;
    }
    private void Publish() => Played?.Invoke(this, new(_playing && !_paused, _playing && !_paused ? Interval : 0));
    /// <summary>窗口真正关闭后退订唯一阅读源和媒体事件，晚到命令不能重启计时。</summary>
    public void Dispose()
    { if (_disposed) return; Stop(); _disposed = true; _operation.Changed -= BookChanged; ResetWaitAnimation(); Played = null; }
}
public sealed class SlideShowPlayedEventArgs(bool isPlaying, double intervalMilliseconds) : EventArgs
{
    public bool IsPlaying => isPlaying;
    public double IntervalMilliseconds => intervalMilliseconds;
}
