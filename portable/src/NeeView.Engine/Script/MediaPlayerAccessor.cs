// Copyright (c) NeeLaboratory. 原MediaPlayerAccessor，MIT。
namespace NeeView;
/// <summary>借用查看器当前真实播放器；不取得资源所有权。</summary>
public sealed class MediaPlayerAccessor(ScriptAccessContext context, IMediaPlayer player)
{
    public bool IsDisposed => context.Read(() => player.IsDisposed);
    public double Duration => context.Read(() => player.Duration.TotalSeconds);
    public object? AudioTrack => throw new NotSupportedException("Mac音轨选择尚未迁入。");
    public object? Subtitle => throw new NotSupportedException("Mac字幕轨选择尚未迁入。");
    public double Volume { get => context.Read(() => Config.Current.Archive.Media.Volume); set => context.Write(() => { Config.Current.Archive.Media.Volume = value; player.Volume = value; }); }
    public bool IsMuted { get => context.Read(() => Config.Current.Archive.Media.IsMuted); set => context.Write(() => { Config.Current.Archive.Media.IsMuted = value; player.IsMuted = value; }); }
    public bool IsRepeat { get => context.Read(() => player.IsRepeat); set => context.Write(() => player.IsRepeat = value); }
    public double Position { get => context.Read(() => player.Position); set => context.Write(() => player.Position = value); }
    public bool IsPlaying { get => context.Read(() => player.IsPlaying); set => context.Write(() => { if (value) player.Play(); else player.Pause(); }); }
    public double Rate { get => context.Read(() => player.Rate); set => context.Write(() => player.Rate = value); }
}
