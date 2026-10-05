using System.Text.Json.Nodes;
using NeeView.Backends;

namespace NeeView.Engine.Tests;

/// <summary>断线/卸载与未完成副本的恢复回归；仅操作隔离合成夹具。</summary>
public sealed class FileRecoveryAvailabilityTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    /// <summary>复演日志已写、复制未安装的进程中断，不通过后端假返回模拟恢复结果。</summary>
    private static async Task<(FileOperationBackend Backend, string Source, string Target, string Temporary, string Journal)> PrepareAsync(Fixture fixture, int length)
    {
        var source = Path.Combine(fixture.Images, "001.png");
        var target = Path.Combine(Directory.CreateDirectory(Path.Combine(fixture.Root, "mounted-target")).FullName, "001.png");
        var backend = new FileOperationBackend(Path.Combine(fixture.State, "FileRecovery"));
        var result = await backend.TransferAsync(new(source, target, false, false), Token);
        var journal = JsonNode.Parse(await File.ReadAllTextAsync(result.Journal, Token))!;
        journal["Completed"] = false;
        var temporary = journal["Temporary"]!.GetValue<string>();
        File.Move(target, temporary);
        using (var stream = File.OpenWrite(temporary)) stream.SetLength(length);
        await File.WriteAllTextAsync(result.Journal, journal.ToJsonString(), Token);
        return (backend, source, target, temporary, result.Journal);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(123)]
    public async Task UnavailableParentKeepsJournalAndReconnectCleansVerifiedPrefix(int length)
    {
        using var fixture = new Fixture(); var transfer = await PrepareAsync(fixture, length);
        var original = await File.ReadAllBytesAsync(transfer.Source, Token);
        var parent = Path.GetDirectoryName(transfer.Target)!; var hidden = parent + "-disconnected";
        Directory.Move(parent, hidden);
        Assert.Single(await transfer.Backend.RecoverAsync(Token));
        Assert.True(File.Exists(transfer.Journal));
        Assert.Equal(length, new FileInfo(Path.Combine(hidden, Path.GetFileName(transfer.Temporary))).Length);
        Directory.Move(hidden, parent);
        Assert.Empty(await transfer.Backend.RecoverAsync(Token));
        Assert.Equal(original, await File.ReadAllBytesAsync(transfer.Source, Token));
        Assert.False(File.Exists(transfer.Target)); Assert.False(File.Exists(transfer.Temporary)); Assert.False(File.Exists(transfer.Journal));
    }

    [Theory]
    [InlineData("temporary")]
    [InlineData("original")]
    [InlineData("oversized")]
    public async Task ChangedOrOversizedPartialCopiesRemainForManualRecovery(string change)
    {
        using var fixture = new Fixture(); var transfer = await PrepareAsync(fixture, 123);
        if (change == "oversized")
        {
            using var stream = File.OpenWrite(transfer.Temporary); stream.SetLength(new FileInfo(transfer.Source).Length + 1);
        }
        else await File.WriteAllTextAsync(change == "original" ? transfer.Source : transfer.Temporary, "external replacement", Token);
        var bytes = await File.ReadAllBytesAsync(transfer.Temporary, Token);
        Assert.Single(await transfer.Backend.RecoverAsync(Token));
        Assert.True(File.Exists(transfer.Journal)); Assert.False(File.Exists(transfer.Target));
        Assert.Equal(bytes, await File.ReadAllBytesAsync(transfer.Temporary, Token));
    }

    [Fact]
    public async Task CompletedJournalAlsoRemainsWhileDestinationParentIsUnavailable()
    {
        using var fixture = new Fixture();
        var source = Path.Combine(fixture.Images, "001.png");
        var parent = Directory.CreateDirectory(Path.Combine(fixture.Root, "mounted-target")).FullName;
        var backend = new FileOperationBackend(Path.Combine(fixture.State, "FileRecovery"));
        var result = await backend.TransferAsync(new(source, Path.Combine(parent, "001.png"), false, false), Token);
        var hidden = parent + "-disconnected"; Directory.Move(parent, hidden);
        Assert.Single(await backend.RecoverAsync(Token)); Assert.True(File.Exists(result.Journal));
        Directory.Move(hidden, parent);
        Assert.Empty(await backend.RecoverAsync(Token)); Assert.True(File.Exists(result.Destination)); Assert.False(File.Exists(result.Journal));
    }
}
