using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using System.Text.Json.Nodes;
using Avalonia.Input;
using Avalonia.Threading;
using NeeView.MacOS.ViewModels;
using NeeView.MacOS.Views;

namespace NeeView.Engine.Tests;

/// <summary>Loupe 的正式 ReaderView/MainWindow 集成回归；不绕过页面来源和输入路由。</summary>
public sealed class LoupeViewerTests
{
    [AvaloniaFact]
    public async Task CommandsAreBookScopedAndScaleCommandsOnlyWorkWhileEnabled()
    {
        using var f = new Fixture(); var state = new SaveData(f.State); await state.LoadAsync(Token);
        await using var op = f.Operation(state); var w = Window(op, state); w.Show();
        try
        {
            Assert.False(w.IsCommandAvailable("LoupeOn")); Assert.False(w.IsCommandAvailable("LoupeScaleUp"));
            await w.OpenAsync(f.Images); Pump(w);
            Assert.True(w.IsCommandAvailable("LoupeOn")); Assert.False(w.IsCommandAvailable("LoupeScaleUp"));
            await w.ExecuteAsync("LoupeOn"); Assert.True(w.Viewer.IsLoupeEnabled); Assert.True(w.IsCommandAvailable("LoupeScaleUp"));
            await w.ExecuteAsync("LoupeScaleUp"); Assert.Equal(3, w.Viewer.LoupeScale);
            await w.ExecuteAsync("LoupeOff"); Assert.False(w.Viewer.IsLoupeEnabled); Assert.False(w.IsCommandAvailable("LoupeScaleDown"));
        }
        finally { await w.PrepareShutdownAsync(); w.Close(); }
    }

    [AvaloniaFact]
    public async Task ToggleParameterHonorsShortcutModesAndMenuAlwaysToggles()
    {
        using var f = new Fixture(); var state = new SaveData(f.State); await state.LoadAsync(Token);
        await using var op = f.Operation(state); var w = Window(op, state); w.Show();
        try
        {
            await w.OpenAsync(f.Images); Pump(w);
            state.SetCommandParameter("ToggleIsLoupe", new ToggleCommandParameter { ToggleMode = ToggleMode.On });
            await w.ExecuteAsync("ToggleIsLoupe"); Assert.True(w.Viewer.IsLoupeEnabled);
            await w.ExecuteAsync("ToggleIsLoupe", true); Assert.False(w.Viewer.IsLoupeEnabled);
            await w.ExecuteAsync("ToggleIsLoupe", true); Assert.True(w.Viewer.IsLoupeEnabled);
        }
        finally { await w.PrepareShutdownAsync(); w.Close(); }
    }

    [AvaloniaFact]
    public async Task StopRetainsScaleUnlessRestartResetAndLeavesOrdinaryTransformUntouched()
    {
        using var f = new Fixture(); var state = new SaveData(f.State); await state.LoadAsync(Token);
        await using var op = f.Operation(state); var w = Window(op, state); w.Show();
        try
        {
            await w.OpenAsync(f.Images); Pump(w); await w.Viewer.ZoomAsync(2); var ordinary = w.Viewer.TransformScale; var rect = w.Viewer.GetContentRect();
            Config.Current.Loupe.IsResetByRestart = false; w.Viewer.SetLoupe(true); await w.Viewer.LoupePending; w.Viewer.LoupeZoom(1); var scale = w.Viewer.LoupeScale; w.Viewer.SetLoupe(false);
            Assert.Equal(ordinary, w.Viewer.TransformScale); Assert.Equal(rect, w.Viewer.GetContentRect()); w.Viewer.SetLoupe(true); Assert.Equal(scale, w.Viewer.LoupeScale);
            w.Viewer.SetLoupe(false); Config.Current.Loupe.IsResetByRestart = true; w.Viewer.SetLoupe(true); Assert.Equal(Config.Current.Loupe.DefaultScale, w.Viewer.LoupeScale);
        }
        finally { await w.PrepareShutdownAsync(); w.Close(); }
    }

