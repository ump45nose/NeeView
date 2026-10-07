using AppKit;
using AVFoundation;
using CoreGraphics;
using CoreMedia;
using CoreVideo;
using Foundation;
using CommunityToolkit.Mvvm.ComponentModel;
using NeeView;
using System.Runtime.InteropServices;
namespace NeeView.Backends;

/// <summary>官方AVFoundation替换原Windows媒体后端；实体化复用原租约及临时预算。</summary>
public sealed class MacVideoPlayerFactory(IArchiveEntryRealizer realizer) : IVideoPlayerFactory
{
    private readonly object _memory=new();
    private long _activeBytes;
    /// <summary>所有可见视频共用工作预算；退役后等待原生释放才归还，不能按页面数累加。</summary>
    private Action Reserve(long bytes,long limit)
    {
        lock(_memory){if(bytes>limit-_activeBytes)throw new NotSupportedException("当前视频原生工作缓冲超过共享预算。");_activeBytes+=bytes;}
        return ()=>{lock(_memory)_activeBytes-=bytes;};
    }
    /// <summary>只读元数据，结束后释放全部临时/原生资源，不改变阅读时间位置。</summary>
    public Task<VideoInfo> ProbeAsync(ArchiveEntry entry, CancellationToken token) => SourceIo.RunReadAsync(async cancellation =>
    {
        await using var files = await ArchiveEntryUtility.RealizeArchiveEntry([entry], ArchivePolicy.SendExtractFile, realizer, cancellation);
        return await VideoMain.RunAsync(async () =>
        {
            cancellation.ThrowIfCancellationRequested();
            using var url = NSUrl.FromFilename(files.Paths.Single()); using var asset = AVAsset.FromUrl(url);
            await LoadAsync(asset, cancellation); return (await InspectAsync(asset)).Info;
        });
    }, token);

    /// <summary>提交一个真正播放器；超时或取消的晚到播放器由原SourceIo释放。</summary>
    public Task<IVideoPlayer> OpenAsync(ArchiveEntry entry, DecodeRequest request, long workingBudget, CancellationToken token)
    {
        if (request.TargetWidth <= 0 || request.TargetHeight <= 0 || workingBudget <= 0
            || (long)request.TargetWidth * request.TargetHeight > workingBudget / 4)
            throw new NotSupportedException("视频输出超过工作预算。");
        return SourceIo.RunReadAsync<IVideoPlayer>(async cancellation =>
        {
            RealizedFilePathList? files = await ArchiveEntryUtility.RealizeArchiveEntry([entry], ArchivePolicy.SendExtractFile, realizer, cancellation);
            MacVideoPlayer? player = null;
            try
            {
                player = await VideoMain.RunAsync(async () =>
                {
                    cancellation.ThrowIfCancellationRequested();
                    using var url = NSUrl.FromFilename(files.Paths.Single()); var asset = AVAsset.FromUrl(url);
                    try
                    {
                        await LoadAsync(asset, cancellation); var metadata = await InspectAsync(asset);
                        var info=metadata.Info;var transform=metadata.Transform;var raw=metadata.Raw;
                        // 三份CoreVideo缓冲、一次源行复制和最多原尺寸输出；动态规格不另开资源预算。
                        var bytes = checked((long)Math.Ceiling(raw.Width) * (long)Math.Ceiling(raw.Height) * 16
                            + (long)Math.Ceiling(info.Size.Width) * (long)Math.Ceiling(info.Size.Height) * 4);
                        if (bytes > workingBudget) throw new NotSupportedException("视频原生工作缓冲超过预算。");
                        var release=Reserve(bytes,workingBudget);
                        try{return new MacVideoPlayer(asset,info,transform,workingBudget,release);}
                        catch{release();throw;}
                    }
                    catch { asset.Dispose(); throw; }
                });
                await player.WaitReadyAsync(cancellation); cancellation.ThrowIfCancellationRequested();
                player.TakeFiles(files); files = null; return player;
            }
            catch { if (player is not null) await player.DisposeAsync(); throw; }
            finally { if (files is not null) await files.DisposeAsync(); }
        }, token);
    }

