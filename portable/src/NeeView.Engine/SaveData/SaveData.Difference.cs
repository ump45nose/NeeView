using System.Collections;
using System.Text.Json;
using System.Collections.Concurrent;
using System.Text.Json.Nodes;
namespace NeeView;

public sealed partial class SaveData
{
    private static readonly ConcurrentDictionary<Type, IDiffJsonConverter> DifferenceConverters = new();
    /// <summary>取得原类型属性元数据，写出和已识别字段合并共用一次缓存。</summary>
    /// <param name="type">已迁入、可无参构造的原配置/参数类型。</param><returns>原默认比较器。</returns>
    private static IDiffJsonConverter GetDifferenceConverter(Type type) => DifferenceConverters.GetOrAdd(type, type =>
        (IDiffJsonConverter)Activator.CreateInstance(typeof(DiffJsonConverter<>).MakeGenericType(type))!);

    /// <summary>已知类型合并时收归原字段大小写；动态字典和未知键仍沿原精确合并。</summary>
    /// <param name="target">唯一运行 JSON 节点。</param><param name="value">调用方明确提交的类型值。</param>
    private static void MergeTyped(JsonObject target, object value)
    {
        var source = JsonSerializer.SerializeToNode(value, value.GetType(), Options)!.AsObject();
        foreach (var property in GetDifferenceConverter(value.GetType()).CompareProperties(value, value))
        {
            foreach (var key in target.Select(pair => pair.Key).Where(key => key != property.Name && key.Equals(property.Name, StringComparison.OrdinalIgnoreCase)).ToArray()) target.Remove(key);
            if (property.Value is not null && source[property.Name] is JsonObject child &&
                property.Type.Assembly == typeof(Config).Assembly && !typeof(IEnumerable).IsAssignableFrom(property.Type) &&
                property.Type.GetConstructor(Type.EmptyTypes) is not null)
            {
                MergeTyped(Object(target, property.Name), property.Value);
                source.Remove(property.Name);
            }
        }
        Merge(target, source);
    }

    /// <summary>按原默认比较生成写出副本；运行节点、未迁配置及未知字段保持。</summary>
    /// <returns>可交给唯一五文件事务的 UserSetting 副本。</returns>
    private JsonObject CreateSettingMemento()
    {
        var result = _setting.DeepClone().AsObject();
        // 必须读取本次已准备的节点，不能借用尚未提交的 Config.Current（例如历史限制草稿）。
        var config = ReadProfileConfig(_setting);
        if (result["Config"] is JsonObject raw)
        {
            // 旧读取别名若留在执行分支，会在新字段恢复默认并省略后复活旧值。
            if (raw["AutoHide"] is JsonObject autoHide)
                foreach (var field in new[] { "AutoHideHitTestMargin", "AutoHideConfrictTopMargin", "AutoHideConfrictBottomMargin" })
                    if (autoHide.ContainsKey(field))
                    {
                        Object(Object(result, "MacImportedLegacyConfigFields"), "AutoHide")[field] = autoHide[field]?.DeepClone();
                        autoHide.Remove(field);
                    }
            TrimKnownObject(raw, config, new Config());
        }
        if (result["Commands"] is JsonObject commands)
        {
            foreach (var name in commands.Select(pair => pair.Key).ToArray())
            {
                if (commands[name] is not JsonObject command) continue;
                if (DefaultInputScheme.IsKnownCommand(name))
                {
                    foreach (var field in new[] { "ShortCutKey", "MouseGesture", "TouchGesture", "IsShowMessage", "Parameter" })
                        if (command.ContainsKey(field) && command[field] is null) command.Remove(field);
                    if (command["ShortCutKey"] is { } shortcut && shortcut.GetValue<string>() == DefaultInputScheme.GetShortcut(name, "", config.Command)) command.Remove("ShortCutKey");
                    if (command["MouseGesture"] is { } gesture && gesture.GetValue<string>() == DefaultInputScheme.GetMouseGesture(name, "", config.Command)) command.Remove("MouseGesture");
                    if (command["TouchGesture"] is { } touch && touch.GetValue<string>() == DefaultInputScheme.GetTouchGesture(name)) command.Remove("TouchGesture");
                    if (command["IsShowMessage"] is { } show && show.GetValue<bool>() == DefaultInputScheme.GetShowMessage(name)) command.Remove("IsShowMessage");
                    TrimCommandParameter(commands, name, command);
                }
                if (command.Count == 0 && DefaultInputScheme.IsKnownCommand(name)) commands.Remove(name);
            }
            // alias 提升可能在遍历中创建新的 owner；统一移除无差异的已知空命令。
            foreach (var name in commands.Where(pair => DefaultInputScheme.IsKnownCommand(pair.Key) && pair.Value is JsonObject { Count: 0 }).Select(pair => pair.Key).ToArray()) commands.Remove(name);
            if (commands.Count == 0) result.Remove("Commands");
        }
        else if (result["Commands"] is null) result.Remove("Commands");
        return result;
    }

