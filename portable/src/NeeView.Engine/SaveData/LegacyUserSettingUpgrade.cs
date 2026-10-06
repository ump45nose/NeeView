// Copyright (c) NeeLaboratory. 原 UserSettingValidator / TitleStringValidator 的版本规则适配。
using System.Globalization;
using System.Text.Json.Nodes;
using NeeView.Runtime.LayoutPanel;
namespace NeeView;

/// <summary>只改导入副本的原字段；未接入的效果/拖动/系统能力继续保留并报告。</summary>
internal static class LegacyUserSettingUpgrade
{
    /// <summary>按原顺序执行已核对的版本分支，不将字段保留等同于能力迁移。</summary>
    /// <param name="raw">可写的独立来源副本。</param><param name="version">原来源版本。</param>
    internal static void Upgrade(JsonObject raw, Version version)
    {
        var config = Branch(raw, "Config");
        var commands = raw["Commands"] as JsonObject;
        var actions = raw["DragActions"] as JsonObject;
        // 原 converter 支持 Type/Value 与 $type；先规范形状再执行 typed 参数分支。
        NormalizeParameters(commands, "CommandParameter");
        NormalizeParameters(actions, "DragActionParameter");
        if (version < new Version(39, 0, 0))
        {
            var panels = Branch(config, "Panels");
            if (panels["FontName"] is { } font) Branch(config, "Fonts")["FontName"] = font.DeepClone();
            LayoutPanelCompatibility.Rename(panels["Layout"] as JsonObject, "PagemarkPanel", "PlaylistPanel");
            RenameLanguage(config, "English", "en"); RenameLanguage(config, "Japanese", "ja");
            var book = Branch(config, "BookSetting");
            if (book["SortMode"]?.ToString() is "FileName" or "0") book["SortMode"] = "Entry";
            else if (book["SortMode"]?.ToString() is "FileNameDescending" or "1") book["SortMode"] = "EntryDescending";
            // 原 Config.BookSetting 的构造默认是 FileName，差分缺省仍需升级。
            else if (book["SortMode"] is null) book["SortMode"] = "Entry";
            Rename(raw, new() { ["ToggleVisiblePagemarkList"] = "ToggleVisiblePlaylist", ["TogglePagemark"] = "TogglePlaylistMark",
                ["PrevPagemark"] = "PrevPlaylistItem", ["NextPagemark"] = "NextPlaylistItem",
                ["PrevPagemarkInBook"] = "PrevPlaylistItemInBook", ["NextPagemarkInBook"] = "NextPlaylistItemInBook" });
        }
        if (version < new Version(40, 0, 0))
        {
            RenameLanguage(config, "zh-TW", "zh-Hant"); RenameLanguage(config, "zh-CN", "zh-Hans");
            var input = Branch(config, "Command");
            input["IsReversePageMoveHorizontalWheel"] = input["IsReversePageMoveWheel"]?.DeepClone() ?? JsonValue.Create(false);
            if (commands?["ToggleMainViewFloating"] is JsonObject floating && string.IsNullOrEmpty(Text(floating, "ShortCutKey")) && !ContainsGesture(commands, "F12"))
                floating["ShortCutKey"] = "F12";
            if (Parameter(actions, "MoveScale", "Sensitive") is { } sensitive)
            {
                actions!["MoveScale"]!["MacImportedLegacyParameter"] ??= sensitive.DeepClone();
                actions["MoveScale"]!["Parameter"] = new JsonObject { ["$type"] = "MoveScale", ["Sensitivity"] = sensitive["Sensitivity"]?.DeepClone() ?? JsonValue.Create(1.0) };
            }
        }
        if (version >= new Version(40, 0, 0) && version < new Version(40, 3, 0) && config["Book"]?["IsInsertDummyPage"]?.GetValue<bool>() == true)
        { var book = Branch(config, "Book"); book["IsInsertDummyFirstPage"] = true; book["IsInsertDummyLastPage"] = true; }
        if (version < new Version(40, 5, 0) && commands is not null)
        {
            if (version < new Version(40, 0, 0))
            {
                if (commands["TogglePageModeReverse"] is { } old) Branch(raw, "MacImportedLegacyCommands")["TogglePageModeReverse"] = old.DeepClone();
                commands["TogglePageModeReverse"] = DefaultMemento("", "TogglePageMode", true); // 原默认参数 IsLoop=true。
            }
            else foreach (var name in new[] { "TogglePageMode", "TogglePageModeReverse" })
                if (commands[name] is JsonObject command && command["Parameter"] is null)
                    command["Parameter"] = new JsonObject { ["$type"] = "TogglePageMode", ["IsLoop"] = false };
        }
        if (version < new Version(41, 0, 0)) AddWithoutConflict(commands, "ToggleAutoScroll", "MiddleClick");
        if (version < new Version(42, 0, 6) && Parameter(commands, "CopyFile", "CopyFile") is { } copy)
        {
            var system = Branch(config, "System");
            system["ArchiveCopyPolicy"] = copy["ArchivePolicy"]?.DeepClone() ?? JsonValue.Create(0);
            system["TextCopyPolicy"] = copy["TextCopyPolicy"]?.DeepClone() ?? JsonValue.Create(0);
        }
        if (version < new Version(43, 0, 3661)) AddWithoutConflict(commands, "CutFile", "Ctrl+X");
        if (version < new Version(45, 0, 3973) && config["Panels"]?["IsVisibleItemsCount"] is { } count)
            foreach (var branch in new[] { "Bookshelf", "Bookmark", "PageList", "History" }) Branch(config, branch)["IsVisibleItemsCount"] = count.DeepClone();
        if (version <= new Version(46, 0, 4065))
        {
            var system = config["System"] as JsonObject;
            Replace(system, "FileManagerFileArgs", "File"); Replace(system, "FileManagerFolderArgs", "File");
            foreach (var app in (system?["ExternalAppCollection"] as JsonArray)?.OfType<JsonObject>() ?? [])
            { Replace(app, "Command", "NeeView"); Replace(app, "Parameter", "File", "Uri"); }
            Replace(config["Information"] as JsonObject, "MapProgramFormat", "LatDeg", "LonDeg", "Lat", "Lon");
            foreach (var branch in new[] { "WindowTitle", "PageTitle" })
                foreach (var field in new[] { branch + "Format1", branch + "Format2", branch + "FormatMedia" })
                    if (config[branch] is JsonObject title && Text(title, field) is { } value) title[field] = UpgradeTitle(value);
            if (Parameter(commands, "OpenExternalApp", "OpenExternalApp") is { } appParameter)
            { Replace(appParameter, "Command", "NeeView"); Replace(appParameter, "Parameter", "File", "Uri"); }
            if (Parameter(commands, "ExportImageAs", "ExportImageAs") is { } export)
            {
                var target = Branch(Branch(config, "Book"), "ExportImageParameter");
                target["ExportFolder"] = export["ExportFolder"]?.DeepClone() ?? JsonValue.Create("");
                target["QualityLevel"] = export["QualityLevel"]?.DeepClone() ?? JsonValue.Create(0);
                // 参数退役前保留未知字段；下次升级不能重新覆盖新导出设置。
                commands!["ExportImageAs"]!["MacImportedLegacyParameter"] = export.DeepClone();
                commands["ExportImageAs"]!["Parameter"] = null;
            }
        }
        if (version <= new Version(46, 0, 4134))
        {
            Branch(config, "FilmStrip")["IsHideFilmStripInAutoHideMode"] = config["Slider"]?["IsHidePageSliderInAutoHideMode"]?.DeepClone() ?? JsonValue.Create(true);
            Rename(raw, new() { ["AutoScrollOn"] = "ToggleAutoScroll", ["ToggleVisibleThumbnailList"] = "ToggleVisibleFilmStrip", ["ToggleHideThumbnailList"] = "ToggleHideFilmStrip" });
        }
        if (version <= new Version(46, 0, 4176))
        {
            // 固定基线 ViewConfig 的两个旧 setter 都为空，getter 从 MovementConstraint 计算。
            // 原 validator 将 Snap 归为 LockUntilResized，其余保持当前约束/默认。
            if (config["View"] is JsonObject view && view["MovementConstraint"]?.ToString() is "Snap" or "3") view["MovementConstraint"] = "LockUntilResized";
            ReplaceDragAction(raw, actions, "ScaleSliderCentered", "ScaleSlider", "Ctrl+LeftButton");
            ReplaceDragAction(raw, actions, "BaseScaleSliderCentered", "BaseScaleSlider", "");
        }
        if (version <= new Version(46, 0, 4209))
        {
            var window = Branch(config, "Window"); window["IsAutoHideInFullDesktop"] = window["IsAutoHideInFullScreen"]?.DeepClone() ?? JsonValue.Create(true);
            var thumbnail = Branch(Branch(config, "Panels"), "ThumbnailItemProfile");
            thumbnail["IsIconOverlay"] = !(thumbnail["IsTextVisible"]?.GetValue<bool>() ?? true);
            AddWithoutConflict(commands, "ToggleFullDesktop", "Shift+F11");
        }
        // 原效果的层/缓存/预设纯数据迁入；旧 Mac 保存的原版本标记也从同一入口继续升级。
        LegacyImageEffectUpgrade.Upgrade(raw, version);
        UpgradeValueAliases(config);
        LayoutPanelCompatibility.Upgrade(config["Panels"]?["Layout"] as JsonObject);
        if (version != new Version(46, 3, 0))
        { raw["MacImportedSourceFormat"] ??= raw["Format"]?.DeepClone(); raw["Format"] = "NeeView/46.3.0"; }
    }

