using System.Security.Cryptography;
using System.Text;
using NeeView.Application;
using NeeView.Core;

namespace NeeView.Persistence;

/// <summary>应用专属原始缩略图缓存；键含内容版本，512 MiB LRU，不扫描或删除用户目录。</summary>
public sealed class DiskThumbnailCache(string root, long budget = 512L * 1024 * 1024) : IThumbnailCache
{
    private readonly SemaphoreSlim _gate = new(1);
    private Dictionary<string, (long Bytes, DateTime Used)>? _entries;
    /// <summary>输入规格键，生成不可受归档路径影响的随机摘要文件名。</summary>
    private string FilePath(ImageCacheKey key) => Path.Combine(root, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(StoreJson.Serialize(key)))) + ".thumb");
    /// <summary>首次访问读取应用缓存清单，遗留临时文件只在本目录内清理。</summary>
    private void Initialize()
    {
        if (_entries is not null) return;
        Directory.CreateDirectory(root);
        _entries = Directory.EnumerateFiles(root, "*.thumb").Select(p => new FileInfo(p)).ToDictionary(f => f.FullName, f => (f.Length, f.LastWriteTimeUtc));
        foreach (var temporary in Directory.EnumerateFiles(root, "*.tmp")) File.Delete(temporary);
    }
    /// <summary>读取固定头和 BGRA8 字节，损坏缓存删除后由解码器重建。</summary>
    public async Task<DecodedImageLease?> GetAsync(ImageCacheKey key, CancellationToken token)
    {
        await _gate.WaitAsync(token);
        try
        {
            Initialize(); var path = FilePath(key); if (!_entries!.ContainsKey(path)) return null;
            try
            {
                var bytes = await File.ReadAllBytesAsync(path, token);
                if (bytes.Length < 8) throw new InvalidDataException();
                var width = BitConverter.ToInt32(bytes, 0); var height = BitConverter.ToInt32(bytes, 4);
                if (width <= 0 || height <= 0 || width > 4096 || height > 4096 || (long)width * height * 4 + 8 != bytes.LongLength) throw new InvalidDataException();
                var now = DateTime.UtcNow; _entries[path] = (bytes.LongLength, now); File.SetLastWriteTimeUtc(path, now);
                return new(new(width, height), bytes[8..]);
            }
            catch (Exception error) when (error is IOException or InvalidDataException) { File.Delete(path); _entries.Remove(path); return null; }
        }
        finally { _gate.Release(); }
    }
    /// <summary>输入像素租约，原子保存；预算按实际磁盘字节回收最旧项。</summary>
    public async Task PutAsync(ImageCacheKey key, DecodedImageLease image, CancellationToken token)
    {
        if (!key.Thumbnail || image.ByteCount + 8 > budget) return;
        await _gate.WaitAsync(token);
        var path = FilePath(key); var temporary = path + ".tmp";
        try
        {
            Initialize();
            await using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None, 65536, true))
            {
                await stream.WriteAsync(BitConverter.GetBytes(image.Size.Width), token); await stream.WriteAsync(BitConverter.GetBytes(image.Size.Height), token);
                await stream.WriteAsync(image.Pixels, token); await stream.FlushAsync(token);
            }
            token.ThrowIfCancellationRequested(); File.Move(temporary, path, true);
            _entries![path] = (image.ByteCount + 8, DateTime.UtcNow);
            var total = _entries.Values.Sum(e => e.Bytes);
            foreach (var entry in _entries.OrderBy(e => e.Value.Used).ToArray())
            {
                if (total <= budget) break;
                File.Delete(entry.Key); _entries.Remove(entry.Key); total -= entry.Value.Bytes;
            }
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); _gate.Release(); }
    }
}
