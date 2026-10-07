using System.Text.Json;
using System.Text.Json.Nodes;
namespace NeeView;
public sealed partial class SaveData
{
    private readonly Dictionary<string, ScriptCommandSource> _scriptDefaults = new(StringComparer.Ordinal);
    /// <summary>动态脚本头部仅提供缺省值；用户差分和已经删除的脚本数据始终保持。</summary>
    public void SetScriptDefaults(IEnumerable<ScriptCommandSource> sources)
    { _scriptDefaults.Clear(); foreach (var s in sources) _scriptDefaults["Script_" + s.Name] = s; }
    internal ScriptCommandParameter GetScriptParameter(string name)
    {
        var value = GetCommandParameterOverride(name) ?? _setting["Commands"]?[name]?["Parameter"];
        var defaults = new ScriptCommandParameter { Argument = _scriptDefaults.GetValueOrDefault(name)?.Args };
        var node = JsonSerializer.SerializeToNode(defaults, Options)!.AsObject();
        if (value is JsonObject patch) Merge(node, patch);
        return node.Deserialize<ScriptCommandParameter>(ReadOptions)!;
    }
    public string GetTouchGesture(string name, string fallback = "") => _setting["Commands"]?[name]?["TouchGesture"]?.GetValue<string>() ?? fallback;
    public bool GetShowMessage(string name) => _setting["Commands"]?[name]?["IsShowMessage"]?.GetValue<bool>() ?? DefaultInputScheme.GetShowMessage(name);
    public void SetTouchGesture(string name, string value) => Object(Object(_setting, "Commands"), name)["TouchGesture"] = value;
    public void SetShowMessage(string name, bool value) => Object(Object(_setting, "Commands"), name)["IsShowMessage"] = value;
    /// <summary>脚本参数通过原owner节点写回，未来字段不丢失。</summary>
    public void SetCommandParameterObject(string name, object value) => SetCommandParameter(name, value);
}
