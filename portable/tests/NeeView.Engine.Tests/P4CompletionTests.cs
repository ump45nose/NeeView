using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using ImageMagick;
using NeeView.Backends;
namespace NeeView.Engine.Tests;

/// <summary>P4剩余删除/接收的真实临时来源回归，不操作用户剪贴板或图片。</summary>
public sealed class P4CompletionTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private sealed class Trash(string root) : IPlatformService
    {
        public int FailOn { get; init; }
        public List<string> Calls { get; } = [];
        public Task RevealAsync(string path, CancellationToken token = default) => Task.CompletedTask;
        public Task TrashAsync(string path, CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            if (Calls.Count + 1 == FailOn) throw new IOException("模拟权限失败");
            Directory.CreateDirectory(root); var destination = Path.Combine(root, Path.GetFileName(path));
            if (Directory.Exists(path)) Directory.Move(path, destination);
            else File.Move(path, destination);
            Calls.Add(path); return Task.CompletedTask;
        }
    }
    private sealed class Clipboard(FileClipboardContent content) : IFileClipboard
    {
        public bool HasFileContent => true;
        public Task<FileClipboardContent> ReadAsync(CancellationToken token) => Task.FromResult(content);
        public Task WriteAsync(FileClipboardContent value, CancellationToken token) => throw new NotSupportedException();
    }
    private sealed class DelayedTrash(Trash inner, TaskCompletionSource entered, Task release) : IPlatformService
    {
        public Task RevealAsync(string path, CancellationToken token = default) => Task.CompletedTask;
        public async Task TrashAsync(string path, CancellationToken token = default) { entered.SetResult(); await release; await inner.TrashAsync(path, token); }
    }
    [Fact]
    public async Task CloseWaitsForAuthorizedDeleteAndCancelsUnconfirmedDelete()
    {
        using var f = new Fixture(); var state = await State(f); var op = f.Operation(state); using var images = new BitmapFactory(new MagickImageDecoder());
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var trash = new Trash(Path.Combine(f.Root, "trash")); op.AttachFileDeletion(new DelayedTrash(trash, entered, release.Task), images); op.ConfirmDeleteAsync = _ => Task.FromResult(true);
        try
        {
            await op.OpenAsync(f.Images, Token); var action = op.DeleteFileAsync(Token); await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), Token);
            var closing = op.DisposeAsync().AsTask(); Assert.False(closing.IsCompleted); release.SetResult(); await action; await closing; Assert.Single(trash.Calls);
        }
        finally { release.TrySetResult(); await op.DisposeAsync(); }
        var second = f.Operation(state); var prompt = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var answer = new TaskCompletionSource<bool>();
        second.AttachFileDeletion(trash, images); second.ConfirmDeleteAsync = _ => { prompt.SetResult(); return answer.Task; };
        try
        {
            await second.OpenAsync(f.Images, Token); var action = second.DeleteFileAsync(Token); await prompt.Task.WaitAsync(TimeSpan.FromSeconds(5), Token);
            await second.DisposeAsync(); answer.SetResult(true); await action; Assert.Single(trash.Calls);
        }
        finally { answer.TrySetResult(false); await second.DisposeAsync(); }
    }
    private static async Task<SaveData> State(Fixture f)
    { var state = new SaveData(f.State, ArchiveFactory.TemporaryDirectory); await state.LoadAsync(Token); Config.Current.System.IsFileWriteAccessEnabled = true; Config.Current.Archive.Zip.IsFileWriteAccessEnabled = true; return state; }
    [Fact]
    public async Task ZipDeletionRequiresOriginalIndependentWritePermissionAndPreservesUnknownConfig()
    {
        using var f = new Fixture(); Directory.CreateDirectory(f.State); File.WriteAllText(Path.Combine(f.State, "UserSetting.json"), "{\"Config\":{\"Archive\":{\"Zip\":{\"Encoding\":2,\"Extra\":7}}}}");
        var state = new SaveData(f.State); await state.LoadAsync(Token); Assert.False(Config.Current.Archive.Zip.IsFileWriteAccessEnabled); Config.Current.System.IsFileWriteAccessEnabled = true;
        await using var op = f.Operation(state); using var images = new BitmapFactory(new MagickImageDecoder()); op.AttachFileDeletion(new Trash(Path.Combine(f.Root, "trash")), images);
        await op.OpenAsync(f.Zip, Token); Assert.False(op.CanDeleteFile); Config.Current.Archive.Zip.IsFileWriteAccessEnabled = true; Assert.True(op.CanDeleteFile); await op.SaveAsync();
        await state.LoadAsync(Token); Assert.True(Config.Current.Archive.Zip.IsFileWriteAccessEnabled); var json = JsonNode.Parse(File.ReadAllText(Path.Combine(f.State, "UserSetting.json")))!;
        Assert.Equal(7, json["Config"]!["Archive"]!["Zip"]!["Extra"]!.GetValue<int>());
    }
    [Fact]
    public async Task SelectedFilesPartialFailureOnlyRemovesCommittedPages()
    {
        using var f = new Fixture(); var state = await State(f); await using var op = f.Operation(state);
        using var images = new BitmapFactory(new MagickImageDecoder()); var trash = new Trash(Path.Combine(f.Root, "trash")) { FailOn = 2 };
        op.AttachFileDeletion(trash, images); op.ConfirmDeleteAsync = _ => Task.FromResult(true);
        await op.OpenAsync(f.Images, Token); var selected = op.Book!.Pages.Take(3).ToArray(); await op.DeletePagesAsync(selected, Token);
        Assert.Single(trash.Calls); Assert.Equal(4, op.Book.Pages.Count); Assert.Contains("模拟权限失败", op.Error);
        Assert.DoesNotContain(selected[0], op.Book.Pages); Assert.Contains(selected[1], op.Book.Pages);
    }
    [Fact]
    public async Task DirectoryDeletionIncludesEmptyAndNonImageFilesButNotLinkedTarget()
    {
        using var f = new Fixture(); var child = Path.Combine(f.Images, "000-child"); Directory.CreateDirectory(Path.Combine(child, "empty"));
        File.WriteAllText(Path.Combine(child, "note.txt"), "keep outside"); Directory.CreateSymbolicLink(Path.Combine(child, "outside"), f.State);
        var state = await State(f); await using var op = f.Operation(state); using var images = new BitmapFactory(new MagickImageDecoder());
        var trash = new Trash(Path.Combine(f.Root, "trash")); op.AttachFileDeletion(trash, images); op.ConfirmDeleteAsync = _ => Task.FromResult(true);
        await op.OpenAsync(f.Images, Token); Assert.True(op.CanDeleteFile); await op.DeleteFileAsync(Token);
        Assert.False(Directory.Exists(child)); Assert.True(Directory.Exists(f.State)); Assert.Equal(5, op.Book!.Pages.Count);
        Assert.True(File.Exists(Path.Combine(f.Root, "trash", "000-child", "note.txt")));
    }
    [Fact]
    public async Task PlaylistDeleteRemovesOneDuplicateAliasAndPreservesEntitiesUnknownFieldsAndReload()
    {
        using var f = new Fixture(); var list = Path.Combine(f.Root, "list.nvpls"); var target = Path.Combine(f.Images, "001.png");
        var root = new JsonObject { ["Format"] = PlaylistSource.CurrentFormat, ["Extra"] = 42, ["Items"] = new JsonArray(
            new JsonObject { ["Path"] = target, ["Name"] = "first", ["Other"] = "kept" }, new JsonObject { ["Path"] = target, ["Name"] = "second" }) };
        await File.WriteAllTextAsync(list, root.ToJsonString(), Token); var state = await State(f);
        await using var op = f.Operation(state); using var images = new BitmapFactory(new MagickImageDecoder()); var trash = new Trash(Path.Combine(f.Root, "trash"));
        op.AttachFileDeletion(trash, images); op.ConfirmDeleteAsync = _ => Task.FromResult(true); await op.OpenAsync(list, Token);
        var page = op.Book!.Pages.Single(p => p.EntryName == "second"); await op.DeletePagesAsync([page], Token);
        Assert.Empty(trash.Calls); Assert.True(File.Exists(target)); Assert.Single(op.Book.Pages);
        var saved = JsonNode.Parse(await File.ReadAllTextAsync(list, Token))!; Assert.Equal(42, saved["Extra"]!.GetValue<int>());
        Assert.Equal("kept", saved["Items"]![0]!["Other"]!.GetValue<string>());
        await op.OpenAsync(f.Images, Token); await op.OpenAsync(list, Token); Assert.Single(op.Book!.Pages);
    }
    [Fact]
    public async Task ZipDeletionAlwaysConfirmsAndPreservesSurvivorIdsBytesAndComments()
    {
        using var f = new Fixture();
        using (var zip = System.IO.Compression.ZipFile.Open(f.Zip, System.IO.Compression.ZipArchiveMode.Update))
        { zip.Comment = "archive comment"; zip.Entries.Single(e => e.FullName == "002.png").Comment = "survivor comment"; }
        var state = await State(f); Config.Current.System.IsRemoveConfirmed = false;
        await using var op = f.Operation(state); using var images = new BitmapFactory(new MagickImageDecoder()); op.AttachFileDeletion(new Trash(Path.Combine(f.Root, "trash")), images);
        await op.OpenAsync(f.Zip, Token); var page = op.Book!.Pages[1]; var bytes = await File.ReadAllBytesAsync(Path.Combine(f.Images, page.EntryName), Token);
        await op.DeleteFileAsync(Token); Assert.Equal(5, op.Book.Pages.Count); // 无确认宿主仍拒绝不可逆动作。
        int prompts = 0; op.ConfirmDeleteAsync = message => { Assert.Contains("永久", message); prompts++; return Task.FromResult(true); };
        await op.DeleteFileAsync(Token); Assert.Equal(1, prompts); Assert.Equal(4, op.Book.Pages.Count); Assert.Same(page, op.Book.CurrentPage);
        await using var stream = await page.ArchiveEntry.Archive.OpenEntryAsync(page.ArchiveEntry, Token); using var buffer = new MemoryStream(); await stream.CopyToAsync(buffer, Token);
        Assert.Equal(bytes, buffer.ToArray());
        using var remaining = System.IO.Compression.ZipFile.OpenRead(f.Zip); Assert.Equal(4, remaining.Entries.Count); Assert.Equal("archive comment", remaining.Comment);
        Assert.Equal("survivor comment", remaining.Entries.Single(e => e.FullName == "002.png").Comment);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ZipImplicitDirectoryDeletionRemovesDescendantsInRootOrLogicalBook(bool logical)
    {
        using var f = new Fixture(); var zipPath = Path.Combine(f.Root, "nested.cbz");
        using (var zip = System.IO.Compression.ZipFile.Open(zipPath, System.IO.Compression.ZipArchiveMode.Create))
        {
            foreach (var name in new[] { "group/child/001.png", "group/keep.png", "outside.png" })
            { using var output = zip.CreateEntry(name).Open(); using var input = File.OpenRead(Path.Combine(f.Images, "001.png")); input.CopyTo(output); }
        }
        var state = await State(f); Config.Current.System.ArchiveRecursiveMode = ArchiveEntryCollectionMode.CurrentDirectory;
        await using var op = f.Operation(state); using var images = new BitmapFactory(new MagickImageDecoder()); op.AttachFileDeletion(new Trash(Path.Combine(f.Root, "trash")), images);
        op.ConfirmDeleteAsync = _ => Task.FromResult(true); await op.OpenAsync(logical ? zipPath + "/group" : zipPath, Token);
        var directory = op.Book!.Pages.Single(p => p.ArchiveEntry.IsDirectory); Assert.True(op.CanDeletePages([directory])); await op.DeletePagesAsync([directory], Token);
        using var result = System.IO.Compression.ZipFile.OpenRead(zipPath);
        Assert.DoesNotContain(result.Entries, e => e.FullName.StartsWith(logical ? "group/child/" : "group/", StringComparison.Ordinal));
        Assert.DoesNotContain(op.Book.Pages, p => p.ArchiveEntry.IsDirectory);
    }
    [Fact]
    public async Task PlaylistExternalEditRefusesCommitWithoutRemovingPages()
    {
        using var f = new Fixture(); var list = Path.Combine(f.Root, "list.nvpls");
        await File.WriteAllBytesAsync(list, PlaylistSourceTools.CreateTemporarySource([Path.Combine(f.Images, "001.png")]), Token);
        var state = await State(f); await using var op = f.Operation(state); using var images = new BitmapFactory(new MagickImageDecoder());
        op.AttachFileDeletion(new Trash(Path.Combine(f.Root, "trash")), images); op.ConfirmDeleteAsync = _ => { File.AppendAllText(list, " "); return Task.FromResult(true); };
        await op.OpenAsync(list, Token); await op.DeleteFileAsync(Token); Assert.Single(op.Book!.Pages); Assert.Contains("外部修改", op.Error);
    }
    [Fact]
    public async Task BitmapAndInlinePasteUseSingleOpenChainAndOriginalAlphaSemantics()
    {
        using var f = new Fixture(); var state = await State(f); byte[] bytes;
        using (var image = new MagickImage(new MagickColor(100, 80, 60, 0), 8, 8)) bytes = image.ToByteArray(MagickFormat.Png);
        await using var receiver = new ContentDropReceiver(Path.Combine(f.Root, "drops")); await using var op = f.Operation(state);
        op.AttachContentDropReceiver(receiver); op.AttachFileClipboard(new Clipboard(new([], [], Content: new([new(bytes, IsBitmap: true)]))));
        await op.OpenAsync(f.Images, Token); await op.PasteFilesAsync(Token); var bitmapPath = op.Book!.CurrentPage!.ArchiveEntry.FilePath!;
        using (var output = new MagickImage(bitmapPath)) Assert.False(output.HasAlpha);
        op.AttachFileClipboard(new Clipboard(new([], [], Content: new([], "<img src='data:image/png;base64," + Convert.ToBase64String(bytes) + "'>"))));
        await op.PasteFilesAsync(Token); using var inline = new MagickImage(op.Book!.CurrentPage!.ArchiveEntry.FilePath!); Assert.True(inline.HasAlpha);
        Assert.True(File.Exists(bitmapPath)); // 切书不释放历史资源。
    }
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> action) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => action(request, token); }
    [Fact]
    public async Task WebDownloadIsLimitedAndFailedBatchCleansAllCreatedFiles()
    {
        using var f = new Fixture(); var bytes = await File.ReadAllBytesAsync(Path.Combine(f.Images, "001.png"), Token); int calls = 0;
        using var client = new HttpClient(new Handler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new ByteArrayContent(++calls == 1 ? bytes : Encoding.UTF8.GetBytes("not image")) })));
        await using var receiver = new ContentDropReceiver(Path.Combine(f.Root, "drops"), client);
        await Assert.ThrowsAnyAsync<Exception>(() => receiver.ReceiveAsync(new([], WebUrls: ["https://fixture.invalid/a.png", "https://fixture.invalid/b.png"]), Token));
        Assert.Empty(Directory.EnumerateFiles(Path.Combine(f.Root, "drops"), "*", SearchOption.AllDirectories));
        var good = await receiver.ReceiveAsync(new([], "<img src='data:image/png;base64," + Convert.ToBase64String(bytes) + "'>", ["https://fixture.invalid/c.png"]), Token);
        Assert.Single(good); Assert.Equal(2, calls); Assert.Equal(bytes, await File.ReadAllBytesAsync(good[0], Token));
    }
    [Fact]
    public async Task BrowserDownloadFailureFallsBackToBitmapAndDiscardsPartialWebBatch()
    {
        using var f = new Fixture(); var bytes = await File.ReadAllBytesAsync(Path.Combine(f.Images, "001.png"), Token); int calls = 0;
        using var client = new HttpClient(new Handler((_, _) => Task.FromResult(new HttpResponseMessage(++calls == 1 ? HttpStatusCode.OK : HttpStatusCode.NotFound) { Content = new ByteArrayContent(bytes) })));
        await using var receiver = new ContentDropReceiver(Path.Combine(f.Root, "drops"), client);
        var result = await receiver.ReceiveAsync(new([new(bytes, IsBitmap: true)], WebUrls: ["https://fixture.invalid/one.png", "https://fixture.invalid/two.png"]), Token);
        Assert.Single(result); Assert.Equal(2, calls); Assert.Single(Directory.EnumerateFiles(Path.Combine(f.Root, "drops"), "*", SearchOption.AllDirectories));
    }
    [Fact]
    public async Task CancelledDownloadPreservesCurrentBookAndLeavesNoFiles()
    {
        using var f = new Fixture(); var state = await State(f); var started = new TaskCompletionSource();
        using var client = new HttpClient(new Handler(async (_, token) => { started.SetResult(); await Task.Delay(Timeout.Infinite, token); return new HttpResponseMessage(); }));
        await using var receiver = new ContentDropReceiver(Path.Combine(f.Root, "drops"), client); await using var op = f.Operation(state);
        op.AttachContentDropReceiver(receiver); op.AttachFileClipboard(new Clipboard(new([], [], Content: new([], WebUrls: ["https://fixture.invalid/image.png"]))));
        await op.OpenAsync(f.Images, Token); var old = op.Book; using var cancel = new CancellationTokenSource(); var action = op.PasteFilesAsync(cancel.Token);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5), Token); cancel.Cancel(); await action;
        Assert.Same(old, op.Book); Assert.False(op.IsUsingClipboard); Assert.Empty(Directory.EnumerateFiles(Path.Combine(f.Root, "drops"), "*", SearchOption.AllDirectories));
    }
}
