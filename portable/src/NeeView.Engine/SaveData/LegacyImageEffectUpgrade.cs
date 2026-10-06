// Copyright (c) NeeLaboratory. 原 UserSettingValidator Alpha.5、EffectUnitCache 和 EffectProfile.Store 的数据适配。
using System.Globalization;
using System.Text.Json.Nodes;
namespace NeeView;

/// <summary>仅转换旧效果的原 JSON 层、缓存和预设，不创建渲染资源或执行效果。</summary>
internal static class LegacyImageEffectUpgrade
{
    private const string UpgradeVersion = "Layers/1";
    // 原 EffectType 的数值顺序；Colorize 是现代类型，但原 ImageEffectConfig 没有旧 ColorizeEffect setter。
    private static readonly string[] Types = ["None", "Level", "Hsv", "ColorSelect", "Colorize", "Blur", "Bloom", "Monochrome", "ColorTone", "Sharpen", "Embossed", "Pixelate", "Magnify", "Ripple", "Swirl"];
    private static readonly string[] ProfileBranches = ["ImageCustomSize", "ImageTrim", "ImageDotKeep", "ImageResizeFilter", "ImageGrid", "ImageEffect"];
    // 只包含原旧 setter 可读的参数。缺省由原实例构造；未来参数不能因默认裁剪丢失。
    private static readonly IReadOnlyDictionary<string, JsonObject> Defaults = new Dictionary<string, JsonObject>
    {
        ["Level"] = Parse("""{"Black":0,"White":1,"Center":0.5,"Minimum":0,"Maximum":1}"""),
        ["Hsv"] = Parse("""{"Hue":0,"Saturation":0,"Value":0}"""),
        ["ColorSelect"] = Parse("""{"Hue":15,"Range":0.1,"Curve":0.1}"""),
        ["Blur"] = Parse("""{"Radius":5}"""),
        ["Bloom"] = Parse("""{"BaseIntensity":1,"BaseSaturation":1,"BloomIntensity":1.25,"BloomSaturation":1,"Threshold":0.25}"""),
        ["Monochrome"] = Parse("""{"Color":"#FFFFFFFF"}"""),
        ["ColorTone"] = Parse("""{"DarkColor":"#FF338000","LightColor":"#FFFFE580","ToneAmount":0.5,"Desaturation":0.5}"""),
        ["Sharpen"] = Parse("""{"Amount":2,"Height":0.5}"""),
        ["Embossed"] = Parse("""{"Color":"#FF808080","Amount":3,"Height":1}"""),
        ["Pixelate"] = Parse("""{"Pixelation":0.75}"""),
        ["Magnify"] = Parse("""{"Center":"0.5,0.5","Amount":0.5,"InnerRadius":0.2,"OuterRadius":0.4}"""),
        ["Ripple"] = Parse("""{"Center":"0.5,0.5","Frequency":40,"Magnitude":0.1,"Phase":10}"""),
        ["Swirl"] = Parse("""{"Center":"0.5,0.5","TwistAmount":10}""")
    };

