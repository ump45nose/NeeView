using System.Collections.Concurrent;
using NeeView.Backends;
namespace NeeView.Engine.Tests;

/// <summary>实际Jint执行、原CLR映射、include和可中断性，不用伪后端证明脚本可运行。</summary>
public sealed class ScriptRuntimeTests
{
    private static IScriptRuntime Create(CancellationToken token) => new JintScriptRuntimeFactory().Create(_ => { }, (_, _) => { }, token);

    [Fact]
    public void PropertyMapIsUsableWithOriginalJavascriptDotAndIndexerSyntax()
    {
        var source = new PropertyMapMigrationTests.Settings();
        using var runtime = Create(TestContext.Current.CancellationToken); runtime.SetValue("nv", new { Config = new PropertyMap("nv.Config", source, null) });
        var result = runtime.Evaluate("nv.Config.Direction = 'RightToLeft'; nv.Config['Words']='one;two'; nv.Config.Child.Value=8; nv.Config.Direction;");
        Assert.Equal("RightToLeft", result); Assert.Equal(8, source.Child.Value); Assert.Equal("one;two", source.Words.ToString());
    }

    [Fact]
    public void NestedIncludeUsesOneEngineAndRestoresRelativePathStack()
    {
        using var fixture = new Fixture(); var directory = Path.Combine(fixture.Root, "scripts"); Directory.CreateDirectory(Path.Combine(directory, "nested"));
        File.WriteAllText(Path.Combine(directory, "main.nvjs"), "var n=2; include('nested/child.nvjs'); include('sibling.nvjs'); n;");
        File.WriteAllText(Path.Combine(directory, "nested/child.nvjs"), "n+=3; log(path.ScriptDirectory);");
        File.WriteAllText(Path.Combine(directory, "sibling.nvjs"), "n+=4; log(path.ScriptDirectory);");
        var paths = new List<object?>(); using var runtime = new JintScriptRuntimeFactory().Create(paths.Add, (_, _) => { }, TestContext.Current.CancellationToken); runtime.SetValue("path", runtime);
        Assert.Equal(9d, runtime.ExecuteFile(Path.Combine(directory, "main.nvjs")));
        Assert.Equal(new[] { Path.Combine(directory, "nested"), directory }, paths.Cast<string>());
        Assert.Null(runtime.ScriptPath); Assert.Null(runtime.ScriptDirectory);
    }

    [Theory]
    [InlineData("while(true){}")]
    [InlineData("sleep(-1)")]
    public async Task PureLoopAndSleepCanBothBeCancelled(string source)
    {
        using var cancellation = new CancellationTokenSource(); using var runtime = Create(cancellation.Token);
        var started = new TaskCompletionSource(); runtime.SetValue("started", (Action)(() => started.TrySetResult()));
        var work = Task.Run(() => runtime.Evaluate("started();" + source), TestContext.Current.CancellationToken);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => work.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
        Assert.Null(runtime.ScriptPath);
    }

    [Fact]
    public void IndependentEnginesCanUseProcessSharedValuesWithoutSharingGlobals()
    {
        var values = new ConcurrentDictionary<string, object?>();
        using (var first = Create(TestContext.Current.CancellationToken)) { first.SetValue("nv", new { Values = values }); first.Evaluate("var privateValue=17;nv.Values.key=6;"); }
        using var second = Create(TestContext.Current.CancellationToken); second.SetValue("nv", new { Values = values });
        Assert.Equal(6d, second.Evaluate("nv.Values.key")); Assert.Equal("undefined", second.Evaluate("typeof privateValue"));
    }

    [Fact]
    public void OriginalColorConverterAndLiteralSystemArgumentsRemain()
    {
        var color = new ColorSource(); var calls = new List<(string, string?)>();
        using var runtime = new JintScriptRuntimeFactory().Create(_ => { }, (path, arguments) => calls.Add((path, arguments)), TestContext.Current.CancellationToken);
        runtime.SetValue("sample", color); Assert.Equal("#FF123456", runtime.Evaluate("sample.Color='#123456'; sample.Color;"));
        runtime.Evaluate("system('/test/app', '\"file name\";literal');"); Assert.Equal(("/test/app", "\"file name\";literal"), Assert.Single(calls));
    }

    [Theory]
    [InlineData("var n=1;\nvar x=;", 2)]
    [InlineData("var n=1;\nthrow new Error('bad');", 2)]
    public void ErrorsKeepRealSourceAndLine(string script, int line)
    {
        using var runtime = Create(TestContext.Current.CancellationToken); var error = Assert.Throws<ScriptExecutionException>(() => runtime.Evaluate(script, "/test/error.nvjs"));
        Assert.Equal("/test/error.nvjs", error.SourcePath); Assert.Equal(line, error.Line); Assert.Null(runtime.ScriptPath);
    }

    [Fact]
    public void IncludeFailureAndDisposeRestoreContextAndPreventFutureReads()
    {
        using var fixture = new Fixture(); var runtime = Create(TestContext.Current.CancellationToken);
        var file = Path.Combine(fixture.Root, "bad.nvjs"); File.WriteAllText(file, "include('missing.nvjs');");
        Assert.Throws<ScriptExecutionException>(() => runtime.ExecuteFile(file)); Assert.Null(runtime.ScriptPath); Assert.Null(runtime.ScriptDirectory);
        runtime.Dispose(); Assert.Throws<ObjectDisposedException>(() => runtime.ExecuteFile(file)); Assert.Throws<ObjectDisposedException>(() => runtime.SetValue("x", 1));
    }
    public sealed class ColorSource { public ThemeRgba Color { get; set; } }
}
