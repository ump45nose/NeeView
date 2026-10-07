// Copyright (c) NeeView. MediaControlViewModel 的表现适配，MIT 许可。
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
namespace NeeView.MacOS.ViewModels;

/// <summary>原媒体条的独立表现状态；所有播放规则留在Engine，样式只消费这些值。</summary>
public sealed class MediaControlViewModel : ObservableObject, IDisposable
{
    private IMediaPlayer? _player;
    private bool _remaining;
    public IMediaPlayer? Player => _player;
    public bool HasAudio => _player?.HasAudio == true;
    public bool IsMuted => _player?.IsMuted == true;
    public string MuteText => IsMuted ? "静音：开" : "静音：关";
    public double Volume => _player?.Volume ?? .5;
    public bool RateEnabled => _player?.RateEnabled == true;
    public double Rate => _player?.Rate ?? 1;
    public IReadOnlyList<double> Rates { get; } = [.5,.75,1,1.5,2];
    public bool IsRefreshing {get;private set;}
    public bool Exists => _player is { IsDisposed: false };
    public string PlayText => _player?.IsPlaying == true ? "Ⅱ" : "▶";
    public string RepeatText => _player?.IsRepeat == true ? "循环：开" : "循环：关";
    public double Position => _player?.Position ?? 0;
    // 原动图以帧编号归一位置显示时间，不能将getter改为经过时长的比例。
    public string DisplayTime => _player is { } player
        ? $"{(_remaining ? "−" : "")}{Format(player.Duration * (_remaining ? 1 - Position : Position))} / {Format(player.Duration)}" : "";
    private static string Format(TimeSpan value) => value.TotalHours >= 1 ? value.ToString(@"h\:mm\:ss") : value.ToString(@"m\:ss\.ff");
    /// <summary>媒体切换先退订旧播放器，避免正文关页后仍被媒体条持有。</summary>
    public void Attach(IMediaPlayer? player)
    {
        if (ReferenceEquals(_player, player)) { Refresh(); return; }
        if (_player is not null) _player.PropertyChanged -= PlayerChanged;
        _player = player;
        if (_player is not null) _player.PropertyChanged += PlayerChanged;
        Refresh();
    }
    private void PlayerChanged(object? sender, PropertyChangedEventArgs args) => Refresh();
    private void Refresh()
    {
        IsRefreshing=true;
        try { foreach (var name in new[] { nameof(Exists), nameof(PlayText), nameof(RepeatText), nameof(Position), nameof(DisplayTime), nameof(HasAudio), nameof(IsMuted), nameof(MuteText), nameof(Volume), nameof(RateEnabled), nameof(Rate) }) OnPropertyChanged(name); }
        finally {IsRefreshing=false;}
    }
    /// <summary>只更改媒体条时间呈现，不写配置或改变播放位置。</summary>
    public void ToggleTimeFormat() { _remaining = !_remaining; OnPropertyChanged(nameof(DisplayTime)); }
    public void Dispose() => Attach(null);
}