    private static async Task LoadAsync(AVAsset asset, CancellationToken token)
    {
        // 官方绑定的可等待key加载；取消先通知系统，再等真实调用结束才释放asset。
        using var cancellation = token.Register(() => asset.CancelLoading());
        await asset.LoadValuesTaskAsync(["duration", "tracks", "playable"]); token.ThrowIfCancellationRequested();
    }
    private static async Task<(VideoInfo Info,CGAffineTransform Transform,CGSize Raw)> InspectAsync(AVAsset asset)
    {
        if (!asset.Playable) throw new NotSupportedException("macOS系统不支持此视频容器或编解码器。");
        var videoResult=await asset.LoadTracksWithMediaType2Async(AVMediaTypes.Video.GetConstant()!);
        var audioResult=await asset.LoadTracksWithMediaType2Async(AVMediaTypes.Audio.GetConstant()!);
        var videos=videoResult;var audio=audioResult;
        try
        {
        var track=videos.FirstOrDefault();var transform=track?.PreferredTransform??CGAffineTransform.MakeIdentity();var raw=track?.NaturalSize??new CGSize(512,512);
        var size = Bounds(raw,transform).Size;
        var formats=track?.FormatDescriptions??[];
        Size aspect=size;
        try {if(formats.OfType<CMVideoFormatDescription>().FirstOrDefault() is {} video)aspect=Bounds(video.GetPresentationDimensions(true,true),transform).Size;}
        finally{foreach(var format in formats)format.Dispose();}
        double seconds = asset.Duration.Seconds;
        if (!double.IsFinite(seconds) || seconds <= 0 || seconds > TimeSpan.MaxValue.TotalSeconds)
            throw new InvalidDataException("媒体时长无效或来源尚未加载。");
        if (!double.IsFinite(size.Width) || !double.IsFinite(size.Height) || size.Width <= 0 || size.Height <= 0)
            throw new InvalidDataException("视频尺寸无效。");
        return (new VideoInfo(size,TimeSpan.FromSeconds(seconds),audio.Length!=0,track is not null){AspectSize=aspect},transform,raw);
        }
        finally { foreach(var item in videos.Concat(audio))item.Dispose(); }
    }
    internal static (Size Size, double X, double Y) Bounds(CGSize size, CGAffineTransform m)
    {
        var corners = new[] {(0d,0d),((double)size.Width,0d),(0d,(double)size.Height),((double)size.Width,(double)size.Height)};
        var xs = corners.Select(p => m.A*p.Item1+m.C*p.Item2+m.Tx).ToArray();
        var ys = corners.Select(p => m.B*p.Item1+m.D*p.Item2+m.Ty).ToArray();
        return (new(xs.Max()-xs.Min(),ys.Max()-ys.Min()),xs.Min(),ys.Min());
    }
}

/// <summary>所有Apple播放器调用串行回到系统主线程；无窗口测试宿主泵送同一主线程。</summary>
internal static class VideoMain
{
    internal static Task<T> RunAsync<T>(Func<Task<T>> action)
    {
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        NSApplication.SharedApplication.BeginInvokeOnMainThread(async () =>
        { try { completion.TrySetResult(await action()); } catch (Exception error) { completion.TrySetException(error); } });
        return completion.Task;
    }
    internal static Task<T> RunAsync<T>(Func<T> action) => RunAsync(() => Task.FromResult(action()));
}

