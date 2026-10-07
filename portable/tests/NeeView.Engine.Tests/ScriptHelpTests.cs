using NeeView;
using Xunit;

namespace NeeView.Engine.Tests;

public sealed class ScriptHelpTests
{
    [Fact]
    public async Task ScriptReferenceDocumentsPortableSurfaceAndBuiltins()
    {
        using var fixture = new Fixture();
        var state = new SaveData(fixture.Root); await state.LoadAsync(TestContext.Current.CancellationToken);
        await using var operation = fixture.Operation(state);
        var html = ScriptReferenceDocument.Create(new ConfigMap(Config.Current), new CommandTable(operation), typeof(NeeView.MacOS.ViewModels.ScriptApplicationHost));
        Assert.Contains("nv.Config", html);
        Assert.Contains("include(path)", html);
        Assert.Contains("sleep(milliseconds)", html);
        Assert.Contains("system(command, argument)", html);
        Assert.Contains("Migration status", html);
        Assert.Contains("[Root Instance] nv", html);
        Assert.Contains("nv.Config.Script.IsScriptFolderEnabled", html);
        Assert.Contains("CopyPage(", html);
    }

}
