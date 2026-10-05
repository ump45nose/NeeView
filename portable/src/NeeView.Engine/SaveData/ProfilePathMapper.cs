namespace NeeView;

/// <summary>显式 Windows→Mac 映射；只比较 Windows 前缀时忽略大小写，不探测或猜测实体。</summary>
public sealed class ProfilePathMapper
{
    private readonly ProfilePathMapping[] _mappings;
    /// <summary>校验绝对前缀及重复来源，最长前缀优先；Mac 路径保留大小写。</summary>
    /// <param name="mappings">用户明确提供的盘符或 UNC 共享映射。</param>
    public ProfilePathMapper(IEnumerable<ProfilePathMapping> mappings)
    {
        _mappings = mappings.Select(m => new ProfilePathMapping(NormalizeWindows(m.WindowsPrefix), m.MacPrefix.TrimEnd('/'))).ToArray();
        foreach (var mapping in _mappings)
        {
            if (!IsWindowsAbsolute(mapping.WindowsPrefix) || HasParentSegment(mapping.WindowsPrefix))
                throw new ArgumentException("Windows 前缀需要绝对盘符或 UNC 共享路径，不能包含 ..。");
            if (!mapping.MacPrefix.StartsWith('/') && mapping.MacPrefix != "")
                throw new ArgumentException("Mac 前缀需要以 / 开始的绝对路径。");
            if (HasParentSegment(mapping.MacPrefix) || mapping.MacPrefix.Contains('\\') || string.IsNullOrEmpty(mapping.MacPrefix))
                throw new ArgumentException("Mac 前缀不能为根目录、包含反斜杠或 ..。");
        }
        if (_mappings.Select(m => m.WindowsPrefix).Distinct(StringComparer.OrdinalIgnoreCase).Count() != _mappings.Length)
            throw new ArgumentException("同一 Windows 前缀只能登记一次。");
        _mappings = _mappings.OrderByDescending(m => m.WindowsPrefix.Length).ToArray();
    }
    /// <summary>仅在路径边界匹配；归档内部尾部转换分隔符，Page/Props 等非路径值不进入此入口。</summary>
    /// <param name="path">原已知路径字段；虚拟、相对和 Mac 路径保留。</param>
    /// <returns>候选路径和状态，未映射绝不猜测盘符。</returns>
    public (string Path, ProfilePathStatus Status) Map(string path)
    {
        var normalized = NormalizeWindows(path);
        if (!IsWindowsAbsolute(normalized)) return (path, ProfilePathStatus.Unchanged);
        if (HasParentSegment(normalized)) return (path, ProfilePathStatus.Unmapped);
        foreach (var mapping in _mappings)
        {
            if (!normalized.StartsWith(mapping.WindowsPrefix, StringComparison.OrdinalIgnoreCase)) continue;
            if (!mapping.WindowsPrefix.EndsWith('/') && normalized.Length != mapping.WindowsPrefix.Length && normalized[mapping.WindowsPrefix.Length] != '/') continue;
            var suffix = normalized[mapping.WindowsPrefix.Length..].TrimStart('/');
            return (mapping.MacPrefix + (suffix.Length > 0 ? "/" + suffix : ""), ProfilePathStatus.Mapped);
        }
        return (path, ProfilePathStatus.Unmapped);
    }
    private static string NormalizeWindows(string path)
    { var value = path.Replace('\\', '/'); return value.Length == 3 && value[1] == ':' && value[2] == '/' ? value : value.TrimEnd('/'); }
    private static bool HasParentSegment(string path) => path.Split('/').Any(p => p is ".." or ".");
    private static bool IsWindowsAbsolute(string path) =>
        (path.Length >= 3 && char.IsAsciiLetter(path[0]) && path[1] == ':' && path[2] == '/') ||
        (path.StartsWith("//", StringComparison.Ordinal) && path[2..].Split('/').Count(p => p.Length > 0) >= 2);
}
