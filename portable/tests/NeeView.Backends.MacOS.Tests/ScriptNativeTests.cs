namespace NeeView.Backends.MacOS.Tests;

/// <summary>正式macOS运行时中的Jint CLR/委托映射和取消；无窗口、不启动外部程序。</summary>
public sealed class ScriptNativeTests
{
    [Fact]
    public void ActualMacRuntimeExecutesIncludePropertyMapAndLiteralDelegates()
    {
        var directory = Path.Combine(Path.GetTempPath(), "neeview-script-native-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        try
        {
            var logs = new List<object?>(); var calls = new List<(string, string?)>(); var sample = new Sample();
            using var runtime = new JintScriptRuntimeFactory().Create(logs.Add, (file, args) => calls.Add((file, args)), TestContext.Current.CancellationToken);
            runtime.SetValue("nv", new { Config = new PropertyMap("nv.Config", sample, null) });
            File.WriteAllText(Path.Combine(directory, "child.nvjs"), "nv.Config.Color='#123456'; nv.Config.Number=7; log(nv.Config.Number); system('/tmp/app','literal;args');");
            File.WriteAllText(Path.Combine(directory, "main.nvjs"), "include('child.nvjs'); sleep(0); nv.Config.Color;");
            Assert.Equal("#FF123456", runtime.ExecuteFile(Path.Combine(directory, "main.nvjs")));
            Assert.Equal(7, sample.Number); Assert.Single(logs); Assert.Equal(("/tmp/app", "literal;args"), Assert.Single(calls)); Assert.Null(runtime.ScriptPath);
        }
        finally { Directory.Delete(directory, true); }
    }
    [Fact]
    public async Task ActualMacRuntimeCancelsPureJavascriptLoop()
    {
        using var cancellation = new CancellationTokenSource(); using var runtime = new JintScriptRuntimeFactory().Create(_ => { }, (_, _) => { }, cancellation.Token);
        var started = new TaskCompletionSource(); runtime.SetValue("started", (Action)(() => started.TrySetResult()));
        var work = Task.Run(() => runtime.Evaluate("started(); while(true){}"), TestContext.Current.CancellationToken);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => work.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
    }
    public sealed class Sample { public int Number { get; set; } public ThemeRgba Color { get; set; } }
}
