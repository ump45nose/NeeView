using System.Text.Json.Nodes;
using NeeView;
using NeeView.Backends;
namespace NeeView.Engine.Tests;

/// <summary>原目录参数、默认归一、随机种子和四文件事务的真实回归。</summary>
public sealed class FolderParameterTests
{
    /// <summary>两个目录独立排序；返回、刷新和重新启动都保持种子，不污染原默认。</summary>
    [Fact]
    public async Task PerDirectoryOrderAndRandomSeedSurviveRefreshReturnAndRestart()
    {
        using var fixture = new Fixture(); var token = TestContext.Current.CancellationToken;
        var library = Path.Combine(fixture.Root, "library"); Directory.CreateDirectory(library);
        for (int i = 0; i < 5; i++) Directory.CreateDirectory(Path.Combine(library, "Book" + i));
        var state = new SaveData(fixture.State); await state.LoadAsync(token);
        await using var operation = fixture.Operation(state);
        await operation.Bookshelf.SetPlaceAsync(library, token: token);
        await operation.ChangeFolderOrderAsync(FolderOrder.Random);
        var seed = state.FolderConfigs.GetFolderParameter(library).Seed;
        var paths = operation.Bookshelf.Items.Select(e => e.Path).ToArray();
        Assert.NotEqual(0, seed); Assert.Equal(FolderOrder.FileName, Config.Current.Bookshelf.DefaultFolderOrder);
        await operation.Bookshelf.SetPlaceAsync(fixture.Images, token: token);
        Assert.Equal(FolderOrder.FileName, operation.Bookshelf.FolderOrder);
        await operation.ChangeFolderOrderAsync(FolderOrder.FileNameDescending);
        await operation.Bookshelf.SetPlaceAsync(library, token: token);
        Assert.Equal(paths, operation.Bookshelf.Items.Select(e => e.Path));
        await operation.Bookshelf.RefreshAsync(token); Assert.Equal(paths, operation.Bookshelf.Items.Select(e => e.Path));
        var fresh = new SaveData(fixture.State); await fresh.LoadAsync(token);
        using var list = new BookshelfFolderList(new ArchiveFactory(), fresh.FolderConfigs);
        await list.SetPlaceAsync(library, token: token);
        Assert.Equal(paths, list.Items.Select(e => e.Path)); Assert.Equal(seed, fresh.FolderConfigs.GetFolderParameter(library).Seed);
        await list.SetPlaceAsync(fixture.Images, token: token); Assert.Equal(FolderOrder.FileNameDescending, list.FolderOrder);
        Assert.False(File.Exists(Path.Combine(fixture.State, "FolderConfig.json")));
    }

    /// <summary>原 nullable 默认、父级递归继承和随机缺种子补值的保存规则。</summary>
    [Fact]
    public void OriginalDefaultNormalizationAndRecursiveInheritanceRemain()
    {
        Config.SetCurrent(new()); var folders = new FolderConfigCollection();
        folders.SetFolderParameter("/books", new() { FolderOrder = FolderOrder.FileName, IsFolderRecursive = true });
        var parameter = new FolderParameter("/books/manga/chapter", folders);
        Assert.True(parameter.IsFolderRecursive); Assert.Null(parameter.CreateMemento().IsFolderRecursive);
        parameter.IsFolderRecursive = false;
        Assert.False(folders.GetFolderParameter(parameter.Path).IsFolderRecursive);
        folders.SetFolderParameter("/books/manga", new() { IsFolderRecursive = false });
        Assert.False(new FolderParameter("/books/manga/other", folders).IsFolderRecursive);
        folders.SetFolderParameter("bookmark:Comics", new() { IsFolderRecursive = true });
        Assert.True(new FolderParameter("bookmark:Comics\\Series\\Volume", folders).IsFolderRecursive);
        folders.SetFolderParameter("bookmark:Comics\\Series", new() { IsFolderRecursive = false });
        Assert.False(new FolderParameter("bookmark:Comics\\Series\\Volume", folders).IsFolderRecursive);
        folders.SetFolderParameter("/random", new() { FolderOrder = FolderOrder.Random });
        var random = new FolderParameter("/random", folders); Assert.NotEqual(0, random.Seed);
        Assert.Equal(random.Seed, folders.GetFolderParameter("/random").Seed);
        random.FolderOrder = FolderOrder.FileName; Assert.Equal(0, random.Seed);
        var json = folders.CreateMemento();
        Assert.DoesNotContain(json["Folders"]!.AsArray().OfType<JsonObject>(), e => e["Place"]!.GetValue<string>() == "/random");
        Assert.Null(json["Folders"]![0]!["Parameter"]!["FolderOrder"]);
    }