    [AvaloniaFact]
    public async Task WheelEscapeAndMatrixFollowLoupeOwnership()
    {
        using var f = new Fixture(); var state = new SaveData(f.State); await state.LoadAsync(Token);
        await using var op = f.Operation(state); var w = Window(op, state); w.Show();
        try
        {
            await w.OpenAsync(f.Images); Pump(w); Assert.False(w.Viewer.TryLoupeWheel(0, 1)); w.Viewer.SetLoupe(true); await w.Viewer.LoupePending;
            var before = w.Viewer.LoupeScale; Assert.True(w.Viewer.TryLoupeWheel(0, 1)); Assert.True(w.Viewer.LoupeScale > before);
            Assert.False(w.Viewer.TryLoupeWheel(2, 1)); Config.Current.Loupe.IsWheelScalingEnabled = false; Assert.False(w.Viewer.TryLoupeWheel(0, 1));
            Assert.False(w.Viewer.TryLoupeEscape(Key.Escape, KeyModifiers.Control)); Assert.True(w.Viewer.TryLoupeEscape(Key.Escape, KeyModifiers.None)); Assert.False(w.Viewer.IsLoupeEnabled);
            Assert.Equal(Matrix.Identity, w.Viewer.LoupeMatrix); Assert.Equal(new Avalonia.Rect(w.Viewer.Bounds.Size), w.Viewer.LoupeVisibleRect);
        }
        finally { await w.PrepareShutdownAsync(); w.Close(); }
    }

    [Fact]
    public void SettingsDraftCancelAndApplyPreserveUneditedValues()
    {
        Config.SetCurrent(new()); Config.Current.Loupe.DefaultScale = 2.34567; Config.Current.Loupe.Speed = 12345.67891;
        var draft = new LoupeSettingsViewModel(Config.Current.Loupe); draft.DefaultScale = 4.2m;
        Assert.Equal(2.34567, Config.Current.Loupe.DefaultScale); Assert.Equal(12345.67891, Config.Current.Loupe.Speed);
        draft.Apply(Config.Current.Loupe); Assert.Equal(4.2, Config.Current.Loupe.DefaultScale); Assert.Equal(12345.67891, Config.Current.Loupe.Speed);
    }