    /// <summary>沿原 Alpha.5 顺序准备全部副本后一次提交；未知旧类型/色彩语法仅保留并报告。</summary>
    /// <param name="raw">唯一版本升级链拥有的独立设置候选。</param>
    /// <param name="version">本次候选的来源版本；早期 Mac 保留材料可带原版本标记。</param>
    internal static void Upgrade(JsonObject raw, Version version)
    {
        if (raw["MacImportedLegacyEffectUpgrade"] is not null) return; // 已转换或未来升级材料均不能降级覆盖。
        var oldFormat = raw["MacImportedLegacyEffectFormat"]?.ToString();
        var oldParts = oldFormat?.Split('/');
        var eligible = version <= new Version(46, 0, 4209) || oldParts is { Length: 2 } &&
            Version.TryParse(oldParts[1], out var oldVersion) && oldVersion >= new Version(38, 0, 0) && oldVersion <= new Version(46, 0, 4209);
        if (!eligible) return;
        var config = raw["Config"] as JsonObject ?? throw new InvalidDataException("Config 必须是对象。");
        // 早期 Mac 标记只是保留来源；当前候选已含现代层时不能借旧标记覆盖用户后续数据。
        if (version > new Version(46, 0, 4209) && config["ImageEffect"] is JsonObject modern && Find(modern, "Layers") is not null) return;
        raw["MacImportedLegacyEffectFormat"] ??= raw["Format"]?.DeepClone();
        try
        {
            var original = config["ImageEffect"] switch { null => new JsonObject(), JsonObject node => node, _ => throw new InvalidDataException("ImageEffect 必须是对象。") };
            var selectedType = ReadType(original);
            var units = new Dictionary<string, JsonObject>();
            // 原旧属性 setter 按 JSON 输入顺序进入 cache，原默认实例不占缓存。
            foreach (var pair in original)
            {
                var type = Defaults.Keys.FirstOrDefault(type => pair.Key.Equals(type + "Effect", StringComparison.OrdinalIgnoreCase));
                if (type is null) continue;
                if (pair.Value is null) continue; // 原 EffectUnitCache.Add(null) 不改变先前缓存。
                if (pair.Value is not JsonObject body) throw new InvalidDataException(pair.Key + " 必须是对象。");
                var unit = CreateUnit(type, body);
                if (unit.Count > 1) units[type] = unit; else units.Remove(type);
            }
            var effect = original.DeepClone().AsObject();
            foreach (var key in effect.Select(p => p.Key).Where(key => key.Equals("EffectType", StringComparison.OrdinalIgnoreCase) || key.Equals("IsHsvMode", StringComparison.OrdinalIgnoreCase) ||
                Defaults.Keys.Any(type => key.Equals(type + "Effect", StringComparison.OrdinalIgnoreCase))).ToArray()) effect.Remove(key);
            var enabled = Find(original, "IsEnabled");
            if (original.Any(p => p.Key.Equals("IsEnabled", StringComparison.OrdinalIgnoreCase)) && !(enabled is JsonValue flag && flag.TryGetValue<bool>(out _))) throw new InvalidDataException("ImageEffect.IsEnabled 必须是布尔值。");
            foreach (var key in effect.Select(p => p.Key).Where(k => k.Equals("IsEnabled", StringComparison.OrdinalIgnoreCase) || k.Equals("Layers", StringComparison.OrdinalIgnoreCase)).ToArray()) effect.Remove(key);
            effect["IsEnabled"] = enabled?.DeepClone() ?? JsonValue.Create(false);
            effect["Layers"] = new JsonArray(new JsonObject { ["IsEnabled"] = true,
                ["Effect"] = selectedType == "None" ? null : units.TryGetValue(selectedType, out var selected) ? selected.DeepClone() : new JsonObject { ["$type"] = selectedType } });
            var cache = config["ImageEffectCache"] switch { null => new JsonArray(), JsonArray array => array.DeepClone().AsArray(), _ => throw new InvalidDataException("ImageEffectCache 必须是数组。") };
            foreach (var pair in units)
            {
                var index = Enumerable.Range(0, cache.Count).FirstOrDefault(i => cache[i] is JsonObject cached && cached["$type"]?.ToString() == pair.Key, -1);
                if (index < 0) cache.Add(pair.Value.DeepClone());
                else
                {
                    cache[index] = pair.Value.DeepClone();
                    for (var i = cache.Count - 1; i > index; i--)
                        if (cache[i] is JsonObject cached && cached["$type"]?.ToString() == pair.Key) cache.RemoveAt(i);
                }
            }
            var profiles = config["EffectProfiles"] switch { null => new JsonObject(), JsonObject node => node.DeepClone().AsObject(), _ => throw new InvalidDataException("EffectProfiles 必须是对象。") };
            var profile = new JsonObject { ["Id"] = 0, ["Name"] = "" };
            foreach (var branch in ProfileBranches)
                profile[branch] = branch == "ImageEffect" ? effect.DeepClone() : config[branch]?.DeepClone() ?? new JsonObject();
            profiles["IdCounter"] ??= 0;
            profiles["Profiles"] = new JsonArray(profile); // 原 validator 替换为唯一默认首项。
            var archive = new JsonObject();
            foreach (var branch in new[] { "ImageEffect", "ImageEffectCache", "EffectProfiles" }) archive[branch] = config[branch]?.DeepClone();
            raw["MacImportedLegacyImageEffects"] ??= archive;
            config["ImageEffect"] = effect; config["ImageEffectCache"] = cache; config["EffectProfiles"] = profiles;
            raw["MacImportedLegacyEffectUpgrade"] = UpgradeVersion;
            raw.Remove("MacImportedLegacyEffectIssue");
        }
        catch (NotSupportedException ex)
        {
            // 与原 Windows 的拒绝不同：Mac 尚无效果执行，允许恢复其他设置，不能编造一个可执行默认效果。
            raw["MacImportedLegacyEffectIssue"] = ex.Message;
        }
    }