/// <summary>AVPlayer拥有真实音频/时钟；镜像状态不引用窗口，帧和异步释放在同一门内。</summary>
internal sealed class MacVideoPlayer : ObservableObject, IVideoPlayer
{
    private readonly AVAsset _asset;
    private readonly AVPlayer _player;
    private readonly AVPlayerItem _item;
    private readonly AVPlayerItemVideoOutput _output;
    private readonly NSObject _ended;
    private readonly CGAffineTransform _transform;
    private DecodeRequest? _lastRequest;
    private readonly long _budget;
    private readonly Action _releaseBudget;
    private readonly SemaphoreSlim _gate = new(1);
    private readonly object _state = new();
    private RealizedFilePathList? _files;
    private bool _disposed, _playing, _enabled = true, _audio = true, _muted, _repeat;
    private double _volume = .5, _rate = 1, _position;
    private double? _seek = 0;
    private bool _forceFrame = true;
    private int _eos;
    private Task? _close;
    private bool _nativeClosed;
    internal MacVideoPlayer(AVAsset asset, VideoInfo info, CGAffineTransform transform, long budget,Action releaseBudget)
    {
        _asset=asset; Info=info; _transform=transform; _budget=budget;_releaseBudget=releaseBudget;
        var attributes=new CVPixelBufferAttributes { PixelFormatType=CVPixelFormatType.CV32BGRA };
        var colors=new AVColorProperties { AVVideoColorPrimaries=AVVideoColorPrimaries.Itu_R_709_2,
            AVVideoTransferFunction=AVVideoTransferFunction.Iec_sRgb };
        // 当前官方绑定的矩阵属性只读，通过官方字典key补齐AVFoundation必需的第三项。
        var library=ObjCRuntime.Dlfcn.dlopen("/System/Library/Frameworks/AVFoundation.framework/AVFoundation",0);
        try
        {
            var matrixKey=ObjCRuntime.Dlfcn.GetStringConstant(library,"AVVideoYCbCrMatrixKey")
                ??throw new NotSupportedException("系统缺少视频颜色矩阵常量。");
            colors.Dictionary.SetValueForKey(AVVideoYCbCrMatrix.Itu_R_709_2,matrixKey);
        }
        finally {if(library!=IntPtr.Zero)ObjCRuntime.Dlfcn.dlclose(library);}
        // CoreVideo像素属性与官方AVFoundation颜色输出属性共用字典；交给显示端的是sRGB BGRA。
        var settings=new AVPlayerItemVideoOutputSettings(attributes.Dictionary) { ColorProperties=colors, AllowWideColor=false };
        try { _output=new AVPlayerItemVideoOutput(settings); }
        finally { colors.Dictionary.Dispose(); settings.Dictionary.Dispose(); }
        _item=new AVPlayerItem(asset);
        _item.AddOutput(_output);
        _player=new AVPlayer(_item) { Muted=true, ActionAtItemEnd=AVPlayerActionAtItemEnd.Pause };
        // 通知只排队，不能在系统主线程阻塞读帧/seek持有的门。
        _ended=AVPlayerItem.Notifications.ObserveDidPlayToEndTime(_item,(_,_)=>_ = ObserveCloseAsync(EndedAsync()));
    }
    internal void TakeFiles(RealizedFilePathList files) => _files=files;
    internal async Task WaitReadyAsync(CancellationToken token)
    {
        var start=System.Diagnostics.Stopwatch.StartNew();
        while (true)
        {
            token.ThrowIfCancellationRequested();
            var status=await VideoMain.RunAsync(()=>_item.Status);
            if(status==AVPlayerItemStatus.ReadyToPlay) return;
            if(status==AVPlayerItemStatus.Failed) throw new NotSupportedException("系统播放器加载失败，容器或编解码器不受支持。");
            if(start.Elapsed>TimeSpan.FromSeconds(10)) throw new TimeoutException("系统播放器准备超时。");
            await Task.Delay(10,token);
        }
    }
    public VideoInfo Info { get; }
    public bool IsDisposed => _disposed;
    public bool HasAudio => Info.HasAudio;
    public bool HasVideo => Info.HasVideo;
    public bool IsEnabled { get=>_enabled; set { if(!_disposed) SetProperty(ref _enabled,value); } }
    public bool IsAudioEnabled { get=>_audio; set { if(!_disposed) SetProperty(ref _audio,value); } }
    public bool IsMuted { get=>_muted; set { if(!_disposed) SetProperty(ref _muted,value); } }
    public bool IsRepeat { get=>_repeat; set { if(!_disposed) SetProperty(ref _repeat,value); } }
    public bool IsPlaying => _playing;
    public TimeSpan Duration => Info.Duration;
    public double Position
    {
        get { lock(_state) return _position; }
        set { if(_disposed||!double.IsFinite(value))return;lock(_state){_position=Math.Clamp(value,0,1);_seek=_position;_forceFrame=true;}OnPropertyChanged(); }
    }
    public bool ScrubbingEnabled => true;
    public double Volume { get=>_volume;set{if(!_disposed&&double.IsFinite(value))SetProperty(ref _volume,Math.Clamp(value,0,1));} }
    public bool RateEnabled => true;
    public double Rate { get=>_rate;set{if(!_disposed&&double.IsFinite(value))SetProperty(ref _rate,Math.Clamp(value,.1,8));} }
    public int EndOfStreamCount { get { lock(_state)return _eos; } }
    public event EventHandler? MediaPlayed;
    public event EventHandler? MediaEnded;
    public event EventHandler? MediaEndOfStreamReached;
    public void Play(){if(_disposed||_playing)return;_playing=true;OnPropertyChanged(nameof(IsPlaying));MediaPlayed?.Invoke(this,EventArgs.Empty);}
    public void Pause(){if(_disposed||!_playing)return;_playing=false;OnPropertyChanged(nameof(IsPlaying));}
    public void TogglePlay(){if(_playing)Pause();else Play();}
    public bool AddPosition(TimeSpan span){if(_disposed)return false;double next=Position+span.TotalSeconds/Duration.TotalSeconds;Position=next;return next<0||next>1;}
    /// <summary>EOS与帧/关闭串行；状态锁也是Dispose即时拒绝回调的边界。</summary>
    private async Task EndedAsync()
    {
        await _gate.WaitAsync();
        try
        {
            await VideoMain.RunAsync(()=>
            {
                lock(_state)
                {
                    if(_disposed)return false;
                    if(_eos<int.MaxValue)_eos++;
                    OnPropertyChanged(nameof(EndOfStreamCount));
                    // 原计数先递增再发事件；事件允许异步关闭，关闭后不继续写回状态。
                    if(_disposed)return false;
                    MediaEndOfStreamReached?.Invoke(this,EventArgs.Empty);
                    if(_disposed)return false;
                    if(_repeat)Position=.001/Duration.TotalSeconds;
                    else
                    {
                        Pause();if(_disposed)return false;
                        _position=1;OnPropertyChanged(nameof(Position));
                        if(!_disposed)MediaEnded?.Invoke(this,EventArgs.Empty);
                    }
                    return true;
                }
            });
        }
        finally{_gate.Release();}
    }
    /// <summary>应用用户意图及待处理seek；禁用仅暂停原生播放，不丢失IsPlaying。</summary>
    public async Task SynchronizeAsync(CancellationToken token)
    {
        await _gate.WaitAsync(token);
        try{ObjectDisposedException.ThrowIf(_disposed,this);await SynchronizeInnerAsync(token);}
        finally{_gate.Release();}
    }
    private Task<bool> SynchronizeInnerAsync(CancellationToken token) => VideoMain.RunAsync(async () =>
    {
        ObjectDisposedException.ThrowIf(_disposed,this);token.ThrowIfCancellationRequested();
        if(_item.Status==AVPlayerItemStatus.Failed)throw new IOException("系统播放器在播放期间失败。");
        double? seek;lock(_state){seek=_seek;_seek=null;}
        if(seek is {} value)
        {
            _player.Pause();await _player.SeekAsync(CMTime.FromSeconds(value*Duration.TotalSeconds,600),CMTime.Zero,CMTime.Zero);
            token.ThrowIfCancellationRequested();ObjectDisposedException.ThrowIf(_disposed,this);
        }
        _player.Muted=_muted||!_audio;_player.Volume=(float)_volume;
        if(_playing&&_enabled)_player.Rate=(float)_rate;else _player.Pause();
        var seconds=_item.CurrentTime.Seconds;
        if(double.IsFinite(seconds)){lock(_state){if(_seek is null)_position=Math.Clamp(seconds/Duration.TotalSeconds,0,1);}OnPropertyChanged(nameof(Position));}
        return true;
    });
    /// <summary>仅当前真实帧输出；读取锁/stride、方向和下采样受同一工作预算约束。</summary>
    public async Task<DecodedImageLease?> ReadFrameAsync(DecodeRequest request,CancellationToken token)
    {
        await _gate.WaitAsync(token);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed,this);await SynchronizeInnerAsync(token);
            using var buffer=await VideoMain.RunAsync(()=>
            {
                ObjectDisposedException.ThrowIf(_disposed,this);token.ThrowIfCancellationRequested();
                if(!HasVideo)return null;
                if(!_forceFrame&&_lastRequest==request&&!_output.HasNewPixelBufferForItemTime(_item.CurrentTime))return null;
                var displayTime=CMTime.Zero;return _output.CopyPixelBuffer(_item.CurrentTime,ref displayTime);
            });
            if(buffer is null)return null;
            var result=await Task.Run(()=>Copy(buffer,request,token),token);
            if(_disposed||token.IsCancellationRequested){result.Dispose();throw new OperationCanceledException(token);}
            lock(_state){_forceFrame=_seek is not null;_lastRequest=request;}return result;
        }
        finally{_gate.Release();}
    }
    private DecodedImageLease Copy(CVPixelBuffer buffer,DecodeRequest request,CancellationToken token)
    {
        int width=checked((int)buffer.Width),height=checked((int)buffer.Height),stride=checked((int)buffer.BytesPerRow);
        var bounds=MacVideoPlayerFactory.Bounds(new(width,height),_transform);
        if(request.TargetWidth<=0||request.TargetHeight<=0)throw new NotSupportedException("视频帧输出尺寸无效。");
        var fitted=PdfArchiveProfile.Fit(bounds.Size,new(request.TargetWidth,request.TargetHeight),false);
        int dw=Math.Max(1,(int)fitted.Width),dh=Math.Max(1,(int)fitted.Height);
        long sourceBytes=checked((long)width*height*4),targetBytes=checked((long)dw*dh*4);
        if(sourceBytes*4+targetBytes>_budget)throw new NotSupportedException("实际视频像素超过工作预算。");
        if(buffer.Lock(CVPixelBufferLock.ReadOnly)!=CVReturn.Success)throw new IOException("视频帧锁定失败。");
        try
        {
            if(buffer.BaseAddress==IntPtr.Zero||stride<width*4)throw new InvalidDataException("系统视频像素布局无效。");
            var source=new byte[(int)sourceBytes];for(int y=0;y<height;y++){token.ThrowIfCancellationRequested();Marshal.Copy(buffer.BaseAddress+y*stride,source,y*width*4,width*4);}
            var pixels=new byte[(int)targetBytes];double determinant=_transform.A*_transform.D-_transform.B*_transform.C;
            if(Math.Abs(determinant)<1e-10)throw new InvalidDataException("视频方向矩阵无效。");
            for(int y=0;y<dh;y++)
            {
                token.ThrowIfCancellationRequested();
                for(int x=0;x<dw;x++)
                {
                    double tx=bounds.X+(x+.5)*bounds.Size.Width/dw-_transform.Tx,ty=bounds.Y+(y+.5)*bounds.Size.Height/dh-_transform.Ty;
                    int sx=Math.Clamp((int)Math.Floor((_transform.D*tx-_transform.C*ty)/determinant),0,width-1);
                    int sy=Math.Clamp((int)Math.Floor((-_transform.B*tx+_transform.A*ty)/determinant),0,height-1);
                    int i=(sy*width+sx)*4,o=(y*dw+x)*4;int a=source[i+3];
                    pixels[o]=(byte)((source[i]*a+127)/255);pixels[o+1]=(byte)((source[i+1]*a+127)/255);pixels[o+2]=(byte)((source[i+2]*a+127)/255);pixels[o+3]=(byte)a;
                }
            }
            return new(new(dw,dh),pixels,sourceSize:Info.Size);
        }
        finally{buffer.Unlock(CVPixelBufferLock.ReadOnly);}
    }
    public void Dispose(){_ = ObserveCloseAsync(DisposeAsync().AsTask());}
    private static async Task ObserveCloseAsync(Task task){try{await task;}catch(Exception error){System.Diagnostics.Trace.WriteLine(error);}}
    /// <summary>关闭立即拒绝旧结果，等待真实seek/读结束后解除通知/output并释放原材料。</summary>
    public ValueTask DisposeAsync()
    {lock(_state){_disposed=true;if(_close is null||_close.IsFaulted||_close.IsCanceled)_close=CloseAsync();return new(_close);}}
    private async Task CloseAsync()
    {
        await _gate.WaitAsync();
        try
        {
            if(!_nativeClosed)
                await VideoMain.RunAsync(()=>{_player.Pause();_ended.Dispose();_item.RemoveOutput(_output);_player.ReplaceCurrentItemWithPlayerItem(null);_output.Dispose();_item.Dispose();_player.Dispose();_asset.Dispose();_nativeClosed=true;_releaseBudget();return true;});
            if(_files is {} files){await files.DisposeAsync();_files=null;}
            MediaPlayed=null;MediaEnded=null;MediaEndOfStreamReached=null;
        }
        finally{_gate.Release();}
    }
}