    /// <summary>沿原公开可读写属性和 Equals/IDefaultable 规则，只删除已识别的默认字段。</summary>
    /// <param name="raw">独立副本中的当前对象，包含未迁移或未知字段。</param>
    /// <param name="value">由同一原 JSON 得到的当前类型投影。</param>
    /// <param name="defaults">父对象提供的原默认实例，四种列表模板不会互相混用。</param>
    private static void TrimKnownObject(JsonObject raw, object value, object defaults)
    {
        var converter = GetDifferenceConverter(value.GetType());
        foreach (var property in converter.CompareProperties(value, defaults))
        {
            foreach (var key in raw.Select(pair => pair.Key).Where(key => key != property.Name && key.Equals(property.Name, StringComparison.OrdinalIgnoreCase)).ToArray()) raw.Remove(key);
            // Mac 的关闭选择需要完整原布局快照；不对动态 Docks/Panels 字典盲目裁剪。
            if (value is PanelsConfig && property.Name == "Layout" && property.Value is not null) continue;
            if (property.Value is not null &&
                property.Type.Assembly == typeof(Config).Assembly && !typeof(IEnumerable).IsAssignableFrom(property.Type) &&
                property.Type.GetConstructor(Type.EmptyTypes) is not null)
            {
                var child = raw[property.Name] as JsonObject ?? JsonSerializer.SerializeToNode(property.Value, property.Type, Options)!.AsObject();
                TrimKnownObject(child, property.Value, property.Default ?? Activator.CreateInstance(property.Type)!);
                if (child.Count == 0 && property.Default is not null) raw.Remove(property.Name);
                else if (child.Parent is null) raw[property.Name] = child;
            }
            else if (property.IsDefault) raw.Remove(property.Name);
            // 原serializer输出实际getter值；旧setter可能覆盖了同一节点已有的现代字段。
            else raw[property.Name] = JsonSerializer.SerializeToNode(property.Value, property.Type, Options);
        }
    }

    /// <summary>原共享参数只写 owner；早期 Mac alias 参数先转为 owner，未知材料不丢失。</summary>
    /// <param name="commands">本次命令写出副本。</param><param name="name">稳定原命令名。</param>
    /// <param name="command">当前命令对象，键位和参数分别处理。</param>
    private static void TrimCommandParameter(JsonObject commands, string name, JsonObject command)
    {
        var type = CommandParameterTypes.Get(name);
        if (type is null || command["Parameter"] is not JsonObject parameter) return;
        // 原 JsonCommandParameterConverter 去掉 CommandParameter 后缀，并要求 $type 为首字段。
        var typeName = type.Name[..^"CommandParameter".Length];
        if (parameter["$type"] is { } discriminator && discriminator.GetValue<string>() != typeName) return;
        var owner = DefaultInputScheme.GetParameterOwner(name);
        if (owner != name)
        {
            // 与读取侧 ?? 后备相同：非空 owner 优先，缺失或 null 时承接早期 Mac alias。
            var ownerCommand = Object(commands, owner);
            if (ownerCommand["Parameter"] is null) ownerCommand["Parameter"] = parameter.DeepClone();
            // 原版不执行 alias 参数；仍留兼容材料，供未来版本/未识别参数迁移。
            Object(command, "MacImportedSharedParameter")["Parameter"] = parameter.DeepClone();
            command.Remove("Parameter");
            command = ownerCommand;
            if (command["Parameter"] is not JsonObject ownerParameter) return;
            parameter = ownerParameter;
            if (parameter["$type"] is { } ownerDiscriminator && ownerDiscriminator.GetValue<string>() != typeName) return;
        }
        var defaults = Activator.CreateInstance(type)!;
        if (defaults is MoveToFolderAsCommandParameter destination && name.StartsWith("MoveToDestinationFolder", StringComparison.Ordinal)
            && int.TryParse(name["MoveToDestinationFolder".Length..], out var number)) destination.Index = number;
        var full = System.Text.Json.JsonSerializer.SerializeToNode(defaults, Options)!.AsObject();
        Merge(full, parameter);
        var value = full.Deserialize(type, ReadOptions)!;
        if (value is ScrollPageCommandParameter)
            foreach (var field in new[] { "IsNScroll", "PageMoveMargin" })
                foreach (var key in parameter.Select(pair => pair.Key).Where(key => key.Equals(field, StringComparison.OrdinalIgnoreCase)).ToArray())
                {
                    Object(command, "MacImportedLegacyParameterFields")[key] = parameter[key]?.DeepClone();
                    parameter.Remove(key);
                }
        TrimKnownObject(parameter, value, defaults);
        // $type 元数据不代表自定义参数；空的默认参数整个省略，有未知字段则保留。
        if (parameter.Count == 0 || parameter.Count == 1 && parameter.ContainsKey("$type")) command.Remove("Parameter");
        else
        {
            var typed = new JsonObject { ["$type"] = typeName };
            foreach (var pair in parameter.Where(pair => pair.Key != "$type")) typed[pair.Key] = pair.Value?.DeepClone();
            command["Parameter"] = typed;
        }
    }
}
