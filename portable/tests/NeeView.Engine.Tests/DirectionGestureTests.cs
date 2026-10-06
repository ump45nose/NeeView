using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using NeeView;
using NeeView.MacOS.Views;
using NeeView.MacOS.ViewModels;
namespace NeeView.Engine.Tests;

/// <summary>原方向判定/差分与正式输入链；不发送系统事件。</summary>
public sealed class DirectionGestureTests
{
    [Fact]
    public void OriginalThresholdAnglesAndClickSequence()
    {
        Config.SetCurrent(new()); var builder = new MouseSequenceBuilder(); builder.Reset(default);
        builder.AddClick(); Assert.True(builder.IsEmpty);
        builder.Move(new(29, 0)); Assert.True(builder.IsEmpty);
        builder.Move(new(30, 0)); Assert.Equal("R", builder.ToMouseSequence().ToString());
        builder.Move(new(60, 30)); Assert.Equal("R", builder.ToMouseSequence().ToString()); // 45度继续旧方向
        builder.Move(new(60, 60)); Assert.Equal("RD", builder.ToMouseSequence().ToString());
        builder.AddClick(); Assert.Equal("RDC", builder.ToMouseSequence().ToString());
        Assert.Equal("→↓Click", builder.ToMouseSequence().GetDisplayString());
        builder.Reset(default); builder.Move(new(30, 30)); Assert.True(builder.IsEmpty); // 首段斜线无方向
        Assert.Equal(new MouseSequence("rdc"), new MouseSequence("RDC")); Assert.True(new MouseSequence("bad").IsEmpty);
    }
    [Fact]
    public async Task OriginalDefaultsCustomUnbindAndUnknownFieldsPersist()
    {
        using var fixture = new Fixture(); var state = new SaveData(fixture.State); await state.LoadAsync(TestContext.Current.CancellationToken);
        var table = new CommandTable(fixture.Operation(state)); Assert.Equal(12, table.Definitions.Count(d => d.MouseGesture.Length > 0));
        Assert.Equal("L", state.GetMouseGesture("NextPage", "").ToString());
        Config.Current.Command.PresetPageReadOrder = PageReadOrder.LeftToRight;
        Assert.Equal("R", state.GetMouseGesture("NextPage", "").ToString());
        state.SetCommandParameter("NextPage", new ReversibleCommandParameter { IsReverse = false });
        state.SetMouseGestureDifference("NextPage", "LC", "L"); state.SetMouseGestureDifference("PrevPage", "", "R");
        await state.SaveAsync(null, TestContext.Current.CancellationToken);
        var fresh = new SaveData(fixture.State); await fresh.LoadAsync(TestContext.Current.CancellationToken);
        Assert.Equal("LC", fresh.GetMouseGesture("NextPage", "L").ToString()); Assert.True(fresh.GetMouseGesture("PrevPage", "R").IsEmpty);
        fresh.SetMouseGestureDifference("NextPage", "R", "L"); await fresh.SaveAsync(null, TestContext.Current.CancellationToken);
        var json = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(fixture.State, "UserSetting.json"), TestContext.Current.CancellationToken))!;
        Assert.Null(json["Commands"]?["NextPage"]?["MouseGesture"]); Assert.False(json["Commands"]!["PrevPage"]!["Parameter"]!["IsReverse"]!.GetValue<bool>());
        var restored = new SaveData(fixture.State); await restored.LoadAsync(TestContext.Current.CancellationToken);
        Assert.Equal("R", restored.GetMouseGesture("NextPage", "L").ToString());
    }
    [Fact]
    public void DirectionEditorSeparatesSequencesAndShowsConflicts()
    {
        var a = new ShortcutEdit(new("NextPage", "下一页", "", "", ""), "", true) { MouseGesture = "LC" };
        var b = new ShortcutEdit(new("PrevPage", "上一页", "", "", ""), "", true) { MouseGesture = "lc" };
        Assert.Contains("手势冲突", Assert.Throws<ArgumentException>(() => SettingsWindow.ValidateMouseGestures([a,b])).Message);
        b.MouseGesture = ""; SettingsWindow.ValidateMouseGestures([a,b]);
        a.MouseGesture = "Ctrl+L"; Assert.Throws<ArgumentException>(() => SettingsWindow.ValidateMouseGestures([a]));
    }
    [AvaloniaFact]
    public void ReleaseClickTerminatorAndCancelNeverDuplicateClick()
    {
        Config.SetCurrent(new()); var reader = new ReaderView { Width = 500, Height = 500, Focusable = true };
        var window = new Window { Width = 500, Height = 500, Content = reader }; var sequences = new List<string>(); var clicks = new List<string>();
        reader.TryMouseSequenceRequested = sequence => { sequences.Add(sequence.ToString()); return false; };
        reader.TryGestureRequested = value => { clicks.Add(value); return false; };
        reader.MouseSequenceText = _ => "下一页";
        window.Show(); Pump(window);
        try
        {
            window.MouseDown(new(250,250),MouseButton.Right); window.MouseMove(new(190,250),RawInputModifiers.RightMouseButton);
            Assert.Equal("下一页\n←", reader.MouseSequenceHint); Assert.Empty(sequences);
            window.MouseUp(new(190,250),MouseButton.Right); Assert.Equal(["L"],sequences); Assert.Empty(clicks); Assert.Equal("",reader.MouseSequenceHint);
            window.MouseDown(new(350,350),MouseButton.Right); window.MouseMove(new(290,350),RawInputModifiers.RightMouseButton);
            window.MouseDown(new(290,350),MouseButton.Left,RawInputModifiers.RightMouseButton);
            window.MouseUp(new(290,350),MouseButton.Left,RawInputModifiers.RightMouseButton); window.MouseUp(new(290,350),MouseButton.Right);
            Assert.Equal(["L","LC"],sequences); Assert.Empty(clicks);
            window.MouseDown(new(150,150),MouseButton.Right); window.MouseMove(new(90,150),RawInputModifiers.RightMouseButton); reader.CancelMouseSequence(); window.MouseUp(new(90,150),MouseButton.Right);
            Assert.Equal(2,sequences.Count); Assert.Empty(clicks);
            window.MouseDown(new(400,150),MouseButton.Right); window.MouseUp(new(400,150),MouseButton.Right); Assert.Single(clicks); Assert.Equal("RightClick",clicks[0]);
        }
        finally { reader.Dispose(); window.Close(); }
    }
    [AvaloniaFact]
    public async Task DefaultDirectionExecutesOriginalPageAndScopeCancels()
    {
        using var fixture = new Fixture(); var state = new SaveData(fixture.State); await state.LoadAsync(TestContext.Current.CancellationToken); var operation = fixture.Operation(state);
        var window = new MainWindow(); window.Bind(new(operation,new CommandTable(operation),state),new BitmapFactory(new NeeView.Backends.MagickImageDecoder()),new NoPlatform()); window.Show();
        try
        {
            await window.OpenAsync(fixture.Images); Pump(window); var point = window.Viewer.TranslatePoint(new(250,250),window)!.Value;
            window.MouseDown(point,MouseButton.Right); window.MouseMove(point-new Avalonia.Vector(60,0),RawInputModifiers.RightMouseButton); Assert.Equal(0,operation.Position.Index);
            window.MouseUp(point-new Avalonia.Vector(60,0),MouseButton.Right); await WaitAsync(()=>operation.Position.Index==1);
            point+=new Avalonia.Vector(0,80); window.MouseDown(point,MouseButton.Right); window.MouseMove(point-new Avalonia.Vector(60,0),RawInputModifiers.RightMouseButton);
            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null); window.KeyRelease(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null); window.MouseUp(point-new Avalonia.Vector(60,0),MouseButton.Right); Assert.Equal(1,operation.Position.Index);
            state.SetShortcut("ViewScaleUp", ""); state.SetShortcut("NextOnePage","RightButton+WheelUp");
            point+=new Avalonia.Vector(0,80); window.MouseDown(point,MouseButton.Right); window.MouseMove(point-new Avalonia.Vector(60,0),RawInputModifiers.RightMouseButton);
            window.MouseWheel(point,new(0,1),RawInputModifiers.RightMouseButton); await WaitAsync(()=>operation.Position.Index==2); window.MouseUp(point,MouseButton.Right); Assert.Equal(2,operation.Position.Index); Assert.Equal("",window.Viewer.MouseSequenceHint);
        }
        finally { await window.PrepareShutdownAsync(); window.Close(); }
    }
    private static void Pump(Window w) { Dispatcher.UIThread.RunJobs(); w.UpdateLayout(); }
    private static async Task WaitAsync(Func<bool> complete) { for(int i=0;i<150&&!complete();i++){ Dispatcher.UIThread.RunJobs(); await Task.Delay(10,TestContext.Current.CancellationToken); } Assert.True(complete()); }
    private sealed class NoPlatform : IPlatformService
    { public Task RevealAsync(string path,CancellationToken token=default)=>throw new NotSupportedException(); public Task TrashAsync(string path,CancellationToken token=default)=>throw new NotSupportedException(); }
}
