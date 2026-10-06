using System.Text.Json.Nodes;
namespace NeeView;

/// <summary>原 Exporter/SaveDataProfile 的五个根文件；保留原特殊拼写。</summary>
public static class ProfileImportFiles
{
    public static IReadOnlyList<string> Names { get; } = Array.AsReadOnly(new[]
        { "UserSetting.json", "History.json", "Bookmark.json", "Foldres.json", "QuicAccess.json" });
    // 原 CreateVersionProps 用提交计数生成 build；固定合并 c5c398d89 的计数为 4340。
    public const int BaselineBuild = 4340;
    public const long MaxFileBytes = 32 * 1024 * 1024;
    public const long MaxTotalBytes = 64 * 1024 * 1024;
    public const int MaxEntries = 10000;
    public const int MaxRecords = 100000;
}
/// <summary>只读来源；目录是 Profile 根，备份是原标准 ZIP 包，不从包中执行或解压脚本。</summary>
public enum ProfileImportSourceKind { Directory, Backup }
public sealed record ProfileImportSource(string Path, ProfileImportSourceKind Kind);
public sealed record ProfileImportBundle(IReadOnlyDictionary<string, string> Files, IReadOnlyList<string> ExtraEntries,
    IReadOnlyDictionary<string, byte[]>? Assets = null);
/// <summary>实际文件读取替换点；界面不枚举来源或打开 ZIP。</summary>
public interface IProfileImportReader
{
    /// <summary>读取有限的原 JSON 和附属字节；错误/取消不能修改来源或当前状态。</summary>
    /// <param name="source">明确选定的 Profile 或备份。</param><param name="token">等待和读取取消。</param>
    /// <returns>原文件文本、附属数据及未处理条目清单；不持有来源流。</returns>
    Task<ProfileImportBundle> ReadAsync(ProfileImportSource source, CancellationToken token);
}
public sealed record ProfilePathMapping(string WindowsPrefix, string MacPrefix);
public enum ProfilePathStatus { Mapped, Unmapped, Unchanged }
public sealed record ProfileImportPath(string File, string Field, string Original, string Result, ProfilePathStatus Status, string? Page)
{
    public string StatusText => Status switch { ProfilePathStatus.Mapped => "已映射", ProfilePathStatus.Unmapped => "未映射，保留", _ => "保留原值" };
}
public sealed record ProfileImportFileSummary(string Name, bool Present, string Format, int Records)
{
    public string Description => Present ? $"{Name} · {Records} 条路径 · {Format}" : $"{Name} · 缺失，使用现有值/原默认值";
}
public sealed record ProfileImportCommand(string Name, string Shortcut, bool Explicit, string Capability)
{
    public string Origin => Explicit ? "原差分配置" : "原方案默认值";
}
/// <summary>只读候选快照，不持有 Config/SaveData；取出 JSON 时复制，避免表现端改变计划。</summary>
public sealed class ProfileImportPreview
{
    private readonly IReadOnlyDictionary<string, JsonObject> _documents;
    private readonly IReadOnlyDictionary<string, byte[]> _assets;
    internal ProfileImportPreview(IReadOnlyDictionary<string, JsonObject> documents, IReadOnlyList<ProfileImportFileSummary> files,
        IReadOnlyList<ProfileImportPath> paths, IReadOnlyList<ProfileImportCommand> commands, IReadOnlyList<string> notices,
        IReadOnlyDictionary<string, byte[]>? assets = null, IReadOnlyList<ProfileImportAssetSummary>? assetSummaries = null)
    {
        (_documents, Files, Paths, Commands, Notices) = (documents, files, paths, commands, notices);
        _assets = assets ?? new Dictionary<string, byte[]>(); Assets = assetSummaries ?? [];
    }
    public IReadOnlyList<ProfileImportFileSummary> Files { get; }
    public IReadOnlyList<ProfileImportPath> Paths { get; }
    public IReadOnlyList<ProfileImportCommand> Commands { get; }
    public IReadOnlyList<string> Notices { get; }
    public IReadOnlyList<ProfileImportAssetSummary> Assets { get; }
    /// <summary>复制附属材料，不允许界面或调用方修改预览候选。</summary>
    /// <param name="path">预览中的规范逻辑路径。</param><returns>独立字节副本；缺失返回 null。</returns>
    public byte[]? GetAsset(string path) => _assets.TryGetValue(path, out var bytes) ? bytes.ToArray() : null;
    public int UnmappedCount => Paths.Count(p => p.Status == ProfilePathStatus.Unmapped);
    /// <summary>确认选项并复制候选；未知/尚未支持的版本在写入前拒绝。</summary>
    /// <param name="selection">实际恢复项目，未选择的项目不改写。</param>
    /// <returns>与预览生命周期独立的候选快照。</returns>
    public ProfileImportRequest CreateRequest(ProfileImportSelection selection) => new(this, selection);
    /// <summary>提供实际导入或检查用的独立 JSON 副本。</summary>
    /// <param name="name">原五文件之一。</param><returns>完整候选，包括未知字段；缺失为 null。</returns>
    public JsonObject? GetDocument(string name) => _documents.TryGetValue(name, out var value) ? value.DeepClone().AsObject() : null;
}
