using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using NeeView;
using NeeView.Backends;
using NeeView.MacOS.Views;

namespace NeeView.Engine.Tests;

/// <summary>原当前页组策略和真实批次结果回归；全部操作临时副本。</summary>
public sealed class DestinationMultiPageTests
{
    private sealed class TestPlatform : IPlatformService
    {
        public Task RevealAsync(string path, CancellationToken token = default) => Task.CompletedTask;
        public Task TrashAsync(string path, CancellationToken token = default) => Task.CompletedTask;
    }
    private sealed class ObservedBackend(IFileOperationBackend inner) : IFileOperationBackend
    {
        public List<string> Sources { get; } = [];
        public bool FailSecond { get; init; }
        public Action? AfterFirst { get; init; }
        public Task<bool> FileExistsAsync(string path, CancellationToken token) => inner.FileExistsAsync(path, token);
        public async Task<FileTransferResult> TransferAsync(FileTransferRequest request, CancellationToken token)
        {
            Sources.Add(Path.GetFileName(request.Source));
            if (FailSecond && Sources.Count == 2) throw new IOException("模拟第二项失败");
            var result = await inner.TransferAsync(request, token);
            if (Sources.Count == 1) AfterFirst?.Invoke();
            return result;
        }
        public Task ReleaseAsync(FileTransferResult result) => inner.ReleaseAsync(result);
        public Task<IReadOnlyList<string>> RecoverAsync(CancellationToken token = default) => inner.RecoverAsync(token);
        public Task CreateDirectoryAsync(string parent, string name, CancellationToken token) => inner.CreateDirectoryAsync(parent, name, token);
    }
    private static void ConfigureWide(PageReadOrder order = PageReadOrder.RightToLeft)
    {
        var settings = Config.Current.BookSettingDefault;
        settings.PageMode = PageMode.WidePage; settings.BookReadOrder = order;
        settings.IsSupportedSingleFirstPage = false; settings.IsSupportedSingleLastPage = false; settings.IsSupportedWidePage = false;
    }
    private static DestinationMoveService Attach(Fixture f, BookOperation operation, BitmapFactory images, IFileOperationBackend? backend = null)
    {
        backend ??= new FileOperationBackend(Path.Combine(f.State, "FileRecovery"));
        var moves = new DestinationMoveService(backend); operation.AttachFileOperations(moves, backend, images);
        return moves;
    }
    private static DestinationFolder Target(Fixture f) => new("目标", Directory.CreateDirectory(Path.Combine(f.Root, "target")).FullName);

    [Theory]
    [InlineData(PageReadOrder.RightToLeft, MultiPagePolicy.Once, "001.png")]
    [InlineData(PageReadOrder.LeftToRight, MultiPagePolicy.Once, "001.png")]
    [InlineData(PageReadOrder.RightToLeft, MultiPagePolicy.All, "001.png,002.png")]
    [InlineData(PageReadOrder.LeftToRight, MultiPagePolicy.All, "001.png,002.png")]
    [InlineData(PageReadOrder.RightToLeft, MultiPagePolicy.AllLeftToRight, "002.png,001.png")]
    [InlineData(PageReadOrder.LeftToRight, MultiPagePolicy.AllLeftToRight, "001.png,002.png")]
    public async Task OriginalCurrentPageGroupSelectionKeepsPolicyAndReadOrder(PageReadOrder order, MultiPagePolicy policy, string expected)
    {
        using var f = new Fixture(); var state = new SaveData(f.State); await state.LoadAsync(TestContext.Current.CancellationToken); ConfigureWide(order);
        await using var op = f.Operation(state); await op.OpenAsync(f.Images, TestContext.Current.CancellationToken);
        Assert.Equal(2, op.Book!.CurrentPages.Count); Assert.Equal("001.png", op.Book.CurrentPage!.EntryName);
        Assert.Equal(expected, string.Join(',', op.CollectFileActionPages(policy).Select(page => page.EntryName)));
    }