    /// <summary>按原 EffectType 数值/名称读取；未来值不降级为 None。</summary>
    /// <param name="effect">旧单效果节点。</param><returns>原短类型名称。</returns>
    private static string ReadType(JsonObject effect)
    {
        var node = Find(effect, "EffectType");
        if (node is null)
        {
            if (effect.Any(p => p.Key.Equals("EffectType", StringComparison.OrdinalIgnoreCase))) throw new InvalidDataException("EffectType 不能为 null。");
            return "None";
        }
        if (node is JsonValue value && value.TryGetValue<int>(out var number) && number >= 0 && number < Types.Length) return Types[number];
        if (node is JsonValue textValue && textValue.TryGetValue<string>(out var text))
        {
            var type = Types.FirstOrDefault(t => t.Equals(text, StringComparison.OrdinalIgnoreCase));
            if (type is not null) return type;
            if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out number) && number >= 0 && number < Types.Length) return Types[number];
        }
        throw new NotSupportedException("未识别的旧 EffectType：" + node.ToJsonString());
    }

    /// <summary>复用原参数默认及 setter 数值规则，生成 $type 首字段的差分；未来键原样保持。</summary>
    /// <param name="type">已核对的原旧类型。</param><param name="body">原参数 JSON。</param><returns>原短类型与非默认参数。</returns>
    private static JsonObject CreateUnit(string type, JsonObject body)
    {
        if (body["$type"] is { } discriminator && discriminator.ToString() != type) throw new NotSupportedException("旧参数包含未来 $type：" + discriminator);
        var unit = new JsonObject { ["$type"] = type };
        foreach (var pair in body)
        {
            if (pair.Key == "$type") continue;
            var field = Defaults[type].Select(p => p.Key).FirstOrDefault(k => k.Equals(pair.Key, StringComparison.OrdinalIgnoreCase));
            if (field is null) { unit[pair.Key] = pair.Value?.DeepClone(); continue; }
            var defaultValue = Defaults[type][field]!;
            unit.Remove(field); // 原反序列化按最后一个兼容大小写字段执行，恢复默认必须移除先前差分。
            JsonNode normalized;
            if (defaultValue is JsonValue defaultNumber && defaultNumber.TryGetValue<double>(out var defaultDouble))
            {
                if (pair.Value is not JsonValue number || !number.TryGetValue<double>(out var d) || !double.IsFinite(d)) throw new InvalidDataException(type + "." + field + " 必须是有限数值。");
                // 原 Level.BlackRaw/WhiteRaw 不舍入、不联动 Center；Bloom Threshold 只有上限。
                if (type != "Level" || field is not ("Black" or "White")) d = Math.Round(type == "Bloom" && field == "Threshold" ? Math.Min(d, 1) : d, 5);
                if (d == defaultDouble) continue;
                normalized = JsonValue.Create(d)!;
            }
            else
            {
                var text = pair.Value is JsonValue textValue && textValue.TryGetValue<string>(out var s) ? s : pair.Value is null && field == "Center" ? "0,0" :
                    throw new InvalidDataException(type + "." + field + " 必须是原字符串格式。");
                text = field == "Center" ? NormalizePoint(text) : NormalizeColor(text);
                if (text == defaultValue.GetValue<string>()) continue;
                normalized = JsonValue.Create(text)!;
            }
            unit[field] = normalized;
        }
        return unit;
    }

    /// <summary>原 Point 的 invariant 字符串和逐坐标五位舍入；不引入 UI 几何类型。</summary>
    /// <param name="text">原 x,y 或空白分隔的两个坐标。</param><returns>原 invariant 输出。</returns>
    private static string NormalizePoint(string text)
    {
        var coordinates = text.Split([',', ' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        if (coordinates.Length != 2 || !double.TryParse(coordinates[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var x) ||
            !double.TryParse(coordinates[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var y) || !double.IsFinite(x) || !double.IsFinite(y))
            throw new NotSupportedException("未核对的旧 Point 语法，原值保留：" + text);
        return Math.Round(x, 5).ToString(CultureInfo.InvariantCulture) + "," + Math.Round(y, 5).ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>规范原十六进制及命名颜色；scRGB 等未核对格式整组保留，不猜测色彩转换。</summary>
    /// <param name="text">原 Color 字符串。</param><returns>八位 ARGB 字符串。</returns>
    private static string NormalizeColor(string text)
    {
        text = text.Trim();
        if (text.StartsWith('#'))
        {
            var hex = text[1..];
            if (hex.Length is 3 or 4) hex = string.Concat(hex.Select(c => new string(c, 2)));
            if (hex.Length == 6) hex = "FF" + hex;
            if (hex.Length == 8 && uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var argb)) return "#" + argb.ToString("X8", CultureInfo.InvariantCulture);
        }
        else if (text.Length > 0 && char.IsLetter(text[0]) && Enum.TryParse<System.Drawing.KnownColor>(text, true, out var known) && Enum.IsDefined(known))
        {
            var color = System.Drawing.Color.FromKnownColor(known);
            if (!color.IsSystemColor) return "#" + unchecked((uint)color.ToArgb()).ToString("X8", CultureInfo.InvariantCulture);
        }
        throw new NotSupportedException("未核对的旧 Color 语法，原值保留：" + text);
    }

    private static JsonNode? Find(JsonObject node, string name) => node.LastOrDefault(p => p.Key.Equals(name, StringComparison.OrdinalIgnoreCase)).Value;
    private static JsonObject Parse(string text) => JsonNode.Parse(text)!.AsObject();
}
