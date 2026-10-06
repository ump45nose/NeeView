using System.IO.Compression;
using System.Text;
using NeeView;
namespace NeeView.Backends;

/// <summary>原 Exporter 的 Profile/ZIP 只读适配；附属文件只读取原三个目录的一级允许文件。</summary>
public sealed class ProfileImportReader : IProfileImportReader
{
    /// <inheritdoc/>
    public Task<ProfileImportBundle> ReadAsync(ProfileImportSource source, CancellationToken token) =>
        SourceIo.RunReadAsync(ct => ReadCoreAsync(source, ct), token);
    /// <summary>后台读取允许范围，数量与声明/实际字节预算共同约束，不落盘来源路径。</summary>
    /// <param name="source">只读Profile或标准ZIP。</param><param name="token">枚举和读取取消。</param><returns>不持有流的文本/字节快照。</returns>
    private static async Task<ProfileImportBundle> ReadCoreAsync(ProfileImportSource source, CancellationToken token)
    {
        var files = new Dictionary<string, string>(StringComparer.Ordinal);
        var assets = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        var keys = new HashSet<string>(StringComparer.Ordinal);
        var extras = new List<string>(); long total = 0, actualTotal = 0; int count = 0;
        ProfileImportAssets.RejectLink(source.Path);
        if (source.Kind == ProfileImportSourceKind.Directory)
        {
            foreach (var entry in new DirectoryInfo(source.Path).EnumerateFileSystemInfos())
            {
                token.ThrowIfCancellationRequested(); CheckCount();
                var name = ProfileImportFiles.Names.FirstOrDefault(n => n.Equals(entry.Name, StringComparison.OrdinalIgnoreCase));
                if (name is not null) { await ReadFileAsync(entry, name); continue; }
                var folder = ProfileImportAssets.Folders.FirstOrDefault(n => n.Equals(entry.Name, StringComparison.OrdinalIgnoreCase));
                if (folder is null) { extras.Add(entry.Name); continue; }
                ProfileImportAssets.RejectLink(entry.FullName);
                if (entry is not DirectoryInfo directory) throw new InvalidDataException(folder + " 必须是目录。");
                foreach (var child in directory.EnumerateFileSystemInfos())
                {
                    token.ThrowIfCancellationRequested(); CheckCount();
                    var logical = folder + "/" + child.Name; var asset = ProfileImportAssets.Normalize(logical);
                    if (asset is null || child is DirectoryInfo) { extras.Add(logical); continue; }
                    await ReadFileAsync(child, asset);
                }
            }
        }
        else
        {
            await using var stream = new FileStream(source.Path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, true);
            using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
            foreach (var entry in zip.Entries)
            {
                token.ThrowIfCancellationRequested(); CheckCount();
                var logical = entry.FullName.Replace('\\', '/');
                var asset = ProfileImportAssets.Normalize(logical);
                var name = ProfileImportFiles.Names.FirstOrDefault(n => n.Equals(logical, StringComparison.OrdinalIgnoreCase));
                if (name is null && asset is null) { extras.Add(entry.FullName); continue; }
                // UNIX zip link entries contain a link target, not the requested file data.
                if (((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000) throw new InvalidDataException("导入来源包含符号链接：" + logical);
                CheckLength(entry.Length);
                await using var content = entry.Open();
                Add(name ?? asset!, await ReadBytesAsync(content, ProfileImportFiles.MaxTotalBytes - actualTotal, token));
            }
        }
        if (files.Count == 0 && assets.Count == 0) throw new InvalidDataException("来源中未找到 NeeView 的五个 JSON 或允许的附属文件。");
        return new(files, extras.AsReadOnly(), assets);

        async Task ReadFileAsync(FileSystemInfo entry, string name)
        {
            ProfileImportAssets.RejectLink(entry.FullName);
            if (entry is not FileInfo file) throw new InvalidDataException(name + " 必须是文件。");
            CheckLength(file.Length);
            await using var stream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, true);
            Add(name, await ReadBytesAsync(stream, ProfileImportFiles.MaxTotalBytes - actualTotal, token));
        }
        void CheckCount() { if (++count > ProfileImportFiles.MaxEntries) throw new InvalidDataException("导入来源条目数量超限。"); }
        void CheckLength(long length)
        {
            if (length > ProfileImportFiles.MaxFileBytes || length < 0 || (total += length) > ProfileImportFiles.MaxTotalBytes)
                throw new InvalidDataException("导入字节数超限（单文件 32 MiB，总计 64 MiB）。");
        }
        void Add(string name, byte[] bytes)
        {
            actualTotal += bytes.Length;
            if (!keys.Add(ProfileImportAssets.CollisionKey(name))) throw new InvalidDataException("重复或大小写/Unicode歧义的导入文件：" + name);
            if (ProfileImportFiles.Names.Contains(name)) files.Add(name, DecodeText(bytes));
            else assets.Add(name, bytes);
        }
    }
    /// <summary>按实际解压字节再次限额；流由调用方持有，不解压来源路径。</summary>
    private static async Task<byte[]> ReadBytesAsync(Stream stream, long remainingBytes, CancellationToken token)
    {
        using var output = new MemoryStream(); var buffer = new byte[65536]; int length;
        while ((length = await stream.ReadAsync(buffer, token)) > 0)
        {
            if (output.Length + length > Math.Min(ProfileImportFiles.MaxFileBytes, remainingBytes)) throw new InvalidDataException("导入实际字节数超限。");
            output.Write(buffer, 0, length);
        }
        return output.ToArray();
    }
    /// <summary>原JSON文本使用严格UTF-8，保留兼容BOM的读取行为。</summary>
    /// <param name="bytes">已受限的根JSON字节。</param><returns>去除开头BOM的原文本。</returns>
    private static string DecodeText(byte[] bytes) => new UTF8Encoding(false, true).GetString(bytes).TrimStart('\uFEFF');
}
