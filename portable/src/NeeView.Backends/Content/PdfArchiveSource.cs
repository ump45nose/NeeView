// Copyright (c) NeeLaboratory. 原PdfPdfiumArchive的页面/流关系；系统渲染由MacPdfRenderer替换。
using ImageMagick;
using NeeView;
namespace NeeView.Backends;

/// <summary>PDF系统替换点只位于后端；测试可替换渲染，不进入Engine或界面。</summary>
public interface IPdfRenderer
{
    PdfDocumentInfo Inspect(string path, CancellationToken token);
    DecodedImageLease Render(string path, int page, Size target, CancellationToken token);
    /// <summary>产生只属于一个已打开来源的口令绑定，不修改共享渲染器或其他书籍。</summary>
    IPdfRenderer WithPassword(string password) => throw new NotSupportedException("此PDF后端不支持密码解锁。");
}
public sealed record PdfOutlineInfo(string Name, int? Page, IReadOnlyList<PdfOutlineInfo> Children);
public sealed record PdfDocumentInfo(IReadOnlyList<Size> Pages, IReadOnlyList<PdfOutlineInfo> Contents, DateTime LastWriteTime, DateTime CreationTime = default);

/// <summary>原PDF归档：每页原Id/001.png命名、页尺寸、书签目录及请求级渲染。</summary>
public sealed class PdfArchiveSource : PdfArchive
{
    public override string BackendName => "macOS PDFKit / CoreGraphics";
    private readonly string _physicalPath;
    private readonly IPdfRenderer _renderer;
    private readonly IAsyncDisposable? _lifetime;
    private readonly PdfDocumentInfo _info;
    private readonly ArchiveEntry[] _entries;
    private readonly SemaphoreSlim _gate = new(1);
    public override IReadOnlyList<ContentsArchiveEntryNode>? Contents { get; }
    /// <summary>在已有SourceIo槽中探测文档；不长期保留原生PDF或所有页面句柄。</summary>
    public PdfArchiveSource(string physicalPath, IPdfRenderer renderer, CancellationToken token, string? logicalPath = null, ArchiveEntry? source = null, IAsyncDisposable? lifetime = null)
        : base(logicalPath ?? physicalPath, source)
    {
        _physicalPath = physicalPath; _renderer = renderer; _lifetime = lifetime;
        try { _info = renderer.Inspect(physicalPath, token); }
        catch (ArchiveKeyRequiredException) { throw new ArchiveKeyRequiredException(logicalPath ?? physicalPath); }
        if (_info.Pages.Count > 100_000) throw new NotSupportedException("PDF超过十万页索引预算。");
        _entries = _info.Pages.Select((_, i) => new ArchiveEntry(this) { Id = i, RawEntryName = $"{i + 1:000}.png", Length = 0, LastWriteTime = _info.LastWriteTime, CreationTime = _info.CreationTime }).ToArray();
        Contents = _info.Contents.Select(Map).ToArray();
        ContentsArchiveEntryNode Map(PdfOutlineInfo node) => new(node.Name, node.Page is { } id && id >= 0 && id < _entries.Length ? _entries[id] : null, node.Children.Select(Map).ToArray());
    }
    /// <summary>原索引快照，无原生读取或解码；来源关闭后不能再请求。</summary>
    public override Task<IReadOnlyList<ArchiveEntry>> GetEntriesAsync(CancellationToken token)
    { token.ThrowIfCancellationRequested(); ObjectDisposedException.ThrowIf(IsDisposed, this); return Task.FromResult<IReadOnlyList<ArchiveEntry>>(_entries); }
    public override Size GetSourceSize(ArchiveEntry entry) { Check(entry); return _info.Pages[entry.Id]; }
    /// <summary>返回延迟流：尺寸探测/正文直接走原PDF渲染，只有复制/提取才生成默认PNG。</summary>
    public override Task<Stream> OpenEntryAsync(ArchiveEntry entry, CancellationToken token)
    { token.ThrowIfCancellationRequested(); Check(entry); return Task.FromResult<Stream>(new PdfPageStream(this, entry)); }
    private void Check(ArchiveEntry entry)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        if (!ReferenceEquals(entry.Archive, this) || entry.Id < 0 || entry.Id >= _entries.Length) throw new InvalidDataException("PDF页面不属于该来源。");
    }
    /// <summary>按目标规格执行唯一后台原生渲染，关闭等待实际工作，不删除仍在使用的内部代理。</summary>
    internal Task<DecodedImageLease> RenderAsync(ArchiveEntry entry, Size target, CancellationToken token) => SourceIo.RunAsync(() =>
        Render(entry, target, token), token);
    /// <summary>导出流已经由后台请求持有，直接渲染避免占I/O槽时再次等待同一队列。</summary>
    internal DecodedImageLease Render(ArchiveEntry entry, Size target, CancellationToken token)
    {
        _gate.Wait(token);
        try
        {
            Check(entry); token.ThrowIfCancellationRequested();
            var result = _renderer.Render(_physicalPath, entry.Id, target, token);
            if (token.IsCancellationRequested) { result.Dispose(); token.ThrowIfCancellationRequested(); }
            return result;
        }
        finally { _gate.Release(); }
    }
    public override async ValueTask DisposeAsync()
    {
        await _gate.WaitAsync();
        try { IsDisposed = true; if (_lifetime is not null) await _lifetime.DisposeAsync(); }
        finally { _gate.Release(); }
    }
}

