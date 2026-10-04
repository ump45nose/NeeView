// Copyright (c) NeeLaboratory. 原 BookMementoTools.RenameRecursive 的明确路径联动，基线 c5c398d89。
namespace NeeView;

public static class BookMementoTools
{
    /// <summary>仅替换完整来源或其子路径；Mac合法反斜杠不是目录边界，未知字段不参与。</summary>
    /// <param name="path">真实目录/归档及内部条目的逻辑地址。</param>
    /// <param name="source">已成功重命名的旧书籍地址。</param><param name="destination">实际新地址。</param>
    /// <returns>不属于该来源时原样返回；不统一路径大小写。</returns>
    public static string RenamePath(string path, string source, string destination) =>
        path == source || path.StartsWith(source + "/", StringComparison.Ordinal) ? destination + path[source.Length..] : path;
}
