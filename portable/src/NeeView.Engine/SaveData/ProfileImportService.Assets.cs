// Copyright (c) NeeLaboratory. 原 Importer 附属选择和 PlaylistSource 路径数据适配。
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
namespace NeeView;

public sealed partial class ProfileImportService
{
    /// <summary>附属数据只读候选；列表复用唯一格式解析器，预览不装载主题、不执行脚本。</summary>
    /// <param name="bundle">受限读取结果。</param><param name="mapper">显式物理路径映射。</param>
    /// <param name="paths">共享路径报告。</param><param name="notices">共享能力报告。</param><param name="token">候选生成取消。</param>
    /// <returns>私有字节快照及逐文件能力状态；坏列表仅阻止选择该类别。</returns>
    private static (IReadOnlyDictionary<string, byte[]> Bytes, IReadOnlyList<ProfileImportAssetSummary> Summaries) PreviewAssets(
        ProfileImportBundle bundle, ProfilePathMapper mapper, List<ProfileImportPath> paths, List<string> notices, CancellationToken token)
    {
        var bytes = new Dictionary<string, byte[]>(StringComparer.Ordinal); var summaries = new List<ProfileImportAssetSummary>();
        var keys = new HashSet<string>(StringComparer.Ordinal); long total = bundle.Files.Values.Sum(s => (long)Encoding.UTF8.GetByteCount(s));
        foreach (var pair in bundle.Assets ?? new Dictionary<string, byte[]>())
        {
            token.ThrowIfCancellationRequested();
            var path = ProfileImportAssets.Normalize(pair.Key) ?? throw new InvalidDataException("不支持的附属文件：" + pair.Key);
            if (!keys.Add(ProfileImportAssets.CollisionKey(path))) throw new InvalidDataException("附属文件名冲突：" + path);
            if (keys.Count + bundle.Files.Count > ProfileImportFiles.MaxEntries || pair.Value.LongLength > ProfileImportFiles.MaxFileBytes ||
                (total += pair.Value.LongLength) > ProfileImportFiles.MaxTotalBytes) throw new InvalidDataException("附属文件导入预算超限。");
            var kind = ProfileImportAssets.Kind(path); var data = pair.Value.ToArray(); string? error = null;
            var capability = kind switch
            {
                ProfileImportAssetKind.Playlists => "原列表格式；选中后覆盖同名，进入现有播放列表链路",
                ProfileImportAssetKind.Themes => "原 JSON 主题；导入后按配置选择加载，失败回退 Dark",
                _ => "原 .nvjs 材料保留；不注册命令、不执行事件脚本"
            };
            if (kind == ProfileImportAssetKind.Playlists)
            {
                try
                {
                    var text = new UTF8Encoding(false, true).GetString(data).TrimStart('\uFEFF');
                    var utf8 = Encoding.UTF8.GetBytes(text);
                    var source = PlaylistSourceTools.Deserialize(utf8);
                    if (source.Items.Count > ProfileImportFiles.MaxRecords - paths.Count) throw new InvalidDataException("播放列表路径记录数量超限。");
                    var root = JsonNode.Parse(text, documentOptions: new() { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip, MaxDepth = 64 })!.AsObject();
                    var items = root["Items"]!.AsArray(); var v1 = root["Format"]!.GetValue<string>() == "NeeViewPlaylist.1"; var changed = false;
                    for (var i = 0; i < items.Count; i++)
                    {
                        token.ThrowIfCancellationRequested();
                        var original = v1 ? items[i]!.GetValue<string>() : items[i]!["Path"]!.GetValue<string>();
                        var mapped = mapper.Map(original);
                        paths.Add(new(path, $"Items[{i}]" + (v1 ? "" : ".Path"), original, mapped.Path, mapped.Status, null));
                        if (mapped.Status != ProfilePathStatus.Mapped) continue;
                        if (v1) items[i] = mapped.Path; else items[i]!["Path"] = mapped.Path;
                        changed = true;
                    }
                    if (changed) data = JsonSerializer.SerializeToUtf8Bytes(root, new JsonSerializerOptions { WriteIndented = true });
                }
                catch (Exception ex) when (ex is JsonException or NotSupportedException or InvalidDataException or DecoderFallbackException or InvalidOperationException or KeyNotFoundException)
                { error = "播放列表不能实际导入：" + ex.Message; notices.Add(path + "：" + error); }
            }
            total += data.LongLength - pair.Value.LongLength;
            if (data.LongLength > ProfileImportFiles.MaxFileBytes || total > ProfileImportFiles.MaxTotalBytes) throw new InvalidDataException("附属文件映射后字节数超限。");
            bytes.Add(path, data); summaries.Add(new(path, data.LongLength, capability, error));
        }
        if (summaries.Any(s => ProfileImportAssets.Kind(s.Path) == ProfileImportAssetKind.Playlists))
            notices.Add("播放列表导入到当前 Mac Profile/Playlists；选中后切换到该目录，保留未覆盖列表。路径映射只处理 Items 的完整 Path，归档内部相对值与未知字段保持。");
        if (summaries.Any(s => ProfileImportAssets.Kind(s.Path) == ProfileImportAssetKind.Themes))
            notices.Add("主题保存到 Mac Profile/Themes；同时导入设置并引用同包主题时使用该目录，重建后加载当前选择，失败回退 Dark。预览不装载主题；只选主题材料不更改目录配置。");
        if (summaries.Any(s => ProfileImportAssets.Kind(s.Path) == ProfileImportAssetKind.Scripts))
            notices.Add("脚本仅保存到当前 Mac Profile/Scripts；不执行 OnStartup/OnBookLoaded 等脚本，实际能力继续占位。");
        return (bytes, summaries.AsReadOnly());
    }
}
