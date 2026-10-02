using System.Text.Json;
using System.Text.Json.Nodes;
namespace NeeView;

/// <summary>沿用 UserSetting/History JSON 和 BookMemento；原始节点保留未迁移字段。</summary>
public sealed class SaveData(string directory)
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true, PropertyNameCaseInsensitive = true, AllowTrailingCommas = true, ReadCommentHandling = JsonCommentHandling.Skip };
    private readonly SemaphoreSlim _gate = new(1);
    private JsonObject _setting = new();
    private JsonObject _history = new();
    public string DirectoryPath { get; } = directory;
    public string? LastBookPath => _setting["Config"]?["StartUp"]?["LastBookV2"]?["Path"]?.GetValue<string>();

    /// <summary>加载原命名文件并恢复 P1 已支持配置；损坏文件不覆盖。</summary>
    public async Task LoadAsync(CancellationToken token = default)
    {
        RecoverInterruptedSave();
        _setting = await ReadAsync("UserSetting.json", token);
        _history = await ReadAsync("History.json", token);
        var raw = _setting["Config"] as JsonObject;
        var config = new Config();
        if (raw is not null)
        {
            config.BookSetting = ReadBranch<BookSettingConfig>(raw, "BookSetting");
            config.BookSettingDefault = ReadBranch<BookSettingConfig>(raw, "BookSettingDefault");
            config.BookSettingPolicy = ReadBranch<BookSettingPolicyConfig>(raw, "BookSettingPolicy");
            config.Book = ReadBranch<BookConfig>(raw, "Book");
            config.View = ReadBranch<ViewConfig>(raw, "View");
            config.Panels = ReadBranch<PanelsConfig>(raw, "Panels");
        }
        Config.SetCurrent(config);
    }

    /// <summary>按原 Path/Page/Props 恢复；Page 是条目名，不是数字页码。</summary>
    public (BookMemento? Memento, int Part) Find(string path)
    {
        var entry = (_history["Items"] as JsonArray)?.OfType<JsonObject>().FirstOrDefault(e => e["Path"]?.GetValue<string>() == path);
        if (entry is null) return (null, 0);
        var props = entry["Props"]?.GetValue<string>();
        // 新版未知 Props 仍保留在节点中，只将当前已识别 token 传入原解析器。
        var known = FilterProps(props, true);
        var memento = BookMemento.ParseWithProperties(path, entry["Page"]?.GetValue<string>(), known);
        // 原 Props 无法无歧义编码 IsWide=false；Mac 扩展仅补充这一缺失值，不改原解析器。
        if (memento is not null && entry["MacIsSupportedWidePage"] is JsonValue wide) memento.IsSupportedWidePage = wide.GetValue<bool>();
        return (memento, entry["MacPagePart"]?.GetValue<int>() ?? 0);
    }

    /// <summary>读取差分命令键位；未配置时使用固定基线默认值。</summary>
    public string GetShortcut(string name, string fallback)
    {
        var item = _setting["Commands"]?[name];
        return item?["ShortCutKey"]?.GetValue<string>() ?? fallback;
    }

    /// <summary>保存阅读状态与布局，已知字段合并进原节点后原子替换文件。</summary>
    public async Task SaveAsync(Book? book, int part, CancellationToken token = default)
    {
        await _gate.WaitAsync(token);
        try
        {
            var config = Object(_setting, "Config");
            foreach (var branch in new[] { "BookSetting", "BookSettingDefault", "BookSettingPolicy", "Book", "View", "Panels" })
            {
                var value = typeof(Config).GetProperty(branch)!.GetValue(Config.Current);
                Merge(Object(config, branch), JsonSerializer.SerializeToNode(value, Options)!.AsObject());
            }
            if (book is not null)
            {
                var memento = book.CreateMemento();
                var items = _history["Items"] as JsonArray ?? new JsonArray();
                _history["Items"] = items;
                var old = items.OfType<JsonObject>().FirstOrDefault(e => e["Path"]?.GetValue<string>() == book.Path);
                var item = old ?? new JsonObject();
                var unknownProps = FilterProps(item["Props"]?.GetValue<string>(), false);
                item["Path"] = book.Path; item["Page"] = memento.Page;
                item["Props"] = string.Join(' ', new[] { memento.ToPropertiesString(), unknownProps }.Where(e => !string.IsNullOrEmpty(e)));
                item["LastAccessTime"] = DateTime.Now;
                item["MacPagePart"] = part;
                item["MacIsSupportedWidePage"] = memento.IsSupportedWidePage;
                if (old is not null) items.Remove(old);
                items.Insert(0, item);
                Object(config, "StartUp")["LastBookV2"] = new JsonObject { ["Path"] = book.Path, ["Page"] = memento.Page, ["Props"] = item["Props"]!.DeepClone() };
            }
            // 保留原 Format；新文件使用原名称和版本结构，避免添加另一套存储格式。
            _setting["Format"] ??= JsonValue.Create("NeeView.UserSetting/46.3.0");
            _history["Format"] ??= JsonValue.Create("NeeView.History/46.3.0");
            await WritePairAsync(token);
        }
        finally { _gate.Release(); }
    }

    /// <summary>读取单个 JSON 文件；无法解析时传播错误，保留原文件。</summary>
    private async Task<JsonObject> ReadAsync(string name, CancellationToken token)
    {
        var path = System.IO.Path.Combine(DirectoryPath, name);
        if (!File.Exists(path)) return new();
        await using var stream = File.OpenRead(path);
        return (await JsonNode.ParseAsync(stream, documentOptions: new() { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip }, cancellationToken: token))?.AsObject() ?? new();
    }
    /// <summary>反序列化已迁移分支，缺失字段保留原默认值。</summary>
    private static T ReadBranch<T>(JsonObject root, string name) where T : class, new() => root[name]?.Deserialize<T>(Options) ?? new();
    /// <summary>获取或创建对象分支；已有未知字段保持原节点。</summary>
    private static JsonObject Object(JsonObject root, string name)
    {
        if (root[name] is JsonObject obj) return obj;
        var created = new JsonObject(); root[name] = created; return created;
    }
    /// <summary>递归更新已知字段，原节点中的其他字段不会被删除。</summary>
    private static void Merge(JsonObject target, JsonObject source)
    {
        foreach (var pair in source)
            if (pair.Value is JsonObject nested) Merge(Object(target, pair.Key), nested);
            else target[pair.Key] = pair.Value?.DeepClone();
    }
    /// <summary>按原 Props 标记集合分离未知 token，便于后续迁移。</summary>
    private static string FilterProps(string? props, bool known)
    {
        var names = new HashSet<string> { "SinglePage", "WidePage", "RightToLeft", "LeftToRight", "IsDivide", "IsSingleFirst", "IsSingleLast", "IsWide", "IsRecursive", "Sort", "Rot", "Base", "Seed", "Fx" };
        return string.Join(' ', (props ?? "").Split([' ', ','], StringSplitOptions.RemoveEmptyEntries).Where(e => names.Contains(e.Split('=')[0]) == known));
    }
    /// <summary>先写完两个临时文件，再保留回滚副本；记录存在时启动恢复旧完整状态。</summary>
    private async Task WritePairAsync(CancellationToken token)
    {
        Directory.CreateDirectory(DirectoryPath);
        string[] names = ["History.json", "UserSetting.json"];
        var marker = System.IO.Path.Combine(DirectoryPath, ".save-pending.json");
        try
        {
            // 准备阶段不改动权威文件；任一序列化或权限失败均可直接重试。
            await WriteTemporaryAsync(names[0], _history, token);
            await WriteTemporaryAsync(names[1], _setting, token);
            var previous = new JsonObject();
            foreach (var name in names)
            {
                var path = System.IO.Path.Combine(DirectoryPath, name);
                previous[name] = File.Exists(path);
                if (File.Exists(path)) File.Copy(path, path + ".save-backup", true);
            }
            await WriteTemporaryAsync(".save-pending.json", previous, token);
            File.Move(marker + ".tmp", marker, true);
            // 提交阶段不接受中途取消；异常由副本回滚，崩溃由下次 Load 恢复。
            foreach (var name in names)
            {
                var path = System.IO.Path.Combine(DirectoryPath, name);
                File.Move(path + ".tmp", path, true);
            }
            File.Delete(marker);
        }
        catch { RecoverInterruptedSave(); throw; }
        finally
        {
            foreach (var name in names.Append(".save-pending.json"))
            {
                var path = System.IO.Path.Combine(DirectoryPath, name);
                if (File.Exists(path + ".tmp")) File.Delete(path + ".tmp");
                // 回滚失败时必须保留副本和标记，不能在 finally 删除唯一恢复材料。
                if (!File.Exists(marker) && File.Exists(path + ".save-backup")) File.Delete(path + ".save-backup");
            }
        }
    }
    /// <summary>恢复未完成的双文件保存；未知或损坏标记传播错误，保留恢复材料。</summary>
    private void RecoverInterruptedSave()
    {
        var marker = System.IO.Path.Combine(DirectoryPath, ".save-pending.json");
        if (!File.Exists(marker)) return;
        var previous = JsonNode.Parse(File.ReadAllText(marker))!.AsObject();
        foreach (var name in new[] { "History.json", "UserSetting.json" })
        {
            var path = System.IO.Path.Combine(DirectoryPath, name);
            if (previous[name]!.GetValue<bool>()) File.Copy(path + ".save-backup", path, true);
            else if (File.Exists(path)) File.Delete(path);
        }
        File.Delete(marker);
    }
    /// <summary>写入同目录临时 JSON 并刷新到磁盘；不替换权威文件。</summary>
    private async Task WriteTemporaryAsync(string name, JsonObject value, CancellationToken token)
    {
        var temp = System.IO.Path.Combine(DirectoryPath, name) + ".tmp";
        await using var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous);
        await JsonSerializer.SerializeAsync(stream, value, Options, token);
        await stream.FlushAsync(token); stream.Flush(true);
    }
}
