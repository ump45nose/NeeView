using System.ComponentModel;
using Avalonia.Threading;
namespace NeeView.MacOS.Views;
public sealed partial class ReaderView
{
    private sealed class Video(Page page,IVideoPlayer player,DecodeRequest request,PropertyChangedEventHandler notify)
    {
        public Page Page {get;}=page;
        public IVideoPlayer Player {get;}=player;
        public DecodeRequest Request {get;set;}=request;
        public long Length {get;}=page.ArchiveEntry.Length;
        public DateTime Version {get;}=page.ArchiveEntry.LastWriteTime;
        public CancellationTokenSource Cancellation {get;}=new();
        public CancellationToken Token => _token ??= Cancellation.Token;
        private CancellationToken? _token;
        public bool Started {get;set;}
        private readonly PropertyChangedEventHandler _notify=notify;
        public bool Retired {get;private set;}
        private Task? _close;
        public bool Closed=>_close?.IsCompletedSuccessfully==true;
        public Task CloseAsync()
        {
            if(!Retired){Retired=true;Cancellation.Cancel();Player.PropertyChanged-=_notify;}
            if(_close is null||_close.IsFaulted||_close.IsCanceled)_close=ClosePlayerAsync();return _close;
        }
        private async Task ClosePlayerAsync(){await Player.DisposeAsync();Cancellation.Dispose();}
    }
    private readonly Dictionary<Page,Video> _videos=[];
    private readonly List<Video> _videoClosing=[];
    internal int VideoCount=>_videos.Count;
    private void RetireVideo(Video video)
    {
        var closing=video.CloseAsync();_videoClosing.Add(video);
        _=ObserveVideoCloseAsync(closing);
    }
    private static async Task ObserveVideoCloseAsync(Task closing)
    {try{await closing;}catch(Exception error){System.Diagnostics.Trace.WriteLine(error);}}
    private void ReconcileVideos(IReadOnlyList<Page> pages)
    {
        foreach(var page in _videos.Keys.Where(p=>!pages.Contains(p)||!p.IsVideo).ToArray())
        {RetireVideo(_videos[page]);_videos.Remove(page);}
        _videoClosing.RemoveAll(video=>video.Closed);
    }
    /// <summary>唯一查看器拥有视频来源；规格变化保持时钟和seek，切书晚到结果只释放。</summary>
    private async Task EnsureVideoAsync(Page page,DecodeRequest request,int revision,CancellationToken token)
    {
        if(_operation?.VideoPlayers is not {} factory||_factory is null)
        {_pageErrors[page]="系统视频后端尚未装配。";return;}
        if(_videos.TryGetValue(page,out var existing))
        {
            if(existing.Length==page.ArchiveEntry.Length&&existing.Version==page.ArchiveEntry.LastWriteTime)
            {existing.Request=request;ApplyVideoOptions(existing);await EnsureVideoFrameAsync(existing,token);return;}
            RetireVideo(existing);_videos.Remove(page);
        }
        IVideoPlayer? player=null;Video? candidate=null;
        try
        {
            player=await factory.OpenAsync(page.ArchiveEntry,request,Math.Min(256L*1024*1024,_factory.Budget),token);
            if(_disposed||revision!=_revision||token.IsCancellationRequested)return;
            candidate=new(page,player,request,MediaPlayerChanged);player=null;
            _=candidate.Token;
            // 先建立原默认播放意图，启动延迟只禁用实际时钟，期间的用户暂停优先。
            candidate.Player.IsEnabled=false;candidate.Player.Play();
            candidate.Player.PropertyChanged+=MediaPlayerChanged;ApplyVideoOptions(candidate);
            _videos[page]=candidate;var video=candidate;candidate=null;
            SelectCurrentMedia();_ = StartVideoAsync(video);
            try
            {
                await EnsureVideoFrameAsync(video,token);
                if(_disposed||revision!=_revision||token.IsCancellationRequested)return;
                if(video.Player.HasVideo&&!_images.ContainsKey(page))throw new TimeoutException("系统播放器首帧输出超时。");
                if(!video.Player.HasVideo)_pageErrors[page]="音频媒体";
                SelectCurrentMedia();StartMediaTimer();
            }
            catch
            {
                // 同一页的新显示代次可以继续使用已准备的播放器，旧首帧取消不能将它退役。
                if(revision==_revision&&ReferenceEquals(_videos.GetValueOrDefault(page),video)){_videos.Remove(page);RetireVideo(video);}
                throw;
            }
        }
        catch(OperationCanceledException){throw;}
        catch(Exception error){if(!_disposed&&revision==_revision&&!token.IsCancellationRequested)_pageErrors[page]=error.Message;}
        finally{if(candidate is not null)await candidate.CloseAsync();if(player is not null)await player.DisposeAsync();}
    }
    private async Task EnsureVideoFrameAsync(Video video,CancellationToken token)
    {
        await RefreshVideoFrameAsync(video,token);
        // 真正AVPlayer输出暂无首帧时有界等待，不模拟时钟；已有帧仍供规格更新期间显示。
        for(int i=0;i<200&&video.Player.HasVideo&&!_images.ContainsKey(video.Page);i++)
        {await Task.Delay(10,token);await RefreshVideoFrameAsync(video,token);}
    }
    private void ApplyVideoOptions(Video video)
    {
        video.Player.IsRepeat=video.Page.ArchiveEntry.TargetArchiveEntry.Archive is MediaArchive
            ?Config.Current.Archive.Media.IsRepeat:Config.Current.Image.IsMediaRepeat;
        video.Player.IsMuted=Config.Current.Archive.Media.IsMuted;video.Player.Volume=Config.Current.Archive.Media.Volume;
        video.Player.IsEnabled=video.Started&&IsVideoVisible(video.Page);
        video.Player.IsAudioEnabled=ReferenceEquals(video.Page,_frame?.Elements.FirstOrDefault(e=>!e.IsDummy)?.Page);
    }
    private bool IsVideoVisible(Page page)
    {
        var viewport=new Avalonia.Rect(Bounds.Size);
        if(_panorama is not null)return _panorama.Frames.Any(frame=>ReaderTransformPresenter.GetTargets(frame.Frame)
            .Any(target=>ReferenceEquals(target.Source.Page,page)&&target.Target.TransformToAABB(PanoramaMatrix(frame)).Intersects(viewport)));
        return _transform.GetTargets().Any(target=>ReferenceEquals(target.Source.Page,page)&&target.Target.TransformToAABB(GetRenderedMatrix()).Intersects(viewport));
    }
    private async Task StartVideoAsync(Video video)
    {
        var token=video.Token;
        try
        {
            double seconds=video.Page.ArchiveEntry.TargetArchiveEntry.Archive is MediaArchive?Config.Current.Archive.Media.MediaStartDelaySeconds:.02;
            await Task.Delay(TimeSpan.FromSeconds(Math.Clamp(seconds,0,3600)),token);
            if(_disposed||video.Retired||!ReferenceEquals(_videos.GetValueOrDefault(video.Page),video))return;
            video.Started=true;ApplyVideoOptions(video);await video.Player.SynchronizeAsync(token);StartMediaTimer();
        }
        catch(OperationCanceledException){}
        catch(Exception error){if(!_disposed&&!video.Retired){_pageErrors[video.Page]=error.Message;InvalidateVisual();}}
    }
    private async Task RefreshVideoFrameAsync(Video video,CancellationToken token)
    {
        if(_factory is null||video.Retired)return;
        var request=video.Request;var pixels=await video.Player.ReadFrameAsync(request,token);if(pixels is null)return;
        if(_disposed||video.Retired||video.Request!=request||!ReferenceEquals(_videos.GetValueOrDefault(video.Page),video)||token.IsCancellationRequested){pixels.Dispose();return;}
        var display=CreateAnimationDisplay(video.Page,request,_factory.RentMediaFrame(pixels));
        if(_images.Remove(video.Page,out var old))old.Dispose();_images[video.Page]=display;_pageErrors.Remove(video.Page);InvalidateVisual();
    }
    private async Task UpdateVideoFramesAsync()
    {
        foreach(var video in _videos.Values.ToArray())
        {
            if(video.Retired)continue;
            ApplyVideoOptions(video);
            try{await RefreshVideoFrameAsync(video,video.Token);}
            catch(OperationCanceledException){}
            catch(Exception error)
            {if(!_disposed&&!video.Retired){video.Player.Pause();_pageErrors[video.Page]=error.Message;InvalidateVisual();MediaChanged?.Invoke(this,EventArgs.Empty);}}
        }
    }
    private void ClearVideos()
    {foreach(var video in _videos.Values)RetireVideo(video);_videos.Clear();}
    /// <summary>正式关闭在保存成功后等待全部原生播放器/临时材料释放。</summary>
    public async Task CloseMediaAsync()
    {ClearAnimations();await Task.WhenAll(_videoClosing.Select(video=>video.CloseAsync()));_videoClosing.Clear();}
}