    /// <summary>原属性 setter 的兼容别名；转换后移出活动字段以免下次保存覆盖新值。</summary>
    private static void UpgradeValueAliases(JsonObject config)
    {
        if (config["Panels"] is JsonObject panels)
        {
            Pair(panels, "IsHidePanel", "IsHideLeftPanel", "IsHideRightPanel");
            Pair(panels, "IsHidePanelInFullscreen", "IsHideLeftPanelInAutoHideMode", "IsHideRightPanelInAutoHideMode");
            Pair(panels, "IsHidePanelInAutoHideMode", "IsHideLeftPanelInAutoHideMode", "IsHideRightPanelInAutoHideMode");
        }
        if (config["View"] is JsonObject view)
        {
            if (view["MainViewMergin"] is { } margin) { view["MainViewMargin"] ??= margin.DeepClone(); Archive(view, "MainViewMergin"); }
            if (view["IsViewStartPositionCenter"] is { } centered)
            {
                var center = centered.GetValue<bool>();
                view["ViewHorizontalOrigin"] ??= JsonValue.Create(center ? "Center" : "CenterOrDirectionDependent");
                view["ViewVerticalOrigin"] ??= JsonValue.Create(center ? "Center" : "CenterOrDirectionDependent");
                Archive(view, "IsViewStartPositionCenter");
            }
        }
        static void Pair(JsonObject branch, string old, string first, string second)
        {
            if (branch[old] is not { } value) return;
            branch[first] ??= value.DeepClone(); branch[second] ??= value.DeepClone(); Archive(branch, old);
        }
        static void Archive(JsonObject branch, string field)
        { Branch(branch, "MacImportedLegacyFields")[field] = branch[field]?.DeepClone(); branch.Remove(field); }
    }

