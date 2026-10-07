using System.Text.Json;
using System.Text.Json.Nodes;
namespace NeeView;

public sealed partial class SaveData
{
    private readonly AsyncLocal<IReadOnlyDictionary<string, JsonObject>?> _parameterOverrides = new();

    /// <summary>原Command.Patch的请求级参数；不用修改并恢复共享JSON，避免异步命令交错。</summary>
    /// <param name="name">待执行原命令，配对参数使用原owner。</param>
    /// <param name="parameter">完整独立参数快照，作用域内不会再被调用方修改。</param>
    /// <returns>须在实际执行调用链内释放的嵌套作用域。</returns>
    public IDisposable OverrideCommandParameter(string name, JsonObject parameter)
    {
        var previous = _parameterOverrides.Value;
        var current = previous is null ? new Dictionary<string, JsonObject>() : new Dictionary<string, JsonObject>(previous);
        current[DefaultInputScheme.GetParameterOwner(name)] = parameter.DeepClone().AsObject();
        _parameterOverrides.Value = current;
        return new ParameterScope(() => _parameterOverrides.Value = previous);
    }

    private JsonObject? GetCommandParameterOverride(string name) =>
        _parameterOverrides.Value?.GetValueOrDefault(DefaultInputScheme.GetParameterOwner(name));

    /// <summary>以唯一参数类型表生成原当前参数对象，供脚本映射使用，不建立另一参数模型。</summary>
    public object? GetCommandParameterObject(string name)
    {
        if (name.StartsWith("Script_", StringComparison.Ordinal)) return GetScriptParameter(name);
        var type = CommandParameterTypes.Get(name); if (type is null) return null;
        if (type == typeof(MoveToFolderAsCommandParameter)) return GetDestinationParameter(name);
        var owner = DefaultInputScheme.GetParameterOwner(name);
        var value = GetCommandParameterOverride(name) ?? _setting["Commands"]?[owner]?["Parameter"] ?? _setting["Commands"]?[name]?["Parameter"];
        return value?.Deserialize(type, ReadOptions) ?? Activator.CreateInstance(type);
    }

    private sealed class ParameterScope(Action restore) : IDisposable
    {
        private Action? _restore = restore;
        public void Dispose() => Interlocked.Exchange(ref _restore, null)?.Invoke();
    }
}
