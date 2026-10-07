// Copyright (c) NeeView. Original IMediaPlayer contract adapted to platform-free values, MIT license.
using System.ComponentModel;
namespace NeeView;

/// <summary>原媒体控制的公共边界；播放器由查看器拥有，业务及媒体条只借用。</summary>
public interface IMediaPlayer : INotifyPropertyChanged, IDisposable
{
    bool IsDisposed { get; }
    bool HasAudio { get; }
    bool HasVideo { get; }
    bool IsEnabled { get; set; }
    bool IsAudioEnabled { get; set; }
    bool IsMuted { get; set; }
    bool IsRepeat { get; set; }
    bool IsPlaying { get; }
    TimeSpan Duration { get; }
    double Position { get; set; }
    bool ScrubbingEnabled { get; }
    double Volume { get; set; }
    bool RateEnabled { get; }
    double Rate { get; set; }
    int EndOfStreamCount { get; }
    event EventHandler? MediaPlayed;
    event EventHandler? MediaEnded;
    event EventHandler? MediaEndOfStreamReached;
    void Play();
    void Pause();
    void TogglePlay();
    bool AddPosition(TimeSpan span);
}

/// <summary>探测视频元数据，不持有播放器或窗口；尺寸已经应用方向及像素宽比。</summary>
public sealed record VideoInfo(Size Size, TimeSpan Duration, bool HasAudio, bool HasVideo)
{public Size AspectSize {get;init;}=Size;}

/// <summary>系统播放器替换点；归档实体化使用原请求租约，不更改逻辑路径。</summary>
public interface IVideoPlayerFactory
{
    /// <summary>只读探测；返回前释放原生元数据及临时材料。</summary>
    Task<VideoInfo> ProbeAsync(ArchiveEntry entry, CancellationToken token);
    /// <summary>打开真实播放时钟及音频；输出规格与原生保守工作预算必须有界。</summary>
    Task<IVideoPlayer> OpenAsync(ArchiveEntry entry, DecodeRequest request, long workingBudget, CancellationToken token);
}

/// <summary>原生播放与当前帧统一的资源所有者；取消后晚到帧只释放。</summary>
public interface IVideoPlayer : IMediaPlayer, IAsyncDisposable
{
    VideoInfo Info { get; }
    /// <summary>同步待处理的seek和播放意图；原生回调更新位置/EOS，不能用壁钟模拟播放。</summary>
    Task SynchronizeAsync(CancellationToken token);
    /// <summary>读取真实当前帧；尚无新帧返回空，调用方拥有BGRA8预乘输出。</summary>
    Task<DecodedImageLease?> ReadFrameAsync(DecodeRequest request, CancellationToken token);
}
