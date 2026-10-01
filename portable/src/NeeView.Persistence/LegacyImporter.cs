using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using NeeView.Application;
using NeeView.Core;

namespace NeeView.Persistence;

/// <summary>旧 nvzip/Profile 只读导入，独立 DTO、预览、备份和原子恢复。</summary>
public sealed class LegacyImporter(SqliteStateStore states, JsonSettingsStore settings) : ILegacyImporter
{
    private readonly SemaphoreSlim _gate = new(1);
    private string RecoveryPath => settings.Path + ".import-recovery.json";
    private sealed record ImportRecovery(string Batch, string PreviousSettings, string BackupDatabase);
    /// <summary>按最长 Windows 前缀映射路径；归档 Page 字符串独立保留。</summary>
    public static string MapPath(string path, IReadOnlyList<PathMapping> mappings)
    {
        var normalized = path.Replace('\\', '/');
        foreach (var mapping in mappings.OrderByDescending(m => m.WindowsPrefix.Length))
        {
            var prefix = mapping.WindowsPrefix.Replace('\\', '/').TrimEnd('/');
            if (!normalized.Equals(prefix, StringComparison.OrdinalIgnoreCase)
                && !normalized.StartsWith(prefix + "/", StringComparison.OrdinalIgnoreCase)) continue;
            return mapping.MacPrefix.TrimEnd('/') + normalized[prefix.Length..];
        }
        return path;
    }
    /// <summary>解析真实 Props 令牌，保留未知项到报告。</summary>
    public static ReaderOptions ParseProps(string? props, ICollection<string>? warnings = null)
    {
        var options = new ReaderOptions();
        foreach (var token in (props ?? "").Split([' ', ','], StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = token.Split('=', 2); var key = parts[0]; var value = parts.ElementAtOrDefault(1);
            options = key switch
            {
                "SinglePage" => options with { DoublePage = false },
                "WidePage" => options with { DoublePage = true },
                "RightToLeft" => options with { Direction = ReadDirection.RightToLeft },
                "LeftToRight" => options with { Direction = ReadDirection.LeftToRight },
                "IsDivide" => options with { DivideWide = true },
                "IsSingleFirst" => options with { SingleFirst = true },
                "IsSingleLast" => options with { SingleLast = true },
                "IsWide" => options with { WidePage = true },
                "Sort" when LegacyDefaults.Sort(value) is { } mode => options with { Sort = mode },
                "Base" when double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var zoom) => options with { Zoom = Math.Clamp(zoom, 0.1, 8) },
                "Seed" when int.TryParse(value, out var seed) => options with { RandomSeed = seed },
                _ => Unknown(options, token, warnings)
            };
        }
        return options;
    }
    private static ReaderOptions Unknown(ReaderOptions options, string token, ICollection<string>? warnings)
    { warnings?.Add($"未支持的阅读属性：{token}"); return options; }
    /// <summary>读取受限大小的旧 JSON，不修改源文件，不执行脚本。</summary>
    public async Task<ImportPlan> PlanImportAsync(string path, IReadOnlyList<PathMapping> mappings, CancellationToken token = default)
    {
        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); var warnings = new List<string>();
        var names = new[] { "UserSetting.json", "History.json", "Bookmark.json", "Foldres.json", "QuicAccess.json" };
        if (Directory.Exists(path))
        {
            foreach (var name in names)
            {
                var file = Path.Combine(path, name); if (!File.Exists(file)) continue;
                if (new FileInfo(file).Length > 16 * 1024 * 1024) throw new InvalidDataException("旧配置超出 16 MiB 限制。");
                files[name] = await File.ReadAllTextAsync(file, token);
            }
        }
        else
        {
            using var archive = ZipFile.OpenRead(path);
            foreach (var entry in archive.Entries)
            {
                token.ThrowIfCancellationRequested();
                if (!names.Contains(entry.FullName, StringComparer.OrdinalIgnoreCase)) { if (entry.FullName.StartsWith("Scripts", StringComparison.OrdinalIgnoreCase)) warnings.Add($"脚本仅保留在源中，不执行：{entry.FullName}"); continue; }
                if (entry.Length > 16 * 1024 * 1024) throw new InvalidDataException("旧配置超出 16 MiB 限制。");
                using var stream = entry.Open(); using var reader = new StreamReader(stream); files[entry.FullName] = await reader.ReadToEndAsync(token);
            }
        }
        if (files.Count == 0) throw new InvalidDataException("来源没有 NeeView 配置文件。");
        var books = new List<ImportBook>(); var marks = new List<ImportBookmark>(); var current = await settings.LoadAsync(token);
        if (files.TryGetValue("History.json", out var historyJson))
        {
            using var history = JsonDocument.Parse(historyJson);
            foreach (var item in Enumerate(history.RootElement, "Items", "Books"))
            {
                if (String(item, "Path") is not { } original) { warnings.Add("历史项缺少 Path。"); continue; }
                var mapped = MapPath(original, mappings);
                if (mapped == original && original.Contains(':')) warnings.Add($"路径未映射，记录保留：{original}");
                var page = LegacyPage(item, warnings);
                var access = DateTimeOffset.TryParse(String(item, "LastAccessTime"), out var time) ? time : DateTimeOffset.UtcNow;
                books.Add(new(mapped, page, ParseProps(String(item, "Props"), warnings), access));
            }
        }
        if (files.TryGetValue("Bookmark.json", out var bookmarkJson))
        {
            using var bookmarks = JsonDocument.Parse(bookmarkJson);
            if (bookmarks.RootElement.TryGetProperty("Nodes", out var root)) Walk(root, null, 0, "root");
            else foreach (var item in Enumerate(bookmarks.RootElement, "Books")) Walk(item, null, marks.Count, $"legacy-{marks.Count}");
        }
        void Walk(JsonElement node, string? parent, int order, string treePath)
        {
            var id = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(path + ":" + treePath)))[..32];
            var original = String(node, "Path"); var mapped = original is null ? null : MapPath(original, mappings);
            marks.Add(new(id, parent, order, String(node, "Name") ?? (original is null ? "文件夹" : Path.GetFileName(original.Replace('\\', '/'))), mapped,
                LegacyPage(node, warnings), ParseProps(String(node, "Props"), warnings)));
            if (node.TryGetProperty("Children", out var children) && children.ValueKind == JsonValueKind.Array)
            { var index = 0; foreach (var child in children.EnumerateArray()) { Walk(child, id, index, treePath + "/" + index); index++; } }
        }
        if (files.TryGetValue("UserSetting.json", out var userJson))
        {
            using var user = JsonDocument.Parse(userJson);
            // 差分文件省略的命令也要使用 Windows 默认；仅新平台命令沿用当前值。
            var portableOnly = current.Shortcuts.Where(binding => binding.Command is "Close" or "Quit" or "Bookmark" or "Reveal" or "Trash" or "Rename");
            current = current with { Shortcuts = [.. portableOnly, .. LegacyDefaults.Bindings(user.RootElement)] };
            if (user.RootElement.TryGetProperty("Commands", out var commands) && commands.ValueKind == JsonValueKind.Object)
            {
                var bindings = current.Shortcuts.ToList();
                foreach (var command in commands.EnumerateObject())
                {
                    if (command.Value.ValueKind != JsonValueKind.Object) { warnings.Add($"命令记录格式异常：{command.Name}"); continue; }
                    if (!CommandCatalog.Supported.Contains(command.Name)) { warnings.Add($"未支持命令：{command.Name} = {command.Value.GetRawText()}"); continue; }
                    // null/缺失恢复原默认，空字符串才表示禁用；Parameter 对默认键位也生效。
                    var existing = bindings.Where(binding => CommandCatalog.Resolve(binding.Command) == CommandCatalog.Resolve(command.Name)).ToArray();
                    bindings.RemoveAll(binding => CommandCatalog.Resolve(binding.Command) == CommandCatalog.Resolve(command.Name));
                    var shortcut = String(command.Value, "ShortCutKey") ?? string.Join(',', existing.Select(binding => binding.Gesture));
                        foreach (var gesture in shortcut.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                            bindings.Add(new(gesture, command.Name, command.Value.TryGetProperty("Parameter", out var parameter) ? parameter.ValueKind == JsonValueKind.String ? parameter.GetString() : parameter.ValueKind == JsonValueKind.Null ? null : parameter.GetRawText() : null));
                    foreach (var field in command.Value.EnumerateObject().Where(p => p.Name is "TouchGesture" or "MouseGesture")) warnings.Add($"手势暂未导入：{command.Name}.{field.Name}={field.Value}");
                }
                current = current with { Shortcuts = bindings };
            }
            if (user.RootElement.TryGetProperty("Config", out var config))
            {
                if (config.TryGetProperty("BookSetting", out var reading)) current = current with { Defaults = ReadLegacyOptions(reading, new(), warnings) };
                if (config.TryGetProperty("BookSettingPolicy", out var policies)) current = current with { RestorePolicies = ReadLegacyPolicies(policies, warnings) };
                if (config.TryGetProperty("System", out var system) && system.TryGetProperty("DestinationFolderCollection", out var destinations) && destinations.ValueKind == JsonValueKind.Array)
                    current = current with { DestinationFolders = destinations.EnumerateArray().Where(p => p.ValueKind == JsonValueKind.String).Select(p => MapPath(p.GetString()!, mappings)).ToList() };
                if (config.TryGetProperty("Panels", out var panels))
                {
                    current = current with { CopyMode = Bool(panels, "IsDestinationFolderCopyMode", current.CopyMode),
                        AutoRefreshDestinations = Bool(panels, "IsDestinationFolderAutoRefreshEnabled", true),
                        MoveHistoryCapacity = Math.Clamp(Int(panels, "DestinationMoveHistoryCapacity", 300), 0, 1000),
                        DestinationRatio = Number(panels, "DestinationFolderSectionRatio", 0.5) };
                }
                foreach (var field in config.EnumerateObject().Where(p => p.Name is not ("BookSetting" or "BookSettingPolicy" or "System" or "Panels"))) warnings.Add($"未支持配置节，保留在源中：{field.Name}");
            }
        }
        if (files.ContainsKey("Foldres.json")) warnings.Add("已识别 Foldres.json；文件夹面板的 Windows 专属配置暂未导入。");
        if (files.ContainsKey("QuicAccess.json")) warnings.Add("已识别 QuicAccess.json；快速访问暂未导入。");
        warnings.AddRange(CommandCatalog.Conflicts(current.Shortcuts).Select(c => "快捷键冲突：" + c));
        var digest = string.Join('\n', files.OrderBy(p => p.Key).Select(p => p.Key + p.Value)) + StoreJson.Serialize(mappings);
        var batch = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(digest)));
        return new(batch, path, books, marks, current, warnings.Distinct().ToArray());
    }
    /// <summary>原版缺失配置字段使用原始默认语义；未知阅读字段报告。</summary>
    private static ReaderOptions ReadLegacyOptions(JsonElement element, ReaderOptions defaults, ICollection<string> warnings)
    {
        var known = new HashSet<string>(["PageMode", "BookReadOrder", "IsSupportedDividePage", "IsSupportedSingleFirstPage", "IsSupportedSingleLastPage", "IsSupportedWidePage", "SortMode", "BaseScale", "Page"]);
        foreach (var field in element.EnumerateObject()) if (!known.Contains(field.Name)) warnings.Add($"未支持阅读设置：{field.Name}={field.Value}");
        return defaults with { DoublePage = EnumValue(element, "PageMode", ["SinglePage", "WidePage"]) is "WidePage", Direction = EnumValue(element, "BookReadOrder", ["RightToLeft", "LeftToRight"]) == "LeftToRight" ? ReadDirection.LeftToRight : ReadDirection.RightToLeft,
            DivideWide = Bool(element, "IsSupportedDividePage", false), SingleFirst = Bool(element, "IsSupportedSingleFirstPage", false),
            SingleLast = Bool(element, "IsSupportedSingleLastPage", false), WidePage = Bool(element, "IsSupportedWidePage", true),
            Sort = element.TryGetProperty("SortMode", out var sorting) ? LegacyDefaults.Sort(sorting.ToString()) ?? SortMode.Entry : SortMode.Entry, Zoom = Number(element, "BaseScale", 1) };
    }
    /// <summary>旧枚举支持名字和固定基线的数值写法；未知值不伪造含义。</summary>
    private static string? EnumValue(JsonElement value, string key, string[] names)
    {
        if (!value.TryGetProperty(key, out var property)) return null;
        if (property.ValueKind == JsonValueKind.String) return property.GetString();
        if (property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out var index) && index >= 0 && index < names.Length) return names[index];
        return null;
    }
    /// <summary>旧 Page 是内容定位字符串；数字页码不直接转成阅读锚点，报告原值。</summary>
    private static string? LegacyPage(JsonElement item, ICollection<string> warnings)
    {
        if (String(item, "Page") is { } page) return page.Replace('\\', '/');
        foreach (var name in new[] { "Page", "Index" })
            if (item.TryGetProperty(name, out var value) && value.ValueKind != JsonValueKind.Null) warnings.Add($"旧定位无法确认语义，未转换为页码：{name}={value.GetRawText()}");
        return null;
    }
    /// <summary>迁移逐字段恢复策略，忽略只用于旧原生功能的字段并留下报告。</summary>
    private static Dictionary<string, RestorePolicy> ReadLegacyPolicies(JsonElement element, ICollection<string> warnings)
    {
        var fields = new Dictionary<string, string> { ["PageMode"] = "DoublePage", ["BookReadOrder"] = "Direction", ["IsSupportedDividePage"] = "DivideWide",
            ["IsSupportedSingleFirstPage"] = "SingleFirst", ["IsSupportedSingleLastPage"] = "SingleLast", ["IsSupportedWidePage"] = "WidePage", ["SortMode"] = "Sort", ["BaseScale"] = "Zoom" };
        var result = new Dictionary<string, RestorePolicy>();
        foreach (var field in element.EnumerateObject())
        {
            var text = EnumValue(element, field.Name, ["Default", "Continue", "RestoreOrDefault", "RestoreOrContinue", "RestoreOrDefaultReset"]);
            if (fields.TryGetValue(field.Name, out var target) && Enum.TryParse<RestorePolicy>(text, out var policy)) result[target] = policy;
            else warnings.Add($"未支持恢复策略：{field.Name}={field.Value}");
        }
        return result;
    }
    /// <summary>单图书籍归一到目录，保留图片名作为 Page；未映射 Windows 路径仍只读保留。</summary>
    private static (string Path, string? Page) NormalizeBook(string path, string? page)
    {
        if (path.Contains('\\') || path.Contains(':')) return (path, page);
        var images = new[] { ".jpg", ".jpeg", ".png", ".webp", ".bmp", ".tif", ".tiff", ".gif" };
        return images.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase)
            ? (Path.GetDirectoryName(path)!, Path.GetFileName(path)) : (path, page);
    }
    private static IEnumerable<JsonElement> Enumerate(JsonElement root, params string[] keys)
    { foreach (var key in keys) if (root.TryGetProperty(key, out var values) && values.ValueKind == JsonValueKind.Array) return values.EnumerateArray().ToArray(); return []; }
    private static string? String(JsonElement value, string key) => value.TryGetProperty(key, out var property) && property.ValueKind == JsonValueKind.String ? property.GetString() : null;
    private static bool Bool(JsonElement value, string key, bool fallback) => value.TryGetProperty(key, out var property) && property.ValueKind is JsonValueKind.True or JsonValueKind.False ? property.GetBoolean() : fallback;
    private static int Int(JsonElement value, string key, int fallback) => value.TryGetProperty(key, out var property) && property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out var number) ? number : fallback;
    private static double Number(JsonElement value, string key, double fallback) => value.TryGetProperty(key, out var property) && property.ValueKind == JsonValueKind.Number && property.TryGetDouble(out var number) && double.IsFinite(number) ? number : fallback;
    /// <summary>应用选定计划；数据库事务与设置恢复清单支持异常和进程中断。</summary>
    public async Task ApplyAsync(ImportPlan plan, CancellationToken token = default)
    {
        await _gate.WaitAsync(token);
        try
        {
            if (await BatchExistsAsync(plan.Id, token)) throw new InvalidOperationException("该数据及路径映射已导入，重复导入未执行。");
            var directory = Path.Combine(Path.GetDirectoryName(settings.Path)!, "backups", DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var previous = await settings.LoadAsync(token); var settingsBackup = Path.Combine(directory, "settings.json");
            await File.WriteAllTextAsync(settingsBackup, StoreJson.Serialize(previous), token);
            var databaseBackup = Path.Combine(directory, "state.sqlite"); await states.BackupAsync(databaseBackup, token);
            await File.WriteAllTextAsync(RecoveryPath, StoreJson.Serialize(new ImportRecovery(plan.Id, settingsBackup, databaseBackup)), token);
            try
            {
                // 配置先替换；批次只有数据库提交后才可见。中断时按批次记录恢复旧设置。
                await settings.SaveAsync(plan.Settings, token);
                await states.WithConnectionAsync(async connection =>
                {
                    using var transaction = connection.BeginTransaction();
                    foreach (var book in plan.Books) await ImportBookAsync(connection, transaction, book, token);
                    foreach (var mark in plan.Bookmarks)
                    {
                        BookId? book = null;
                        var normalized = mark.Path is null ? (Path: (string?)null, Page: mark.Page) : NormalizeBook(mark.Path, mark.Page);
                        if (normalized.Path is { } path)
                        {
                            book = new(await IdentityAsync(connection, transaction, "book", new(path), token));
                            if (!plan.Books.Any(b => b.Path == path)) await ImportBookAsync(connection, transaction, new(path, normalized.Page, mark.Options, DateTimeOffset.UtcNow), token);
                        }
                        var bookmark = new Bookmark(mark.Id, mark.Parent, mark.Order, mark.Name, book, normalized.Path is null ? null : new(normalized.Path), null, normalized.Page);
                        using var command = connection.CreateCommand(); command.Transaction = transaction;
                        command.CommandText = "INSERT INTO bookmarks VALUES($id,$parent,$order,$data) ON CONFLICT(id) DO UPDATE SET parent_id=$parent,sort_order=$order,data=$data";
                        command.Parameters.AddWithValue("$id", mark.Id); command.Parameters.AddWithValue("$parent", (object?)mark.Parent ?? DBNull.Value); command.Parameters.AddWithValue("$order", mark.Order); command.Parameters.AddWithValue("$data", StoreJson.Serialize(bookmark));
                        await command.ExecuteNonQueryAsync(token);
                    }
                    using var batch = connection.CreateCommand(); batch.Transaction = transaction;
                    batch.CommandText = "INSERT INTO import_batches VALUES($id,$source,$time)";
                    batch.Parameters.AddWithValue("$id", plan.Id); batch.Parameters.AddWithValue("$source", plan.Source); batch.Parameters.AddWithValue("$time", DateTimeOffset.UtcNow.ToString("O")); await batch.ExecuteNonQueryAsync(token);
                    transaction.Commit(); return true;
                }, token);
                await File.WriteAllTextAsync(Path.Combine(directory, "report.json"), StoreJson.Serialize(plan.Warnings), CancellationToken.None);
                File.Delete(RecoveryPath);
            }
            catch
            {
                if (!await BatchExistsAsync(plan.Id, CancellationToken.None)) await settings.SaveAsync(previous);
                File.Delete(RecoveryPath); throw;
            }
        }
        finally { _gate.Release(); }
    }
    /// <summary>恢复上次中断导入；已提交批次保留新配置，未提交则恢复旧配置。</summary>
    public async Task RecoverAsync(CancellationToken token = default)
    {
        if (!File.Exists(RecoveryPath)) return;
        var recovery = StoreJson.Read<ImportRecovery>(await File.ReadAllTextAsync(RecoveryPath, token));
        if (!await BatchExistsAsync(recovery.Batch, token)) await settings.SaveAsync(StoreJson.Read<AppSettings>(await File.ReadAllTextAsync(recovery.PreviousSettings, token)), token);
        File.Delete(RecoveryPath);
    }
    private Task<bool> BatchExistsAsync(string id, CancellationToken token) => states.WithConnectionAsync(async connection =>
    { using var command = connection.CreateCommand(); command.CommandText = "SELECT 1 FROM import_batches WHERE id=$id"; command.Parameters.AddWithValue("$id", id); return await command.ExecuteScalarAsync(token) is not null; }, token);
    /// <summary>在导入事务中登记身份，避免调用外层连接锁。</summary>
    private static async Task<string> IdentityAsync(SqliteConnection connection, SqliteTransaction transaction, string kind, SourceLocator locator, CancellationToken token)
    {
        using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = "INSERT OR IGNORE INTO identities VALUES($kind,$locator,$id); SELECT id FROM identities WHERE kind=$kind AND locator=$locator";
        command.Parameters.AddWithValue("$kind", kind); command.Parameters.AddWithValue("$locator", SqliteStateStore.LocatorKey(locator)); command.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("N")); return (string)(await command.ExecuteScalarAsync(token))!;
    }
    private static async Task ImportBookAsync(SqliteConnection connection, SqliteTransaction transaction, ImportBook book, CancellationToken token)
    {
        var normalized = NormalizeBook(book.Path, book.Page);
        var id = new BookId(await IdentityAsync(connection, transaction, "book", new(normalized.Path), token));
        var state = new ReadingState(id, new(normalized.Path), null, book.Options, book.Access, normalized.Page);
        using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = "INSERT INTO reading_states VALUES($id,$data,$time) ON CONFLICT(book_id) DO UPDATE SET data=$data,accessed=$time";
        command.Parameters.AddWithValue("$id", id.Value); command.Parameters.AddWithValue("$data", StoreJson.Serialize(state)); command.Parameters.AddWithValue("$time", book.Access.ToString("O")); await command.ExecuteNonQueryAsync(token);
    }
}