    [AvaloniaTheory]
    [InlineData(BrowseLayoutMode.Paged, true, false)]
    [InlineData(BrowseLayoutMode.Paged, false, true)]
    [InlineData(BrowseLayoutMode.Panorama, true, true)]
    public async Task RangeResetKeepsOriginalPanoramaExceptionAndSourceSwitchAlwaysExits(BrowseLayoutMode mode,bool reset,bool expected)
    {
        using var f=new Fixture(); var state=new SaveData(f.State); await state.LoadAsync(Token);
        var op=f.Operation(state); var w=Window(op,state); w.Show();
        try
        {
            await w.OpenAsync(f.Images); await op.SetBrowseModeAsync(mode); await w.Viewer.RefreshAsync();
            Config.Current.Loupe.IsResetByPageChanged=reset;
            w.Viewer.SetLoupe(true); await w.Viewer.LoupePending; w.Viewer.LoupePanBy(new(10,20));
            await op.MoveAsync(1); await w.Viewer.RefreshAsync(); Assert.Equal(expected,w.Viewer.IsLoupeEnabled);
            if (expected) { Assert.NotEqual(default,w.Viewer.LoupePan); await w.OpenAsync(f.Zip); await w.Viewer.RefreshAsync(); Assert.False(w.Viewer.IsLoupeEnabled); }
        }
        finally { await w.PrepareShutdownAsync(); w.Close(); }
    }
    [AvaloniaFact]
    public async Task RelativeLeaseClosesOnBlurReplacementSourceAndShutdownAndRejectsOldCallbacks()
    {
        using var f=new Fixture(); var state=new SaveData(f.State); await state.LoadAsync(Token);
        var op=f.Operation(state); var w=Window(op,state); w.Show(); var input=new PointerInput();
        try
        {
            await w.OpenAsync(f.Images); await w.Viewer.RefreshAsync();
            w.Viewer.AttachLoupeInput(input,()=>123); w.Viewer.SetLoupe(true); var first=input.Leases.Single(); first.Move(5,9);
            Assert.NotEqual(default,w.Viewer.LoupePan); w.FindControl<TextBox>("AddressBar")!.Focus();
            // Real focus handoff must release before another control receives pointer input.
            Assert.True(first.Disposed); Assert.False(w.Viewer.IsLoupeEnabled);
            w.Viewer.SetLoupe(true); var second=input.Leases.Last(); var before=w.Viewer.LoupePan; first.Move(500,500); Assert.Equal(before,w.Viewer.LoupePan);
            w.Viewer.AttachLoupeInput(input,()=>123); Assert.True(second.Disposed);
            w.Viewer.SetLoupe(true); var third=input.Leases.Last(); await w.OpenAsync(f.Zip); await w.Viewer.RefreshAsync(); Assert.True(third.Disposed);
            w.Viewer.SetLoupe(true); var last=input.Leases.Last(); await w.PrepareShutdownAsync(); Assert.True(last.Disposed);
        }
        finally { await w.PrepareShutdownAsync(); w.Close(); }
    }
    [AvaloniaFact]
    public async Task NativeLeaseReleaseDisablesLoupeWithoutAvaloniaFocusEvent()
    {
        using var f=new Fixture(); var state=new SaveData(f.State); await state.LoadAsync(Token);
        var op=f.Operation(state); var w=Window(op,state); w.Show(); var input=new PointerInput();
        try
        {
            await w.OpenAsync(f.Images); await w.Viewer.RefreshAsync();
            w.Viewer.AttachLoupeInput(input,()=>123); w.Viewer.SetLoupe(true);
            var first=input.Leases.Single(); first.Dispose();
            Assert.False(w.Viewer.IsLoupeEnabled); Assert.False(w.IsCommandAvailable("LoupeScaleUp"));
            w.Viewer.SetLoupe(true); var pan=w.Viewer.LoupePan;
            first.ReleaseAgain(); first.Move(100,100);
            Assert.True(w.Viewer.IsLoupeEnabled); Assert.Equal(pan,w.Viewer.LoupePan);
        }
        finally { await w.PrepareShutdownAsync(); w.Close(); }
    }
    [AvaloniaFact]
    public async Task FrozenPointerAnchorAndDetailRequestDoNotMutateOrdinaryTransform()
    {
        using var f=new Fixture(); using (var image=new ImageMagick.MagickImage(ImageMagick.MagickColors.Red,1200,2400)) image.Write(Path.Combine(f.Images,"001.png"));
        var state=new SaveData(f.State); await state.LoadAsync(Token); var op=f.Operation(state); var w=Window(op,state); w.Show();
        try
        {
            await w.OpenAsync(f.Images); await w.Viewer.RefreshAsync(); var created=w.Viewer.BitmapCreationCount;
            var point=new Point(w.Viewer.Bounds.Width/2+40,w.Viewer.Bounds.Height/2+60);
            w.MouseMove(w.Viewer.TranslatePoint(point,w)!.Value); w.Viewer.SetLoupe(true); await w.Viewer.LoupePending;
            Assert.Equal(point,w.Viewer.LoupeMatrix.Transform(point)); Assert.True(w.Viewer.BitmapCreationCount>created);
            var basePoint=w.Viewer.LoupePan; w.Viewer.LoupeZoom(1); await w.Viewer.LoupePending; Assert.Equal(basePoint,w.Viewer.LoupePan);
            var ordinary=w.Viewer.GetContentRect(); w.Viewer.LoupePanBy(new(20,30)); Assert.Equal(ordinary,w.Viewer.GetContentRect());
            w.Viewer.SetLoupe(false); await w.Viewer.LoupePending; Assert.Equal(Matrix.Identity,w.Viewer.LoupeMatrix);
            Config.Current.Loupe.IsBaseOnOriginal=true; w.Viewer.SetLoupe(true); await w.Viewer.LoupePending;
            var element=op.Frame!.Elements.First(e=>!e.IsDummy); var normalScale=element.Scale*op.Frame.Scale*w.Viewer.TransformScale;
            Assert.Equal(w.Viewer.LoupeScale,normalScale*w.Viewer.LoupeFixedScale,6);
        }
        finally { await w.PrepareShutdownAsync(); w.Close(); }
    }
    [AvaloniaTheory]
    [InlineData(BrowseLayoutMode.Continuous)]
    [InlineData(BrowseLayoutMode.Masonry)]
    public async Task BrowseLoupeChangesOnlyPresentationAndReleasesOnModeChange(BrowseLayoutMode mode)
    {
        using var f=new Fixture(); var state=new SaveData(f.State); await state.LoadAsync(Token); var op=f.Operation(state); var w=Window(op,state); w.Show();
        try
        {
            await w.OpenAsync(f.Images); await op.SetBrowseModeAsync(mode); await w.Viewer.RefreshAsync();
            var layout=w.Viewer.BrowseLayout; var offset=w.Viewer.BrowseOffset; var scale=op.BrowseScale;
            w.Viewer.SetLoupe(true); await w.Viewer.LoupePending;
            for (int i=0;i<100;i++) w.Viewer.LoupePanBy(new(0,.1)); await w.Viewer.LoupePending;
            Assert.True(w.Viewer.IsLoupeEnabled); Assert.Same(layout,w.Viewer.BrowseLayout); Assert.Equal(offset,w.Viewer.BrowseOffset); Assert.Equal(scale,op.BrowseScale);
            using var rendered=w.CaptureRenderedFrame(); Assert.NotNull(rendered);
            await op.SetBrowseModeAsync(BrowseLayoutMode.Paged); await w.Viewer.RefreshAsync(); Assert.False(w.Viewer.IsLoupeEnabled);
        }
        finally { await w.PrepareShutdownAsync(); w.Close(); }
    }
    [AvaloniaFact]
    public async Task FormalLoupeSettingsCancelSaveAndFailureUseSameJsonTransaction()
    {
        using var f=new Fixture(); var state=new SaveData(f.State); await state.LoadAsync(Token); var op=f.Operation(state); var w=Window(op,state); w.Show();
        var blocked=Path.Combine(f.State,"UserSetting.json.tmp");
        try
        {
            var settings=new SettingsWindow(new(op,new(op),state)); settings.Show(w); settings.FindControl<ListBox>("SettingsNavigation")!.SelectedIndex=12;
            settings.FindControl<NumericUpDown>("LoupeDefaultScale")!.Value=7; settings.Close(); Assert.Equal(2,Config.Current.Loupe.DefaultScale);
            settings=new(new(op,new(op),state)); settings.Show(w); settings.FindControl<ListBox>("SettingsNavigation")!.SelectedIndex=12;
            settings.FindControl<NumericUpDown>("LoupeDefaultScale")!.Value=7; settings.FindControl<CheckBox>("LoupeIsBaseOnOriginal")!.IsChecked=true;
            Directory.CreateDirectory(blocked); settings.FindControl<Button>("SaveSettings")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Wait(()=>settings.FindControl<TextBlock>("Message")!.Text?.StartsWith("保存失败") == true); Assert.Equal(2,Config.Current.Loupe.DefaultScale); Assert.False(settings.WasSaved);
            Directory.Delete(blocked); settings.FindControl<Button>("SaveSettings")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Wait(()=>!settings.IsVisible);
            Assert.True(settings.WasSaved); await new SaveData(f.State).LoadAsync(Token); Assert.Equal(7,Config.Current.Loupe.DefaultScale); Assert.True(Config.Current.Loupe.IsBaseOnOriginal);
        }
        finally { if(Directory.Exists(blocked)) Directory.Delete(blocked); await w.PrepareShutdownAsync(); w.Close(); }
    }
    private static async Task Wait(Func<bool> done)
    { for(int i=0;i<400&&!done();i++) { Dispatcher.UIThread.RunJobs(); await Task.Delay(10,Token); } Assert.True(done()); }
    private sealed class PointerInput:IPlatformInput
    {
        public List<PointerLease> Leases { get; }=[];
        public void Attach(Func<PlatformGesture,bool> handler) { }
        public IDisposable? BeginRelativePointer(nint window,Action<double,double> handler,Action? released=null) { Assert.Equal(123,window); var lease=new PointerLease(handler,released); Leases.Add(lease); return lease; }
        public void Dispose() { foreach(var lease in Leases) lease.Dispose(); }
    }
    private sealed class PointerLease(Action<double,double> handler,Action? released):IDisposable
    {
        public bool Disposed { get; private set; }
        public void Move(double x,double y)=>handler(x,y);
        public void ReleaseAgain()=>released?.Invoke();
        public void Dispose() { if(Disposed)return; Disposed=true; released?.Invoke(); }
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private static MainWindow Window(BookOperation op, SaveData state) { var w = new MainWindow(); w.Bind(new ReaderWorkspaceViewModel(op, new CommandTable(op), state), new BitmapFactory(new NeeView.Backends.MagickImageDecoder()), new NoPlatform()); return w; }
    private static void Pump(Window w) { Dispatcher.UIThread.RunJobs(); w.UpdateLayout(); Dispatcher.UIThread.RunJobs(); }
    private sealed class NoPlatform : IPlatformService { public Task RevealAsync(string path, CancellationToken token) => Task.CompletedTask; public Task TrashAsync(string path, CancellationToken token) => Task.CompletedTask; }
}
