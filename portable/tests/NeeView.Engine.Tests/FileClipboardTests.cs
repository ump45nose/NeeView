using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using ImageMagick;
using NeeView;
using NeeView.Backends;
using NeeView.MacOS.ViewModels;
using NeeView.MacOS.Views;
namespace NeeView.Engine.Tests;

/// <summary>原文件页组/书籍复制和Paste加载语义；临时来源及模拟系统剪贴板，不污染用户数据。</summary>
public sealed class FileClipboardTests
{
    private sealed class Clipboard : IFileClipboard
    {
        public FileClipboardContent Content { get; set; } = new([], []);
        public bool HasFileContent => Content.Files.Count > 0 || Content.QueryPaths.Count > 0;
        public Func<CancellationToken, Task>? BeforeWrite { get; set; }
        public Func<Task>? AfterCommit { get; set; }
        public Func<CancellationToken, Task>? BeforeRead { get; set; }
        public int Writes { get; private set; }
        public async Task WriteAsync(FileClipboardContent content, CancellationToken token)
        {
            if (BeforeWrite is not null) await BeforeWrite(token);
            token.ThrowIfCancellationRequested();
            Content = content; Writes++;
            if (AfterCommit is not null) await AfterCommit(); // 提交后不以晚取消伪装失败。
        }
        public async Task<FileClipboardContent> ReadAsync(CancellationToken token)
        { if (BeforeRead is not null) await BeforeRead(token); return Content; }
    }
    private sealed class Platform : IPlatformService
    {
        public Task RevealAsync(string path, CancellationToken token = default) => Task.CompletedTask;
        public Task TrashAsync(string path, CancellationToken token = default) => throw new InvalidOperationException("剪贴板不应删除文件");
    }
    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static void Wide(PageReadOrder order = PageReadOrder.RightToLeft)
    {
        var setting = Config.Current.BookSettingDefault;
        setting.PageMode = PageMode.WidePage; setting.BookReadOrder = order;
        setting.IsSupportedSingleFirstPage = false; setting.IsSupportedSingleLastPage = false; setting.IsSupportedWidePage = false;
    }
    private static async Task<SaveData> State(Fixture f)
    { var state = new SaveData(f.State); await state.LoadAsync(TestContext.Current.CancellationToken); return state; }
    [Theory]
    [InlineData(PageReadOrder.RightToLeft, MultiPagePolicy.Once, "001.png")]
    [InlineData(PageReadOrder.LeftToRight, MultiPagePolicy.Once, "001.png")]
    [InlineData(PageReadOrder.RightToLeft, MultiPagePolicy.All, "001.png,002.png")]
    [InlineData(PageReadOrder.LeftToRight, MultiPagePolicy.All, "001.png,002.png")]
    [InlineData(PageReadOrder.RightToLeft, MultiPagePolicy.AllLeftToRight, "002.png,001.png")]
    [InlineData(PageReadOrder.LeftToRight, MultiPagePolicy.AllLeftToRight, "001.png,002.png")]
    public async Task CopyUsesOriginalPageGroupWithoutWritingSourceOrMoveHistory(PageReadOrder order, MultiPagePolicy policy, string expected)
    {
        using var f = new Fixture(); var state = await State(f); Wide(order);
        await using var op = f.Operation(state); using var images = new BitmapFactory(new MagickImageDecoder());
        var backend = new FileOperationBackend(Path.Combine(f.State, "Recovery")); var moves = new DestinationMoveService(backend); op.AttachFileOperations(moves, backend, images);
        var clipboard = new Clipboard(); op.AttachFileClipboard(clipboard);
        state.SetCommandParameter("CopyFile", new CopyFileCommandParameter { MultiPagePolicy = policy });
        await op.OpenAsync(f.Images, TestContext.Current.CancellationToken); var book = op.Book; var page = book!.CurrentPage; var position = op.Position;
        Assert.False(Config.Current.System.IsFileWriteAccessEnabled); Assert.True(op.CanCopyFiles(policy));
        await new CommandTable(op).ExecuteAsync("CopyFile");
        Assert.Equal(expected, string.Join(',', clipboard.Content.Files.Select(Path.GetFileName)));
        Assert.Equal(clipboard.Content.Files, clipboard.Content.QueryPaths); Assert.Null(clipboard.Content.Text);
        Assert.Same(book, op.Book); Assert.Same(page, book.CurrentPage); Assert.Equal(position, op.Position);
        Assert.Equal(5, Directory.GetFiles(f.Images).Length); Assert.Equal(0, moves.UndoCount); Assert.Equal(1, clipboard.Writes);
    }
    [Fact]
    public async Task SplitPageIsCopiedOnceAndMasonryRequiresExplicitSelection()
    {
        using var f = new Fixture(); var state = await State(f);
        using (var image = new MagickImage(MagickColors.Red, 1200, 400)) image.Write(Path.Combine(f.Images, "001.png"));
        Config.Current.BookSettingDefault.IsSupportedDividePage = true;
        await using var op = f.Operation(state); var clipboard = new Clipboard(); op.AttachFileClipboard(clipboard);
        state.SetCommandParameter("CopyFile", new CopyFileCommandParameter { MultiPagePolicy = MultiPagePolicy.All });
        await op.OpenAsync(f.Images, TestContext.Current.CancellationToken); await op.CopyFilesAsync(token: TestContext.Current.CancellationToken); Assert.Single(clipboard.Content.Files);
        await op.SetBrowseModeAsync(BrowseLayoutMode.Masonry); Assert.False(op.CanCopyFiles(MultiPagePolicy.All));
        await op.SelectFileActionPageAsync(op.Book!, op.Book!.Pages[2]); await op.CopyFilesAsync(token: TestContext.Current.CancellationToken); Assert.EndsWith("003.png", Assert.Single(clipboard.Content.Files));
    }
    [Theory]
    [InlineData("directory")]
    [InlineData("archive")]
    [InlineData("image")]
    public async Task CopyBookTargetsRootEntityAndIgnoresInvalidCopyFileParameter(string kind)
    {
        using var f = new Fixture(); var state = await State(f); await using var op = f.Operation(state); var clipboard = new Clipboard(); op.AttachFileClipboard(clipboard);
        state.SetCommandParameter("CopyFile", new CopyFileCommandParameter { MultiPagePolicy = (MultiPagePolicy)99 });
        await op.OpenAsync(kind == "archive" ? f.Zip : kind == "image" ? Path.Combine(f.Images, "003.png") : f.Images, TestContext.Current.CancellationToken);
        Assert.True(op.CanCopyBook); Assert.False(op.CanCopyFiles((MultiPagePolicy)99)); await op.CopyFilesAsync(book: true, token: TestContext.Current.CancellationToken);
        Assert.Equal(kind == "archive" ? f.Zip : f.Images, Assert.Single(clipboard.Content.Files)); Assert.Null(op.Error);
        if (kind == "archive") { Assert.False(op.CanCopyFiles(MultiPagePolicy.Once)); await op.CopyFilesAsync(token: TestContext.Current.CancellationToken); Assert.Equal(1, clipboard.Writes); }
    }
    [Theory]
    [InlineData(TextCopyPolicy.None)]
    [InlineData(TextCopyPolicy.CopyFilePath)]
    [InlineData(TextCopyPolicy.OriginalPath)]
    public async Task OriginalTextPolicyPreservesNumbersAndOptionalText(TextCopyPolicy policy)
    {
        using var f = new Fixture(); var state = await State(f); await using var op = f.Operation(state); var clipboard = new Clipboard(); op.AttachFileClipboard(clipboard);
        Config.Current.System.TextCopyPolicy = policy; await op.OpenAsync(f.Images, TestContext.Current.CancellationToken); await op.CopyFilesAsync(token: TestContext.Current.CancellationToken);
        Assert.Equal(policy == TextCopyPolicy.None ? null : Path.Combine(f.Images, "001.png"), clipboard.Content.Text);
        await op.SaveAsync(); await State(f); Assert.Equal(policy, Config.Current.System.TextCopyPolicy);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingOrSymbolicEntityDoesNotReplaceClipboard(bool link)
    {
        using var f = new Fixture(); var state = await State(f); await using var op = f.Operation(state); var clipboard = new Clipboard { Content = new([f.Zip], []) }; op.AttachFileClipboard(clipboard);
        await op.OpenAsync(f.Images, TestContext.Current.CancellationToken); var path = Path.Combine(f.Images, "001.png"); File.Delete(path);
        if (link) File.CreateSymbolicLink(path, Path.Combine(f.Images, "002.png"));
        await op.CopyFilesAsync(token: TestContext.Current.CancellationToken); Assert.Equal(0, clipboard.Writes); Assert.Equal(f.Zip, Assert.Single(clipboard.Content.Files)); Assert.NotNull(op.Error); Assert.False(op.IsUsingClipboard);
    }
    [Fact]
    public async Task CancellationFailureAndCommittedLateCancellationRemainReusable()
    {
        using var f = new Fixture(); var state = await State(f); await using var op = f.Operation(state); var clipboard = new Clipboard(); op.AttachFileClipboard(clipboard);
        await op.OpenAsync(f.Images, TestContext.Current.CancellationToken);
        clipboard.BeforeWrite = _ => throw new IOException("拒绝写入"); await op.CopyFilesAsync(token: TestContext.Current.CancellationToken); Assert.Contains("拒绝写入", op.Error); Assert.False(op.IsUsingClipboard); Assert.Equal(0, clipboard.Writes);
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel(); clipboard.BeforeWrite = null;
        await op.CopyFilesAsync(token: cancelled.Token); Assert.Equal(0, clipboard.Writes); Assert.False(op.IsUsingClipboard);
        using var late = new CancellationTokenSource(); clipboard.AfterCommit = () => { late.Cancel(); return Task.CompletedTask; };
        await op.CopyFilesAsync(token: late.Token); Assert.Equal(1, clipboard.Writes); Assert.Null(op.Error); Assert.False(op.IsUsingClipboard);
        clipboard.AfterCommit = null; await op.CopyFilesAsync(token: TestContext.Current.CancellationToken); Assert.Equal(2, clipboard.Writes);
    }
    [Fact]
    public async Task SwitchingBookCancelsQueuedCopyBeforeClipboardCommit()
    {
        using var f = new Fixture(); var state = await State(f); await using var op = f.Operation(state); var clipboard = new Clipboard(); op.AttachFileClipboard(clipboard);
        await op.OpenAsync(f.Images, TestContext.Current.CancellationToken); var entered = Signal();
        clipboard.BeforeWrite = async token => { entered.SetResult(); await Task.Delay(Timeout.Infinite, token); };
        var copy = op.CopyFilesAsync(token: TestContext.Current.CancellationToken); await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await op.OpenAsync(f.Zip, TestContext.Current.CancellationToken); await copy;
        Assert.Equal(f.Zip, op.Book!.Path); Assert.Equal(0, clipboard.Writes); Assert.Null(op.Error); Assert.False(op.IsUsingClipboard);
    }
    [Fact]
    public async Task LateCommittedCopyCannotClearNewerOpenFailure()
    {
        using var f = new Fixture(); var state = await State(f); await using var op = f.Operation(state); var entered = Signal(); var release = Signal();
        var clipboard = new Clipboard { AfterCommit = async () => { entered.SetResult(); await release.Task; } }; op.AttachFileClipboard(clipboard);
        await op.OpenAsync(f.Images, TestContext.Current.CancellationToken); var copy = op.CopyFilesAsync(token: TestContext.Current.CancellationToken);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            await op.OpenAsync(Path.Combine(f.Root, "missing.cbz"), TestContext.Current.CancellationToken); var error = op.Error; Assert.NotNull(error);
            release.SetResult(); await copy; Assert.Equal(error, op.Error); Assert.Equal(1, clipboard.Writes);
        }
        finally { release.TrySetResult(); await copy; }
    }
    [Fact]
    public async Task CloseCancelsLockWaitWithoutReleasingAnotherOwnersNavigationLock()
    {
        using var f = new Fixture(); var state = await State(f); var op = f.Operation(state); var clipboard = new Clipboard(); op.AttachFileClipboard(clipboard);
        await op.OpenAsync(f.Images, TestContext.Current.CancellationToken);
        var gate = (SemaphoreSlim)typeof(BookOperation).GetField("_gate", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(op)!;
        await gate.WaitAsync(TestContext.Current.CancellationToken);
        try
        {
            var copy = op.CopyFilesAsync(token: TestContext.Current.CancellationToken); Assert.True(op.IsUsingClipboard); var closing = op.DisposeAsync().AsTask();
            await copy.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken); Assert.False(closing.IsCompleted); Assert.Equal(0, gate.CurrentCount); gate.Release();
            await closing.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken); Assert.Equal(1, gate.CurrentCount); Assert.Equal(0, clipboard.Writes);
        }
        finally { if (gate.CurrentCount == 0) gate.Release(); await op.DisposeAsync(); }
    }
    [Fact]
    public async Task CommittedCopyBlocksFileActionsAndCloseWaitsForItsActualResult()
    {
        using var f = new Fixture(); var state = await State(f); var op = f.Operation(state); using var images = new BitmapFactory(new MagickImageDecoder()); var backend = new FileOperationBackend(Path.Combine(f.State, "Recovery"));
        op.AttachFileOperations(new(backend), backend, images); op.AttachFileDeletion(new Platform(), images); Config.Current.System.IsFileWriteAccessEnabled = true;
        var entered = Signal(); var release = Signal(); var clipboard = new Clipboard { AfterCommit = async () => { entered.SetResult(); await release.Task; } }; op.AttachFileClipboard(clipboard);
        await op.OpenAsync(f.Images, TestContext.Current.CancellationToken); var copy = op.CopyFilesAsync(token: TestContext.Current.CancellationToken); await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        try
        {
            Assert.False(op.CanUnload); Assert.False(op.CanRenameBook); Assert.False(op.CanDeleteFile); Assert.False(op.CanFileAction); Assert.False(op.CanCopyBook); Assert.False(op.CanPasteFiles);
            await op.UnloadAsync(TestContext.Current.CancellationToken); Assert.NotNull(op.Book); var closing = op.DisposeAsync().AsTask(); await Task.Delay(20, TestContext.Current.CancellationToken); Assert.False(closing.IsCompleted);
            release.SetResult(); await copy; await closing; Assert.Null(op.Book); Assert.Equal(1, clipboard.Writes); Assert.Null(op.Error);
        }
        finally { release.TrySetResult(); await op.DisposeAsync(); }
    }
    [Fact]
    public async Task FailedCloseRestoresClipboardAbilityForRetry()
    {
        using var f = new Fixture(); var state = await State(f); var op = f.Operation(state); var clipboard = new Clipboard(); op.AttachFileClipboard(clipboard);
        await op.OpenAsync(f.Images, TestContext.Current.CancellationToken); var blocker = Path.Combine(f.State, "UserSetting.json.tmp"); Directory.CreateDirectory(blocker);
        try { await Assert.ThrowsAnyAsync<Exception>(() => op.DisposeAsync().AsTask()); Assert.True(op.CanCopyBook); await op.CopyFilesAsync(token: TestContext.Current.CancellationToken); Assert.Equal(1, clipboard.Writes); }
        finally { Directory.Delete(blocker); await op.DisposeAsync(); }
    }
    [Theory]
    [InlineData("image")]
    [InlineData("directory")]
    [InlineData("archive")]
    public async Task PasteOpensOneSourceThroughOriginalChainWithQueryPathPriority(string kind)
    {
        using var f = new Fixture(); var state = await State(f); await using var op = f.Operation(state);
        var target = kind == "image" ? Path.Combine(f.Images, "003.png") : kind == "archive" ? f.Zip : f.Images;
        var clipboard = new Clipboard { Content = new(["/missing-stale-file"], [target]) }; op.AttachFileClipboard(clipboard);
        await op.OpenAsync(f.Zip, TestContext.Current.CancellationToken); await new CommandTable(op).ExecuteAsync("Paste");
        Assert.Equal(kind == "archive" ? f.Zip : f.Images, op.Book!.Path);
        if (kind == "image") Assert.Equal("003.png", op.Book.CurrentPage!.EntryName);
        Assert.Null(op.Error); Assert.Equal(5, Directory.GetFiles(f.Images).Length); Assert.Equal(0, clipboard.Writes);
    }
    [Fact]
    public async Task PasteFailureMultipleItemsAndUnsupportedTextKeepCurrentBook()
    {
        using var f = new Fixture(); var state = await State(f); await using var op = f.Operation(state); var clipboard = new Clipboard(); op.AttachFileClipboard(clipboard);
        await op.OpenAsync(f.Zip, TestContext.Current.CancellationToken); var old = op.Book;
        clipboard.Content = new([Path.Combine(f.Root, "missing.png")], []); await op.PasteFilesAsync(TestContext.Current.CancellationToken); Assert.Same(old, op.Book); Assert.NotNull(op.Error);
        clipboard.Content = new([Path.Combine(f.Images, "001.png"), Path.Combine(f.Images, "002.png")], []);
        await op.PasteFilesAsync(TestContext.Current.CancellationToken); Assert.Same(old, op.Book); Assert.Contains("未打开任何项目", op.Error);
        clipboard.Content = new([], [], Path.Combine(f.Images, "003.png")); Assert.False(op.CanPasteFiles); await op.PasteFilesAsync(TestContext.Current.CancellationToken); Assert.Same(old, op.Book);
    }
    [Fact]
    public async Task LatePasteReadCannotReplaceNewerBookOrItsError()
    {
        using var f = new Fixture(); var state = await State(f); await using var op = f.Operation(state); var entered = Signal(); var release = Signal();
        var clipboard = new Clipboard { Content = new([f.Images, f.Zip], []), BeforeRead = async _ => { entered.SetResult(); await release.Task; } }; op.AttachFileClipboard(clipboard);
        await op.OpenAsync(f.Images, TestContext.Current.CancellationToken); var paste = op.PasteFilesAsync(TestContext.Current.CancellationToken); await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await op.OpenAsync(f.Zip, TestContext.Current.CancellationToken); release.SetResult(); await paste;
        Assert.Equal(f.Zip, op.Book!.Path); Assert.Null(op.Error); Assert.False(op.IsUsingClipboard);
    }
    [Theory]
    [InlineData("file:///tmp/%E4%B8%AD%E6%96%87%20%23%25%5C.png", "/tmp/中文 #%\\.png")]
    [InlineData("file://localhost/tmp/a.png", "/tmp/a.png")]
    [InlineData("file://otherhost/tmp/a.png", null)]
    [InlineData("https://example.org/a.png", null)]
    [InlineData("file:///tmp/a.png#fragment", null)]
    [InlineData("file:///tmp/a.png?query=1", null)]
    [InlineData("file:///tmp/a%00.png", null)]
    [InlineData("/tmp/raw-path.png", null)]
    public void FileUrlDecodingKeepsMacPathsAndRejectsUnsupportedAddresses(string value, string? expected) => Assert.Equal(expected, FileClipboardCodec.DecodeFileUrl(value));
    [Fact]
    public void InvalidUrlWithinMultipleItemsCannotSilentlyBecomeOneFile()
    {
        Assert.Throws<NotSupportedException>(() => FileClipboardCodec.DecodeFileUrls(["file:///tmp/valid.png", "file://remote/tmp/invalid.png"]));
        Assert.Throws<NotSupportedException>(() => FileClipboardCodec.DecodeFileUrls([null]));
        Assert.Throws<NotSupportedException>(() => FileClipboardCodec.ValidatePaths(Enumerable.Repeat("/tmp/a.png", FileClipboardCodec.MaximumItems + 1)));
        Assert.Throws<NotSupportedException>(() => FileClipboardCodec.ValidatePaths(["relative.png"]));
        Assert.Throws<NotSupportedException>(() => FileClipboardCodec.DecodeQueryPaths("[null]"));
        Assert.Throws<JsonException>(() => FileClipboardCodec.DecodeQueryPaths("{}"));
        var paths = new[] { "/tmp/中文 % #\\.png", "/tmp/漫画.cbz/001.png" }; Assert.Equal(paths, FileClipboardCodec.DecodeQueryPaths(FileClipboardCodec.EncodeQueryPaths(paths)));
    }
    [AvaloniaFact]
    public async Task FormalMenusAndTextInputUseRealClipboardCapabilityAndKeepCutPlaceholder()
    {
        using var f = new Fixture(); var state = await State(f); var op = f.Operation(state); var clipboard = new Clipboard(); op.AttachFileClipboard(clipboard);
        var images = new BitmapFactory(new MagickImageDecoder()); var window = new MainWindow(); window.Bind(new(op, new(op), state), images, new Platform()); window.Show();
        try
        {
            await window.OpenAsync(f.Images); Assert.True(window.IsCommandAvailable("CopyFile")); Assert.True(window.IsCommandAvailable("CopyBook"));
            Assert.False(window.IsCommandAvailable("Paste")); Assert.False(window.IsCommandAvailable("CutFile")); Assert.False(window.IsCommandAvailable("CutBook"));
            await window.ExecuteAsync("CopyFile"); Assert.Equal(1, clipboard.Writes); Assert.True(window.IsCommandAvailable("Paste"));
            var input = window.FindControl<TextBox>("AddressBar")!; input.Focus();
            input.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.C, KeyModifiers = KeyModifiers.Control });
            Assert.Equal(1, clipboard.Writes);
            await window.OpenAsync(f.Zip); Assert.False(window.IsCommandAvailable("CopyFile")); Assert.True(window.IsCommandAvailable("CopyBook"));
            var method = typeof(MainWindow).GetMethod("IsCommandImplemented", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var items = new CommandTable(op).Definitions.Select(d => new { d.Name, d.Text, d.Shortcut, d.MouseGesture, d.Source, d.Stage, implemented = (bool)method.Invoke(window, [d.Name])! }).ToArray();
            Assert.Equal(235, items.Length); Assert.Equal(195, items.Count(i => i.implemented));
            await File.WriteAllTextAsync(Output("commands.json"), JsonSerializer.Serialize(new { scope = "执行入口登记，不等于完整原功能覆盖率", total = items.Length, implemented = items.Count(i => i.implemented), items }, new JsonSerializerOptions { WriteIndented = true }), TestContext.Current.CancellationToken);
            window.UpdateLayout(); using var bitmap = new RenderTargetBitmap(new PixelSize((int)window.Bounds.Width, (int)window.Bounds.Height)); bitmap.Render(window); bitmap.Save(Output("layout.png"), PngBitmapEncoderOptions.Default);
        }
        finally { await window.PrepareShutdownAsync(); window.Close(); }
    }
    [AvaloniaFact]
    public async Task FormalCloseCancelsPendingCopyWithoutWaitingForever()
    {
        using var f = new Fixture(); var state = await State(f); var op = f.Operation(state); var entered = Signal();
        var clipboard = new Clipboard { BeforeWrite = async token => { entered.SetResult(); await Task.Delay(Timeout.Infinite, token); } }; op.AttachFileClipboard(clipboard);
        var window = new MainWindow(); window.Bind(new(op, new(op), state), new(new MagickImageDecoder()), new Platform()); window.Show();
        try
        {
            await window.OpenAsync(f.Images); var action = window.ExecuteAsync("CopyFile"); await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            await window.PrepareShutdownAsync().WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken); await action;
            Assert.Equal(0, clipboard.Writes); Assert.Null(op.Book);
        }
        finally { await window.PrepareShutdownAsync(); window.Close(); }
    }
    [AvaloniaFact]
    public async Task FormalCloseWaitsForCommittedCopyAndPreventsDuplicateAction()
    {
        using var f = new Fixture(); var state = await State(f); var op = f.Operation(state); var entered = Signal(); var release = Signal();
        var clipboard = new Clipboard { AfterCommit = async () => { entered.SetResult(); await release.Task; } }; op.AttachFileClipboard(clipboard);
        var window = new MainWindow(); window.Bind(new(op, new(op), state), new(new MagickImageDecoder()), new Platform()); window.Show();
        try
        {
            await window.OpenAsync(f.Images); var copy = window.ExecuteAsync("CopyFile"); await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            await window.ExecuteAsync("CopyBook"); Assert.Equal(1, clipboard.Writes);
            var closing = window.PrepareShutdownAsync(); await Task.Delay(20, TestContext.Current.CancellationToken); Assert.False(closing.IsCompleted); Assert.NotNull(op.Book);
            release.SetResult(); await copy; await closing; Assert.Equal(1, clipboard.Writes); Assert.Null(op.Book);
        }
        finally { release.TrySetResult(); await window.PrepareShutdownAsync(); window.Close(); }
    }
    [AvaloniaFact]
    public async Task FormalTextAndCopyParametersSaveAtomicallyAndKeepUnknownFields()
    {
        using var f = new Fixture(); Directory.CreateDirectory(f.State);
        await File.WriteAllTextAsync(Path.Combine(f.State, "UserSetting.json"), "{\"Config\":{\"System\":{\"TextCopyPolicy\":0,\"Future\":\"kept\"}},\"Commands\":{\"CopyFile\":{\"Parameter\":{\"MultiPagePolicy\":0,\"Future\":\"kept\"}}}}", TestContext.Current.CancellationToken);
        var state = await State(f); var op = f.Operation(state); var images = new BitmapFactory(new MagickImageDecoder()); var model = new ReaderWorkspaceViewModel(op, new(op), state);
        var window = new MainWindow(); window.Bind(model, images, new Platform()); window.Show();
        try
        {
            await window.OpenAsync(f.Images); var settings = new SettingsWindow(model); settings.Show(window); settings.FindControl<ListBox>("SettingsNavigation")!.SelectedIndex = 6;
            settings.FindControl<ComboBox>("TextCopyPolicy")!.SelectedIndex = 2;
            var draft = CommandParameterEdit.Create(state, "CopyFile")!; var parameters = new CommandParameterWindow(draft, "复制文件"); parameters.Show(settings);
            parameters.FindControl<ComboBox>("MultiPagePolicy")!.SelectedIndex = 2; parameters.UpdateLayout();
            using (var bitmap = new RenderTargetBitmap(new PixelSize((int)parameters.Bounds.Width, (int)parameters.Bounds.Height))) { bitmap.Render(parameters); bitmap.Save(Output("copy-parameter.png"), PngBitmapEncoderOptions.Default); }
            parameters.Close(); Assert.Equal(MultiPagePolicy.Once, state.GetCommandParameter<CopyFileCommandParameter>("CopyFile").MultiPagePolicy);
            settings.UpdateLayout(); using (var bitmap = new RenderTargetBitmap(new PixelSize((int)settings.Bounds.Width, (int)settings.Bounds.Height))) { bitmap.Render(settings); bitmap.Save(Output("settings.png"), PngBitmapEncoderOptions.Default); }
            settings.Close(); Assert.Equal(TextCopyPolicy.None, Config.Current.System.TextCopyPolicy);
            settings = new SettingsWindow(model); settings.Show(window); settings.FindControl<ComboBox>("TextCopyPolicy")!.SelectedIndex = 1;
            var drafts = (Dictionary<string, CommandParameterEdit>)typeof(SettingsWindow).GetField("_parameters", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(settings)!;
            drafts["CopyFile"] = draft;
            var blocker = Path.Combine(f.State, "UserSetting.json.tmp"); Directory.CreateDirectory(blocker);
            try
            {
                settings.FindControl<Button>("SaveSettings")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await WaitAsync(() => settings.FindControl<TextBlock>("Message")!.Text?.StartsWith("保存失败") == true);
                Assert.Equal(TextCopyPolicy.None, Config.Current.System.TextCopyPolicy); Assert.Equal(MultiPagePolicy.Once, state.GetCommandParameter<CopyFileCommandParameter>("CopyFile").MultiPagePolicy);
            }
            finally { Directory.Delete(blocker); }
            settings.FindControl<Button>("SaveSettings")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await WaitAsync(() => !settings.IsVisible);
            Assert.Equal(TextCopyPolicy.CopyFilePath, Config.Current.System.TextCopyPolicy); Assert.Equal(MultiPagePolicy.AllLeftToRight, state.GetCommandParameter<CopyFileCommandParameter>("CopyFile").MultiPagePolicy);
            var saved = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(f.State, "UserSetting.json"), TestContext.Current.CancellationToken))!;
            Assert.Equal("kept", saved["Config"]!["System"]!["Future"]!.GetValue<string>()); Assert.Equal("kept", saved["Commands"]!["CopyFile"]!["Parameter"]!["Future"]!.GetValue<string>());
        }
        finally { await window.PrepareShutdownAsync(); window.Close(); }
    }
    private static async Task WaitAsync(Func<bool> condition)
    { for (int i = 0; i < 200 && !condition(); i++) { Dispatcher.UIThread.RunJobs(); await Task.Delay(10, TestContext.Current.CancellationToken); } Assert.True(condition()); }
    private static string Output(string suffix)
    { var phase = Environment.GetEnvironmentVariable("NEEVIEW_ACCEPTANCE_PHASE") ?? "p4-clipboard"; return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../acceptance", phase + "-" + suffix)); }
}
