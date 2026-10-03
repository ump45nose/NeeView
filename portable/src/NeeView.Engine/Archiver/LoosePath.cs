// Copyright (c) NeeLaboratory. 迁入原 Archiver/LoosePath.GetDirectoryName/GetFileName 子集，沿用仓库 MIT。
namespace NeeView;

/// <summary>历史中的逻辑路径解析；不改变传给文件系统的路径，也不替代导入路径映射。</summary>
public static class LoosePath
{
    /// <summary>保留明确Windows定位符的双分隔符语义；Mac路径中的反斜杠仍是合法文件名字符。</summary>
    /// <param name="path">原历史来源路径。</param><returns>是否为盘符或UNC形式，禁止统一小写或重写实际路径。</returns>
    private static bool IsWindowsPath(string path) => (path.Length >= 2 && char.IsAsciiLetter(path[0]) && path[1] == ':') || path.StartsWith("\\\\", StringComparison.Ordinal);

    /// <summary>取得直接父目录；Windows逻辑记录沿原parts规则，Mac遵循自身文件系统。</summary>
    /// <param name="path">未映射旧路径或真实Mac路径。</param><returns>逻辑父级，根或缺失时为空。</returns>
    public static string GetDirectoryName(string? path)
    {
        if (string.IsNullOrEmpty(path)) return "";
        if (!IsWindowsPath(path)) return System.IO.Path.GetDirectoryName(path.TrimEnd('/')) ?? "";
        // 原LoosePath使用两类分隔符解析虚拟路径，但不把其结果当实际读取路径。
        var parts = path.Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries).ToList();
        if (parts.Count <= 1) return "";
        parts.RemoveAt(parts.Count - 1);
        var parent = (path.StartsWith("\\\\", StringComparison.Ordinal) ? "\\\\" : "") + string.Join('\\', parts);
        return parts.Count == 1 && parent.EndsWith(':') ? parent + "\\" : parent;
    }

    /// <summary>取得原书名；Windows记录支持双分隔符，Mac保留文件名中的反斜杠。</summary>
    /// <param name="path">书籍来源。</param><returns>供显示/查询的末段名称，不是完整路径。</returns>
    public static string GetFileName(string path) => IsWindowsPath(path)
        ? path.Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? ""
        : System.IO.Path.GetFileName(path.TrimEnd('/'));
}
