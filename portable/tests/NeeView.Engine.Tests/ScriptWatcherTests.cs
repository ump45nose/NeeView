using System.Reflection;
namespace NeeView.Engine.Tests;
public sealed class ScriptWatcherTests
{
    [Fact]
    public async Task ScriptWatcherRebuildsAfterErrorAndAcceptsUpperCaseExtension()
    {
        using var f = new Fixture(); var folder = Directory.CreateDirectory(Path.Combine(f.Root, "Watch")).FullName;
        using var watcher = new ScriptFolderWatcher(); var changed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        watcher.Changed += (_, _) => changed.TrySetResult(); watcher.Start(folder);
        var field = typeof(ScriptFolderWatcher).GetField("_watcher", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var before = field.GetValue(watcher);
        typeof(ScriptFolderWatcher).GetMethod("OnError", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(watcher, [before, new ErrorEventArgs(new InternalBufferOverflowException())]);
        Assert.NotNull(field.GetValue(watcher)); Assert.NotSame(before, field.GetValue(watcher));
        File.WriteAllText(Path.Combine(folder, "UPPER.NVJS"), "1");
        await changed.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        watcher.Stop(); Assert.Null(field.GetValue(watcher));
    }
    [Theory]
    [InlineData("nv.Config.Th", 12, 0, "nv.Config.Th")]
    [InlineData("log(nv.Book.Pa", 14, 4, "nv.Book.Pa")]
    [InlineData("1 + nv.", 7, 4, "nv.")]
    public void ScriptCompletionReplacesOnlyCurrentMember(string text, int caret, int start, string prefix)
    { var result = ScriptCompletion.Prefix(text, caret); Assert.Equal(start, result.Start); Assert.Equal(prefix, result.Prefix); }
}