    /// <summary>关闭目录状态保存不裁剪运行参数，不删除缩略配置或未知字段。</summary>
    [Fact]
    public void KeepFolderStatusOnlyChangesSavedCopyAndPreservesUnknownFields()
    {
        Config.SetCurrent(new()); var folders = new FolderConfigCollection();
        folders.Restore(JsonNode.Parse("""{"Format":"NeeView.Folders/46.3.0","FutureRoot":7,"Folders":[{"Place":"/books","Parameter":{"FolderOrder":1,"Seed":42,"FutureParameter":"value"},"Thumbs":{"book":"cover.png"},"FutureUnit":true}]}""")!.AsObject());
        folders.SetFolderParameter("/books", new() { FolderOrder = FolderOrder.Random, Seed = 123 });
        var saved = folders.CreateMemento(); Assert.Equal(12, saved["Folders"]![0]!["Parameter"]!["FolderOrder"]!.GetValue<int>());
        Assert.Equal("value", saved["Folders"]![0]!["Parameter"]!["FutureParameter"]!.GetValue<string>());
        Config.Current.History.IsKeepFolderStatus = false; saved = folders.CreateMemento();
        Assert.Null(saved["Folders"]![0]!["Parameter"]!["FolderOrder"]);
        Assert.Equal("cover.png", saved["Folders"]![0]!["Thumbs"]!["book"]!.GetValue<string>());
        Assert.True(saved["Folders"]![0]!["FutureUnit"]!.GetValue<bool>()); Assert.Equal(7, saved["FutureRoot"]!.GetValue<int>());
        Assert.Equal(123, folders.GetFolderParameter("/books").Seed);
        var reloaded = new FolderConfigCollection(); reloaded.Restore(saved);
        Assert.Equal(FolderOrder.FileName, new FolderParameter("/books", reloaded).FolderOrder);
    }

    /// <summary>原命令排序与切换进入当前路径参数；已登记入口仍按当前来源限制执行。</summary>
    [Fact]
    public async Task OriginalOrderCommandsUsePathAndToggleSupportedOrderMap()
    {
        using var fixture = new Fixture(); var state = new SaveData(fixture.State); await state.LoadAsync(TestContext.Current.CancellationToken);
        await using var operation = fixture.Operation(state); var commands = new CommandTable(operation);
        await operation.Bookshelf.SetPlaceAsync(fixture.Root, token: TestContext.Current.CancellationToken);
        await commands.ExecuteAsync("SetBookOrderByFileNameD"); Assert.Equal(FolderOrder.FileNameDescending, operation.Bookshelf.FolderOrder);
        await commands.ExecuteAsync("ToggleBookOrder"); Assert.Equal(FolderOrder.FileType, operation.Bookshelf.FolderOrder);
        await commands.ExecuteAsync("SetBookOrderByRandom"); var firstSeed = state.FolderConfigs.GetFolderParameter(fixture.Root).Seed;
        await commands.ExecuteAsync("SetBookOrderByRandom"); Assert.NotEqual(firstSeed, state.FolderConfigs.GetFolderParameter(fixture.Root).Seed);
        await commands.ExecuteAsync("ToggleBookOrder"); Assert.Equal(FolderOrder.FileName, operation.Bookshelf.FolderOrder);
        Assert.True(commands.IsAvailable("SetBookOrderByEntryTimeA")); Assert.True(commands.IsAvailable("SetBookOrderByPathA"));
        Assert.False(operation.CanChangeFolderOrder(FolderOrder.EntryTime)); Assert.False(operation.CanChangeFolderOrder(FolderOrder.Path));
        Assert.Equal(FolderOrder.FileName, Config.Current.Bookshelf.DefaultFolderOrder);
    }

