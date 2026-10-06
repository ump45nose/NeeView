using System.IO.Compression;
using ImageMagick;
using NeeView.Backends;
using NeeView.Tests;
using NeeView;
[assembly: CollectionBehavior(DisableTestParallelization = true)]
namespace NeeView.Backends.MacOS.Tests;

/// <summary>实际官方macOS绑定/native引导测试；不创建窗口或激活正式应用。</summary>
public sealed class PdfNativeTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    [Fact]
    public void NativePageIndexCropRotationAndOutlineUseRealPdf()
    {
        using var fixture = new SyntheticPdf(); var renderer = new MacPdfRenderer(); var info = renderer.Inspect(fixture.Path, Token);
        Assert.Equal([new Size(200, 300), new Size(200, 300)], info.Pages);
        var chapter = Assert.Single(info.Contents); Assert.Equal("Chapter one", chapter.Name); Assert.Equal(0, chapter.Page);
        var child = Assert.Single(chapter.Children); Assert.Equal("Rotated page", child.Name); Assert.Equal(1, child.Page);
        Assert.Equal(new DateTime(2026, 10, 6, 4, 0, 0, DateTimeKind.Utc), info.LastWriteTime.ToUniversalTime());
        Assert.Equal(new DateTime(2026, 10, 6, 3, 0, 0, DateTimeKind.Utc), info.CreationTime.ToUniversalTime());
    }
    [Fact]
    public void NativePixelsAreTopDownSrgbBgraWithWhitePaperAndCorrectRotation()
    {
        using var fixture = new SyntheticPdf(); var renderer = new MacPdfRenderer();
        using var image = renderer.Render(fixture.Path, 0, new(200, 300), Token);
        Assert.Equal(200 * 300 * 4, image.ByteCount);
        Assert.Equal(new byte[] { 0, 0, 255, 255 }, Pixel(image, 100, 20));
        Assert.Equal(new byte[] { 255, 255, 255, 255 }, Pixel(image, 100, 150));
        Assert.Equal(new byte[] { 0, 255, 0, 255 }, Pixel(image, 100, 280));
        if (Environment.GetEnvironmentVariable("NEEVIEW_P5_PDF_NATIVE_SCREENSHOT") is { } path)
        { Assert.True(System.IO.Path.IsPathFullyQualified(path)); using var raster = new MagickImage(image.Pixels, new MagickReadSettings { Format = MagickFormat.Bgra, Width = 200, Height = 300, Depth = 8 }); raster.Write(path); }
        using var rotated = renderer.Render(fixture.Path, 1, new(100, 150), Token);
        Assert.Equal(new byte[] { 255, 0, 0, 255 }, Pixel(rotated, 50, 75));
    }
    [Fact]
    public async Task ActualArchiveAndDecoderProbeWithoutRasterizingThenRenderAndExportPng()
    {
        using var fixture = new SyntheticPdf(); Config.SetCurrent(new());
        var factory = new ArchiveFactory(pdfRenderer: new MacPdfRenderer()); var decoder = new MagickImageDecoder();
        await using var archive = await factory.OpenAsync(fixture.Path, Token); var entries = await archive.GetEntriesAsync(Token);
        Assert.Equal(["001.png", "002.png"], entries.Select(e => e.EntryName)); Assert.False(archive.CanDelete(entries));
        await using var stream = await archive.OpenEntryAsync(entries[0], Token);
        Assert.Equal(new Size(200, 300), (await decoder.ProbeAsync(stream, Token)).Size);
        using var image = await decoder.DecodeAsync(stream, new(100, 150, true), Token); Assert.Equal(new Size(100, 150), image.Size);
        using var encoded = new MemoryStream(); await stream.CopyToAsync(encoded, Token); Assert.True(encoded.Length > 0); encoded.Position = 0;
        using var png = new MagickImage(encoded); Assert.Equal(MagickFormat.Png, png.Format); Assert.Equal(720U, png.Width); Assert.Equal(1080U, png.Height);
        var pages = entries.Select(e => new Page(e)).ToArray(); var tree = BookTableOfContents.Create(pages, Token, archive);
        Assert.Same(pages[0], Assert.Single(tree.Children).Page); Assert.Same(pages[1], Assert.Single(tree.Children[0].Children).Page);
        await using var realizer = new ArchiveEntryRealizer(); await using var file = await realizer.ExtractAsync(entries[0], 1024 * 1024, Token);
        using var realized = new MagickImage(file.Path); Assert.Equal(MagickFormat.Png, realized.Format);
    }
    [Fact]
    public async Task ActualNestedPdfAndExplicitPageKeepOriginalLogicalLocation()
    {
        using var fixture = new SyntheticPdf(); Config.SetCurrent(new()); var outer = System.IO.Path.Combine(fixture.Root, "nested.cbz");
        using (var zip = ZipFile.Open(outer, ZipArchiveMode.Create)) zip.CreateEntryFromFile(fixture.Path, "章节/inside.pdf");
        var factory = new ArchiveFactory(pdfRenderer: new MacPdfRenderer());
        var path = System.IO.Path.Combine(outer, "章节/inside.pdf", "002.png");
        await using var archive = await factory.OpenAsync(path, Token); Assert.Equal(outer, archive.RootArchivePath); Assert.Equal("002.png", archive.RequestedEntryName);
        var entries = await archive.GetEntriesAsync(Token); await using var stream = await archive.OpenEntryAsync(entries[1], Token);
        using var pixels = await new MagickImageDecoder().DecodeAsync(stream, new(100, 150, true), Token); Assert.Equal(new Size(100, 150), pixels.Size);
        Assert.Equal(path, entries[1].SystemPath); Assert.True(await factory.ExistsAsync(path, Token)); Assert.False(await factory.ExistsAsync(path.Replace("002.png", "003.png"), Token));
    }
    [Fact]
    public void ActualNativeFailureCancellationBudgetAndRepeatedRelease()
    {
        using var fixture = new SyntheticPdf(); var renderer = new MacPdfRenderer(); var bad = System.IO.Path.Combine(fixture.Root, "bad.pdf"); File.WriteAllText(bad, "invalid");
        Assert.Throws<InvalidDataException>(() => renderer.Inspect(bad, Token));
        Assert.Throws<InvalidDataException>(() => renderer.Render(fixture.Path, 5, new(100, 100), Token));
        Assert.Throws<NotSupportedException>(() => renderer.Render(fixture.Path, 0, new(10_000, 10_000), Token));
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel(); Assert.ThrowsAny<OperationCanceledException>(() => renderer.Render(fixture.Path, 0, new(100, 100), cancelled.Token));
        for (var i = 0; i < 40; i++) { Assert.Equal(2, renderer.Inspect(fixture.Path, Token).Pages.Count); using var image = renderer.Render(fixture.Path, i % 2, new(80, 120), Token); Assert.Equal(80 * 120 * 4, image.ByteCount); }
    }
    [Fact]
    public async Task PasswordProtectedNativeDocumentFailsClearlyAndKeepsPreviousBook()
    {
        using var fixture = new SyntheticPdf(); Config.SetCurrent(new()); using var url = Foundation.NSUrl.FromFilename(fixture.Path);
        using var document = new PdfKit.PdfDocument(url); var encrypted = System.IO.Path.Combine(fixture.Root, "locked.pdf");
        // 隔离夹具密码，不是连接凭据；测试不尝试解密或存储用户密码。
        Assert.True(document.Write(encrypted, new PdfKit.PdfDocumentWriteOptions { UserPassword = "synthetic-test-only", OwnerPassword = "synthetic-owner-only" }));
        var state = new SaveData(System.IO.Path.Combine(fixture.Root, "Profile")); await state.LoadAsync(Token);
        await using var operation = new BookOperation(new ArchiveFactory(pdfRenderer: new MacPdfRenderer()), new MagickImageDecoder(), state);
        await operation.OpenAsync(fixture.Path, Token); Assert.Null(operation.Error); var previous = operation.Book;
        await operation.OpenAsync(encrypted, Token); Assert.Same(previous, operation.Book); Assert.Contains("密码", operation.Error);
    }
    private static byte[] Pixel(DecodedImageLease image, int x, int y) => image.Pixels.AsSpan(y * image.Stride + x * 4, 4).ToArray();
}
