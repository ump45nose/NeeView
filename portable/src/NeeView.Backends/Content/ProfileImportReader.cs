using System.IO.Compression;
using System.Text;
using NeeView;
namespace NeeView.Backends;

/// <summary>原 Exporter 的 Profile/标准 ZIP 读取适配；只读五文件，不解压包内路径。</summary>
public sealed class ProfileImportReader : IProfileImportReader
{
    /// <inheritdoc/>
    public Task<ProfileImportBundle> ReadAsync(ProfileImportSource source, CancellationToken token) =>
        SourceIo.RunReadAsync(ct => ReadCoreAsync(source, ct), token);
    private static async Task<ProfileImportBundle> ReadCoreAsync(ProfileImportSource source, CancellationToken token)
    {
        var files = new Dictionary<string, string>(StringComparer.Ordinal);
        var extras = new List<string>(); long total = 0, actualTotal = 0; int count = 0;
        if (source.Kind == ProfileImportSourceKind.Directory)
        {
            foreach (var entry in new DirectoryInfo(source.Path).EnumerateFileSystemInfos())
            {
                token.ThrowIfCancellationRequested(); CheckCount();
                var name = ProfileImportFiles.Names.FirstOrDefault(n => n.Equals(entry.Name, StringComparison.OrdinalIgnoreCase));
                if (name is null) { extras.Add(entry.Name); continue; }
                if (entry is not FileInfo file) throw new InvalidDataException(name + " 必须是文件。");
                CheckLength(file.Length);
                await using var stream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, true);
                Add(name, await ReadTextAsync(stream, ProfileImportFiles.MaxTotalBytes - actualTotal, token));
            }
        }
        else
        {
            await using var stream = new FileStream(source.Path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, true);
            using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
            foreach (var entry in zip.Entries)
            {
                token.ThrowIfCancellationRequested(); CheckCount();
                // Windows 导出可能含反斜杠；仅规范逻辑名，不生成任何落盘路径。
                var logical = entry.FullName.Replace('\\', '/');
                var name = ProfileImportFiles.Names.FirstOrDefault(n => n.Equals(logical, StringComparison.OrdinalIgnoreCase));
                if (name is null) { extras.Add(entry.FullName); continue; }
                CheckLength(entry.Length);
                await using var content = entry.Open();
                Add(name, await ReadTextAsync(content, ProfileImportFiles.MaxTotalBytes - actualTotal, token));
            }
        }
        if (files.Count == 0) throw new InvalidDataException("来源根目录中未找到 NeeView 的五个 JSON 文件。");
        return new(files, extras.AsReadOnly());

        void CheckCount() { if (++count > ProfileImportFiles.MaxEntries) throw new InvalidDataException("导入来源条目数量超限。"); }
        void CheckLength(long length)
        {
            if (length > ProfileImportFiles.MaxFileBytes || length < 0 || (total += length) > ProfileImportFiles.MaxTotalBytes)
                throw new InvalidDataException("导入 JSON 字节数超限（单文件 32 MiB，总计 64 MiB）。");
        }
        void Add(string name, (string Text, long Bytes) result)
        { actualTotal += result.Bytes; if (!files.TryAdd(name, result.Text)) throw new InvalidDataException("重复的导入文件：" + name); }
    }
    /// <summary>流长度可能与声明不同，按实际解压字节再次限额；严格 UTF-8，兼容原 BOM。</summary>
    private static async Task<(string Text, long Bytes)> ReadTextAsync(Stream stream, long remainingBytes, CancellationToken token)
    {
        using var output = new MemoryStream(); var buffer = new byte[65536]; int length;
        while ((length = await stream.ReadAsync(buffer, token)) > 0)
        {
            if (output.Length + length > Math.Min(ProfileImportFiles.MaxFileBytes, remainingBytes)) throw new InvalidDataException("导入 JSON 实际字节数超限。");
            output.Write(buffer, 0, length);
        }
        var text = new UTF8Encoding(false, true).GetString(output.GetBuffer(), 0, checked((int)output.Length));
        return (text.TrimStart('\uFEFF'), output.Length);
    }
}