    /// <summary>原 Type/Value 包装规范为 $type；未知包装字段随旁路副本保留。</summary>
    private static void NormalizeParameters(JsonObject? entries, string suffix)
    {
        foreach (var entry in entries?.Select(pair => pair.Value).OfType<JsonObject>() ?? [])
            if (entry["Parameter"] is JsonObject parameter && parameter["Type"] is { } discriminator)
            {
                var name = discriminator.GetValue<string>();
                if (parameter["Value"] is not JsonObject body) throw new InvalidDataException("旧参数 Value 必须是对象。");
                if (!name.EndsWith(suffix, StringComparison.Ordinal)) throw new InvalidDataException("旧参数 Type 后缀无效。");
                if (name == "MovePlaylsitItemInBookCommandParameter") name = "MovePlaylistItemInBookCommandParameter";
                var result = new JsonObject { ["$type"] = name[..^suffix.Length] };
                foreach (var pair in body) result[pair.Key] = pair.Value?.DeepClone();
                entry["MacImportedLegacyParameter"] = parameter.DeepClone(); entry["Parameter"] = result;
            }
    }

    private static JsonObject? Parameter(JsonObject? entries, string name, string type) => entries?[name]?["Parameter"] is JsonObject parameter && Text(parameter, "$type") == type ? parameter : null;
    private static JsonObject Branch(JsonObject parent, string name)
    {
        if (parent[name] is null) parent[name] = new JsonObject();
        return parent[name] as JsonObject ?? throw new InvalidDataException(name + " 必须是对象。");
    }
    private static string? Text(JsonObject node, string field) => node[field]?.GetValue<string>();
    private static void RenameLanguage(JsonObject config, string source, string target)
    { if (config["System"] is JsonObject system && Text(system, "Language") == source) system["Language"] = target; }
    private static bool ContainsGesture(JsonObject commands, string gesture) => commands.Select(pair => pair.Value).OfType<JsonObject>()
        .Any(command => (Text(command, "ShortCutKey") ?? "").Split(',').Contains(gesture, StringComparer.Ordinal));
    private static void AddWithoutConflict(JsonObject? commands, string name, string shortcut)
    { if (commands is not null && !commands.ContainsKey(name)) commands[name] = DefaultMemento(ContainsGesture(commands, shortcut) ? "" : shortcut, name == "CutFile" ? "CopyFile" : "Toggle", name == "CutFile"); }
    /// <summary>原 CreateMemento 的默认字段；参数 diff 的构造默认由现有类型恢复。</summary>
    private static JsonObject DefaultMemento(string shortcut, string parameter, bool showMessage) => new()
    { ["ShortCutKey"] = shortcut, ["MouseGesture"] = "", ["TouchGesture"] = "", ["IsShowMessage"] = showMessage, ["Parameter"] = new JsonObject { ["$type"] = parameter } };

