using System.IO.Compression;
using System.Security.Cryptography;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using NeeView.Backends;
using NeeView.MacOS.ViewModels;
using NeeView.MacOS.Views;
namespace NeeView.Engine.Tests;

/// <summary>公开/合成密码夹具的实际解压、重试、缓存与原阅读链；不使用用户口令或图片。</summary>
public sealed class CompressedPasswordTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    public static TheoryData<string> Archives => new()
    {
        "Zip.pkware.synthetic.zip", "Zip.deflate.WinzipAES.zip", "Rar.encrypted_filesOnly.rar",
        "Rar.encrypted_filesAndHeader.rar", "Rar5.encrypted_filesOnly.rar", "Rar5.encrypted_filesAndHeader.rar",
        "7Zip.LZMA.Aes.7z", "7Zip.LZMA2.Aes.7z"
    };
    private static string Password(string fixture) => fixture.Contains("synthetic") ? "synthetic-test-only" : fixture.EndsWith(".7z") ? "testpassword" : "test";
    private static string Copy(Fixture f, string fixture)
    { var path = Path.Combine(f.Root, fixture); File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", fixture), path); return path; }
    [Theory]
    [MemberData(nameof(Archives))]
    public async Task ActualWrongThenCorrectPasswordReadsImageAndOnlyCachesVerifiedKey(string fixture)
    {
        using var f = new Fixture(); Config.SetCurrent(new()); var path = Copy(f, fixture); var factory = new ArchiveFactory(); var requests = new List<ArchiveKeyRequest>();
        var hash = SHA256.HashData(await File.ReadAllBytesAsync(path, Token));
        try
        {
            await Assert.ThrowsAsync<ArchiveKeyRequiredException>(() => factory.OpenAsync(path, Token));
            await using (var source = await factory.OpenAsync(path, Token, (request, ignoredToken) =>
            {
                requests.Add(request); Assert.True(requests.Count <= 3, "正确夹具口令不能无限重试"); Assert.Equal(path, request.ArchivePath); Assert.False(ArchiveKeyCache.Current.TryGetValue(path, out _));
                return Task.FromResult<string?>(requests.Count == 1 ? "wrong-test-only" : Password(fixture));
            }))
            {
                var all = await source.GetEntriesAsync(Token); var entries = all.Where(e => e.IsImage()).ToArray(); Assert.True(entries.Length > 0, string.Join(";", all.Select(e => $"{e.EntryName},dir={e.IsDirectory},length={e.Length}")));
                foreach (var entry in entries.Reverse().Concat(entries.Take(1)))
                {
                    await using var stream = await source.OpenEntryAsync(entry, Token); Assert.Equal(entry.Length, stream.Length);
                    using var image = await new MagickImageDecoder().DecodeAsync(stream, new(100, 100, true), Token); Assert.True(image.ByteCount > 0);
                }
            }
            Assert.Equal(2, requests.Count); Assert.False(requests[0].IsRetry); Assert.True(requests[1].IsRetry);
            if (fixture.StartsWith("7Zip") || fixture.StartsWith("Rar.")) Assert.True(requests[1].MayBeDamaged);
            Assert.Equal(Password(fixture), ArchiveKeyCache.Current.GetValue(path));
            await using var cached = await factory.OpenAsync(path, Token, (_, _) => throw new InvalidOperationException("已验证来源不应再弹窗"));
            Assert.NotEmpty(await cached.GetEntriesAsync(Token));
            Assert.Equal(hash, SHA256.HashData(await File.ReadAllBytesAsync(path, Token)));
        }
        finally { ArchiveKeyCache.Current.Remove(path); }
    }
    [Theory]
    [MemberData(nameof(Archives))]
    public async Task NestedEncryptedSourceUsesLogicalLocationAndReleasesRejectedProxies(string fixture)
    {
        using var f = new Fixture(); Config.SetCurrent(new()); var inner = Copy(f, fixture); var outer = Path.Combine(f.Root, "outer.cbz");
        using (var zip = ZipFile.Open(outer, ZipArchiveMode.Create)) zip.CreateEntryFromFile(inner, "章节/inside" + Path.GetExtension(fixture));
        var logical = Path.Combine(outer, "章节/inside" + Path.GetExtension(fixture)); var factory = new ArchiveFactory(); var before = NestedArchiveFile.ReservedBytes; int count = 0;
        try
        {
            await using (var source = await factory.OpenAsync(logical, Token, (request, _) =>
            {
                Assert.Equal(logical, request.ArchivePath); Assert.Equal(before, NestedArchiveFile.ReservedBytes);
                Assert.True(++count <= 3, "正确夹具口令不能无限重试"); return Task.FromResult<string?>(count == 1 ? "wrong-test-only" : Password(fixture));
            }))
            {
                Assert.Equal(outer, source.RootArchivePath); Assert.NotNull(source.Parent); Assert.NotNull(source.Source);
                var page = (await source.GetEntriesAsync(Token)).First(e => e.IsImage());
                Assert.True(await factory.ExistsAsync(page.SystemPath, Token));
                await using var stream = await source.OpenEntryAsync(page, Token); Assert.True((await new MagickImageDecoder().ProbeAsync(stream, Token)).Size.Width > 0);
            }
            Assert.Equal(2, count); Assert.Equal(before, NestedArchiveFile.ReservedBytes);
            Assert.Equal(Password(fixture), ArchiveKeyCache.Current.GetValue(logical));
        }
        finally { ArchiveKeyCache.Current.Remove(logical); }
    }
    [Fact]
    public async Task CancelAndLateInputKeepOriginalBookAndDoNotPersistOrCachePassword()
    {
        using var f = new Fixture(); var state = new SaveData(f.State); await state.LoadAsync(Token); var path = Copy(f, "Rar5.encrypted_filesOnly.rar");
        await using var operation = new BookOperation(new ArchiveFactory(), new MagickImageDecoder(), state); await operation.OpenAsync(f.Images, Token); var previous = operation.Book;
        operation.RequestArchiveKeyAsync = (_, _) => Task.FromResult<string?>(null); await operation.OpenAsync(path, Token);
        Assert.Same(previous, operation.Book); Assert.False(operation.IsLoading);
        using var canceled = new CancellationTokenSource(); operation.RequestArchiveKeyAsync = (_, _) => { canceled.Cancel(); return Task.FromResult<string?>("test"); };
        await operation.OpenAsync(path, canceled.Token); Assert.Same(previous, operation.Book); Assert.False(ArchiveKeyCache.Current.TryGetValue(path, out _));
        await operation.SaveAsync(); Assert.DoesNotContain("ArchiveKey", await File.ReadAllTextAsync(Path.Combine(f.State, "UserSetting.json"), Token));
    }
    [Fact]
    public async Task CorruptUnencryptedArchiveDoesNotRequestPassword()
    {
        using var f = new Fixture(); Config.SetCurrent(new()); var path = Path.Combine(f.Root, "bad.cbz"); File.WriteAllText(path, "broken plaintext archive"); var called = false;
        await Assert.ThrowsAnyAsync<Exception>(() => new ArchiveFactory().OpenAsync(path, Token, (_, _) => { called = true; return Task.FromResult<string?>("test"); }));
        Assert.False(called); Assert.False(ArchiveKeyCache.Current.TryGetValue(path, out _));
    }
    [Fact]
    public async Task EncryptedZipStaysReadOnlyToAvoidRemovingEncryptionDuringRewrite()
    {
        using var f = new Fixture(); Config.SetCurrent(new()); Config.Current.Archive.Zip.IsFileWriteAccessEnabled = true;
        var path = Copy(f, "Zip.deflate.WinzipAES.zip"); var hash = SHA256.HashData(await File.ReadAllBytesAsync(path, Token));
        try
        {
            await using var source = await new ArchiveFactory().OpenAsync(path, Token, (_, _) => Task.FromResult<string?>("test")); var entries = await source.GetEntriesAsync(Token);
            Assert.False(source.CanDelete(entries)); await Assert.ThrowsAsync<NotSupportedException>(() => source.DeleteAsync(entries, new NoPlatform(), Token));
            Assert.Equal(hash, SHA256.HashData(await File.ReadAllBytesAsync(path, Token)));
        }
        finally { ArchiveKeyCache.Current.Remove(path); }
    }
    [AvaloniaFact]
    public async Task FormalWindowRetriesActualEncryptedZipAndRendersThroughOriginalReader()
    {
        using var f = new Fixture(); var state = new SaveData(f.State); await state.LoadAsync(Token); var path = Copy(f, "Zip.deflate.WinzipAES.zip");
        var operation = new BookOperation(new ArchiveFactory(), new MagickImageDecoder(), state); var images = new BitmapFactory(new MagickImageDecoder());
        var model = new ReaderWorkspaceViewModel(operation, new(operation), state); var window = new MainWindow(); window.Bind(model, images, new NoPlatform()); window.Show();
        try
        {
            await window.OpenAsync(f.Images); var previous = operation.Book; var opening = window.OpenAsync(path);
            await Wait(() => window.OwnedWindows.OfType<PasswordDialog>().Any()); var first = window.OwnedWindows.OfType<PasswordDialog>().Single();
            first.FindControl<TextBox>("PasswordInput")!.Text = "wrong-test-only"; await Wait(() => first.FindControl<Button>("AcceptPassword")!.IsEnabled); Click(first);
            await Wait(() => window.OwnedWindows.OfType<PasswordDialog>().Any(d => !ReferenceEquals(d, first))); var retry = window.OwnedWindows.OfType<PasswordDialog>().Single();
            Assert.Same(previous, operation.Book); retry.FindControl<TextBox>("PasswordInput")!.Text = "test"; await Wait(() => retry.FindControl<Button>("AcceptPassword")!.IsEnabled); Click(retry);
            await opening;
            Assert.Null(operation.Error); Assert.Equal(path, operation.Book!.Path);
            // 原归档首页可能是目录卡片；明确进入图片，不能把卡片/旧缓存当作解锁后的正文验收。
            var imagePage = operation.Book.Pages.First(p => p.IsImage);
            await operation.JumpAsync(imagePage.Index); await window.Viewer.RefreshAsync(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            Assert.Same(imagePage, operation.Book.CurrentPage); Assert.True(images.ByteCount > 0);
            if (Environment.GetEnvironmentVariable("NEEVIEW_P5_COMPRESSED_PASSWORD_SCREENSHOT") is { } screenshot)
            { using var frame = window.CaptureRenderedFrame(); Assert.NotNull(frame); frame!.Save(screenshot, Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default); }
        }
        finally { await window.PrepareShutdownAsync(); window.Close(); ArchiveKeyCache.Current.Remove(path); }
    }
    private static void Click(Window window) => window.FindControl<Button>("AcceptPassword")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static async Task Wait(Func<bool> done)
    { for (var i = 0; i < 400 && !done(); i++) { Dispatcher.UIThread.RunJobs(); await Task.Delay(10, Token); } Assert.True(done()); }
    private sealed class NoPlatform : IPlatformService
    { public Task RevealAsync(string path, CancellationToken token = default) => Task.CompletedTask; public Task TrashAsync(string path, CancellationToken token = default) => Task.CompletedTask; }
}
