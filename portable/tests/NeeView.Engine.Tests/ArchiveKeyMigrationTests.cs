using System.IO.Compression;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using NeeView.Backends;
using NeeView.MacOS.ViewModels;
using NeeView.MacOS.Views;
namespace NeeView.Engine.Tests;

/// <summary>原口令状态/进程缓存与唯一加载链；替换渲染器验证编排，真实解锁由原生专项证明。</summary>
public sealed class ArchiveKeyMigrationTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private const string TestPassword = "synthetic-test-only";
    private sealed class LockedRenderer(string? key = null) : IPdfRenderer
    {
        public IPdfRenderer WithPassword(string password) => new LockedRenderer(password);
        public PdfDocumentInfo Inspect(string path, CancellationToken token)
        { token.ThrowIfCancellationRequested(); if (key != TestPassword) throw new ArchiveKeyRequiredException(); return new([new(200, 300)], [], default); }
        public DecodedImageLease Render(string path, int page, Size target, CancellationToken token)
        { _ = Inspect(path, token); return new(target, new byte[checked((int)target.Width * (int)target.Height * 4)]); }
    }
    private static string Locked(Fixture f) { var path = Path.Combine(f.Root, "locked.pdf"); File.WriteAllText(path, "locked test provider"); return path; }
    [Fact]
    public async Task OriginalKeyStateCancellationAndLateTextDoNotCommit()
    {
        var path = Guid.NewGuid().ToString(); var key = new ArchiveKey(path); key.TryRestoreCache(); Assert.Equal(ArchiveKey.ArchiveKeyState.None, key.State);
        ArchiveKeyRequest? request = null;
        Assert.True(await key.UpdateArchiveKeyByUserAsync((value, _) => { request = value; return Task.FromResult<string?>(TestPassword); }, Token));
        Assert.False(request!.IsRetry); Assert.Equal(path, request.ArchivePath); Assert.Equal(TestPassword, key.Key);
        Assert.False(ArchiveKeyCache.Current.TryGetValue(path, out _));
        Assert.False(await key.UpdateArchiveKeyByUserAsync((value, _) => { Assert.True(value.IsRetry); return Task.FromResult<string?>(null); }, Token));
        Assert.Equal(ArchiveKey.ArchiveKeyState.Canceled, key.State);
        Assert.False(await key.UpdateArchiveKeyByUserAsync((_, _) => throw new InvalidOperationException(), Token));
        using var canceled = new CancellationTokenSource(); var fresh = new ArchiveKey(path);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fresh.UpdateArchiveKeyByUserAsync((_, _) => { canceled.Cancel(); return Task.FromResult<string?>(TestPassword); }, canceled.Token));
        Assert.Equal(ArchiveKey.ArchiveKeyState.None, fresh.State);
    }
    [Fact]
    public void ProcessCacheKeepsExactPathAndCanClearWithoutPersistence()
    {
        var cache = new ArchiveKeyCache(); var path = "/Books/Upper.PDF";
        cache.Add(path, TestPassword); Assert.Equal(TestPassword, cache.GetValue(path)); Assert.Equal("", cache.GetValue(path.ToLowerInvariant()));
        cache.Add(path, "replacement-test-only"); Assert.Equal("replacement-test-only", cache.GetValue(path));
        Assert.True(cache.Remove(path)); Assert.False(cache.TryGetValue(path, out _)); cache.Add(path, TestPassword); cache.Clear(); Assert.False(cache.TryGetValue(path, out _));
    }
    [Fact]
    public async Task WrongThenCorrectInputUsesSameLogicalPathAndOnlySuccessIsCached()
    {
        using var f = new Fixture(); Config.SetCurrent(new()); var path = Locked(f); var factory = new ArchiveFactory(pdfRenderer: new LockedRenderer()); var requests = new List<ArchiveKeyRequest>();
        try
        {
            await Assert.ThrowsAsync<ArchiveKeyRequiredException>(() => factory.OpenAsync(path, Token));
            await using (var source = await factory.OpenAsync(path, Token, (request, ignoredToken) =>
            { requests.Add(request); Assert.False(ArchiveKeyCache.Current.TryGetValue(path, out _)); return Task.FromResult<string?>(requests.Count == 1 ? "wrong-test-only" : TestPassword); }))
            { Assert.Single(await source.GetEntriesAsync(Token)); }
            Assert.Equal(2, requests.Count); Assert.All(requests, request => Assert.Equal(path, request.ArchivePath)); Assert.False(requests[0].IsRetry); Assert.True(requests[1].IsRetry);
            Assert.Equal(TestPassword, ArchiveKeyCache.Current.GetValue(path));
            await using var cached = await factory.OpenAsync(path, Token, (_, _) => throw new InvalidOperationException("命中缓存不应重新输入"));
            await using var stream = await cached.OpenEntryAsync((await cached.GetEntriesAsync(Token))[0], Token);
            using var image = await new MagickImageDecoder().DecodeAsync(stream, new(100, 150, true), Token); Assert.Equal(new Size(100, 150), image.Size);
        }
        finally { ArchiveKeyCache.Current.Remove(path); }
    }
    [Fact]
    public async Task PdfInsideZipUsesLogicalCacheKeyAndReleasesEveryRejectedProxy()
    {
        using var f = new Fixture(); Config.SetCurrent(new()); var path = Locked(f); var outer = Path.Combine(f.Root, "outer.cbz");
        using (var zip = ZipFile.Open(outer, ZipArchiveMode.Create)) zip.CreateEntryFromFile(path, "章节/inside.pdf");
        var logical = Path.Combine(outer, "章节/inside.pdf"); var requested = Path.Combine(logical, "001.png"); var factory = new ArchiveFactory(pdfRenderer: new LockedRenderer());
        try
        {
            var before = NestedArchiveFile.ReservedBytes; var count = 0;
            await using (var source = await factory.OpenAsync(requested, Token, (request, _) =>
            { Assert.Equal(logical, request.ArchivePath); Assert.Equal(before, NestedArchiveFile.ReservedBytes); return Task.FromResult<string?>(++count == 1 ? "wrong-test-only" : TestPassword); }))
            { Assert.Equal("001.png", source.RequestedEntryName); Assert.Equal(outer, source.RootArchivePath); }
            Assert.Equal(before, NestedArchiveFile.ReservedBytes); Assert.Equal(TestPassword, ArchiveKeyCache.Current.GetValue(logical));
            Assert.True(await factory.ExistsAsync(requested, Token));
            ArchiveKeyCache.Current.Remove(logical); await Assert.ThrowsAsync<ArchiveKeyRequiredException>(() => factory.ExistsAsync(requested, Token));
            Assert.Equal(before, NestedArchiveFile.ReservedBytes);
        }
        finally { ArchiveKeyCache.Current.Remove(logical); }
    }
    [Fact]
    public async Task CancelOrCancelledLateInputKeepsOldBookAndNeverWritesPassword()
    {
        using var f = new Fixture(); var state = new SaveData(f.State); await state.LoadAsync(Token); var path = Locked(f);
        await using var operation = new BookOperation(new ArchiveFactory(pdfRenderer: new LockedRenderer()), new MagickImageDecoder(), state);
        await operation.OpenAsync(f.Images, Token); var previous = operation.Book;
        operation.RequestArchiveKeyAsync = (_, _) => Task.FromResult<string?>(null); await operation.OpenAsync(path, Token);
        Assert.Same(previous, operation.Book); Assert.False(operation.IsLoading); Assert.False(ArchiveKeyCache.Current.TryGetValue(path, out _));
        using var canceled = new CancellationTokenSource(); operation.RequestArchiveKeyAsync = (_, _) => { canceled.Cancel(); return Task.FromResult<string?>(TestPassword); };
        await operation.OpenAsync(path, canceled.Token); Assert.Same(previous, operation.Book); Assert.False(ArchiveKeyCache.Current.TryGetValue(path, out _));
        await operation.SaveAsync(); foreach (var file in Directory.GetFiles(f.State, "*.json")) Assert.DoesNotContain(TestPassword, await File.ReadAllTextAsync(file, Token));
    }
    [AvaloniaFact]
    public async Task FormalPasswordDialogUsesMaskNonemptyEnterAndCancel()
    {
        var owner = new Window(); owner.Show(); var dialog = new PasswordDialog(new("/books/示例.pdf", true)); var result = dialog.ShowDialog<string?>(owner);
        try
        {
            var input = dialog.FindControl<TextBox>("PasswordInput")!; Assert.Equal('●', input.PasswordChar); Assert.False(dialog.FindControl<Button>("AcceptPassword")!.IsEnabled);
            Assert.True(dialog.FindControl<TextBlock>("IncorrectPassword")!.IsVisible); input.Text = " synthetic-test-only ";
            // TextChanged由Avalonia排队发送，按真实输入事件循环检查确认按钮。
            Dispatcher.UIThread.RunJobs(); dialog.UpdateLayout(); Assert.True(dialog.FindControl<Button>("AcceptPassword")!.IsEnabled);
            if (Environment.GetEnvironmentVariable("NEEVIEW_P5_PDF_PASSWORD_SCREENSHOT") is { } screenshot) { using var frame = dialog.CaptureRenderedFrame(); Assert.NotNull(frame); frame!.Save(screenshot, Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default); }
            dialog.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null); owner.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null); Assert.Equal(" synthetic-test-only ", await result); Assert.Equal("", input.Text);
            var cancel = new PasswordDialog(new("example.pdf", false)); var canceled = cancel.ShowDialog<string?>(owner); cancel.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null); owner.KeyRelease(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null); Assert.Null(await canceled);
        }
        finally { dialog.Close(); owner.Close(); }
    }
    [AvaloniaFact]
    public async Task MainWindowPasswordRetryAndSwitchBookCancelTheOwnedDialog()
    {
        using var f = new Fixture(); var state = new SaveData(f.State); await state.LoadAsync(Token); var path = Locked(f);
        var operation = new BookOperation(new ArchiveFactory(pdfRenderer: new LockedRenderer()), new MagickImageDecoder(), state); var images = new BitmapFactory(new MagickImageDecoder());
        var model = new ReaderWorkspaceViewModel(operation, new(operation), state); var window = new MainWindow(); window.Bind(model, images, new NoPlatform()); window.Show();
        try
        {
            await window.OpenAsync(f.Images); var previous = operation.Book; var opening = window.OpenAsync(path);
            await Wait(() => window.OwnedWindows.OfType<PasswordDialog>().Any()); var first = window.OwnedWindows.OfType<PasswordDialog>().Single();
            first.FindControl<TextBox>("PasswordInput")!.Text = "wrong-test-only"; Click(first, "AcceptPassword");
            await Wait(() => window.OwnedWindows.OfType<PasswordDialog>().Any(d => !ReferenceEquals(d, first))); var retry = window.OwnedWindows.OfType<PasswordDialog>().Single();
            Assert.True(retry.FindControl<TextBlock>("IncorrectPassword")!.IsVisible); Assert.Same(previous, operation.Book);
            // 通过同一Engine打开新书，实际取消令牌关闭旧拥有窗口；不能把迟到口令用于新书。
            var switched = operation.OpenAsync(f.Zip, Token); await opening; await switched; await Wait(() => !window.OwnedWindows.OfType<PasswordDialog>().Any());
            Assert.Equal(f.Zip, operation.Book!.Path); Assert.False(ArchiveKeyCache.Current.TryGetValue(path, out _));
        }
        finally { await window.PrepareShutdownAsync(); window.Close(); ArchiveKeyCache.Current.Remove(path); }
    }
    [AvaloniaFact]
    public async Task ClosingMainWindowCancelsOwnedPasswordAndReleasesNestedSource()
    {
        using var f = new Fixture(); var state = new SaveData(f.State); await state.LoadAsync(Token); var locked = Locked(f);
        var outer = Path.Combine(f.Root, "closing.cbz"); using (var zip = ZipFile.Open(outer, ZipArchiveMode.Create)) zip.CreateEntryFromFile(locked, "inside.pdf");
        var logical = Path.Combine(outer, "inside.pdf"); var before = NestedArchiveFile.ReservedBytes;
        var operation = new BookOperation(new ArchiveFactory(pdfRenderer: new LockedRenderer()), new MagickImageDecoder(), state); var images = new BitmapFactory(new MagickImageDecoder());
        var model = new ReaderWorkspaceViewModel(operation, new(operation), state); var window = new MainWindow(); window.Bind(model, images, new NoPlatform()); window.Show();
        try
        {
            await window.OpenAsync(f.Images); var opening = window.OpenAsync(logical);
            await Wait(() => window.OwnedWindows.OfType<PasswordDialog>().Any());
            await window.PrepareShutdownAsync(); await opening; window.Close();
            Assert.Empty(window.OwnedWindows); Assert.Equal(before, NestedArchiveFile.ReservedBytes);
            Assert.False(ArchiveKeyCache.Current.TryGetValue(logical, out _)); Assert.False(operation.IsLoading);
        }
        finally { await window.PrepareShutdownAsync(); window.Close(); ArchiveKeyCache.Current.Remove(logical); }
    }
    private sealed class NoPlatform : IPlatformService
    { public Task RevealAsync(string path, CancellationToken token = default) => Task.CompletedTask; public Task TrashAsync(string path, CancellationToken token = default) => Task.CompletedTask; }
    private static void Click(Window window, string name) => window.FindControl<Button>(name)!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static async Task Wait(Func<bool> done)
    { for (var i = 0; i < 400 && !done(); i++) { Dispatcher.UIThread.RunJobs(); await Task.Delay(10, Token); } Assert.True(done()); }
}
