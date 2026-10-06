// Copyright (c) NeeView. AnimatedMediaPlayer/MediaPlayerOperator 纯播放业务适配，MIT 许可。
using CommunityToolkit.Mvvm.ComponentModel;
namespace NeeView;

/// <summary>保留原帧位置及播放控制；时钟、解码和界面由表现端拥有。</summary>
public sealed class AnimatedMediaPlayer : ObservableObject, IDisposable
{
    private readonly AnimatedImageInfo _info;
    private readonly long[] _starts;
    private bool _isEnabled = true, _isRepeat = true, _isPlaying, _disposed;
    private int _currentFrame;
    private long _elapsed;
    /// <summary>输入真实帧时长；至少两帧且每帧必须具有正时长。</summary>
    public AnimatedMediaPlayer(AnimatedImageInfo info)
    {
        ArgumentNullException.ThrowIfNull(info);
        if (info.FrameCount < 2 || info.FrameDurations.Any(t => t <= TimeSpan.Zero)) throw new ArgumentException("动画帧及时间无效。", nameof(info));
        _info = info; _starts = new long[info.FrameCount + 1];
        for (int i = 0; i < info.FrameCount; i++) _starts[i + 1] = checked(_starts[i] + info.FrameDurations[i].Ticks);
    }
    public event EventHandler? MediaPlayed;
    public event EventHandler? MediaEnded;
    public event EventHandler? MediaEndOfStreamReached;
    public AnimatedImageInfo Info => _info;
    public bool HasAudio => false;
    public bool HasVideo => true;
    public bool IsDisposed => _disposed;
    // 原IsEnabled只暂停实际推进，不丢弃用户的播放意图，重新启用后继续。
    public bool IsEnabled { get => _isEnabled; set { if (!_disposed) SetProperty(ref _isEnabled, value); } }
    public bool IsRepeat { get => _isRepeat; set { if (!_disposed) SetProperty(ref _isRepeat, value); } }
    public bool IsPlaying { get => _isPlaying; private set => SetProperty(ref _isPlaying, value); }
    public bool ScrubbingEnabled => true;
    public bool RateEnabled => false;
    public double Rate => 1;
    public TimeSpan Duration => TimeSpan.FromTicks(_starts[^1]);
    public int CurrentFrameIndex => _currentFrame;
    public int EndOfStreamCount { get; private set; }
    public double Position
    {
        get => (double)_currentFrame / (_info.FrameCount - 1);
        set
        {
            if (_disposed || !double.IsFinite(value)) return;
            SetFrame(Math.Min(_info.FrameCount - 1, (int)(_info.FrameCount * Math.Clamp(value, 0, 1))));
            _elapsed = _starts[_currentFrame];
        }
    }
    /// <summary>保留原Play意图，即使当前禁用也允许重新启用后播放。</summary>
    public void Play() { if (_disposed || IsPlaying) return; IsPlaying = true; MediaPlayed?.Invoke(this, EventArgs.Empty); }
    public void Pause() { if (!_disposed) IsPlaying = false; }
    public void TogglePlay() { if (IsPlaying) Pause(); else Play(); }
    /// <summary>按原MediaPlayerOperator以秒数/总时长增加归一帧位置，越界返回true。</summary>
    public bool AddPosition(TimeSpan span)
    {
        if (_disposed) return false;
        var position = Position + span.TotalSeconds / Duration.TotalSeconds;
        Position = Math.Clamp(position, 0, 1); return position < 0 || position > 1;
    }
    /// <summary>按真实时长推进；停顿后的完整周期折算一次，事件每次推进最多回报一次，计数保留跨越周期数。</summary>
    /// <param name="elapsed">表现时钟的非负间隔，禁用或暂停时不累计。</param>
    public void Advance(TimeSpan elapsed)
    {
        if (_disposed || !IsEnabled || !IsPlaying || elapsed <= TimeSpan.Zero) return;
        var duration = _starts[^1];
        var cycles = elapsed.Ticks / duration;
        var tail = elapsed.Ticks % duration;
        // 避免TimeSpan.MaxValue及极长周期相加溢出。
        var crosses = tail >= duration - _elapsed;
        var remainder = crosses ? tail - (duration - _elapsed) : _elapsed + tail;
        if (crosses) cycles++;
        if (cycles > 0)
        {
            EndOfStreamCount = (int)Math.Min(int.MaxValue, (long)EndOfStreamCount + (IsRepeat ? cycles : 1));
            _elapsed = IsRepeat ? remainder : _starts[^2];
            SetFrame(IsRepeat ? FindFrame(remainder) : _info.FrameCount - 1);
            if (!IsRepeat) IsPlaying = false;
            MediaEndOfStreamReached?.Invoke(this, EventArgs.Empty);
            if (!_disposed && !IsRepeat) MediaEnded?.Invoke(this, EventArgs.Empty);
        }
        else { _elapsed = remainder; SetFrame(FindFrame(remainder)); }
    }
    private int FindFrame(long ticks)
    { var i = Array.BinarySearch(_starts, ticks); return Math.Min(_info.FrameCount - 1, i >= 0 ? i : ~i - 1); }
    private void SetFrame(int frame)
    { if (_currentFrame == frame) return; _currentFrame = frame; OnPropertyChanged(nameof(CurrentFrameIndex)); OnPropertyChanged(nameof(Position)); }
    /// <summary>关闭后停止推进及事件，纯状态不持有任何原生资源。</summary>
    public void Dispose() { if (_disposed) return; IsPlaying = false; _disposed = true; MediaPlayed = null; MediaEnded = null; MediaEndOfStreamReached = null; }
}
