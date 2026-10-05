using System.Text.Json.Nodes;
using NeeView.Backends;
namespace NeeView.Engine.Tests;

/// <summary>真实临时链接验证：操作目录项本身、保留目标文字、失败保留恢复材料。</summary>
public sealed class P4LinkTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private static FileOperationBackend Backend(Fixture f) => new(Path.Combine(f.State, "Recovery"));
    private static string Target(Fixture f) => Directory.CreateDirectory(Path.Combine(f.Root, "target")).FullName;

    [Fact]
    public async Task LinkClassificationOverwriteUndoRedoPreservesBothLinkTargets()
    {
        using var f = new Fixture(); var source = Path.Combine(f.Images, "000-link.png");
        var original = Path.Combine(f.Images, "001.png"); var old = Path.Combine(f.Images, "002.png");
        File.CreateSymbolicLink(source, original); var target = Target(f); var destination = Path.Combine(target, Path.GetFileName(source)); File.CreateSymbolicLink(destination, old);
        var state = new SaveData(f.State); await state.LoadAsync(Token); Config.Current.System.IsFileWriteAccessEnabled = true;
        await using var op = f.Operation(state); using var images = new BitmapFactory(new MagickImageDecoder()); var backend = Backend(f);
        var moves = new DestinationMoveService(backend); op.AttachFileOperations(moves, backend, images); moves.ConfirmOverwriteAsync = _ => Task.FromResult(true);
        await op.OpenAsync(source, Token); Assert.True(op.CanFileAction); await op.ClassifyAsync(new("target", target), false, Token);
        Assert.Null(op.Error); Assert.Null(new FileInfo(source).LinkTarget); Assert.Equal(original, new FileInfo(destination).LinkTarget);
        await op.ReplayDestinationMoveAsync(true, Token); Assert.Equal(original, new FileInfo(source).LinkTarget); Assert.Equal(old, new FileInfo(destination).LinkTarget);
        await op.ReplayDestinationMoveAsync(false, Token); Assert.Equal(original, new FileInfo(destination).LinkTarget);
        Assert.True(File.Exists(original)); Assert.True(File.Exists(old)); Assert.Equal(1, moves.UndoCount);
        Config.Current.Panels.DestinationMoveHistoryCapacity = 0; await moves.ApplyHistoryCapacityAsync(); Assert.Empty(await backend.RecoverAsync(Token));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task RootDirectoryLinkTransferActsOnLinkWithoutCopyingOrRemovingTarget(bool move)
    {
        using var f = new Fixture(); var link = Path.Combine(f.Root, "book-link"); Directory.CreateSymbolicLink(link, f.Images); var backend = Backend(f);
        var plan = await backend.PlanBookTransferAsync(link, Target(f), Token); Assert.False(plan.Target.IsDirectory); Assert.NotNull(plan.Target.LinkTarget);
        var result = await backend.TransferBookAsync(plan, move, Token); Assert.Equal(f.Images, new FileInfo(result.Destination).LinkTarget);
        Assert.Equal(!move, new FileInfo(link).LinkTarget is not null); await backend.ReleaseAsync(result);
        Assert.Equal(5, Directory.GetFiles(f.Images).Length); Assert.Empty(await backend.RecoverAsync(Token));
    }

    [Fact]
    public async Task InterruptedLinkMoveRestoresBothLinksAndNeverTouchesTheirTargets()
    {
        using var f = new Fixture(); var source = Path.Combine(f.Root, "source.png"); var destination = Path.Combine(Target(f), "source.png");
        var first = Path.Combine(f.Images, "001.png"); var second = Path.Combine(f.Images, "002.png"); File.CreateSymbolicLink(source, first); File.CreateSymbolicLink(destination, second);
        var backend = Backend(f); var result = await backend.TransferAsync(new(source, destination, true, true, PreserveSourceLink: true), Token);
        var node = JsonNode.Parse(await File.ReadAllTextAsync(result.Journal, Token))!; node["Completed"] = false;
        await File.WriteAllTextAsync(result.Journal, node.ToJsonString(), Token); Assert.Empty(await backend.RecoverAsync(Token));
        Assert.Equal(first, new FileInfo(source).LinkTarget); Assert.Equal(second, new FileInfo(destination).LinkTarget); Assert.Equal(5, Directory.GetFiles(f.Images).Length);
    }

    [Fact]
    public async Task ChangedLinkBackupIsPreservedAndRegularFileCannotBecomeAuthorizedLink()
    {
        using var f = new Fixture(); var source = Path.Combine(f.Root, "link.png"); var first = Path.Combine(f.Images, "001.png"); var destination = Path.Combine(Target(f), "link.png");
        File.CreateSymbolicLink(source, first); File.CreateSymbolicLink(destination, Path.Combine(f.Images, "002.png")); var backend = Backend(f);
        var result = await backend.TransferAsync(new(source, destination, true, true, PreserveSourceLink: true), Token);
        File.Delete(result.Backup!); File.CreateSymbolicLink(result.Backup!, first); await Assert.ThrowsAsync<IOException>(() => backend.ReleaseAsync(result));
        Assert.Single(await backend.RecoverAsync(Token)); Assert.True(File.Exists(result.Journal)); Assert.Equal(first, new FileInfo(result.Backup!).LinkTarget);
        var regularPlan = await backend.PlanBookTransferAsync(f.Zip, Target(f), Token); File.Delete(f.Zip); File.CreateSymbolicLink(f.Zip, first);
        await Assert.ThrowsAsync<IOException>(() => backend.TransferBookAsync(regularPlan, true, Token)); Assert.True(File.Exists(first));
    }

    [Fact]
    public async Task TreeCleanupDoesNotFollowExternalDirectoryOrBrokenLinks()
    {
        using var f = new Fixture(); var outside = Directory.CreateDirectory(Path.Combine(f.Root, "outside")).FullName; File.WriteAllText(Path.Combine(outside, "keep"), "keep");
        Directory.CreateSymbolicLink(Path.Combine(f.Images, "dir-link"), outside); File.CreateSymbolicLink(Path.Combine(f.Images, "broken"), "missing"); var backend = Backend(f);
        var result = await backend.TransferBookAsync(await backend.PlanBookTransferAsync(f.Images, Target(f), Token), true, Token); await backend.ReleaseAsync(result);
        Assert.Equal("keep", File.ReadAllText(Path.Combine(outside, "keep"))); Assert.Equal("missing", new FileInfo(Path.Combine(result.Destination, "broken")).LinkTarget);
        Assert.Equal(outside, new FileInfo(Path.Combine(result.Destination, "dir-link")).LinkTarget); Assert.Empty(await backend.RecoverAsync(Token));
    }

    [Fact]
    public async Task ReaderRootLinkRenameUsesOriginalPathStateAndRetainsTargetDirectory()
    {
        using var f = new Fixture(); var link = Path.Combine(f.Root, "reader-link"); Directory.CreateSymbolicLink(link, f.Images);
        var state = new SaveData(f.State); await state.LoadAsync(Token); Config.Current.System.IsFileWriteAccessEnabled = true;
        await using var op = f.Operation(state); using var images = new BitmapFactory(new MagickImageDecoder()); var backend = Backend(f);
        op.AttachFileOperations(new(backend), backend, images); op.AskBookNameAsync = _ => Task.FromResult<string?>("reader-renamed");
        await op.OpenAsync(link, Token); Assert.True(op.Book!.Source.IsRootShortcut); await op.RenameBookAsync(Token);
        Assert.Null(op.Error); Assert.Equal(Path.Combine(f.Root, "reader-renamed"), op.Book!.Path); Assert.Equal(5, op.Book.Pages.Count);
        Assert.Equal(f.Images, new FileInfo(op.Book.Path).LinkTarget); Assert.Equal(5, Directory.GetFiles(f.Images).Length);
    }
    [Fact]
    public async Task RootLinkRenameUpdatesOnlyLinkPathAndRejectsReplacementAfterSnapshot()
    {
        using var f = new Fixture(); var link = Path.Combine(f.Root, "root-link"); Directory.CreateSymbolicLink(link, f.Images); var backend = Backend(f);
        var target = await backend.GetRenameTargetAsync(link, Token); var plan = await backend.PlanRenameAsync(target, "renamed-link", Token); await backend.RenameAsync(plan, Token);
        Assert.Equal(f.Images, new FileInfo(plan.Destination).LinkTarget); Assert.True(await backend.WasRenamedAsync(plan, Token)); Assert.Equal(5, Directory.GetFiles(f.Images).Length);
        target = await backend.GetRenameTargetAsync(plan.Destination, Token); var second = await backend.PlanRenameAsync(target, "again", Token);
        File.Delete(plan.Destination); File.CreateSymbolicLink(plan.Destination, f.State); await Assert.ThrowsAsync<IOException>(() => backend.RenameAsync(second, Token));
        Assert.Equal(f.State, new FileInfo(plan.Destination).LinkTarget);
    }
    [Fact]
    public async Task AliasResolverOpensRealTargetAndRejectsCycles()
    {
        using var f = new Fixture(); var alias = Path.Combine(f.Root, "alias"); File.WriteAllText(alias, "alias fixture");
        await using var archive = await new ArchiveFactory(path => path == alias ? f.Zip : null).OpenAsync(alias, Token); Assert.Equal(f.Zip, archive.Path);
        await Assert.ThrowsAsync<IOException>(() => new ArchiveFactory(_ => alias).OpenAsync(alias, Token));
    }
}