    /// <summary>按原 Name:Number 改名，新名已有配置优先；上下文菜单只修改 Command 节点。</summary>
    private static void Rename(JsonObject raw, Dictionary<string, string> map)
    {
        if (raw["Commands"] is JsonObject commands)
            foreach (var pair in commands.ToArray())
                if (Renamed(pair.Key) is { } name)
                {
                    if (!commands.ContainsKey(name)) commands[name] = pair.Value?.DeepClone();
                    else Branch(raw, "MacImportedLegacyCommands")[pair.Key] = pair.Value?.DeepClone();
                    commands.Remove(pair.Key);
                }
        if (raw["ContextMenu"] is JsonObject menu)
            foreach (var node in ProfileImportCompatibility.Walk(menu))
                if (node["MenuElementType"]?.ToString() is "Command" or "2" && Text(node, "CommandName") is { } name && Renamed(name) is { } replacement) node["CommandName"] = replacement;
        string? Renamed(string command)
        {
            var parts = command.Split(':');
            var source = parts.Length == 2 ? parts[0] : command;
            if (!map.TryGetValue(source, out var target)) return null;
            int number = parts.Length == 2 ? int.Parse(parts[1], CultureInfo.InvariantCulture) : 0;
            return number == 0 ? target : target + ":" + number.ToString(CultureInfo.InvariantCulture);
        }
    }

    /// <summary>只继承空目标键位，ScaleSlider 本身有默认键而 BaseScaleSlider 没有。</summary>
    private static void ReplaceDragAction(JsonObject raw, JsonObject? actions, string source, string target, string defaultKey)
    {
        if (actions?[source] is not JsonObject old) return;
        if (old["MouseButton"] is { } key)
        {
            if (actions[target] is JsonObject current)
            { if ((Text(current, "MouseButton") ?? defaultKey) == "") current["MouseButton"] = key.DeepClone(); }
            else if (defaultKey == "") actions[target] = new JsonObject { ["MouseButton"] = key.DeepClone() };
        }
        // 元数据存入动作本身之外，不能混入原 action 登记表。
        Branch(raw, "MacImportedLegacyDragActions")[source] = old.DeepClone();
        actions.Remove(source);
    }
    private static void Replace(JsonObject? node, string field, params string[] placeholders)
    {
        if (node is null || Text(node, field) is not { } text) return;
        foreach (var placeholder in placeholders) text = text.Replace("$" + placeholder, "{" + placeholder + "}", StringComparison.Ordinal);
        node[field] = text;
    }

    // 原表顺序必须保留：先 PageMax/PageL/PageR，最后才 Page；其它前缀同理。
    private static readonly (string Old, string New)[] TitlePlaceholders =
    [
        ("$Book", "{Book}"), ("$PageMax", "{PageMax}"), ("$PageL", "{PageL}"), ("$PageR", "{PageR}"), ("$Page", "{Page}{Part: (#)}"),
        ("$FullPathL", "{FullPathL:/ > }"), ("$FullPathR", "{FullPathR:/ > }"), ("$FullPath", "{FullPath:/ > }"),
        ("$FullNameL", "{EntryPathL:/ > }"), ("$FullNameR", "{EntryPathR:/ > }"), ("$FullName", "{EntryPath:/ > }"),
        ("$NameL", "{NameL}"), ("$NameR", "{NameR}"), ("$Name", "{Name}"),
        ("$SizeExL", "{SizeL}{BitsL: x #}"), ("$SizeExR", "{SizeR}{BitsR: x #}"), ("$SizeEx", "{Size}{Bits: x #}"),
        ("$SizeL", "{SizeL}"), ("$SizeR", "{SizeR}"), ("$Size", "{Size}"), ("$ViewScale", "{ViewScale:#%}"),
        ("$ScaleL", "{ScaleL:#%}"), ("$ScaleR", "{ScaleR:#%}"), ("$Scale", "{Scale:#%}")
    ];
    private static string UpgradeTitle(string value)
    { foreach (var (old, replacement) in TitlePlaceholders) value = value.Replace(old, replacement, StringComparison.Ordinal); return value; }
}