/// <summary>原PDF页面流与按需像素入口共用一个请求；不把PDF字节交给Magick delegate。</summary>
internal sealed class PdfPageStream(PdfArchiveSource archive, ArchiveEntry entry) : Stream
{
    private readonly object _sync = new();
    private readonly CancellationTokenSource _closing = new();
    private MemoryStream? _encoded;
    private bool _disposed;
    internal Size SourceSize => archive.GetSourceSize(entry);
    internal ImageInfo Probe()
    { lock (_sync) { ObjectDisposedException.ThrowIf(_disposed, this); return new(PdfArchiveProfile.GetDisplaySize(SourceSize), "PDF/CoreGraphics"); } }
    /// <summary>正文/缩略按需渲染，不先渲染默认导出PNG，也不复制经过Magick的像素。</summary>
    internal Task<DecodedImageLease> DecodeAsync(DecodeRequest request, CancellationToken token)
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var target = PdfArchiveProfile.GetRenderSize(PdfArchiveProfile.GetDisplaySize(SourceSize), new(Math.Max(1, request.TargetWidth), Math.Max(1, request.TargetHeight)), request.IsThumbnail);
            // 只在锁内启动请求；来源负责等待实际渲染，不能持流锁等待后台完成。
            return archive.RenderAsync(entry, target, token);
        }
    }
    /// <summary>调用方持有_sync直到本次流操作结束；只生成一次PNG，像素租约立即释放。</summary>
    private MemoryStream Data
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this); if (_encoded is not null) return _encoded;
            using var pixels = archive.Render(entry, PdfArchiveProfile.GetExportSize(SourceSize), _closing.Token);
            using var image = new MagickImage(pixels.Pixels, new MagickReadSettings { Format = MagickFormat.Bgra, Width = (uint)pixels.Size.Width, Height = (uint)pixels.Size.Height, Depth = 8 });
            var data = image.ToByteArray(MagickFormat.Png); _closing.Token.ThrowIfCancellationRequested();
            return _encoded = new MemoryStream(data, false);
        }
    }
    public override bool CanRead { get { lock (_sync) return !_disposed; } }
    public override bool CanSeek { get { lock (_sync) return !_disposed; } }
    public override bool CanWrite => false;
    public override long Length { get { lock (_sync) return Data.Length; } }
    public override long Position { get { lock (_sync) return Data.Position; } set { lock (_sync) Data.Position = value; } }
    public override int Read(byte[] buffer, int offset, int count) { lock (_sync) return Data.Read(buffer, offset, count); }
    public override int Read(Span<byte> buffer) { lock (_sync) return Data.Read(buffer); }
    public override long Seek(long offset, SeekOrigin origin) { lock (_sync) return Data.Seek(offset, origin); }
    public override void Flush() { }
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    protected override void Dispose(bool disposing)
    {
        // 等正在生成/读取的导出完成，防止覆盖缓冲或释放仍在使用的流/取消源。
        lock (_sync)
        {
            if (!_disposed) { _disposed = true; _closing.Cancel(); _encoded?.Dispose(); _encoded = null; _closing.Dispose(); }
        }
        base.Dispose(disposing);
    }
}
