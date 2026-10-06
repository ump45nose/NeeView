using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
namespace NeeView;

/// <summary>原五文件的只读预览；保留原树和差分，实际应用通过独立候选接入 SaveData 事务。</summary>
public sealed partial class ProfileImportService(IProfileImportReader reader, IReadOnlyList<CommandDefinition> definitions, IReadOnlySet<string> available)
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
            "已支持版本先按原规则升级再展示；未知设置能力完整保留，不代表已有执行入口。缺失差分使用原默认值。",
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
            var before = paths.Count;
            var format = String(raw, "Format") ?? "未声明版本";
            CheckFormat(name, format, raw);
            if (ProfileImportCompatibility.BlockReason(name, raw) is null)
            {
                raw = ProfileImportCompatibility.Upgrade(name, raw);
                if (format != String(raw, "Format")) notices.Add(name + " 已按原版本规则升级候选：" + format + " → " + String(raw, "Format") + "；来源只读。");
            }
            docs.Add(name, raw);
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
                    Map(Object(config, "Theme"), "CustomThemeFolder", "Config.Theme");
                    Map(Object(config, "Playlist"), "PlaylistFolder", "Config.Playlist");
                    Map(Object(config, "Playlist"), "CurrentPlaylist", "Config.Playlist");
                    Map(Object(Object(config, "Book"), "ExportImageParameter"), "ExportFolder", "Config.Book.ExportImageParameter");
                    Map(Object(Object(config, "Book"), "ExportBookParameter"), "ExportFolder", "Config.Book.ExportBookParameter");
                    if (Object(raw, "Commands")?["ExportImage"] is JsonObject directExport)
                    {
                        var parameter = Object(directExport, "Parameter");
                        Map(Object(parameter, "Value") ?? parameter, "ExportFolder", "Commands.ExportImage.Parameter");
                    }
                    if (raw["MacImportedLegacyEffectUpgrade"]?.ToString() == "Layers/1")
                        notices.Add("旧 ImageEffect 已按原规则转换为效果层、参数缓存和默认预设，原材料保留；Level/Hsv/ColorSelect/Colorize 已接入；其余效果逐项标记待迁。");
                    else if (raw["MacImportedLegacyEffectFormat"] is not null && config?["ImageEffect"]?["Layers"] is null)
                        notices.Add("旧 ImageEffect 尚未转换，原参数与缓存完整保留；Level/Hsv/ColorSelect/Colorize 已接入；其余效果逐项标记待迁。" + raw["MacImportedLegacyEffectIssue"]?.ToString());
                    else if (config?["ImageEffect"] is not null || config?["EffectProfiles"] is not null || config?["ImageEffectCache"] is not null)
                        notices.Add("现代效果层、缓存和预设数据保持，未知类型不改写；Level/Hsv/ColorSelect/Colorize 已接入；其余效果逐项标记待迁。");
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
                // 原目录缩略目标可以是绝对文件或相对书内页；仅绝对Windows目标由映射器转换。
                if (name == "Foldres.json" && Object(node, "Thumbs") is { } thumbs)
                    foreach (var key in thumbs.Where(p => p.Value is JsonValue value && value.TryGetValue<string>(out _)).Select(p => p.Key).ToArray())
                    { token.ThrowIfCancellationRequested(); Map(thumbs, key, field + ".Thumbs"); }
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
        if (setting?["ContextMenu"] is not null) notices.Add("ContextMenu 原树与版本命令改名保留；自定义菜单的实际显示/执行仍需按能力清单核对。");
        if (setting?["SusiePlugins"] is not null) notices.Add("SusiePlugins 原配置保留；Windows 专属插件不能在 Mac 执行。");
        if (setting?["DragActions"] is not null) notices.Add("DragActions 原配置及已核对的版本参数升级保留；自定义拖动执行尚未接入。");
        foreach (var pair in setting ?? new JsonObject())
            if (pair.Key is not ("Format" or "Config" or "Commands" or "ContextMenu" or "SusiePlugins" or "DragActions" or
                "MacImportedSourceFormat" or "MacImportedLegacyEffectFormat" or "MacImportedLegacyEffectUpgrade" or "MacImportedLegacyEffectIssue" or "MacImportedLegacyImageEffects" or "MacImportedLegacyCommands" or "MacImportedLegacyDragActions"))
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
            notices.Add("检测到 History.Folders（原 FoldersLegacy），按原字典转换恢复；默认排序在最终配置确定后归一，不套用独立文件递归升级。");
        if (docs.GetValueOrDefault("Bookmark.json")?["QuickAccess"] is not null && !docs.ContainsKey("QuicAccess.json"))
            notices.Add("检测到 Bookmark.QuickAccess（原 QuickAccessLegacy），保留内嵌Format并单独校验，未知/未来版本不能实际应用。");
        var assets = PreviewAssets(bundle, mapper, paths, notices, token);
        foreach (var extra in bundle.ExtraEntries) notices.Add("附属项未导入：" + extra + "（脚本不执行）");
        return new(docs, summaries.AsReadOnly(), paths.AsReadOnly(), commands.AsReadOnly(), notices.AsReadOnly(), assets.Bytes, assets.Summaries);

        // 仅报告现有配置投影外字段，不建立第二套设置 schema，也不把字段存在当作功能通过。
        void ReportSettings(JsonObject? node, Type type, string field, int depth)
        {
            if (node is null) return;
            // 动态材料没有配置属性 schema；JsonObject 的两个 Item 索引器也不是设置字段。
            if (typeof(JsonNode).IsAssignableFrom(type))
            {
                if (node.Count > 0) notices.Add("动态配置材料保留，执行兼容逐项核对：" + field);
                return;
            }
            var properties = type.GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
                .Where(p => p.GetIndexParameters().Length == 0 && p.GetCustomAttributes(typeof(JsonIgnoreAttribute), true).OfType<JsonIgnoreAttribute>().All(a => a.Condition != JsonIgnoreCondition.Always))
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
