using System.Text.Json.Nodes;
namespace NeeView;

/// <summary>原导入默认仅设置；目录参数/快速访问随设置恢复，也允许分别选择。</summary>
public sealed record ProfileImportSelection(bool Settings = true, bool Folders = true, bool QuickAccess = true, bool History = false, bool Bookmarks = false,
    bool Playlists = false, bool Themes = false, bool Scripts = false)
{
    public IEnumerable<string> Files => ProfileImportFiles.Names.Where(name => name switch
    { "UserSetting.json" => Settings, "Foldres.json" => Folders, "QuicAccess.json" => QuickAccess, "History.json" => History, _ => Bookmarks });
    /// <summary>按原三个独立附属选择判断实际应用范围。</summary>
    /// <param name="kind">已经验证的附属类别。</param><returns>只有显式选中才为true。</returns>
    public bool Includes(ProfileImportAssetKind kind) => kind switch
    { ProfileImportAssetKind.Playlists => Playlists, ProfileImportAssetKind.Themes => Themes, _ => Scripts };
}
/// <summary>确认时复制的不可变候选；重建窗口不依赖已经关闭的预览 VM。</summary>
public sealed class ProfileImportRequest
{
    private readonly Dictionary<string, JsonObject> _documents;
    private readonly Dictionary<string, byte[]> _assets;
    /// <summary>向SaveData提供独立字节副本，不暴露确认快照的内部数组。</summary>
    internal IReadOnlyDictionary<string, byte[]> GetAssets() => _assets.ToDictionary(p => p.Key, p => p.Value.ToArray());
    internal IEnumerable<string> AssetNames => _assets.Keys;
    public ProfileImportSelection Selection { get; }
    internal ProfileImportRequest(ProfileImportPreview preview, ProfileImportSelection selection)
    {
        Selection = selection;
        _documents = ProfileImportFiles.Names.Select(name => (name, value: preview.GetDocument(name)))
            .Where(p => p.value is not null).ToDictionary(p => p.name, p => p.value!);
        _assets = new(StringComparer.Ordinal);
        foreach (var item in preview.Assets.Where(item => selection.Includes(ProfileImportAssets.Kind(item.Path))))
        {
            if (item.Error is not null) throw new InvalidDataException(item.Path + "：" + item.Error);
            _assets.Add(item.Path, preview.GetAsset(item.Path)!);
        }
        // 只校验实际选项，未知设置版本不阻止导入同包内已支持的历史。
        foreach (var name in selection.Files)
            if (GetEffectiveDocument(name) is { } raw && ProfileImportCompatibility.BlockReason(name, raw) is { } reason)
                throw new InvalidDataException(reason);
        if (_assets.Count == 0 && !selection.Files.Any(name => GetEffectiveDocument(name) is not null)) throw new InvalidDataException("选定项目在来源中均不存在。");
    }
    internal JsonObject? GetEffectiveDocument(string name)
    {
        if (_documents.TryGetValue(name, out var raw)) return raw.DeepClone().AsObject();
        // 原 Importer 的独立文件优先、旧内嵌后备；后备来源仍须版本核对。
        if (name == "Foldres.json" && _documents.GetValueOrDefault("History.json") is { } history && history["Folders"] is JsonObject folders)
        {
            var reason = ProfileImportCompatibility.BlockReason("History.json", history);
            if (reason is not null) throw new InvalidDataException(reason);
            return LegacyFolderConfigUpgrade.FromHistory(history);
        }
        if (name == "QuicAccess.json" && _documents.GetValueOrDefault("Bookmark.json") is { } bookmark && bookmark["QuickAccess"] is JsonObject quick)
        {
            var reason = ProfileImportCompatibility.BlockReason("Bookmark.json", bookmark);
            if (reason is not null) throw new InvalidDataException(reason);
            var result = quick.DeepClone().AsObject();
            // 原memento构造器仅对缺失Format提供默认；明确的未知/未来/null格式不能被覆盖。
            if (!result.ContainsKey("Format")) result["Format"] = "NeeView.QuickAccess/" +
                (bookmark["MacImportedSourceFormat"] ?? bookmark["Format"])!.GetValue<string>().Split('/')[1];
            return result;
        }
        return null;
    }
}
