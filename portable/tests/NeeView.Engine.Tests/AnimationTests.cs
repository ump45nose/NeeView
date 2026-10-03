using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using NeeView;
using NeeView.PageFrames;
using NeeView.MacOS.Views;
namespace NeeView.Engine.Tests;

/// <summary>原动画方向、时长、取消与退出帧租约回归；不运行正式App。</summary>
public sealed class AnimationTests
{
    [Theory]
    [InlineData(PageReadOrder.LeftToRight, PageFrameOrientation.Horizontal, 1, 101, -150)]
    [InlineData(PageReadOrder.RightToLeft, PageFrameOrientation.Horizontal, 1, -501, -150)]
    [InlineData(PageReadOrder.LeftToRight, PageFrameOrientation.Horizontal, -1, -501, -150)]
    [InlineData(PageReadOrder.RightToLeft, PageFrameOrientation.Horizontal, -1, 101, -150)]
    [InlineData(PageReadOrder.LeftToRight, PageFrameOrientation.Vertical, 1, -200, 201)]
    [InlineData(PageReadOrder.RightToLeft, PageFrameOrientation.Vertical, -1, -200, -501)]
    public void OriginalAdjacentLayout(PageReadOrder order,PageFrameOrientation orientation,int direction,double x,double y)
    {
        var config=new Config(); config.Book.Orientation=orientation;
        var context=new PageFrameContext(new(){BookReadOrder=order},config);
        Assert.Equal(new NeeView.Rect(x,y,400,300),PageFrameContainerLayout.Layout(new(-100,-200,200,400),new(400,300),context,direction));
    }
    [Fact]
    public void OriginalZeroDurationAndInterpolationCancel()
    {
        var config=new Config(); config.View.PageMoveType=PageMoveType.Fade;
        var context=new PageFrameContext(new(),config); Assert.Equal(PageMoveType.Scroll,context.PageChangeType);
        config.View.PageMoveDuration=.5; Assert.Equal(PageMoveType.Fade,context.PageChangeType);
        Assert.Equal(new NeeView.Rect(-100,-200,400,300),PageFrameContainerLayout.Layout(new(-100,-200,200,400),new(400,300),context,1));
        double clock=0; var motion=new ReaderMotionPresenter(()=>clock);
        motion.BeginPan(default,new(100,0),TimeSpan.FromSeconds(1)); clock=.5; Assert.Equal(-25,motion.GetPanOffset().X);
        Assert.Equal(-25,motion.CancelPan().X); Assert.False(motion.IsActive);
        motion.BeginPan(default,new(100,0),TimeSpan.FromSeconds(1),true); clock=1; Assert.Equal(-50,motion.GetPanOffset().X);
        motion.BeginPage(new(200,0),PageMoveType.Fade,TimeSpan.FromSeconds(1)); clock=1.5;
        Assert.Equal(.5,motion.GetPageState().IncomingOpacity); Assert.Equal(.5,motion.GetPageState().OutgoingOpacity);
        motion.BeginPage(new(200,0),PageMoveType.Scroll,TimeSpan.FromSeconds(1)); clock=2;
        Assert.Equal(50,motion.GetPageState().Incoming.X); Assert.Equal(-150,motion.GetPageState().Outgoing.X);
        clock=3; Assert.False(motion.IsActive); motion.Clear(); Assert.Equal(0,motion.GetPageState().OutgoingOpacity);
        config.View.PageMoveDuration=double.NaN; Assert.Equal(TimeSpan.Zero,context.PageChangeDuration);
    }
    [AvaloniaTheory]
    [InlineData(PageMoveType.Scroll)]
    [InlineData(PageMoveType.Fade)]
    public async Task TransitionRetainsOneFrameAndReleasesOnFinishSwitchClose(PageMoveType type)
    {
        using var fixture=new Fixture(); var state=new SaveData(fixture.State); await state.LoadAsync(TestContext.Current.CancellationToken);
        var operation=fixture.Operation(state); using var factory=new BitmapFactory(new NeeView.Backends.MagickImageDecoder()){Budget=1};
        var reader=new ReaderView(); reader.Attach(operation,factory);
        var window=new Window{Width=600,Height=500,Content=reader}; window.Show(); Pump(window);
        try
        {
            await operation.OpenAsync(fixture.Images,TestContext.Current.CancellationToken); await reader.RefreshAsync();
            Config.Current.View.PageMoveDuration=.4; Config.Current.View.PageMoveType=type;
            await operation.MoveAsync(1); await reader.RefreshAsync(); Assert.True(reader.IsMotionActive); Assert.Equal(1,reader.TransitionDisplayCount); Assert.True(factory.ByteCount>0);
            await Task.Delay(100,TestContext.Current.CancellationToken); Pump(window); SaveFrame(window,type.ToString().ToLowerInvariant());
            await WaitAsync(()=>reader.TransitionDisplayCount==0); Assert.False(reader.IsMotionActive);
            await operation.MoveAsync(1); await reader.RefreshAsync(); Assert.Equal(1,reader.TransitionDisplayCount);
            await operation.MoveAsync(1); await reader.RefreshAsync(); Assert.Equal(1,reader.TransitionDisplayCount); // 快速翻页不累积旧帧
            await operation.OpenAsync(fixture.Zip,TestContext.Current.CancellationToken); await reader.RefreshAsync(); Assert.Equal(0,reader.TransitionDisplayCount);
            await operation.MoveAsync(1); await reader.RefreshAsync(); Assert.Equal(1,reader.TransitionDisplayCount);
            reader.Dispose(); Assert.Equal(0,reader.TransitionDisplayCount); Assert.Equal(0,reader.DisplayCount); Assert.Equal(0,factory.ByteCount);
        }
        finally { reader.Dispose(); await operation.DisposeAsync(); window.Close(); }
    }
    [AvaloniaFact]
    public async Task ContinuousWheelHoverAndScrollCommandsKeepOriginalPriorities()
    {
        using var fixture=new Fixture(); var state=new SaveData(fixture.State); await state.LoadAsync(TestContext.Current.CancellationToken); var operation=fixture.Operation(state);
        var window=new MainWindow(); window.Bind(new(operation,new CommandTable(operation),state),new BitmapFactory(new NeeView.Backends.MagickImageDecoder()),new NoPlatform()); window.Show();
        try
        {
            await window.OpenAsync(fixture.Images); await window.Viewer.ZoomAsync(3); Pump(window);
            var point=window.Viewer.TranslatePoint(new(250,250),window)!.Value;
            Config.Current.Mouse.IsMouseWheelScrollEnabled=true; Config.Current.Mouse.MouseWheelScrollDuration=0;
            var before=window.Viewer.GetContentRect(); window.MouseWheel(point,new(0,-1)); Assert.Equal(0,operation.Position.Index); Assert.NotEqual(before,window.Viewer.GetContentRect());
            state.SetShortcut("NextScrollPage",""); state.SetShortcut("NextOnePage","Ctrl+WheelDown"); window.MouseWheel(point,new(0,-1),RawInputModifiers.Control); await WaitAsync(()=>operation.Position.Index==1);
            Config.Current.Mouse.IsMouseWheelScrollEnabled=false; await window.Viewer.ZoomAsync(3);
            await window.ExecuteAsync("ToggleHoverScroll",true); Assert.True(Config.Current.Mouse.IsHoverScroll);
            Config.Current.Mouse.HoverScrollDuration=0; window.MouseMove(point+new Avalonia.Vector(100,80)); before=window.Viewer.GetContentRect();
            window.Viewer.ScrollView("ViewScrollDown",new()); Assert.Equal(before,window.Viewer.GetContentRect());
            window.Viewer.ScrollNType(1,new()); Assert.Equal(before,window.Viewer.GetContentRect());
            await window.ExecuteAsync("NextScrollPage",true); Assert.Equal(2,operation.Position.Index); // 悬停禁滚但保留分页
        }
        finally{await window.PrepareShutdownAsync();window.Close();}
    }
    [Fact]
    public async Task AnimationSettingsPersistInOriginalBranches()
    {
        using var fixture=new Fixture();Directory.CreateDirectory(fixture.State);
        await File.WriteAllTextAsync(Path.Combine(fixture.State,"UserSetting.json"),"""{"Config":{"View":{"PageMoveType":1,"PageMoveDuration":0.3,"ScrollDuration":0.4,"Future":7},"Mouse":{"IsHoverScroll":true,"HoverScrollSensitivity":3,"IsMouseWheelScrollEnabled":true,"MouseWheelScrollDuration":0.1,"Future":9}}}""",TestContext.Current.CancellationToken);
        var state=new SaveData(fixture.State);await state.LoadAsync(TestContext.Current.CancellationToken);Assert.Equal(PageMoveType.Fade,Config.Current.View.PageMoveType);Assert.True(Config.Current.Mouse.IsHoverScroll);
        await state.SaveAsync(null,0,TestContext.Current.CancellationToken);var json=JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(fixture.State,"UserSetting.json"),TestContext.Current.CancellationToken))!;
        Assert.Equal(7,json["Config"]!["View"]!["Future"]!.GetValue<int>());Assert.Equal(9,json["Config"]!["Mouse"]!["Future"]!.GetValue<int>());
    }
    private static void SaveFrame(Window window,string mode){using var frame=window.CaptureRenderedFrame();var phase=Environment.GetEnvironmentVariable("NEEVIEW_ACCEPTANCE_PHASE")??"p2-animation";frame!.Save(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,$"../../../../../acceptance/{phase}-{mode}-layout.png")),Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);}
    private static void Pump(Window w){Dispatcher.UIThread.RunJobs();w.UpdateLayout();}
    private static async Task WaitAsync(Func<bool> complete){for(int i=0;i<200&&!complete();i++){Dispatcher.UIThread.RunJobs();await Task.Delay(10,TestContext.Current.CancellationToken);}Assert.True(complete());}
    private sealed class NoPlatform:IPlatformService{public Task RevealAsync(string path,CancellationToken token=default)=>throw new NotSupportedException();public Task TrashAsync(string path,CancellationToken token=default)=>throw new NotSupportedException();}
}
