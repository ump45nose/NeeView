using System.Text.Json.Nodes;
namespace NeeView;

/// <summary>原导入默认仅设置；目录参数/快速访问随设置恢复，也允许分别选择。</summary>
public sealed record ProfileImportSelection(bool Settings = true, bool Folders = true, bool QuickAccess = true, bool History = false, bool Bookmarks = false)
{
    public IEnumerable<string> Files => ProfileImportFiles.Names.Where(name => name switch
    { "UserSetting.json" => Settings, "Foldres.json" => Folders, "QuicAccess.json" => QuickAccess, "History.json" => History, _ => Bookmarks });
}
/// <summary>确认时复制的不可变候选；重建窗口不依赖已经关闭的预览 VM。</summary>
public sealed class ProfileImportRequest
{
    private readonly Dictionary<string, JsonObject> _documents;
    public ProfileImportSelection Selection { get; }
    internal ProfileImportRequest(ProfileImportPreview preview, ProfileImportSelection selection)
    {
        Selection = selection;
        _documents = ProfileImportFiles.Names.Select(name => (name, value: preview.GetDocument(name)))
            .Where(p => p.value is not null).ToDictionary(p => p.name, p => p.value!);
        // 只校验实际选项，未知设置版本不阻止导入同包内已支持的历史。
        foreach (var name in selection.Files)
            if (GetEffectiveDocument(name) is { } raw && ProfileImportCompatibility.BlockReason(name, raw) is { } reason)
                throw new InvalidDataException(reason);
        if (!selection.Files.Any(name => GetEffectiveDocument(name) is not null)) throw new InvalidDataException("选定项目在来源中均不存在。");
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
