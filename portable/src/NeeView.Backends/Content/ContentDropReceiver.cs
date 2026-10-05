// Copyright (c) NeeLaboratory. 原ContentDropReceiver内联图片/网页/位图接收语义，基线c5c398d89。
using System.Net;
using System.Text.RegularExpressions;
using ImageMagick;
using NeeView;
namespace NeeView.Backends;

/// <summary>有界临时下载/图片批次，成功资源进程持有；关窗不破坏阅读历史。</summary>
public sealed class ContentDropReceiver(string? temporaryRoot = null, HttpClient? client = null) : IContentDropReceiver, IAsyncDisposable
{
    public const int MaximumFileBytes = 64 * 1024 * 1024;
    private const long BatchBudget = 128L * 1024 * 1024, ProcessBudget = 512L * 1024 * 1024;
    private readonly string _root = Path.Combine(temporaryRoot ?? ArchiveFactory.TemporaryDirectory, "Drops", Guid.NewGuid().ToString("N"));
    private readonly HttpClient _http = client ?? new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
    private readonly bool _ownsHttp = client is null;
    private readonly SemaphoreSlim _gate = new(1);
    private long _bytes;
    private bool _disposed;
    /// <summary>原浏览器优先内联/虚拟文件，再网络；普通来源图片兜底。每批最多16个实体。</summary>
    public async Task<IReadOnlyList<string>> ReceiveAsync(ContentDropData content, CancellationToken token)
    {
        await _gate.WaitAsync(token).ConfigureAwait(false);
        string? batch = null;
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (content.Images.Count > 16 || content.WebUrls?.Count > 16 || content.BrowserFiles?.Count > 16 || content.Html?.Length > 1024 * 1024)
                throw new NotSupportedException("接收内容超出16项/1MiB HTML预算。");
            ContentWrite.RejectLinkedPath(_root); batch = Path.Combine(_root, Guid.NewGuid().ToString("N")); Directory.CreateDirectory(batch);
            var inline = GetImageSources(content.Html).Where(s => s.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase)).ToArray();
            var paths = new List<string>(); long total = 0; Exception? lastFailure = null;
            if (inline.Length > 0)
            {
                await TryStageAsync(async () =>
                {
                foreach (var source in inline)
                {
                    var marker = source.IndexOf("base64,", StringComparison.Ordinal);
                    if (marker < 0) continue;
                    if (source.Length - marker > (MaximumFileBytes * 4L / 3) + 16) throw new NotSupportedException("内联图片超出预算。");
                    await SaveAsync(new(Convert.FromBase64String(source[(marker + 7)..])));
                }
                });
            }
            if (paths.Count == 0 && content.BrowserFiles?.Count > 0)
            {
                await TryStageAsync(async () =>
                {
                foreach (var path in FileClipboardCodec.ValidatePaths(content.BrowserFiles))
                {
                    var bytes = await ReadLimitedAsync(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete), token);
                    await SaveAsync(new(bytes, Path.GetFileName(path)));
                }
                });
            }
            if (paths.Count == 0)
            {
                await TryStageAsync(async () =>
                {
                var web = GetImageSources(content.Html).Where(IsWebUrl).ToArray();
                var urls = web.Length > 0 ? web : content.WebUrls ?? [];
                foreach (var value in urls)
                {
                    if (!IsWebUrl(value)) throw new NotSupportedException("只接收HTTP/HTTPS图片地址。");
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromSeconds(30));
                    using var response = await _http.GetAsync(value, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
                    response.EnsureSuccessStatusCode();
                    if (response.RequestMessage?.RequestUri is { } final && !IsWebUrl(final.AbsoluteUri)) throw new NotSupportedException("下载跳转到不支持的协议。");
                    if (response.Content.Headers.ContentLength > MaximumFileBytes) throw new NotSupportedException("下载图片超出64MiB预算。");
                    var bytes = await ReadLimitedAsync(await response.Content.ReadAsStreamAsync(timeout.Token), timeout.Token);
                    await SaveAsync(new(bytes, Path.GetFileName(new Uri(value).AbsolutePath)));
                }
                });
            }
            if (paths.Count == 0) foreach (var image in content.Images) await SaveAsync(image);
            if (paths.Count == 0) throw lastFailure ?? new NotSupportedException("内容中没有支持的图片或归档。");
            token.ThrowIfCancellationRequested(); _bytes += total; batch = null; return paths;

            // 原接收器一般读取/解码失败后尝试下一种数据。超预算和用户取消仍立即停止；失败阶段不留下部分批次。
            async Task TryStageAsync(Func<Task> receive)
            {
                try { await receive(); }
                catch (Exception ex) when (ex is IOException or HttpRequestException or MagickException or FormatException
                    || ex is OperationCanceledException && !token.IsCancellationRequested)
                {
                    lastFailure = ex; Directory.Delete(batch!, true); Directory.CreateDirectory(batch!); paths.Clear(); total = 0;
                }
            }

            async Task SaveAsync(ContentDropImage image)
            {
                if (paths.Count >= 16 || image.Bytes.Length > MaximumFileBytes) throw new NotSupportedException("接收批次超出预算。");
                var data = await Task.Run(() => Prepare(image), token).ConfigureAwait(false);
                total += data.Bytes.Length;
                if (total > BatchBudget || _bytes + total > ProcessBudget) throw new NotSupportedException("接收内容超出128MiB批次或512MiB进程预算。");
                var stem = Path.GetFileNameWithoutExtension(image.Name ?? "Image");
                if (string.IsNullOrWhiteSpace(stem)) stem = "Image";
                stem = new string(stem.Where(c => !char.IsControl(c) && c != '/' && c != '\\').Take(80).ToArray());
                var path = Path.Combine(batch!, $"{paths.Count + 1:D2}-{stem}{data.Extension}");
                await File.WriteAllBytesAsync(path, data.Bytes, token).ConfigureAwait(false); paths.Add(path);
            }
        }
        finally
        {
            try { if (batch is not null && Directory.Exists(batch)) Directory.Delete(batch, true); }
            finally { _gate.Release(); }
        }
    }
    /// <summary>解析img src，不执行HTML/脚本；正则有时限与数量边界。</summary>
    private static string[] GetImageSources(string? html)
    {
        if (html is null) return [];
        var matches = Regex.Matches(html, "<img\\b[^>]*?\\bsrc\\s*=\\s*(?:\"([^\"]*)\"|'([^']*)'|([^\\s>]+))", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(250));
        if (matches.Count > 16) throw new NotSupportedException("HTML图片数量超出16项预算。");
        return matches.Select(m => WebUtility.HtmlDecode(m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Success ? m.Groups[2].Value : m.Groups[3].Value)).ToArray();
    }
    private static bool IsWebUrl(string value) => value.Length <= 16384 && Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https";
    /// <summary>读响应/文件到有界缓冲，流由本方法释放；失败不遗留部分输出。</summary>
    private static async Task<byte[]> ReadLimitedAsync(Stream input, CancellationToken token)
    {
        await using (input)
        using (var output = new MemoryStream())
        {
            byte[] buffer = new byte[81920]; int read;
            while ((read = await input.ReadAsync(buffer, token)) > 0)
            { if (output.Length + read > MaximumFileBytes) throw new NotSupportedException("接收文件超出64MiB预算。"); await output.WriteAsync(buffer.AsMemory(0, read), token); }
            return output.ToArray();
        }
    }
    /// <summary>内联/下载按真实编码取扩展名；位图按原Bgr32丢弃alpha写PNG。</summary>
    private static (byte[] Bytes, string Extension) Prepare(ContentDropImage image)
    {
        MagickImageInfo info;
        try { info = new MagickImageInfo(image.Bytes); }
        catch (MagickException) when (!image.IsBitmap && image.Name is not null && ArchiveFormats.IsCompressedArchive(image.Name))
        { return (image.Bytes, Path.GetExtension(image.Name)); }
        var extension = info.Format switch { MagickFormat.Jpeg => ".jpg", MagickFormat.Png or MagickFormat.Png32 or MagickFormat.Png24 => ".png", MagickFormat.WebP => ".webp", MagickFormat.Gif => ".gif", MagickFormat.Bmp => ".bmp", MagickFormat.Tiff => ".tiff", _ => null };
        if (!image.IsBitmap) return extension is null ? throw new NotSupportedException("接收图片格式尚未支持。") : (image.Bytes, extension);
        if ((long)info.Width * info.Height * 4 > BatchBudget) throw new NotSupportedException("位图像素超出解码预算。");
        using var bitmap = new MagickImage(image.Bytes); bitmap.Alpha(AlphaOption.Off); bitmap.Depth = 8;
        return (bitmap.ToByteArray(MagickFormat.Png), ".png");
    }
    /// <summary>进程退出清理本进程目录，不扫描用户目录或其他进程材料。</summary>
    public async ValueTask DisposeAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try { ContentWrite.RejectLinkedPath(_root); if (Directory.Exists(_root)) Directory.Delete(_root, true); _disposed = true; if (_ownsHttp) _http.Dispose(); }
        finally { _gate.Release(); }
    }
}
