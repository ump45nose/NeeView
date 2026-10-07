using System.Security.Cryptography;
using NeeView;
using NeeView.Backends;
namespace NeeView.Engine.Tests;

/// <summary>SharpCompress官方真实分卷及字节分卷，验证任意卷打开、内容完整与缺卷拒绝。</summary>
public sealed class MultipartArchiveTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "neeview-volumes-" + Guid.NewGuid().ToString("N"));
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    public MultipartArchiveTests() => Directory.CreateDirectory(_root);
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
    private string CopySet(string primary)
    {
        foreach (var file in Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Multipart")))
            File.Copy(file, Path.Combine(_root, Path.GetFileName(file)));
        return Path.Combine(_root, primary);
    }
    private static async Task<Dictionary<string, string>> ReadAll(Archive archive)
    {
        var result = new Dictionary<string, string>();
        foreach (var entry in (await archive.GetEntriesAsync(Token)).Where(e => !e.IsDirectory))
        {
            await using var stream = await archive.OpenEntryAsync(entry, Token);
            using var output = new MemoryStream(); await stream.CopyToAsync(output);
            Assert.Equal(entry.Length, output.Length); result[entry.EntryName] = Convert.ToHexString(SHA256.HashData(output.ToArray()));
        }
        Assert.NotEmpty(result); return result;
    }
    [Theory]
    [InlineData("Rar.multi.part01.rar", "Rar.multi.part06.rar")]
    [InlineData("Rar.multi.solid.part01.rar", "Rar.multi.solid.part06.rar")]
    [InlineData("Rar2.multi.rar", "Rar2.multi.r05")]
    [InlineData("Rar5.multi.part01.rar", "Rar5.multi.part06.rar")]
    [InlineData("Infozip.nocomp.multi.zip", "Infozip.nocomp.multi.z01")]
    public async Task OfficialSetOpensFromAnyVolumeAndReadsEntireContent(string primary, string selected)
    {
        CopySet(primary);
        await using var archive = await new ArchiveFactory().OpenAsync(Path.Combine(_root, selected), Token);
        Assert.Equal(Path.Combine(_root, primary), archive.Path);
        var actual = await ReadAll(archive);
        if (primary == "Rar.multi.solid.part01.rar") actual = actual.ToDictionary(e => e.Key["Rar.multi.solid/".Length..], e => e.Value);
        await using var baseline = new CompressedArchive(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Rar.rar"));
        var expected = await ReadAll(baseline);
        Assert.Equal(expected.OrderBy(e => e.Key), actual.OrderBy(e => e.Key));
        var entries = await archive.GetEntriesAsync(Token); Assert.False(archive.CanDelete(entries));
    }
    [Theory]
    [InlineData("7Zip.LZMA.7z", "中文.7z")]
    [InlineData("", "中文.zip")]
    public async Task BinarySplitPreservesContentAndMainPath(string fixture, string name)
    {
        string original = Path.Combine(AppContext.BaseDirectory, "Fixtures", fixture);
        if (fixture.Length == 0)
        {
            original = Path.Combine(_root, "plain.zip");
            using var zip = System.IO.Compression.ZipFile.Open(original, System.IO.Compression.ZipArchiveMode.Create);
            using var writer = new StreamWriter(zip.CreateEntry("目录/图.txt").Open()); writer.Write("test-image-bytes");
        }
        var data = await File.ReadAllBytesAsync(original, Token);
        var half = data.Length / 2; var first = Path.Combine(_root, name + ".001"); var second = Path.Combine(_root, name + ".002");
        await File.WriteAllBytesAsync(first, data[..half], Token); await File.WriteAllBytesAsync(second, data[half..], Token);
        await using var archive = await new ArchiveFactory().OpenAsync(second, Token);
        Assert.Equal(first, archive.Path);
        await using var baseline = new CompressedArchive(original);
        Assert.Equal((await ReadAll(baseline)).OrderBy(e => e.Key), (await ReadAll(archive)).OrderBy(e => e.Key));
    }
    [Theory]
    [InlineData("Rar.multi.part01.rar", "Rar.multi.part03.rar")]
    [InlineData("Rar2.multi.rar", "Rar2.multi.r02")]
    [InlineData("Infozip.nocomp.multi.zip", "Infozip.nocomp.multi.z01")]
    public async Task MissingVolumeNeverReportsCompleteSource(string primary, string missing)
    {
        var path = CopySet(primary); File.Delete(Path.Combine(_root, missing));
        var error = await Record.ExceptionAsync(async () => { await using var archive = await new ArchiveFactory().OpenAsync(path, Token); await ReadAll(archive); });
        Assert.NotNull(error);
    }
    [Fact]
    public async Task MissingRarTailAndNonFirstOnlyCannotSilentlyReadPartialBook()
    {
        var path = CopySet("Rar.multi.part01.rar"); File.Delete(Path.Combine(_root, "Rar.multi.part06.rar"));
        var error = await Record.ExceptionAsync(async () => { await using var archive = new CompressedArchive(path); await ReadAll(archive); });
        Assert.NotNull(error);
        foreach (var file in Directory.GetFiles(_root, "Rar.multi.part*.rar")) if (!file.EndsWith("part05.rar")) File.Delete(file);
        Assert.ThrowsAny<Exception>(() => new CompressedArchive(Path.Combine(_root, "Rar.multi.part05.rar")));
    }
    [Fact]
    public async Task OrdinaryRarWithPartNameRemainsReadableAndClosedSetReleasesFiles()
    {
        var path = Path.Combine(_root, "normal.part09.rar"); File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Rar.rar"), path);
        var archive = new CompressedArchive(path); await ReadAll(archive); await archive.DisposeAsync();
        Assert.True(archive.ResourceDrainCompletion.IsCompletedSuccessfully); File.Delete(path);
        Assert.False(File.Exists(path)); Assert.False(ArchiveFormats.IsCompressedArchive("photo.001"));
    }
    [Fact]
    public async Task EncryptedRarMultipartIsExplicitlyUnsupportedWithoutPromptLoopOrPasswordCache()
    {
        var main = CopySet("Rar.EncryptedParts.part01.rar"); int prompts = 0;
        ArchiveKeyCache.Current.Clear();
        try
        {
            var error = await Assert.ThrowsAsync<NotSupportedException>(() => new ArchiveFactory().OpenAsync(Path.Combine(_root, "Rar.EncryptedParts.part06.rar"), Token,
                (request, token) => { prompts++; return Task.FromResult<string?>("test"); }));
            Assert.Contains("加密RAR分卷", error.Message); Assert.InRange(prompts, 0, 1);
            Assert.False(ArchiveKeyCache.Current.TryGetValue(main, out _));
        }
        finally { ArchiveKeyCache.Current.Clear(); }
    }
    [Fact]
    public async Task EncryptedBinarySevenZipTailUsesCanonicalPasswordKeyAndReadsWholeContent()
    {
        var original = Path.Combine(AppContext.BaseDirectory, "Fixtures", "7Zip.LZMA.Aes.7z");
        var data = await File.ReadAllBytesAsync(original, Token); var half = data.Length / 2;
        var main = Path.Combine(_root, "密码.7z.001"); var tail = Path.Combine(_root, "密码.7z.002");
        await File.WriteAllBytesAsync(main, data[..half], Token); await File.WriteAllBytesAsync(tail, data[half..], Token);
        int prompts = 0; ArchiveKeyCache.Current.Clear();
        try
        {
            await using var source = await new ArchiveFactory().OpenAsync(tail, Token,
                (request, token) => { Assert.Equal(main, request.ArchivePath); prompts++; return Task.FromResult<string?>("testpassword"); });
            Assert.Equal(1, prompts);
            await using var baseline = new CompressedArchive(original, password: "testpassword");
            Assert.Equal((await ReadAll(baseline)).OrderBy(e => e.Key), (await ReadAll(source)).OrderBy(e => e.Key));
            await using var reopened = await new ArchiveFactory().OpenAsync(main, Token); await ReadAll(reopened);
        }
        finally { ArchiveKeyCache.Current.Clear(); }
    }
    [Fact]
    public async Task BookshelfHidesOnlySecondaryVolumesWithKnownMainAndKeepsStandalonePart()
    {
        CopySet("Rar.multi.part01.rar");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Rar.rar"), Path.Combine(_root, "normal.part09.rar"));
        var items = await new ArchiveFactory().ListBooksAsync(_root, Token);
        Assert.Contains(items, item => item.Name == "Rar.multi.part01.rar");
        Assert.Contains(items, item => item.Name == "normal.part09.rar");
        Assert.DoesNotContain(items, item => item.Name == "Rar.multi.part06.rar" || item.Name.EndsWith(".r05") || item.Name.EndsWith(".z01"));
    }
    [Fact]
    public async Task LogicalCloseReturnsWhileBorrowedReadGateDrainsAndNestedOwnerIsReleasedAfterward()
    {
        var path = CopySet("Rar.multi.part01.rar"); var lifetime = new Lifetime();
        var archive = new CompressedArchive(path, lifetime: lifetime);
        var gate = (SemaphoreSlim)typeof(CompressedArchive).GetField("_gate", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(archive)!;
        await gate.WaitAsync(Token);
        try
        {
            await archive.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5), Token);
            Assert.True(archive.IsDisposed); Assert.False(archive.ResourceDrainCompletion.IsCompleted); Assert.False(lifetime.Released);
            await Assert.ThrowsAsync<ObjectDisposedException>(() => archive.GetEntriesAsync(Token));
        }
        finally { gate.Release(); }
        await archive.ResourceDrainCompletion.WaitAsync(TimeSpan.FromSeconds(5), Token); Assert.True(lifetime.Released);
        await archive.DisposeAsync(); Assert.Equal(1, lifetime.Count);
    }
    private sealed class Lifetime : IAsyncDisposable
    {
        public int Count { get; private set; }
        public bool Released => Count > 0;
        public ValueTask DisposeAsync() { Count++; return ValueTask.CompletedTask; }
    }
}
