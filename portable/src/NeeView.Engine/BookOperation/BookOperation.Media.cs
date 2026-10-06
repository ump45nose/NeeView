namespace NeeView;
public sealed partial class BookOperation
{
    /// <summary>表现端借给原命令业务的当前播放器；所有者仍是唯一查看器。</summary>
    public AnimatedMediaPlayer? CurrentMediaPlayer { get; set; }
    public bool MediaExists() => CurrentMediaPlayer is { IsDisposed: false };
    public bool IsMediaPlaying() => CurrentMediaPlayer?.IsPlaying == true;
    /// <summary>保留原ToggleMediaPlay命令含义；没有播放器时不改变阅读位置。</summary>
    public void ToggleMediaPlay() => CurrentMediaPlayer?.TogglePlay();
    /// <summary>原Prev/NextMediaPosition的秒数入口，播放器负责归一位置及边界。</summary>
    public void MoveMediaPosition(double seconds)
    { if (double.IsFinite(seconds)) CurrentMediaPlayer?.AddPosition(TimeSpan.FromSeconds(seconds)); }

    /// <summary>原前后媒体命令各自保存 Delta；零值回退到 Archive.Media.PageSeconds。</summary>
    /// <param name="name">原命令标识。</param><param name="direction">前进为1，后退为-1。</param>
    public void MoveMediaPositionCommand(string name, int direction)
    {
        var delta = saveData.GetCommandParameter<MoveMediaPositionCommandParameter>(name).Delta;
        if (delta == 0) delta = Config.Current.Archive.Media.PageSeconds;
        MoveMediaPosition(direction * delta);
    }

    /// <summary>原动图循环配置经唯一导航锁和JSON事务保存；失败保持播放器及配置原值。</summary>
    /// <param name="repeat">用户选择的循环状态，不影响视频配置。</param>
    public async Task SetImageMediaRepeatAsync(bool repeat)
    {
        await _gate.WaitAsync();
        try
        {
            if (_disposed || _closing || IsLoading) throw new InvalidOperationException("当前不能修改媒体设置。");
            _saving?.Cancel(); await saveData.SynchronizeWritesAsync();
            var previous = Config.Current.Image.IsMediaRepeat;
            try { Config.Current.Image.IsMediaRepeat = repeat; await SaveConfigurationAsync(); }
            catch { Config.Current.Image.IsMediaRepeat = previous; throw; }
            if (CurrentMediaPlayer is { IsDisposed: false } player) player.IsRepeat = repeat;
        }
        finally { _gate.Release(); }
    }
}
