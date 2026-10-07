using System.Text.RegularExpressions;
using System.Buffers.Binary;
namespace NeeView.Backends;

/// <summary>同一归档的物理分卷；逻辑地址始终使用主卷，不拼接临时副本或依赖压缩CLI。</summary>
internal sealed record ArchiveVolumeSet(string PrimaryPath, IReadOnlyList<FileInfo> Files, bool IsMultipart)
{
    /// <summary>从任意卷解析完整连续序列；只匹配同一名称和编号宽度，数字后缀本身不代表归档。</summary>
    /// <param name="path">用户选择或嵌套代理的真实路径。</param><param name="token">后台枚举取消。</param>
    /// <returns>按SharpCompress约定排列的物理卷；分卷ZIP以中央目录.zip在前。</returns>
    public static ArchiveVolumeSet Resolve(string path, CancellationToken token)
    {
        path = Path.GetFullPath(path); var name = Path.GetFileName(path); var parent = Path.GetDirectoryName(path)!;
        var rar = Regex.Match(name, @"^(.*\.part)(\d+)(\.rar)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (rar.Success)
        {
            var files = Numbered(parent, rar.Groups[1].Value, rar.Groups[3].Value, rar.Groups[2].Length, 1, token, allowStandalone: true);
            if (files.Count > 1 || int.TryParse(rar.Groups[2].Value, out var selected) && selected == 1) return new(files[0].FullName, files, files.Count > 1);
            // 单文件可合法使用partNN名称；只有真实分卷头才要求主卷。
            return new(path, [new(path)], false);
        }
        var split = Regex.Match(name, @"^(.*\.(?:7z|zip)\.)(\d{3})$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (split.Success)
        {
            var files = Numbered(parent, split.Groups[1].Value, "", 3, 1, token);
            return new(files[0].FullName, files, true);
        }
        var legacy = Regex.Match(name, @"^(.*)\.([rz])(\d{2,})$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        bool zip = legacy.Success ? legacy.Groups[2].Value.Equals("z", StringComparison.OrdinalIgnoreCase) : Path.GetExtension(name).Equals(".zip", StringComparison.OrdinalIgnoreCase);
        bool root = Path.GetExtension(name).Equals(".rar", StringComparison.OrdinalIgnoreCase) || zip;
        if (legacy.Success || root)
        {
            var stem = legacy.Success ? legacy.Groups[1].Value : Path.GetFileNameWithoutExtension(name);
            var extension = zip ? ".zip" : ".rar";
            var matches = new DirectoryInfo(parent).EnumerateFiles().Where(f => Path.GetFileNameWithoutExtension(f.Name) == stem && f.Extension.Equals(extension, StringComparison.OrdinalIgnoreCase)).Take(2).ToArray();
            if (matches.Length != 1) { if (legacy.Success) throw new FileNotFoundException("缺少或无法唯一确定归档主卷。", path); return new(path, [new(path)], false); }
            var width = legacy.Success ? legacy.Groups[3].Length : 2;
            var parts = Numbered(parent, stem + (zip ? ".z" : ".r"), "", width, zip ? 1 : 0, token, required: false);
            if (zip) ValidateZipDisks(matches[0].FullName, parts.Count);
            if (parts.Count > 0) return new(matches[0].FullName, new[] { matches[0] }.Concat(parts).ToArray(), true);
            if (legacy.Success) throw new FileNotFoundException("缺少归档分卷。", path);
        }
        return new(path, [new(path)], false);
    }

    /// <summary>书架只在同次枚举已知主卷存在时隐藏后续卷，不新增扫描或误删独立partNN书籍。</summary>
    internal static bool IsSecondaryName(string name, ISet<string> names)
    {
        var part = Regex.Match(name, @"^(.*\.part)(\d+)(\.rar)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (part.Success && int.TryParse(part.Groups[2].Value, out int number) && number > 1)
            return names.Contains(part.Groups[1].Value + "1".PadLeft(part.Groups[2].Length, '0') + part.Groups[3].Value);
        var split = Regex.Match(name, @"^(.*\.(?:7z|zip)\.)(\d{3})$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (split.Success && split.Groups[2].Value != "001") return names.Contains(split.Groups[1].Value + "001");
        var legacy = Regex.Match(name, @"^(.*)\.([rz])\d{2,}$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return legacy.Success && names.Contains(legacy.Groups[1].Value + (legacy.Groups[2].Value.Equals("z", StringComparison.OrdinalIgnoreCase) ? ".zip" : ".rar"));
    }

    /// <summary>ZIP末卷的EOCD/ZIP64卷号必须与找到的.zNN数量一致；没有z01不能当普通ZIP。</summary>
    private static void ValidateZipDisks(string path, int parts)
    {
        using var input = File.OpenRead(path);
        var size = (int)Math.Min(input.Length, 65557 + 20);
        var tail = new byte[size]; input.Position = input.Length - size; input.ReadExactly(tail);
        for (int i = tail.Length - 22; i >= 0; i--)
        {
            var data = tail.AsSpan(i);
            if (BinaryPrimitives.ReadUInt32LittleEndian(data) != 0x06054b50 || i + 22 + BinaryPrimitives.ReadUInt16LittleEndian(data[20..]) != tail.Length) continue;
            int disk = BinaryPrimitives.ReadUInt16LittleEndian(data[4..]);
            int directoryDisk = BinaryPrimitives.ReadUInt16LittleEndian(data[6..]);
            if (i >= 20 && BinaryPrimitives.ReadUInt32LittleEndian(tail.AsSpan(i - 20)) == 0x07064b50)
            {
                var count = BinaryPrimitives.ReadUInt32LittleEndian(tail.AsSpan(i - 4));
                if (count != parts + 1) throw new FileNotFoundException("ZIP64分卷不完整，缺少卷或多出卷。");
            }
            else if (disk != parts || directoryDisk > disk)
                throw new FileNotFoundException("ZIP分卷不完整，缺少卷或多出卷。");
            return;
        }
        // 损坏中央目录由解码器报告；不将无EOCD的文件伪造为正常ZIP。
    }

    private static IReadOnlyList<FileInfo> Numbered(string parent, string prefix, string suffix, int width, int first, CancellationToken token, bool required = true, bool allowStandalone = false)
    {
        var files = new SortedDictionary<int, FileInfo>();
        foreach (var file in new DirectoryInfo(parent).EnumerateFiles())
        {
            token.ThrowIfCancellationRequested(); var name = file.Name;
            if (!name.StartsWith(prefix, StringComparison.Ordinal) || !name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) || name.Length != prefix.Length + width + suffix.Length) continue;
            if (!int.TryParse(name.AsSpan(prefix.Length, width), out var number)) continue;
            if (files.Count >= 1024) throw new NotSupportedException("归档分卷超过1024卷限制。");
            if (!files.TryAdd(number, file)) throw new InvalidDataException("归档卷号不唯一。");
        }
        if (files.Count == 0 && !required) return [];
        if (files.Count == 1 && allowStandalone) return files.Values.ToArray();
        if (files.Count == 0 || files.Keys.First() != first) throw new FileNotFoundException("缺少归档主卷。");
        int expected = first;
        foreach (var number in files.Keys) if (number != expected++) throw new FileNotFoundException("归档分卷不连续，缺少卷号 " + (expected - 1) + "。");
        return files.Values.ToArray();
    }
}