    /// <summary>准备第四文件失败时，排序、种子、选中对象和已有三个文件均回滚，再试可成功。</summary>
    [Fact]
    public async Task FourthFilePreparationFailureRollsBackOrderSelectionAndAllFiles()
    {
        using var fixture = new Fixture(); var state = new SaveData(fixture.State); await state.LoadAsync(TestContext.Current.CancellationToken);
        await using var operation = fixture.Operation(state);
        await operation.OpenAsync(fixture.Images, TestContext.Current.CancellationToken); await operation.Bookshelf.SetPlaceAsync(fixture.Root, fixture.Zip, TestContext.Current.CancellationToken);
        await operation.SaveAsync();
        var names = new[] { "History.json", "UserSetting.json", "Bookmark.json", FolderConfigCollection.FileName };
        var previous = names.ToDictionary(e => e, e => File.ReadAllBytes(Path.Combine(fixture.State, e)));
        var selected = operation.Bookshelf.SelectedItem;
        var obstruction = Path.Combine(fixture.State, FolderConfigCollection.FileName + ".tmp"); Directory.CreateDirectory(obstruction);
        try { await Assert.ThrowsAsync<UnauthorizedAccessException>(() => operation.ChangeFolderOrderAsync(FolderOrder.Random)); }
        finally { Directory.Delete(obstruction); }
        Assert.Equal(FolderOrder.FileName, operation.Bookshelf.FolderOrder); Assert.Same(selected, operation.Bookshelf.SelectedItem);
        Assert.Equal(0, state.FolderConfigs.GetFolderParameter(fixture.Root).Seed);
        foreach (var name in names) Assert.Equal(previous[name], File.ReadAllBytes(Path.Combine(fixture.State, name)));
        await operation.ChangeFolderOrderAsync(FolderOrder.Random); Assert.Equal(FolderOrder.Random, operation.Bookshelf.FolderOrder);
    }