    [Theory]
    [InlineData(MultiPagePolicy.All, "001.png,002.png", "002.png")]
    [InlineData(MultiPagePolicy.AllLeftToRight, "002.png,001.png", "001.png")]
    public async Task BatchMoveUsesCapturedGroupAndRecordsEachActualSuccess(MultiPagePolicy policy, string order, string undoPage)
    {
        using var f = new Fixture(); var state = new SaveData(f.State); await state.LoadAsync(TestContext.Current.CancellationToken); ConfigureWide();
        await using var op = f.Operation(state); using var images = new BitmapFactory(new MagickImageDecoder());
        var backend = new ObservedBackend(new FileOperationBackend(Path.Combine(f.State, "FileRecovery"))); var moves = Attach(f, op, images, backend);
        Config.Current.System.IsFileWriteAccessEnabled = true; var target = Target(f);
        Config.Current.System.DestinationFolderCollection = new([target]); state.SetCommandParameter("MoveToDestinationFolder1", new MoveToFolderAsCommandParameter { Index = 1, MultiPagePolicy = policy });
        await op.OpenAsync(f.Images, TestContext.Current.CancellationToken); await new CommandTable(op).ExecuteAsync("MoveToDestinationFolder1");
        Assert.Equal(order, string.Join(',', backend.Sources)); Assert.Equal(2, moves.UndoCount);
        Assert.Equal(3, op.Book!.Pages.Count); Assert.Equal("003.png", op.Book.CurrentPage!.EntryName);
        Assert.False(File.Exists(Path.Combine(f.Images, "001.png"))); Assert.False(File.Exists(Path.Combine(f.Images, "002.png")));
        await op.ReplayDestinationMoveAsync(true, TestContext.Current.CancellationToken);
        Assert.True(File.Exists(Path.Combine(f.Images, undoPage))); Assert.Equal(undoPage, op.Book!.CurrentPage!.EntryName);
        Assert.Equal(1, moves.UndoCount); Assert.Equal(1, moves.RedoCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedOrLateCancelledBatchStillCoordinatesAlreadyCommittedItem(bool cancel)
    {
        using var f = new Fixture(); var state = new SaveData(f.State); await state.LoadAsync(TestContext.Current.CancellationToken); ConfigureWide();
        await using var op = f.Operation(state); using var images = new BitmapFactory(new MagickImageDecoder()); using var token = new CancellationTokenSource();
        var backend = new ObservedBackend(new FileOperationBackend(Path.Combine(f.State, "FileRecovery"))) { FailSecond = !cancel, AfterFirst = cancel ? token.Cancel : null };
        var moves = Attach(f, op, images, backend); Config.Current.System.IsFileWriteAccessEnabled = true; var target = Target(f);
        await op.OpenAsync(f.Images, TestContext.Current.CancellationToken); await op.ClassifyAsync(target, false, token.Token, MultiPagePolicy.All);
        Assert.Equal(1, moves.UndoCount); Assert.Equal(4, op.Book!.Pages.Count); Assert.Equal("002.png", op.Book.CurrentPage!.EntryName);
        Assert.False(File.Exists(Path.Combine(f.Images, "001.png"))); Assert.True(File.Exists(Path.Combine(f.Images, "002.png")));
        Assert.True(File.Exists(Path.Combine(target.Path, "001.png"))); Assert.False(File.Exists(Path.Combine(target.Path, "002.png")));
        if (!cancel) Assert.Contains("第二项失败", op.Error);
        await op.ReplayDestinationMoveAsync(true, TestContext.Current.CancellationToken); Assert.Equal(5, op.Book!.Pages.Count);
    }

    [Theory]
    [InlineData(MultiPagePolicy.Once, "001.png")]
    [InlineData(MultiPagePolicy.All, "001.png,002.png")]
    [InlineData(MultiPagePolicy.AllLeftToRight, "002.png,001.png")]
    public async Task FixedCopyIgnoresPanelMoveModeAndSourceWriteSwitchAndKeepsReadingIdentity(MultiPagePolicy policy, string order)
    {
        using var f = new Fixture(); var state = new SaveData(f.State); await state.LoadAsync(TestContext.Current.CancellationToken); ConfigureWide();
        await using var op = f.Operation(state); using var images = new BitmapFactory(new MagickImageDecoder());
        var backend = new ObservedBackend(new FileOperationBackend(Path.Combine(f.State, "FileRecovery"))); var moves = Attach(f, op, images, backend); var target = Target(f);
        await op.OpenAsync(f.Images, TestContext.Current.CancellationToken); var book = op.Book!; var page = book.CurrentPage;
        Assert.False(op.CanFileAction); Assert.True(op.CanTransferFileActionPages(MultiPagePolicy.All, requireWriteAccess: false));
        await op.CopyToFolderAsync(target, policy, TestContext.Current.CancellationToken);
        Assert.Same(book, op.Book); Assert.Same(page, book.CurrentPage); Assert.Equal(5, book.Pages.Count); Assert.Equal(2, book.CurrentPages.Count);
        Assert.Equal(order, string.Join(',', backend.Sources)); Assert.Equal(order.Split(',').Length, Directory.GetFiles(target.Path).Length);
        Assert.Equal(5, Directory.GetFiles(f.Images).Length); Assert.Equal(0, moves.UndoCount);
        await op.OpenAsync(f.Zip, TestContext.Current.CancellationToken); Assert.False(op.CanTransferFileActionPages(MultiPagePolicy.All, requireWriteAccess: false));
    }

    [Fact]
    public async Task DecliningSecondOverwriteRetainsFirstSuccessAndOriginalTarget()
    {
        using var f = new Fixture(); var state = new SaveData(f.State); await state.LoadAsync(TestContext.Current.CancellationToken); ConfigureWide();
        await using var op = f.Operation(state); using var images = new BitmapFactory(new MagickImageDecoder()); var moves = Attach(f, op, images); var target = Target(f);
        Config.Current.System.IsFileWriteAccessEnabled = true; moves.ConfirmOverwriteAsync = _ => Task.FromResult(false);
        await File.WriteAllTextAsync(Path.Combine(target.Path, "002.png"), "已有目标", TestContext.Current.CancellationToken);
        await op.OpenAsync(f.Images, TestContext.Current.CancellationToken); await op.ClassifyAsync(target, false, TestContext.Current.CancellationToken, MultiPagePolicy.All);
        Assert.Equal(1, moves.UndoCount); Assert.Equal("002.png", op.Book!.CurrentPage!.EntryName); Assert.Equal(4, op.Book.Pages.Count);
        Assert.Equal("已有目标", await File.ReadAllTextAsync(Path.Combine(target.Path, "002.png"), TestContext.Current.CancellationToken));
        Assert.True(File.Exists(Path.Combine(f.Images, "002.png"))); Assert.False(File.Exists(Path.Combine(f.Images, "001.png")));
    }

    [Fact]
    public async Task MissingSecondSourceRejectsWholeCapturedGroupBeforeFirstTransfer()
    {
        using var f = new Fixture(); var state = new SaveData(f.State); await state.LoadAsync(TestContext.Current.CancellationToken); ConfigureWide();
        await using var op = f.Operation(state); using var images = new BitmapFactory(new MagickImageDecoder()); var moves = Attach(f, op, images); var target = Target(f);
        Config.Current.System.IsFileWriteAccessEnabled = true;
        await op.OpenAsync(f.Images, TestContext.Current.CancellationToken); File.Delete(Path.Combine(f.Images, "002.png"));
        await op.ClassifyAsync(target, false, TestContext.Current.CancellationToken, MultiPagePolicy.All);
        Assert.Equal(0, moves.UndoCount); Assert.Empty(Directory.GetFiles(target.Path)); Assert.True(File.Exists(Path.Combine(f.Images, "001.png")));
        Assert.Equal("001.png", op.Book!.CurrentPage!.EntryName); Assert.Contains("源图片已不存在", op.Error);
    }

    [Fact]
    public async Task MixedCurrentGroupIsRejectedBeforeAnyFileOperation()
    {
        using var f = new Fixture(); var state = new SaveData(f.State); await state.LoadAsync(TestContext.Current.CancellationToken); ConfigureWide();
        Directory.CreateDirectory(Path.Combine(f.Images, "000-child"));
        await using var op = f.Operation(state); using var images = new BitmapFactory(new MagickImageDecoder());
        var backend = new ObservedBackend(new FileOperationBackend(Path.Combine(f.State, "FileRecovery"))); Attach(f, op, images, backend); Config.Current.System.IsFileWriteAccessEnabled = true;
        await op.OpenAsync(f.Images, TestContext.Current.CancellationToken);
        Assert.Contains(op.Book!.CurrentPages, page => !page.IsImage); Assert.Equal(2, op.Book.CurrentPages.Count);
        Assert.False(op.CanTransferFileActionPages(MultiPagePolicy.All)); await op.ClassifyAsync(Target(f), false, TestContext.Current.CancellationToken, MultiPagePolicy.All);
        Assert.Empty(backend.Sources); Assert.Equal(5, Directory.GetFiles(f.Images).Length);
    }

    [AvaloniaFact]
    public async Task FormalFixedCopyUsesOriginalParameterEditorAndMenuCommand()
    {
        using var f = new Fixture(); var state = new SaveData(f.State); await state.LoadAsync(TestContext.Current.CancellationToken); ConfigureWide();
        var op = f.Operation(state); var images = new BitmapFactory(new MagickImageDecoder()); var moves = Attach(f, op, images); var target = Target(f);
        Config.Current.System.DestinationFolderCollection = new([target]);
        state.SetCommandParameter("CopyToFolderAs", new CopyToFolderAsCommandParameter { Index = 1, MultiPagePolicy = MultiPagePolicy.AllLeftToRight });
        var draft = NeeView.MacOS.ViewModels.CommandParameterEdit.Create(state, "CopyToFolderAs")!;
        Assert.Equal(MultiPagePolicy.AllLeftToRight, Assert.IsType<CopyToFolderAsCommandParameter>(draft.Value).MultiPagePolicy);
        var window = new MainWindow(); window.Bind(new(op, new CommandTable(op), state), images, new TestPlatform()); window.Show();
        try
        {
            await window.OpenAsync(f.Images); Assert.True(window.IsCommandAvailable("CopyToFolderAs")); Assert.False(window.IsCommandAvailable("MoveToFolderAs"));
            var main = op.Book!.CurrentPage; await window.ExecuteAsync("CopyToFolderAs");
            Assert.Equal(2, Directory.GetFiles(target.Path).Length); Assert.Same(main, op.Book.CurrentPage); Assert.Equal(0, moves.UndoCount);
            await window.PrepareShutdownAsync();
        }
        finally { await window.PrepareShutdownAsync(); window.Close(); }
    }
}
