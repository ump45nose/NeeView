using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using NeeView.Backends;
namespace NeeView.Engine.Tests;

public sealed class ScriptIntegrationTests
{
    [Fact]
    public void LiveConfigMapBuildsAndKeepsOriginalThemeNameAndReadOnlySlider()
    {
        var config = new Config(); var map = new ConfigMap(config, PropertyMapOptions.Create(new InlinePropertyMapDispatcher())).Map;
        var theme = Assert.IsType<PropertyMap>(map["Theme"]); theme["ThemeType"] = "Light";
        Assert.Equal("Light", config.Theme.ThemeString);
        Assert.Throws<KeyNotFoundException>(() => theme["CustomThemeFolderRaw"]);
        Assert.Throws<KeyNotFoundException>(() => map["ImageEffectCache"]);
        var slider = Assert.IsType<PropertyMap>(map["Slider"]); slider["IsEnabled"] = false; Assert.True(config.Slider.IsEnabled);
    }
    [Fact]
    public async Task RequestParameterScopesNestPairAndDoNotMutateOtherTasksOrSavedJson()
    {
        using var fixture = new Fixture(); var state = new SaveData(fixture.Root); await state.LoadAsync(TestContext.Current.CancellationToken);
        var defaults = state.GetMoveSizeParameter().Size;
        using (state.OverrideCommandParameter("NextSizePage", new() { ["Size"] = 7 }))
        {
            Assert.Equal(7, state.GetMoveSizeParameter().Size);
            using (state.OverrideCommandParameter("PrevSizePage", new() { ["Size"] = 9 })) Assert.Equal(9, state.GetMoveSizeParameter().Size);
            Assert.Equal(7, state.GetMoveSizeParameter().Size);
        }
        var ready = new TaskCompletionSource(); var finish = new TaskCompletionSource();
        var first = Task.Run(async () => { using var s = state.OverrideCommandParameter("NextSizePage", new() { ["Size"] = 11 }); ready.SetResult(); await finish.Task; Assert.Equal(11, state.GetMoveSizeParameter().Size); }, TestContext.Current.CancellationToken);
        await ready.Task; Assert.Equal(defaults, state.GetMoveSizeParameter().Size); finish.SetResult(); await first;
        Assert.Equal(8, state.GetDestinationParameter("MoveToDestinationFolder8").Index);
        await state.SaveAsync(null, TestContext.Current.CancellationToken); Assert.DoesNotContain("\"Size\": 11", await File.ReadAllTextAsync(Path.Combine(fixture.Root, "UserSetting.json"), TestContext.Current.CancellationToken));
    }
    [Fact]
    public async Task RealManagerRunsIsolatedEnginesWithSharedValuesAndCancelsPureLoops()
    {
        var shared = new ConcurrentDictionary<string, object?>(); var config = new ScriptConfig();
        await using var manager = new ScriptManager(new JintScriptRuntimeFactory(), run => new { Values = run.Values, Args = run.Args }, (_, _) => { }, config, shared);
        Assert.Equal(5d, await manager.EvaluateAsync("var hidden=10; nv.Values.x=5; nv.Values.x;", TestContext.Current.CancellationToken));
        Assert.Equal("undefined", await manager.EvaluateAsync("typeof hidden", TestContext.Current.CancellationToken)); Assert.Equal(5d, shared["x"]);
        var ready = new TaskCompletionSource(); manager.Log += (_, e) => { if (e.Message == "started") ready.TrySetResult(); };
        var loop = manager.EvaluateAsync("log('started');while(true){}", TestContext.Current.CancellationToken); await ready.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken); manager.CancelAll();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => loop.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
        for (var i = 0; i < 100 && manager.ActiveCount > 0; ++i) await Task.Delay(1, TestContext.Current.CancellationToken);
        Assert.Equal(0, manager.ActiveCount);
    }
    [Fact]
    public async Task DirectoryScanAndEventSwitchAreRealAndDoNotScanNestedScripts()
    {
        using var f = new Fixture(); Directory.CreateDirectory(Path.Combine(f.Root, "sub"));
        File.WriteAllText(Path.Combine(f.Root, "OnPageEnd.nvjs"), "nv.Values.direction=nv.Args[0];");
        File.WriteAllText(Path.Combine(f.Root, "sample.nvjs"), "/// @name A\n1;"); File.WriteAllText(Path.Combine(f.Root, "sub/hidden.nvjs"), "throw 1;");
        var config = new ScriptConfig { ScriptFolder = f.Root, IsScriptFolderEnabled = true }; var values = new ConcurrentDictionary<string, object?>();
        await using var manager = new ScriptManager(new JintScriptRuntimeFactory(), run => new { Values = run.Values, Args = run.Args }, (_, _) => { }, config, values);
        await manager.ReloadAsync(TestContext.Current.CancellationToken); Assert.Equal(2, manager.Sources.Count); await manager.RunEventAsync("OnPageEnd", [-1], TestContext.Current.CancellationToken); Assert.Equal(-1d, Convert.ToDouble(values["direction"]));
        config.IsScriptFolderEnabled = false; await manager.ReloadAsync(TestContext.Current.CancellationToken); Assert.Empty(manager.Sources); await manager.RunEventAsync("OnPageEnd", [1], TestContext.Current.CancellationToken); Assert.Equal(-1d, Convert.ToDouble(values["direction"]));
    }
}
