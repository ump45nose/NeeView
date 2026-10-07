// Copyright (c) NeeLaboratory. 原CommandAccessor的名称/输入/参数/Patch关系，MIT。
using System.Text.Json;
using System.Text.Json.Nodes;
namespace NeeView;
/// <summary>原命令适配；Patch只修改请求快照，Parameter编辑则更新唯一JSON。</summary>
public sealed class CommandAccessor
{
    private readonly ScriptAccessContext _context;
    private readonly IReadOnlyDictionary<string, object?> _patch;
    private CommandDefinition Definition => _context.Commands.Definitions.First(d => d.Name == Name);
    public CommandAccessor(ScriptAccessContext context, string name) : this(context, name, new Dictionary<string, object?>()) { }
    private CommandAccessor(ScriptAccessContext context, string name, IReadOnlyDictionary<string, object?> patch) { _context = context; Name = name; _patch = patch; }
    public string Name { get; }
    public bool IsShowMessage { get => _context.Read(() => _context.State.GetShowMessage(Name)); set => _context.Write(() => _context.State.SetShowMessage(Name, value)); }
    public string ShortCutKey { get => _context.Read(() => _context.State.GetShortcut(Name, Definition.Shortcut)); set => _context.Write(() => _context.State.SetShortcut(Name, value)); }
    public string TouchGesture { get => _context.Read(() => _context.State.GetTouchGesture(Name, Definition.TouchGesture)); set => _context.Write(() => _context.State.SetTouchGesture(Name, value)); }
    public string MouseGesture { get => _context.Read(() => _context.State.GetMouseGesture(Name, Definition.MouseGesture).ToString()); set => _context.Write(() => _context.State.SetMouseGestureDifference(Name, value, Definition.MouseGesture)); }
    public PropertyMap? Parameter => _context.Read(() =>
    {
        var value = _context.State.GetCommandParameterObject(Name); if (value is null) return null;
        var map = new PropertyMap("nv.Command." + Name + ".Parameter", null, null, value, _context.Diagnostics, "", PropertyMapOptions.Create(_context.Dispatcher));
        map.PropertyChanged += (_, _) => _context.Write(() => _context.State.SetCommandParameterObject(Name, value));
        return map;
    });
    /// <summary>克隆并累计Patch，原实例和共享配置不变。</summary>
    public CommandAccessor Patch(IDictionary<string, object?> patch)
    {
        var merged = new Dictionary<string, object?>(_patch);
        foreach (var pair in patch) merged[pair.Key] = pair.Value;
        return new(_context, Name, merged);
    }
    /// <summary>等待实际异步命令结束；取消/错误不会当作成功。</summary>
    public bool Execute(params object?[] args)
    {
        bool success = false;
        _context.Run(async () =>
        {
            if (!_context.CanExecute(Name)) return;
            var parameter = _context.State.GetCommandParameterObject(Name);
            JsonObject? snapshot = null;
            if (parameter is not null)
            {
                var map = new PropertyMap("nv.Command." + Name + ".Parameter", null, null, parameter, _context.Diagnostics, "", PropertyMapOptions.Create(_context.Dispatcher));
                foreach (var pair in _patch) map[pair.Key] = pair.Value;
                snapshot = JsonSerializer.SerializeToNode(parameter, parameter.GetType())!.AsObject();
            }
            else if (_patch.Count != 0) throw new InvalidOperationException("此命令没有可覆盖参数。");
            using var scope = snapshot is null ? null : _context.State.OverrideCommandParameter(Name, snapshot);
            success = await _context.ExecuteCommandAsync(Name, args);
        });
        return success;
    }
}
