using System.IO.Compression;
using System.Text.Json.Nodes;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using NeeView.Backends;
using NeeView.MacOS.ViewModels;
using NeeView.MacOS.Views;
namespace NeeView.Engine.Tests;

/// <summary>原单页媒体书/普通视频页与唯一JSON、查看器、资源预算的闭环回归。</summary>
public sealed class VideoIntegrationTests
{
    private static CancellationToken Token=>TestContext.Current.CancellationToken;
    private static string PutVideo(Fixture f,string name="000.mp4")
    {var path=Path.Combine(f.Images,name);File.WriteAllBytes(path,[1,2,3]);return path;}
    private static BookOperation Operation(Fixture f,SaveData state,VideoFactory factory)
    {var operation=f.Operation(state);operation.AttachVideoPlayers(factory);return operation;}
    [Fact]
    public async Task OriginalVideoDefaultsDifferenceUnknownAndWindowsFieldsSurviveSave()
    {
        using var f=new Fixture();var state=new SaveData(f.State);await state.LoadAsync(Token);var config=Config.Current.Archive.Media;
        Assert.True(config.IsEnabled);Assert.False(config.IsMediaPageEnabled);Assert.Equal(".asf;.avi;.mkv;.mov;.mp4;.wmv",config.SupportFileTypes.ToString());
        Assert.Equal(10,config.PageSeconds);Assert.Equal(.5,config.MediaStartDelaySeconds);Assert.Equal(.5,config.Volume);Assert.False(config.IsMuted);Assert.False(config.IsRepeat);
        config.IsLibVlcEnabled=true;config.LibVlcPath="C:\\VLC";config.DefaultSubtitle=DefaultSubtitle.Disable;
        config.ExtensionData=new(){["Future"]=System.Text.Json.JsonSerializer.SerializeToElement(42)};
        config.Volume=.123456;config.IsMediaPageEnabled=true;await state.SaveAsync(null,Token);await new SaveData(f.State).LoadAsync(Token);
        Assert.Equal(.12346,Config.Current.Archive.Media.Volume);Assert.True(Config.Current.Archive.Media.IsLibVlcEnabled);Assert.Equal("C:\\VLC",Config.Current.Archive.Media.LibVlcPath);
        Assert.Equal(DefaultSubtitle.Disable,Config.Current.Archive.Media.DefaultSubtitle);Assert.Equal(42,Config.Current.Archive.Media.ExtensionData!["Future"].GetInt32());
        var raw=JsonNode.Parse(File.ReadAllText(Path.Combine(f.State,"UserSetting.json")))!["Config"]!["Archive"]!["Media"]!;
        Assert.Null(raw["MediaStartDelaySeconds"]);Assert.Null(raw["IsEnabled"]);
    }
    [Theory]
    [InlineData(false,false)] [InlineData(false,true)] [InlineData(true,false)] [InlineData(true,true)]
    public async Task DirectMediaIsOnePageAndOrdinarySourceUsesIndependentPageSwitch(bool zip,bool enabled)
    {
        using var f=new Fixture();var path=PutVideo(f);var state=new SaveData(f.State);await state.LoadAsync(Token);var factory=new VideoFactory();
        await using var operation=Operation(f,state,factory);await operation.OpenAsync(path,Token);
        Assert.True(operation.Book!.IsMedia);Assert.Single(operation.Book.Pages);Assert.True(operation.Book.CurrentPage!.IsVideo);Assert.Equal(new Size(640,320),operation.Book.CurrentPage.Content.PageDataSource.Size);
        Config.Current.Archive.Media.IsMediaPageEnabled=enabled;
        var source=f.Images;if(zip){source=Path.Combine(f.Root,"with-media.cbz");ZipFile.CreateFromDirectory(f.Images,source);}
        await operation.OpenAsync(source,Token);Assert.False(operation.Book!.IsMedia);
        var video=operation.Book.Pages.Single(p=>p.EntryName=="000.mp4");Assert.Equal(enabled,video.IsVideo);Assert.Equal(enabled?PageType.File:PageType.Archive,video.PageType);
        Config.Current.System.ArchiveRecursiveMode=ArchiveEntryCollectionMode.IncludeSubArchives;
        await operation.ApplyOptionsAsync(()=>Config.Current.BookSetting.IsRecursiveFolder=true,(-1,TimeSpan.Zero));
        Assert.Single(operation.Book!.Pages,p=>p.EntryName.EndsWith("000.mp4"));Assert.False(operation.Book.IsMedia);
    }
    [Fact]
    public async Task MediaNavigationUsesSecondsOnSamePageAndDoesNotPersistPlaybackTime()
    {
        using var f=new Fixture();var path=PutVideo(f);var state=new SaveData(f.State);await state.LoadAsync(Token);var factory=new VideoFactory();
        await using var operation=Operation(f,state,factory);await operation.OpenAsync(path,Token);using var player=new VideoPlayer();operation.CurrentMediaPlayer=player;
        await operation.MoveAsync(1);Assert.Equal(.1,player.Position);await operation.MoveAsync(1,true);Assert.Equal(.2,player.Position);
        await operation.MoveSizeAsync(-1);Assert.Equal(.1,player.Position);Assert.Equal(0,operation.Position.Index);
        var commands=new CommandTable(operation);await commands.ExecuteAsync("LastPage");Assert.Equal(1,player.Position);await commands.ExecuteAsync("FirstPage");Assert.Equal(0,player.Position);
        state.SetCommandParameter("NextMediaPosition",new MoveMediaPositionCommandParameter{Delta=3});await commands.ExecuteAsync("NextMediaPosition");Assert.Equal(.03,player.Position);
        await operation.DisposeAsync();var history=File.ReadAllText(Path.Combine(f.State,"History.json"));Assert.DoesNotContain("VideoPosition",history);Assert.DoesNotContain("MediaPosition",history);
    }
    [Fact]
    public async Task BookRepeatAudioAndFailedWritesPreserveOriginalConfigurationAndPlayer()
    {
        using var f=new Fixture();var path=PutVideo(f);var state=new SaveData(f.State);await state.LoadAsync(Token);var factory=new VideoFactory();
        await using var operation=Operation(f,state,factory);await operation.OpenAsync(path,Token);using var player=new VideoPlayer{IsRepeat=false};operation.CurrentMediaPlayer=player;
        var blocked=Path.Combine(f.State,"UserSetting.json.tmp");Directory.CreateDirectory(blocked);
        try
        {
            await Assert.ThrowsAnyAsync<Exception>(()=>operation.SetMediaRepeatAsync(true));Assert.False(player.IsRepeat);Assert.False(Config.Current.Archive.Media.IsRepeat);
            await Assert.ThrowsAnyAsync<Exception>(()=>operation.SetMediaAudioAsync(true,.2));Assert.False(player.IsMuted);Assert.Equal(.5,player.Volume);Assert.Equal(.5,Config.Current.Archive.Media.Volume);
        }
        finally{Directory.Delete(blocked);}
        await operation.SetMediaRepeatAsync(true);Assert.True(player.IsRepeat);Assert.True(Config.Current.Image.IsMediaRepeat);
        await operation.SetMediaAudioAsync(true,.2);Assert.True(player.IsMuted);Assert.Equal(.2,player.Volume);
        using var replacement=new VideoPlayer();operation.CurrentMediaPlayer=replacement;await operation.SetMediaAudioAsync(player,false,.8);Assert.True(Config.Current.Archive.Media.IsMuted);Assert.Equal(.2,Config.Current.Archive.Media.Volume);
        await operation.OpenAsync(f.Images,Token);await operation.SetMediaRepeatAsync(false);Assert.False(Config.Current.Image.IsMediaRepeat);Assert.True(Config.Current.Archive.Media.IsRepeat);
    }
    [AvaloniaFact]
    public async Task FormalVideoDisplaysSeeksResizesWithoutReopeningAndHonorsPauseDuringDelay()
    {
        using var f=new Fixture();var path=PutVideo(f);var state=new SaveData(f.State);await state.LoadAsync(Token);Config.Current.Archive.Media.MediaStartDelaySeconds=.15;
        var factory=new VideoFactory();await using var operation=Operation(f,state,factory);await operation.OpenAsync(path,Token);using var images=new BitmapFactory(new MagickImageDecoder());
        var viewer=new ReaderView();var window=new Window{Width=500,Height=400,Content=viewer};window.Show();window.UpdateLayout();viewer.Attach(operation,images);
        try
        {
            await viewer.RefreshAsync();await Wait(()=>operation.MediaExists());var player=Assert.IsType<VideoPlayer>(operation.CurrentMediaPlayer);
            Assert.Equal(1,factory.Opens);Assert.Equal(1,viewer.VideoCount);Assert.Equal(1,viewer.DisplayCount);Assert.True(images.ByteCount>0);Assert.True(player.IsPlaying);Assert.False(player.IsEnabled);
            player.Pause();await Task.Delay(200,Token);Dispatcher.UIThread.RunJobs();Assert.True(player.IsEnabled);Assert.False(player.IsPlaying);
            player.Position=.65;await viewer.RefreshMediaAsync();await viewer.ZoomAsync(.25);var request=player.Requests.Last();await viewer.ZoomAsync(2);Assert.Same(player,operation.CurrentMediaPlayer);Assert.Equal(1,factory.Opens);Assert.Equal(.65,player.Position);Assert.NotEqual(request,player.Requests.Last());
            if(Environment.GetEnvironmentVariable("NEEVIEW_P5_VIDEO_SCREENSHOT") is {} screenshot){Dispatcher.UIThread.RunJobs();using var frame=window.CaptureRenderedFrame();frame!.Save(screenshot,Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);}
            await operation.OpenAsync(f.Images,Token);await viewer.RefreshAsync();await viewer.CloseMediaAsync();Assert.True(player.IsDisposed);Assert.False(operation.MediaExists());Assert.Equal(0,viewer.VideoCount);
        }
        finally{await viewer.CloseMediaAsync();viewer.Dispose();window.Close();}images.Dispose();Assert.Equal(0,images.ByteCount);
    }
    [AvaloniaFact]
    public async Task LateOpenedVideoAfterSwitchIsDisposedAndCannotReplaceNewBook()
    {
        using var f=new Fixture();var path=PutVideo(f);var state=new SaveData(f.State);await state.LoadAsync(Token);var factory=new VideoFactory{DelayOpen=true};
        await using var operation=Operation(f,state,factory);await operation.OpenAsync(path,Token);using var images=new BitmapFactory(new MagickImageDecoder());
        var viewer=new ReaderView();var window=new Window{Width=500,Height=400,Content=viewer};window.Show();window.UpdateLayout();viewer.Attach(operation,images);
        try
        {
            var old=viewer.RefreshAsync();await factory.Entered.Task;await operation.OpenAsync(f.Images,Token);await viewer.RefreshAsync();var current=operation.Book;
            factory.Complete.SetResult();await old;Assert.Same(current,operation.Book);Assert.True(factory.Players.Single().IsDisposed);Assert.False(operation.MediaExists());Assert.Equal(0,viewer.VideoCount);
        }
        finally{factory.Complete.TrySetResult();await viewer.CloseMediaAsync();viewer.Dispose();window.Close();}
    }
    [AvaloniaFact]
    public async Task MediaBarRefreshCannotWriteVolumeAndLatestEditWins()
    {
        using var f=new Fixture();var state=new SaveData(f.State);await state.LoadAsync(Token);await using var operation=f.Operation(state);
        using var first=new VideoPlayer{Rate=2};operation.CurrentMediaPlayer=first;using var control=new MediaControlView();var refresh=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int calls=0;control.Attach(operation,async()=>{if(++calls==1)await refresh.Task;});var window=new Window{Content=control};window.Show();
        try
        {
            Assert.Equal(2d,control.FindControl<ComboBox>("MediaRate")!.SelectedItem);control.FindControl<Slider>("MediaVolume")!.Value=.9;Assert.Equal(.5,Config.Current.Archive.Media.Volume);
            var editing=control.SetAudioAsync(true,.2);await Wait(()=>calls==1);_ = control.SetAudioAsync(false,.7);_ = control.SetAudioAsync(true,.8);refresh.SetResult();await editing;Assert.True(first.IsMuted);Assert.Equal(.8,first.Volume);Assert.Equal(.8,Config.Current.Archive.Media.Volume);
            using var next=new VideoPlayer();operation.CurrentMediaPlayer=next;control.UpdatePlayer();Assert.Equal(1d,control.FindControl<ComboBox>("MediaRate")!.SelectedItem);Assert.Equal("静音：关",control.FindControl<Button>("MediaMute")!.Content);
        }
        finally{refresh.TrySetResult();await control.PrepareCloseAsync();window.Close();}
    }
    [AvaloniaFact]
    public async Task NewRevisionReusesPreparingPlayerAndDisposesLateCanceledFrame()
    {
        using var f=new Fixture();var path=PutVideo(f);var state=new SaveData(f.State);await state.LoadAsync(Token);var factory=new VideoFactory{DelayFirstFrame=true};
        await using var operation=Operation(f,state,factory);await operation.OpenAsync(path,Token);using var images=new BitmapFactory(new MagickImageDecoder());
        var viewer=new ReaderView();var window=new Window{Width=500,Height=400,Content=viewer};window.Show();window.UpdateLayout();viewer.Attach(operation,images);
        var old=viewer.RefreshAsync();await Wait(()=>factory.Players.Count==1);var player=factory.Players[0];await player.FrameEntered.Task;
        try
        {
            await viewer.RefreshAsync();Assert.Equal(1,viewer.DisplayCount);Assert.Same(player,operation.CurrentMediaPlayer);player.FrameComplete.SetResult();await old;
            Assert.False(player.IsDisposed);Assert.Same(player,operation.CurrentMediaPlayer);Assert.Equal(1,factory.Opens);Assert.NotNull(player.LateFrame);Assert.Empty(player.LateFrame.Pixels);
        }
        finally{player.FrameComplete.TrySetResult();await old;await viewer.CloseMediaAsync();viewer.Dispose();window.Close();}
    }
    [Fact]
    public async Task MediaPageSettingRecollectsOriginalSourceAndKeepsEntry()
    {
        using var f=new Fixture();PutVideo(f);var state=new SaveData(f.State);await state.LoadAsync(Token);var factory=new VideoFactory();
        await using var operation=Operation(f,state,factory);await operation.OpenAsync(f.Images,Token);await operation.JumpAsync(operation.Book!.Pages.Single(p=>p.EntryName=="000.mp4").Index);
        var before=operation.Book;await operation.ApplyOptionsAsync(()=>Config.Current.Archive.Media.IsMediaPageEnabled=true,(-1,TimeSpan.Zero));
        Assert.NotSame(before,operation.Book);Assert.Equal("000.mp4",operation.Book!.CurrentPage!.EntryName);Assert.True(operation.Book.CurrentPage.IsVideo);Assert.Equal(new Size(640,320),operation.Book.CurrentPage.Content.PageDataSource.Size);
        var retained=operation.Book;await operation.SetMediaAudioAsync(true,.3);Assert.Same(retained,operation.Book);
    }
    [AvaloniaFact]
    public async Task VideoSettingsAreIsolatedAndSaveKeepsWindowsFields()
    {
        using var f=new Fixture();var state=new SaveData(f.State);await state.LoadAsync(Token);Config.Current.Archive.Media.LibVlcPath="legacy";
        await using var operation=f.Operation(state);var model=new ReaderWorkspaceViewModel(operation,new(operation),state);
        var settings=new SettingsWindow(model);settings.Show();settings.FindControl<CheckBox>("VideoEnabled")!.IsChecked=false;settings.Close();Assert.True(Config.Current.Archive.Media.IsEnabled);
        settings=new SettingsWindow(model);settings.Show();settings.FindControl<CheckBox>("VideoPageEnabled")!.IsChecked=true;
        settings.FindControl<Button>("SaveSettings")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Wait(()=>!settings.IsVisible);
        Assert.True(settings.WasSaved);Assert.True(Config.Current.Archive.Media.IsMediaPageEnabled);Assert.Equal("legacy",Config.Current.Archive.Media.LibVlcPath);
    }
    [AvaloniaTheory]
    [InlineData(false)] [InlineData(true)]
    public async Task DoublePageAndPanoramaGiveAudioOnlyToSelectedFirstElement(bool panorama)
    {
        using var f=new Fixture();foreach(var image in Directory.GetFiles(f.Images))File.Delete(image);
        PutVideo(f,"000.mp4");PutVideo(f,"001.mp4");PutVideo(f,"002.mp4");
        var state=new SaveData(f.State);await state.LoadAsync(Token);Config.Current.Archive.Media.IsMediaPageEnabled=true;
        var factory=new VideoFactory();await using var operation=Operation(f,state,factory);await operation.OpenAsync(f.Images,Token);
        await operation.ApplySettingAsync(s=>{s.PageMode=PageMode.WidePage;s.IsSupportedWidePage=false;});
        if(panorama)await operation.SetBrowseModeAsync(BrowseLayoutMode.Panorama);
        using var images=new BitmapFactory(new MagickImageDecoder());var viewer=new ReaderView();var window=new Window{Width=800,Height=600,Content=viewer};window.Show();window.UpdateLayout();viewer.Attach(operation,images);
        try
        {
            await viewer.RefreshAsync();await Wait(()=>factory.Players.Count>=2);await viewer.RefreshMediaAsync();
            Assert.Same(operation.CurrentMediaPlayer,Assert.Single(factory.Players,p=>p.IsAudioEnabled));
            Assert.All(factory.Players,p=>Assert.True(p.IsPlaying));
            await operation.JumpAsync(2);await viewer.RefreshAsync();await viewer.RefreshMediaAsync();
            Assert.Same(operation.CurrentMediaPlayer,Assert.Single(factory.Players,p=>!p.IsDisposed&&p.IsAudioEnabled));
        }
        finally{await viewer.CloseMediaAsync();viewer.Dispose();window.Close();}
    }
    [AvaloniaFact]
    public async Task FailedPlayerCloseCanBeRetriedWithoutLosingItsOwner()
    {
        using var f=new Fixture();var path=PutVideo(f);var state=new SaveData(f.State);await state.LoadAsync(Token);var factory=new VideoFactory{CloseFailures=2};
        await using var operation=Operation(f,state,factory);await operation.OpenAsync(path,Token);using var images=new BitmapFactory(new MagickImageDecoder());
        var viewer=new ReaderView();var window=new Window{Width=500,Height=400,Content=viewer};window.Show();window.UpdateLayout();viewer.Attach(operation,images);
        try
        {
            await viewer.RefreshAsync();var player=Assert.Single(factory.Players);
            await Assert.ThrowsAsync<IOException>(()=>viewer.CloseMediaAsync());Assert.False(player.IsDisposed);
            await viewer.CloseMediaAsync();Assert.True(player.IsDisposed);Assert.Equal(3,player.CloseAttempts);
        }
        finally{await viewer.CloseMediaAsync();viewer.Dispose();window.Close();}
    }
    [Fact]
    public async Task VideoEosReleasesOriginalSlideShowWaitThroughSharedPlayerContract()
    {
        using var f=new Fixture();var state=new SaveData(f.State);await state.LoadAsync(Token);await using var operation=f.Operation(state);await operation.OpenAsync(f.Images,Token);
        Config.Current.SlideShow.IsWaitAnimation=true;using var player=new VideoPlayer();operation.CurrentMediaPlayer=player;player.Play();
        double clock=0;int calls=0;using var show=new SlideShow(operation,()=>clock);show.Play();clock=5;
        await Tick();Assert.True(show.IsWaitingAnimation);Assert.Equal(0,calls);player.ReachEnd();await Tick();Assert.Equal(1,calls);Assert.False(show.IsWaitingAnimation);
        Task Tick()=>show.TickAsync(_=>true,_=>true,_=>{calls++;return Task.CompletedTask;});
    }
    [Fact]
    public void UntouchedExtremeLegacyVideoNumbersSurviveSettingsDraft()
    {
        var config=new MediaArchiveConfig{MediaStartDelaySeconds=double.MaxValue,Volume=-double.MaxValue};
        var draft=new VideoSettingsViewModel(config);draft.IsMuted=true;draft.Apply(config);
        Assert.Equal(double.MaxValue,config.MediaStartDelaySeconds);Assert.Equal(-double.MaxValue,config.Volume);Assert.True(config.IsMuted);
    }
    private static async Task Wait(Func<bool> done)
    {for(int i=0;i<400&&!done();i++){Dispatcher.UIThread.RunJobs();await Task.Delay(10,Token);}Assert.True(done());}
    private sealed class VideoFactory:IVideoPlayerFactory
    {
        public int Opens,CloseFailures;public bool DelayOpen,DelayFirstFrame;
        public TaskCompletionSource Entered {get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Complete {get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public List<VideoPlayer> Players {get;}=[];
        public Task<VideoInfo> ProbeAsync(ArchiveEntry entry,CancellationToken token)=>Task.FromResult(new VideoInfo(new(640,320),TimeSpan.FromSeconds(100),true,true));
        public async Task<IVideoPlayer> OpenAsync(ArchiveEntry entry,DecodeRequest request,long budget,CancellationToken token)
        {Opens++;Entered.TrySetResult();if(DelayOpen)await Complete.Task;var player=new VideoPlayer{DelayFirstFrame=DelayFirstFrame,CloseFailures=CloseFailures};Players.Add(player);return player;}
    }
    private sealed class VideoPlayer:ObservableObject,IVideoPlayer
    {
        public VideoInfo Info {get;}=new(new(640,320),TimeSpan.FromSeconds(100),true,true);
        public bool IsDisposed {get;private set;} public bool HasAudio=>true;public bool HasVideo=>true;
        public bool IsEnabled {get;set;}=true;public bool IsAudioEnabled {get;set;}=true;
        private bool _muted,_repeat,_playing;private double _position,_volume=.5,_rate=1;
        public bool IsMuted {get=>_muted;set=>SetProperty(ref _muted,value);}public bool IsRepeat {get=>_repeat;set=>SetProperty(ref _repeat,value);}
        public bool IsPlaying=>_playing;public TimeSpan Duration=>Info.Duration;
        public double Position {get=>_position;set=>SetProperty(ref _position,Math.Clamp(value,0,1));}
        public double Volume {get=>_volume;set=>SetProperty(ref _volume,value);}public double Rate {get=>_rate;set=>SetProperty(ref _rate,value);}
        public bool ScrubbingEnabled=>true;public bool RateEnabled=>true;public int EndOfStreamCount {get;private set;}
        public List<DecodeRequest> Requests {get;}=[];
        public bool DelayFirstFrame;
        public TaskCompletionSource FrameEntered {get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource FrameComplete {get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public DecodedImageLease? LateFrame;
        public event EventHandler? MediaPlayed;public event EventHandler? MediaEnded {add{}remove{}}public event EventHandler? MediaEndOfStreamReached;
        public void ReachEnd(){EndOfStreamCount++;MediaEndOfStreamReached?.Invoke(this,EventArgs.Empty);}
        public void Play(){_playing=true;OnPropertyChanged(nameof(IsPlaying));MediaPlayed?.Invoke(this,EventArgs.Empty);}
        public void Pause(){_playing=false;OnPropertyChanged(nameof(IsPlaying));}public void TogglePlay(){if(_playing)Pause();else Play();}
        public bool AddPosition(TimeSpan span){double value=Position+span.TotalSeconds/Duration.TotalSeconds;Position=value;return value<0||value>1;}
        public Task SynchronizeAsync(CancellationToken token){token.ThrowIfCancellationRequested();return Task.CompletedTask;}
        public async Task<DecodedImageLease?> ReadFrameAsync(DecodeRequest request,CancellationToken token)
        {
            token.ThrowIfCancellationRequested();Requests.Add(request);int w=Math.Min(64,request.TargetWidth),h=Math.Min(32,request.TargetHeight);
            var pixels=Enumerable.Range(0,w*h).SelectMany(_=>Position<.5?new byte[]{0,0,255,255}:new byte[]{0,255,0,255}).ToArray();
            var frame=new DecodedImageLease(new(w,h),pixels,sourceSize:Info.Size);
            if(DelayFirstFrame){DelayFirstFrame=false;FrameEntered.TrySetResult();await FrameComplete.Task;LateFrame=frame;}
            return frame;
        }
        public int CloseFailures,CloseAttempts;
        public void Dispose()=>IsDisposed=true;public ValueTask DisposeAsync(){CloseAttempts++;if(CloseAttempts<=CloseFailures)return ValueTask.FromException(new IOException("synthetic close failure"));Dispose();return ValueTask.CompletedTask;}
    }
}
