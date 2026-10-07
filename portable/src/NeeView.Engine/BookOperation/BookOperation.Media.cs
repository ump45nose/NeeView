namespace NeeView;
public sealed partial class BookOperation
{
    /// <summary>表现端借给原命令业务的当前播放器；所有者仍是唯一查看器。</summary>
    public IMediaPlayer? CurrentMediaPlayer { get; set; }
    public IVideoPlayerFactory? VideoPlayers { get; private set; }
    /// <summary>启动处装配唯一视频替换点；前端只使用这个Engine契约。</summary>
    public void AttachVideoPlayers(IVideoPlayerFactory factory) => VideoPlayers = factory;
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
    /// <summary>原MediaPageMoveControl：翻页按秒偏移，越过首尾才执行原书尾动作。</summary>
    private async Task<bool> TryMoveMediaAsync(int delta)
    {
        Book? book=null;long generation=0;PagePosition position=default;bool terminated=false;
        await _gate.WaitAsync();
        try
        {
            if(Book?.IsMedia!=true)return false;
            if(_disposed||_closing||IsLoading||CurrentMediaPlayer is not {IsDisposed:false} player)return true;
            book=Book;generation=_generation;position=Position;
            terminated=player.AddPosition(TimeSpan.FromSeconds(delta*Config.Current.Archive.Media.PageSeconds));Notify();
        }
        finally{_gate.Release();}
        if(terminated&&book is not null)await HandlePageEndAsync(book,generation,position,Math.Sign(delta));return true;
    }
    /// <summary>原媒体首尾跳转改变同一播放器时间；普通书仍使用原索引定位。</summary>
    public async Task MoveToBoundaryAsync(bool last)
    {
        await _gate.WaitAsync();bool media;
        try{media=Book?.IsMedia==true;if(media&&!_disposed&&!_closing&&!IsLoading&&CurrentMediaPlayer is {IsDisposed:false} player){player.Position=last?1:0;Notify();}}
        finally{_gate.Release();}
        if(!media)await JumpAsync(last?(Book?.Pages.Count??1)-1:0,last);
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
            if (Book?.IsMedia != true && CurrentMediaPlayer is { IsDisposed: false } player) player.IsRepeat = repeat;
        }
        finally { _gate.Release(); }
    }
    /// <summary>媒体书循环使用Archive.Media，普通媒体页/动图继续使用Image.IsMediaRepeat。</summary>
    public async Task SetMediaRepeatAsync(bool repeat)
    {
        await _gate.WaitAsync();
        try
        {
            if(_disposed||_closing||IsLoading)throw new InvalidOperationException("当前不能修改媒体设置。");
            _saving?.Cancel();await saveData.SynchronizeWritesAsync();
            // 与切书共用导航锁，配置分支不能由排队前的书籍决定。
            var media=Book?.IsMedia==true;
            var previous=media?Config.Current.Archive.Media.IsRepeat:Config.Current.Image.IsMediaRepeat;
            try{if(media)Config.Current.Archive.Media.IsRepeat=repeat;else Config.Current.Image.IsMediaRepeat=repeat;await SaveConfigurationAsync();}
            catch{if(media)Config.Current.Archive.Media.IsRepeat=previous;else Config.Current.Image.IsMediaRepeat=previous;throw;}
            if(CurrentMediaPlayer is {IsDisposed:false} player)player.IsRepeat=repeat;
        }
        finally{_gate.Release();}
    }
    /// <summary>原视频音量/静音与图像循环分离；保存失败不改变当前播放器。</summary>
    public async Task SetMediaAudioAsync(bool muted,double volume)
        =>await SetMediaAudioAsync(null,muted,volume);
    /// <summary>排队的媒体条编辑绑定实际播放器，切书后不修改新媒体的配置。</summary>
    public async Task SetMediaAudioAsync(IMediaPlayer? expectedPlayer,bool muted,double volume)
    {
        if(!double.IsFinite(volume))return;await _gate.WaitAsync();
        try
        {
            if(_disposed||_closing||IsLoading)throw new InvalidOperationException("当前不能修改媒体设置。");
            if(expectedPlayer is not null&&!ReferenceEquals(expectedPlayer,CurrentMediaPlayer))return;
            var config=Config.Current.Archive.Media;var old=(config.IsMuted,config.Volume);
            try{config.IsMuted=muted;config.Volume=Math.Clamp(volume,0,1);await SaveConfigurationAsync();}
            catch{config.IsMuted=old.IsMuted;config.Volume=old.Volume;throw;}
            if(CurrentMediaPlayer is {IsDisposed:false} player){player.IsMuted=muted;player.Volume=config.Volume;}
        }
        finally{_gate.Release();}
    }
}
