using System.IO.Compression;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using ImageMagick;
using NeeView.Backends;
using NeeView.MacOS.ViewModels;
using NeeView.MacOS.Views;
using NeeView.Tests;
namespace NeeView.Engine.Tests;

/// <summary>原JSON、目录/ZIP、唯一查看器与租约的闭环回归，不以播放器状态替代实际像素。</summary>
public sealed class AnimatedIntegrationTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    [Fact]
    public async Task OriginalDefaultsDifferenceAndUnknownFieldsSurviveRestart()
    {
        using var f=new Fixture();Directory.CreateDirectory(f.State);
        File.WriteAllText(Path.Combine(f.State,"UserSetting.json"),"""{"Config":{"Image":{"Standard":{"Future":7},"Future":8},"Archive":{"Media":{"Future":9,"IsRepeat":false}}}}""");
        var state=new SaveData(f.State);await state.LoadAsync(Token);
        Assert.True(Config.Current.Image.IsMediaRepeat);Assert.True(Config.Current.Image.Standard.IsAnimatedPngEnabled);Assert.Equal(10,Config.Current.Archive.Media.PageSeconds);
        await state.SaveAsync(null,Token);var defaults=JsonNode.Parse(File.ReadAllText(Path.Combine(f.State,"UserSetting.json")))!;
        Assert.Null(defaults["Config"]!["Image"]!["IsMediaRepeat"]);Assert.Null(defaults["Config"]!["Image"]!["Standard"]!["IsAnimatedGifEnabled"]);
        Config.Current.Image.Standard.IsAnimatedGifEnabled=false;Config.Current.Image.IsMediaRepeat=false;Config.Current.Archive.Media.PageSeconds=.123456;
        await state.SaveAsync(null,Token);await new SaveData(f.State).LoadAsync(Token);
        Assert.False(Config.Current.Image.Standard.IsAnimatedGifEnabled);Assert.False(Config.Current.Image.IsMediaRepeat);Assert.Equal(.12346,Config.Current.Archive.Media.PageSeconds);
        var raw=JsonNode.Parse(File.ReadAllText(Path.Combine(f.State,"UserSetting.json")))!;
        Assert.Equal(7,raw["Config"]!["Image"]!["Standard"]!["Future"]!.GetValue<int>());Assert.Equal(8,raw["Config"]!["Image"]!["Future"]!.GetValue<int>());
        Assert.Equal(9,raw["Config"]!["Archive"]!["Media"]!["Future"]!.GetValue<int>());Assert.Null(raw["Config"]!["Archive"]!["Media"]!["IsRepeat"]);Assert.False(Config.Current.Archive.Media.IsRepeat);
    }
    [Fact]
    public async Task MediaCommandsKeepIndependentDeltaDefaultsAndUnknownJson()
    {
        using var f = new Fixture(); var state = new SaveData(f.State); await state.LoadAsync(Token);
        await using var operation = f.Operation(state);
        using var player = new AnimatedMediaPlayer(new(new(2, 2), AnimatedImageType.Gif,
            Enumerable.Repeat(TimeSpan.FromSeconds(5), 8).ToArray(), 0));
        operation.CurrentMediaPlayer = player;
        var commands = new CommandTable(operation);
        state.SetCommandParameter("NextMediaPosition", new MoveMediaPositionCommandParameter { Delta = 10 });
        state.SetCommandParameter("PrevMediaPosition", new MoveMediaPositionCommandParameter { Delta = 15 });
        await commands.ExecuteAsync("NextMediaPosition"); Assert.Equal(2, player.CurrentFrameIndex);
        state.SetCommandParameter("NextMediaPosition", new MoveMediaPositionCommandParameter());
        Config.Current.Archive.Media.PageSeconds = 5;
        await commands.ExecuteAsync("NextMediaPosition"); Assert.Equal(3, player.CurrentFrameIndex);
        await commands.ExecuteAsync("PrevMediaPosition"); Assert.Equal(0, player.CurrentFrameIndex);
        var draft = CommandParameterEdit.Create(state, "PrevMediaPosition")!;
        Assert.Equal("PrevMediaPosition", draft.Owner);
        Assert.IsType<MoveMediaPositionCommandParameter>(draft.Value).Delta = .123456;
        Assert.Equal(15, state.GetCommandParameter<MoveMediaPositionCommandParameter>("PrevMediaPosition").Delta);
        draft.Apply(state); await state.SaveAsync(null, Token); await new SaveData(f.State).LoadAsync(Token);
        var saved = JsonNode.Parse(File.ReadAllText(Path.Combine(f.State, "UserSetting.json")))!;
        Assert.Equal(.12346, saved["Commands"]!["PrevMediaPosition"]!["Parameter"]!["Delta"]!.GetValue<double>());
        Assert.Null(saved["Commands"]?["NextMediaPosition"]?["Parameter"]?["Delta"]);
        Assert.Equal(0, new MoveMediaPositionCommandParameter { Delta = -1 }.Delta);
    }
    [Fact]
    public async Task RepeatAndSettingsFailuresRollbackThenRetry()
    {
        using var f=new Fixture();var state=new SaveData(f.State);await state.LoadAsync(Token);await state.SaveAsync(null,Token);
        await using var operation=f.Operation(state);using var player=new AnimatedMediaPlayer(new(new(2,2),AnimatedImageType.Gif,[TimeSpan.FromMilliseconds(100),TimeSpan.FromMilliseconds(300)],32));operation.CurrentMediaPlayer=player;
        var blocked=Path.Combine(f.State,"UserSetting.json.tmp");Directory.CreateDirectory(blocked);
        try
        {
            await Assert.ThrowsAnyAsync<Exception>(()=>operation.SetImageMediaRepeatAsync(false));Assert.True(player.IsRepeat);Assert.True(Config.Current.Image.IsMediaRepeat);
            await Assert.ThrowsAnyAsync<Exception>(()=>operation.ApplyOptionsAsync(()=>{Config.Current.Image.Standard.IsAnimatedGifEnabled=false;Config.Current.Archive.Media.PageSeconds=1;},(-1,TimeSpan.Zero)));
            Assert.True(Config.Current.Image.Standard.IsAnimatedGifEnabled);Assert.Equal(10,Config.Current.Archive.Media.PageSeconds);
        }
        finally {Directory.Delete(blocked);}
        await operation.SetImageMediaRepeatAsync(false);Assert.False(player.IsRepeat);await new SaveData(f.State).LoadAsync(Token);Assert.False(Config.Current.Image.IsMediaRepeat);
    }
    [Theory]
    [InlineData(AnimatedImageType.Gif,false)]
    [InlineData(AnimatedImageType.Gif,true)]
    [InlineData(AnimatedImageType.Webp,false)]
    [InlineData(AnimatedImageType.Webp,true)]
    public async Task ActualSourceFramesShareFactoryAndReleaseAllAccounting(AnimatedImageType format,bool zip)
    {
        using var f=new Fixture();var path=PutAnimation(f,format,zip);var state=new SaveData(f.State);await state.LoadAsync(Token);
        await using var operation=f.Operation(state);await operation.OpenAsync(path,Token);var page=operation.Book!.Pages.First(p=>p.EntryName.EndsWith(format==AnimatedImageType.Gif?".gif":".webp"));
        using var factory=new BitmapFactory(new MagickImageDecoder());using(var source=await factory.OpenAnimationAsync(page,new(20,20),Token))
        {
            Assert.NotNull(source);Assert.True(factory.GetDiagnostics().AnimationBytes>0);
            using(var frame=await source.ReadFrameAsync(1,Token)){frame.RegisterDisplayBytes(16);Assert.True(factory.ByteCount>source.Info.ResourceBytes);Assert.True(frame.Image.Pixels[1]>240);}
        }
        Assert.Equal(0,factory.ByteCount);
        Config.Current.Image.Standard.IsAnimatedGifEnabled=false;Config.Current.Image.Standard.IsAnimatedWebpEnabled=false;
        Assert.Null(await factory.OpenAnimationAsync(page,new(20,20),Token));Assert.Null(await factory.OpenAnimationAsync(page,new(20,20,true),Token));
    }
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FormalReaderPlaysPausesSeeksThenClearsOnBrowseAndClose(bool zip)
    {
        using var f=new Fixture();var path=PutAnimation(f,AnimatedImageType.Gif,zip);var state=new SaveData(f.State);await state.LoadAsync(Token);
        var operation=f.Operation(state);var factory=new BitmapFactory(new MagickImageDecoder());var window=new MainWindow();window.Bind(new(operation,new(operation),state),factory,new NoPlatform());window.Show();
        try
        {
            await window.OpenAsync(path);await operation.JumpAsync(operation.Book!.Pages.First(p=>p.EntryName.EndsWith("000.gif")).Index);await window.Viewer.RefreshAsync();await Wait(()=>operation.MediaExists(),()=> $"page={operation.Book?.CurrentPage?.EntryName}; type={operation.Book?.CurrentPage?.PageType}; error={window.Viewer.GetPageError(operation.Book!.CurrentPage!)}; animations={window.Viewer.AnimationCount}; displays={window.Viewer.DisplayCount}; bounds={window.Viewer.Bounds}");var player=Assert.IsType<AnimatedMediaPlayer>(operation.CurrentMediaPlayer);
            player.Pause();player.Position=0;await window.Viewer.RefreshMediaAsync();Dispatcher.UIThread.RunJobs();window.UpdateLayout();var red=CenterPixel(window);
            Assert.True(red[2]>240&&red[1]<10);player.Position=1;await window.Viewer.RefreshMediaAsync();Dispatcher.UIThread.RunJobs();var green=CenterPixel(window);Assert.True(green[1]>240&&green[2]<10);
            var control=window.FindControl<MediaControlView>("DockMediaControlSocket")!;Assert.True(control.IsVisible);
            // 程序回显保持seek原意图；按钮和原命令共用播放器，不重新解码来源。
            await control.SeekAsync(0);Assert.Equal(0,player.CurrentFrameIndex);operation.ToggleMediaPlay();Assert.True(player.IsPlaying);await window.Viewer.RefreshMediaAsync();
            await Wait(()=>player.CurrentFrameIndex==1);operation.ToggleMediaPlay();Assert.False(player.IsPlaying);
            if(Environment.GetEnvironmentVariable("NEEVIEW_P5_ANIMATION_SCREENSHOT") is { } screenshot){using var frame=window.CaptureRenderedFrame();frame!.Save(screenshot,Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);}
            await operation.SetBrowseModeAsync(BrowseLayoutMode.Masonry);await window.Viewer.RefreshAsync();Assert.False(operation.MediaExists());Assert.Equal(0,window.Viewer.AnimationCount);Assert.False(control.IsVisible);Assert.Equal(0,factory.GetDiagnostics().AnimationBytes);
            await operation.SetBrowseModeAsync(BrowseLayoutMode.Paged);await window.Viewer.RefreshAsync();await Wait(()=>operation.MediaExists(),()=> $"after browse page={operation.Book?.CurrentPage?.EntryName}; position={operation.Position}; error={window.Viewer.GetPageError(operation.Book!.CurrentPage!)}; animations={window.Viewer.AnimationCount}; displays={window.Viewer.DisplayCount}");
            var staticPage=operation.Book!.Pages.First(p=>p.EntryName.EndsWith("001.png"));await operation.JumpAsync(staticPage.Index);await window.Viewer.RefreshAsync();Assert.False(operation.MediaExists());Assert.Equal(0,factory.GetDiagnostics().AnimationBytes);
        }
        finally {await window.PrepareShutdownAsync();window.Close();}
        await Wait(()=>factory.ByteCount==0);
    }
    [Fact]
    public async Task FactoryCloseRejectsLateNativeFrameAndReleasesOnlyAfterRealCompletion()
    {
        using var f=new Fixture();var path=PutAnimation(f,AnimatedImageType.Gif,false);var state=new SaveData(f.State);await state.LoadAsync(Token);
        await using var operation=f.Operation(state);await operation.OpenAsync(path,Token);var decoder=new DelayedDecoder();using var factory=new BitmapFactory(decoder);
        using var source=await factory.OpenAnimationAsync(operation.Book!.CurrentPage!,new(2,2),Token);Assert.NotNull(source);
        var reading=source.ReadFrameAsync(1,Token);await decoder.Source.Entered.Task;
        factory.Dispose();Assert.False(decoder.Source.Disposed);Assert.True(factory.ByteCount>0);
        decoder.Source.Complete.SetResult();await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>reading);
        Assert.True(decoder.Source.Disposed);Assert.Equal(0,factory.ByteCount);
    }
    private sealed class DelayedDecoder:IImageDecoder,IAnimatedImageDecoder
    {
        public DelayedSource Source {get;}=new();
        public Task<ImageInfo> ProbeAsync(Stream stream,CancellationToken token)=>new MagickImageDecoder().ProbeAsync(stream,token);
        public Task<DecodedImageLease> DecodeAsync(Stream stream,DecodeRequest request,CancellationToken token)=>new MagickImageDecoder().DecodeAsync(stream,request,token);
        public Task<IAnimatedImageSource?> OpenAnimationAsync(Stream stream,DecodeRequest request,long budget,CancellationToken token)=>Task.FromResult<IAnimatedImageSource?>(Source);
    }
    private sealed class DelayedSource:IAnimatedImageSource
    {
        public AnimatedImageInfo Info {get;}=new(new(2,2),AnimatedImageType.Gif,[TimeSpan.FromMilliseconds(100),TimeSpan.FromMilliseconds(300)],32);
        public TaskCompletionSource Entered {get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Complete {get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool Disposed {get;private set;}
        public async Task<DecodedImageLease> ReadFrameAsync(int index,CancellationToken token){Entered.SetResult();await Complete.Task;return new(new(2,2),new byte[16]);}
        public void Dispose()=>Disposed=true;
    }
    [AvaloniaFact]
    public async Task CanceledFirstFrameDoesNotCacheSuccessfulAttemptAndBlockNextRevision()
    {
        using var f=new Fixture();var path=PutAnimation(f,AnimatedImageType.Gif,false);var state=new SaveData(f.State);await state.LoadAsync(Token);
        await using var operation=f.Operation(state);await operation.OpenAsync(path,Token);
        var decoder=new FirstFrameGateDecoder();using var factory=new BitmapFactory(decoder);var viewer=new ReaderView();var window=new Window{Width=500,Height=400,Content=viewer};window.Show();window.UpdateLayout();viewer.Attach(operation,factory);
        try
        {
            var first=viewer.RefreshAsync();await decoder.Entered.Task;
            var next=viewer.RefreshAsync();await next;Assert.True(operation.MediaExists());
            decoder.Complete.SetResult();await first;Assert.True(operation.MediaExists());Assert.Equal(1,viewer.AnimationCount);
        }
        finally {decoder.Complete.TrySetResult();viewer.Dispose();window.Close();}
    }
    private sealed class FirstFrameGateDecoder:IImageDecoder,IAnimatedImageDecoder
    {
        private readonly MagickImageDecoder _decoder=new();private int _opens;
        public TaskCompletionSource Entered {get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Complete {get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<ImageInfo> ProbeAsync(Stream stream,CancellationToken token)=>_decoder.ProbeAsync(stream,token);
        public Task<DecodedImageLease> DecodeAsync(Stream stream,DecodeRequest request,CancellationToken token)=>_decoder.DecodeAsync(stream,request,token);
        public async Task<IAnimatedImageSource?> OpenAnimationAsync(Stream stream,DecodeRequest request,long budget,CancellationToken token)
        {var source=await _decoder.OpenAnimationAsync(stream,request,budget,token);return source is not null&&Interlocked.Increment(ref _opens)==1?new FirstFrameGateSource(source,Entered,Complete):source;}
    }
    private sealed class FirstFrameGateSource(IAnimatedImageSource source,TaskCompletionSource entered,TaskCompletionSource complete):IAnimatedImageSource
    {
        public AnimatedImageInfo Info=>source.Info;
        public async Task<DecodedImageLease> ReadFrameAsync(int index,CancellationToken token){entered.TrySetResult();await complete.Task;return await source.ReadFrameAsync(index,token);}
        public void Dispose()=>source.Dispose();
    }
    [AvaloniaFact]
    public async Task AnimationSettingsAreDraftUntilSuccessfulOriginalTransaction()
    {
        using var f=new Fixture();var state=new SaveData(f.State);await state.LoadAsync(Token);
        var operation=f.Operation(state);var images=new BitmapFactory(new MagickImageDecoder());var model=new ReaderWorkspaceViewModel(operation,new(operation),state);var window=new MainWindow();window.Bind(model,images,new NoPlatform());window.Show();
        try
        {
            var settings=new SettingsWindow(model);settings.Show(window);settings.FindControl<ListBox>("SettingsNavigation")!.SelectedIndex=9;
            settings.FindControl<CheckBox>("AnimatedGifEnabled")!.IsChecked=false;settings.Close();Assert.True(Config.Current.Image.Standard.IsAnimatedGifEnabled);
            settings=new SettingsWindow(model);settings.Show(window);settings.FindControl<ListBox>("SettingsNavigation")!.SelectedIndex=9;
            settings.FindControl<CheckBox>("AnimatedGifEnabled")!.IsChecked=false;settings.FindControl<NumericUpDown>("MediaPageSeconds")!.Value=2;
            if(Environment.GetEnvironmentVariable("NEEVIEW_P5_ANIMATION_SETTINGS_SCREENSHOT") is { } screenshot){Dispatcher.UIThread.RunJobs();settings.UpdateLayout();var scroll=settings.FindControl<ScrollViewer>("ArchiveSettings")!;scroll.Offset=new Avalonia.Vector(0,scroll.Extent.Height);Dispatcher.UIThread.RunJobs();settings.UpdateLayout();using var frame=settings.CaptureRenderedFrame();frame!.Save(screenshot,Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);}
            settings.FindControl<Button>("SaveSettings")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Wait(()=>!settings.IsVisible);
            Assert.True(settings.WasSaved);Assert.False(Config.Current.Image.Standard.IsAnimatedGifEnabled);Assert.Equal(2,Config.Current.Archive.Media.PageSeconds);
            await new SaveData(f.State).LoadAsync(Token);Assert.False(Config.Current.Image.Standard.IsAnimatedGifEnabled);
        }
        finally{await window.PrepareShutdownAsync();window.Close();}
    }
    private static string PutAnimation(Fixture f,AnimatedImageType type,bool zip)
    {
        var file=Path.Combine(f.Images,type==AnimatedImageType.Gif?"000.gif":"000.webp");File.WriteAllBytes(file,AnimationFixture.Create(type));
        if(!zip)return file;var path=Path.Combine(f.Root,"animated.cbz");ZipFile.CreateFromDirectory(f.Images,path);return path;
    }
    private static byte[] CenterPixel(MainWindow window)
    {
        using var frame=window.CaptureRenderedFrame();Assert.NotNull(frame);using var output=new MemoryStream();frame.Save(output,Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);output.Position=0;using var image=new MagickImage(output);
        var point=window.Viewer.TranslatePoint(new Point(window.Viewer.Bounds.Width/2,window.Viewer.Bounds.Height/2),window)!.Value;
        var bytes=image.ToByteArray(MagickFormat.Bgra);var offset=checked(((int)point.Y*(int)image.Width+(int)point.X)*4);return bytes.Skip(offset).Take(4).ToArray();
    }
    private static async Task Wait(Func<bool> done,Func<string>? details=null){for(int i=0;i<400&&!done();i++){Dispatcher.UIThread.RunJobs();await Task.Delay(10,Token);}Assert.True(done(),details?.Invoke());}
    private sealed class NoPlatform:IPlatformService{public Task RevealAsync(string path,CancellationToken token=default)=>Task.CompletedTask;public Task TrashAsync(string path,CancellationToken token=default)=>Task.CompletedTask;}
}
