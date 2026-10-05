using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
namespace NeeView;

/// <summary>原五文件的只读预览；保留原树和差分，实际应用通过独立候选接入 SaveData 事务。</summary>
public sealed class ProfileImportService(IProfileImportReader reader, IReadOnlyList<CommandDefinition> definitions, IReadOnlySet<string> available)
{
    private readonly SemaphoreSlim _gate = new(1);
    /// <summary>有界读取后后台解析；同一服务串行，取消/旧请求不接触当前配置。</summary>
    /// <param name="source">只读来源。</param><param name="mappings">本次用户路径映射快照。</param><param name="token">关闭或换来源取消。</param>
    /// <returns>包含完整未知字段的候选，以及可展示的兼容报告。</returns>
    public async Task<ProfileImportPreview> PreviewAsync(ProfileImportSource source, IEnumerable<ProfilePathMapping> mappings, CancellationToken token = default)
    {
        var mapper = new ProfilePathMapper(mappings); // 在读取前拒绝有歧义的映射。
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            var bundle = await reader.ReadAsync(source, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            return await Task.Run(() => CreatePreview(bundle, mapper, token), token).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }
    private ProfileImportPreview CreatePreview(ProfileImportBundle bundle, ProfilePathMapper mapper, CancellationToken token)
    {
        var docs = new Dictionary<string, JsonObject>(); var paths = new List<ProfileImportPath>(); int nodeCount = 0;
        var summaries = new List<ProfileImportFileSummary>(); var notices = new List<string>
        {
            "本次仅预览，没有修改当前设置、历史、书签或来源文件。",
            "可应用版本按文件单独校验；完整旧设置升级、未知设置能力及旧布局转换仍待迁移。缺失差分使用原默认值。",
            "Page 保留为原条目名，Props 与未知字段保留；归档内部页名不作为物理文件路径映射。"
        };
        foreach (var name in ProfileImportFiles.Names)
        {
            token.ThrowIfCancellationRequested();
            if (!bundle.Files.TryGetValue(name, out var text)) { summaries.Add(new(name, false, "", 0)); continue; }
            JsonObject raw;
            try { raw = JsonNode.Parse(text, documentOptions: new() { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip, MaxDepth = 64 }) as JsonObject
                    ?? throw new InvalidDataException(name + " 的根必须是对象。"); }
            catch (JsonException ex) { throw new InvalidDataException(name + " JSON 损坏：" + ex.Message, ex); }
            docs.Add(name, raw); var before = paths.Count;
            var format = String(raw, "Format") ?? "未声明版本";
            CheckFormat(name, format, raw);
            switch (name)
            {
                case "History.json":
                    Walk(Array(raw, "Items"), "Items", "Path"); Walk(Array(raw, "Books"), "Books", "Path");
                    // 原 [JsonPropertyName("Folders")] 是路径键字典，不是 CLR 属性名 FoldersLegacy。
                    if (Object(raw, "Folders") is { } legacyFolders)
                        foreach (var pair in legacyFolders.ToArray())
                        {
                            token.ThrowIfCancellationRequested(); var mapped = mapper.Map(pair.Key);
                            AddPath("Folders[" + pair.Key + "]", pair.Key, mapped, null);
                            if (mapped.Status != ProfilePathStatus.Mapped) continue;
                            if (mapped.Path != pair.Key && legacyFolders.ContainsKey(mapped.Path)) throw new InvalidDataException("旧目录路径映射后重名：" + mapped.Path);
                            legacyFolders.Remove(pair.Key); legacyFolders.Add(mapped.Path, pair.Value);
                        }
                    break;
                case "Bookmark.json":
                    if (Object(raw, "Nodes") is { } root) WalkNode(root, "Nodes", "Path");
                    Walk(Array(raw, "Books"), "Books", "Path");
                    // 原 [JsonPropertyName("QuickAccess")] 包含 QuickAccessCollectionMemento.Items。
                    Walk(Array(Object(raw, "QuickAccess"), "Items"), "QuickAccess.Items", "Path"); break;
                case "Foldres.json": Walk(Array(raw, "Folders"), "Folders", "Place"); break;
                case "QuicAccess.json": Walk(Array(raw, "Items"), "Items", "Path"); break;
                case "UserSetting.json":
                    var config = Object(raw, "Config"); var start = Object(config, "StartUp");
                    foreach (var field in new[] { "LastBookV2", "LastBook", "LastFolder", "LastBookmarkFolder" })
                    { var node = Object(start, field); Map(node, "Path", "Config.StartUp." + field); Map(node, "Select", "Config.StartUp." + field); }
                    Map(start, "LastFolderPath", "Config.StartUp");
                    foreach (var field in new[] { "DestinationFolderCollection", "DestinationFodlerCollection" })
                        Walk(Array(Object(config, "System"), field), "Config.System." + field, "Path");
                    Map(Object(config, "Playlist"), "PlaylistFolderRaw", "Config.Playlist");
                    break;
            }
            summaries.Add(new(name, true, format, paths.Count - before));

            // 已知集合字段按原顺序遍历；不对任意未知字符串做全局替换。
            void Walk(JsonArray? items, string field, string pathKey)
            {
                if (items is null) return;
                for (var i = 0; i < items.Count; i++)
                {
                    token.ThrowIfCancellationRequested();
                    if (items[i] is not JsonObject node) throw new InvalidDataException($"{name}:{field}[{i}] 必须是对象。");
                    WalkNode(node, $"{field}[{i}]", pathKey);
                }
            }
            void WalkNode(JsonObject node, string field, string pathKey)
            {
                token.ThrowIfCancellationRequested();
                if (++nodeCount > ProfileImportFiles.MaxRecords) throw new InvalidDataException("导入记录数量超限。");
                Map(node, pathKey, field); Walk(Array(node, "Children"), field + ".Children", pathKey);
            }
            void Map(JsonObject? node, string key, string field)
            {
                var original = String(node, key); if (string.IsNullOrEmpty(original)) return;
                var mapped = mapper.Map(original); var page = String(node, "Page");
                AddPath(field + "." + key, original, mapped, page);
                if (mapped.Status == ProfilePathStatus.Mapped) node![key] = mapped.Path;
            }
            void AddPath(string field, string original, (string Path, ProfilePathStatus Status) mapped, string? page)
            {
                if (paths.Count >= ProfileImportFiles.MaxRecords) throw new InvalidDataException("导入路径记录数量超限。");
                paths.Add(new(name, field, original, mapped.Path, mapped.Status, page));
            }
        }
        var setting = docs.GetValueOrDefault("UserSetting.json"); var commandConfig = new CommandConfig();
        ReportSettings(Object(setting, "Config"), typeof(Config), "Config", 0);
        foreach (var section in new[] { "ContextMenu", "SusiePlugins", "DragActions" })
            if (setting?[section] is not null) notices.Add(section + " 的原配置保留；自定义菜单/Windows 插件/拖动动作的兼容尚未核对。");
        foreach (var pair in setting ?? new JsonObject())
            if (pair.Key is not ("Format" or "Config" or "Commands" or "ContextMenu" or "SusiePlugins" or "DragActions"))
            {
                token.ThrowIfCancellationRequested(); CheckRecordBudget();
                notices.Add("未知 UserSetting 字段保留，兼容待核对：" + pair.Key);
            }
        try
        {
            var options = new JsonSerializerOptions(); options.Converters.Add(new JsonStringEnumConverter());
            commandConfig = Object(Object(setting, "Config"), "Command")?.Deserialize<CommandConfig>(options) ?? new();
            if (!Enum.IsDefined(commandConfig.PresetInputScheme) || !Enum.IsDefined(commandConfig.PresetPageReadOrder))
                throw new JsonException("未识别的输入方案枚举值。");
        }
        catch (JsonException) { commandConfig = new(); notices.Add("输入方案含未识别值；缺省键位暂按原默认方案展示，原字段仍完整保留。"); }
        var commandNodes = Object(setting, "Commands"); var commands = new List<ProfileImportCommand>();
        if ((commandNodes?.Count ?? 0) > ProfileImportFiles.MaxRecords - nodeCount) throw new InvalidDataException("导入命令/记录数量超限。");
        var knownNames = definitions.Select(d => d.Name).ToHashSet(StringComparer.Ordinal);
        foreach (var definition in definitions)
        {
            token.ThrowIfCancellationRequested(); var entry = Object(commandNodes, definition.Name);
            var shortcut = String(entry, "ShortCutKey");
            commands.Add(new(definition.Name, shortcut ?? DefaultInputScheme.GetShortcut(definition.Name, definition.Shortcut, commandConfig),
                shortcut is not null, available.Contains(definition.Name) ? "已有执行入口，参数兼容待核对" : "未迁移，保留配置占位"));
        }
        foreach (var pair in commandNodes ?? new JsonObject())
            if (!knownNames.Contains(pair.Key))
            {
                token.ThrowIfCancellationRequested();
                commands.Add(new(pair.Key, pair.Value is JsonObject node ? String(node, "ShortCutKey") ?? "" : "", true, "未知/脚本命令，保留且不执行"));
                notices.Add("未支持的命令保留：" + pair.Key);
            }
        if (paths.Any(p => p.Status == ProfilePathStatus.Unmapped)) notices.Add($"{paths.Count(p => p.Status == ProfilePathStatus.Unmapped)} 条 Windows 路径未映射，保留原值，可补充映射后重新预览。");
        if (docs.GetValueOrDefault("History.json")?["Folders"] is not null && !docs.ContainsKey("Foldres.json"))
            notices.Add("检测到 History.Folders（原 FoldersLegacy），后续应用需沿原导入顺序恢复旧目录参数。");
        if (docs.GetValueOrDefault("Bookmark.json")?["QuickAccess"] is not null && !docs.ContainsKey("QuicAccess.json"))
            notices.Add("检测到 Bookmark.QuickAccess（原 QuickAccessLegacy），后续应用需恢复旧快速访问。");
        foreach (var extra in bundle.ExtraEntries) notices.Add("附属项未导入：" + extra + "（脚本不执行）");
        return new(docs, summaries.AsReadOnly(), paths.AsReadOnly(), commands.AsReadOnly(), notices.AsReadOnly());

        // 仅报告现有配置投影外字段，不建立第二套设置 schema，也不把字段存在当作功能通过。
        void ReportSettings(JsonObject? node, Type type, string field, int depth)
        {
            if (node is null) return;
            var properties = type.GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
                .Where(p => p.GetCustomAttributes(typeof(JsonIgnoreAttribute), true).OfType<JsonIgnoreAttribute>().All(a => a.Condition != JsonIgnoreCondition.Always))
                .ToDictionary(p => p.GetCustomAttributes(typeof(JsonPropertyNameAttribute), true).OfType<JsonPropertyNameAttribute>().FirstOrDefault()?.Name ?? p.Name);
            foreach (var pair in node)
            {
                token.ThrowIfCancellationRequested(); CheckRecordBudget(); var location = field + "." + pair.Key;
                if (!properties.TryGetValue(pair.Key, out var property))
                {
                    // 原启动快照仍由 SaveData 的原 JSON 直接读取，没有独立 Config 属性。
                    if (field == "Config.StartUp" && pair.Key is "LastBookV2" or "LastBook" or "LastFolderPath") continue;
                    notices.Add("配置投影外字段保留，兼容待核对：" + location);
                }
                else if (depth < 2 && pair.Value is JsonObject child) ReportSettings(child, property.PropertyType, location, depth + 1);
            }
        }
        void CheckRecordBudget()
        { if (++nodeCount > ProfileImportFiles.MaxRecords) throw new InvalidDataException("导入记录数量超限。"); }
        void CheckFormat(string file, string format, JsonObject raw)
        {
            if (ProfileImportCompatibility.BlockReason(file, raw) is { } reason) { notices.Add(reason); return; }
            var parts = format.Split('/');
            if (parts.Length != 2 || !(parts[0] == "NeeView" || parts[0].StartsWith("NeeView.", StringComparison.Ordinal)) || !Version.TryParse(parts[1], out var version))
                notices.Add(file + " 版本缺失或未识别，仅保留并预览。");
            else if (version.Major != 46 || version.Minor != 3) notices.Add(file + " 版本 " + parts[1] + " 按文件核对旧版本兼容，应用时再次校验。");
        }
    }
    private static JsonObject? Object(JsonObject? root, string name) => root?[name] switch
    { null => null, JsonObject value => value, _ => throw new InvalidDataException(name + " 必须是对象。") };
    private static JsonArray? Array(JsonObject? root, string name) => root?[name] switch
    { null => null, JsonArray value => value, _ => throw new InvalidDataException(name + " 必须是数组。") };
    private static string? String(JsonObject? root, string name) => root?[name] switch
    { null => null, JsonValue value when value.TryGetValue<string>(out var text) => text, _ => throw new InvalidDataException(name + " 必须是字符串。") };
}