    /// <summary>四文件中断恢复与旧三文件标记兼容；新目录文件不能遗留半提交参数。</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task InterruptedSaveRestoresFolderFileOrRemovesNewFile(bool existed)
    {
        using var fixture = new Fixture(); Directory.CreateDirectory(fixture.State);
        var path = Path.Combine(fixture.State, FolderConfigCollection.FileName);
        if (existed) await File.WriteAllTextAsync(path + ".save-backup", """{"Folders":[{"Place":"/books","Parameter":{"FolderOrder":1}}]}""", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(path, """{"Folders":[{"Place":"/books","Parameter":{"FolderOrder":12,"Seed":123}}]}""", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(fixture.State, ".save-pending.json"), new JsonObject { [FolderConfigCollection.FileName] = existed }.ToJsonString(), TestContext.Current.CancellationToken);
        var state = new SaveData(fixture.State); await state.LoadAsync(TestContext.Current.CancellationToken);
        Assert.Equal(existed ? FolderOrder.FileNameDescending : null, state.FolderConfigs.GetFolderParameter("/books").FolderOrder);
        Assert.Equal(existed, File.Exists(path)); Assert.False(File.Exists(Path.Combine(fixture.State, ".save-pending.json")));
    }

    /// <summary>损坏参数及重复路径拒绝加载，原文件保持只读；Mac大小写不同路径不合并。</summary>
    [Fact]
    public async Task CorruptFolderDataIsNotOverwrittenAndCaseSensitivePathsStayDistinct()
    {
        using var fixture = new Fixture(); Directory.CreateDirectory(fixture.State);
        var path = Path.Combine(fixture.State, FolderConfigCollection.FileName);
        var text = """{"Folders":[{"Place":"/books","Parameter":{"Seed":"bad"}}]}"""; await File.WriteAllTextAsync(path, text, TestContext.Current.CancellationToken);
        await Assert.ThrowsAsync<System.Text.Json.JsonException>(() => new SaveData(fixture.State).LoadAsync(TestContext.Current.CancellationToken)); Assert.Equal(text, await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
        var folders = new FolderConfigCollection(); folders.SetFolderParameter("/Books", new() { FolderOrder = FolderOrder.Size });
        folders.SetFolderParameter("/books", new() { FolderOrder = FolderOrder.TimeStamp });
        Assert.Equal(FolderOrder.Size, folders.GetFolderParameter("/Books").FolderOrder); Assert.Equal(FolderOrder.TimeStamp, folders.GetFolderParameter("/books").FolderOrder);
        Assert.Throws<System.Text.Json.JsonException>(() => folders.Restore(JsonNode.Parse("""{"Folders":[{"Place":"/a"},{"Place":"/a"}]}""")!.AsObject()));
        Assert.Throws<System.Text.Json.JsonException>(() => folders.Restore(JsonNode.Parse("""{"Folders":42}""")!.AsObject()));
        Assert.Throws<System.Text.Json.JsonException>(() => folders.Restore(JsonNode.Parse("""{"Folders":[null]}""")!.AsObject()));
    }

    /// <summary>原RandomBook排除正文当前书，使用独立书架；失败不提交，也不切换排序。</summary>
    [Fact]
    public async Task RandomBookKeepsOrderAndExcludesCurrentBook()
    {
        using var fixture = new Fixture(); var state = new SaveData(fixture.State); await state.LoadAsync(TestContext.Current.CancellationToken);
        await using var operation = fixture.Operation(state); await operation.OpenAsync(fixture.Images, TestContext.Current.CancellationToken);
        var library = Path.Combine(fixture.Root, "library"); Directory.CreateDirectory(library);
        File.Copy(fixture.Zip, Path.Combine(library, "Only.cbz"));
        await operation.Bookshelf.SetPlaceAsync(library, token: TestContext.Current.CancellationToken);
        var commands = new CommandTable(operation); await commands.ExecuteAsync("RandomBook");
        Assert.Equal("Only.cbz", Path.GetFileName(operation.Book!.Path)); Assert.Equal(operation.Book.Path, operation.Bookshelf.SelectedItem!.Path);
        Assert.Equal(FolderOrder.FileName, operation.Bookshelf.FolderOrder);
        var book = operation.Book; await commands.ExecuteAsync("RandomBook"); Assert.Same(book, operation.Book);
        await operation.Bookshelf.SetPlaceAsync(fixture.Images, token: TestContext.Current.CancellationToken);
        await commands.ExecuteAsync("RandomBook"); Assert.Same(book, operation.Book);
    }

    /// <summary>取消排队参数编辑不执行候选，四文件落盘状态与运行状态一致。</summary>
    [Fact]
    public async Task CancelledEditDoesNotApplyCandidate()
    {
        using var fixture = new Fixture(); var state = new SaveData(fixture.State); await state.LoadAsync(TestContext.Current.CancellationToken);
        state.FolderConfigs.SetFolderParameter("/books", new() { FolderOrder = FolderOrder.Size });
        await state.SaveAsync(null, TestContext.Current.CancellationToken);
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => state.EditFolderParametersAsync(
            () => state.FolderConfigs.SetFolderParameter("/books", new() { FolderOrder = FolderOrder.Random, Seed = 1 }), cancelled.Token));
        Assert.Equal(FolderOrder.Size, state.FolderConfigs.GetFolderParameter("/books").FolderOrder);
        var fresh = new SaveData(fixture.State); await fresh.LoadAsync(TestContext.Current.CancellationToken);
        Assert.Equal(FolderOrder.Size, fresh.FolderConfigs.GetFolderParameter("/books").FolderOrder);
    }

    /// <summary>失败快照与原始空/null缩略字段一致，保存副本的默认清理不污染运行集合。</summary>
    [Fact]
    public void RollbackSnapshotKeepsRawEmptyThumbnailFields()
    {
        Config.SetCurrent(new()); var raw = JsonNode.Parse("""{"Folders":[{"Place":"/empty","Parameter":null,"Thumbs":{}},{"Place":"/null","Thumbs":null}]}""")!.AsObject();
        var folders = new FolderConfigCollection(); folders.Restore(raw);
        Assert.True(JsonNode.DeepEquals(raw, folders.CreateMemento(forSave: false)));
        Assert.Empty(folders.CreateMemento()["Folders"]!.AsArray());
        Assert.True(JsonNode.DeepEquals(raw, folders.CreateMemento(forSave: false)));
    }
}
