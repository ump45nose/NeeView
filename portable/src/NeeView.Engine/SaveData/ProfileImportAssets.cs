// Copyright (c) NeeLaboratory. 原 Exporter/Importer 的附属文件边界适配，基线 c5c398d89。
using System.Text;
namespace NeeView;

/// <summary>原三类可独立选择的Profile附属材料。</summary>
public enum ProfileImportAssetKind { Playlists, Themes, Scripts }
/// <summary>逐文件大小、实际能力及阻止该类导入的错误，不等同于运行能力通过。</summary>
public sealed record ProfileImportAssetSummary(string Path, long Bytes, string Capability, string? Error);

/// <summary>原导出仅包含三个目录的一级文件；不将任意包内路径变成 Profile 写入地址。</summary>
public static class ProfileImportAssets
{
    public static IReadOnlyList<string> Folders { get; } = Array.AsReadOnly(new[] { "Playlists", "Themes", "Scripts" });
    /// <summary>兼容原 Windows 和 ZIP 分隔符；不支持的附属材料返回 null，危险路径拒绝。</summary>
    /// <param name="path">来源条目逻辑名。</param><returns>原目录名与文件名；Default/Pagemark恢复原固定命名，不是绝对路径。</returns>
    public static string? Normalize(string path)
    {
        var parts = path.Replace('\\', '/').Split('/');
        if (parts[0] == "" || parts.Any(p => p is "." or ".." || p.Contains(':') || p.Any(char.IsControl)))
            throw new InvalidDataException("附属文件路径无效：" + path);
        var folder = Folders.FirstOrDefault(f => f.Equals(parts[0], StringComparison.OrdinalIgnoreCase));
        if (folder is null) return null;
        if (parts.Length != 2 || string.IsNullOrEmpty(parts[1])) return null;
        var extension = folder switch { "Playlists" => ".nvpls", "Themes" => ".json", _ => ".nvjs" };
        if (!System.IO.Path.GetExtension(parts[1]).Equals(extension, StringComparison.OrdinalIgnoreCase)) return null;
        // 原默认列表/全局页标记有固定逻辑身份；Windows大小写别名恢复到Mac原固定名。
        var name = folder == "Playlists" ? new[] { "Default.nvpls", "Pagemark.nvpls" }.FirstOrDefault(n => n.Equals(parts[1], StringComparison.OrdinalIgnoreCase)) ?? parts[1] : parts[1];
        return folder + "/" + name;
    }
    /// <summary>按目标 Mac 默认文件系统的大小写/Unicode 别名拒绝歧义，不改变保存文件名。</summary>
    /// <param name="path">仅用于附属文件名歧义检查的逻辑路径。</param><returns>比较用键，不能用于落盘。</returns>
    public static string CollisionKey(string path) => path.Normalize(NormalizationForm.FormC).ToUpperInvariant();
    /// <summary>取得已规范附属路径所属的原类别。</summary>
    /// <param name="path">已经Normalize/ValidateTarget的规范路径。</param><returns>独立选择类别。</returns>
    public static ProfileImportAssetKind Kind(string path) => Enum.Parse<ProfileImportAssetKind>(path.Split('/')[0]);
    /// <summary>验证事务、清单和恢复只能操作原根 JSON 或受限附属文件。</summary>
    /// <param name="name">规范相对路径。</param>
    public static void ValidateTarget(string name)
    {
        if (!ProfileImportFiles.Names.Contains(name) && Normalize(name) != name)
            throw new InvalidDataException("非 Profile 事务文件：" + name);
    }
    /// <summary>目标与暂存不能经过 Profile 内符号链接；损坏链接同样拒绝。</summary>
    /// <param name="root">应用拥有的 Profile 或备份根。</param><param name="name">已验证的相对路径。</param>
    /// <returns>实际写入路径，不创建文件或文件夹。</returns>
    internal static string ResolveTarget(string root, string name)
    {
        ValidateTarget(name);
        var fullRoot = System.IO.Path.GetFullPath(root); RejectLink(fullRoot);
        var path = System.IO.Path.Combine(fullRoot, name);
        RejectLink(System.IO.Path.GetDirectoryName(path)!);
        RejectLink(path); RejectLink(path + ".tmp"); RejectLink(path + ".save-backup");
        return path;
    }
    /// <summary>使用属性查询识别链接，包括悬空链接；权限与 I/O 错误不能视为缺失。</summary>
    /// <param name="path">读取或写入前要检查的真实路径。</param>
    public static void RejectLink(string path)
    {
        try { if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Profile 导入不跟随符号链接：" + path); }
        catch (FileNotFoundException) { }
        catch (DirectoryNotFoundException) { }
    }
}
