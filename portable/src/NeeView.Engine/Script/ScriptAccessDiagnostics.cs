// Copyright (c) NeeLaboratory. 原脚本旧成员错误级别，MIT。
namespace NeeView;
public sealed class ScriptAccessDiagnostics(Action<string, string>? notice = null) : IAccessDiagnostics
{
    public T? Throw<T>(Exception ex) { Throw(ex); return default; }
    public object? Throw(Exception ex, Type type) { Throw(ex); return type.IsValueType ? Activator.CreateInstance(type) : null; }
    public void Throw(Exception ex) => Throw(ex, ScriptErrorLevel.Error);
    public void Throw(Exception ex, ScriptErrorLevel errorLevel)
    {
        var level = (ScriptErrorLevel)Math.Min((int)Config.Current.Script.ErrorLevel, (int)errorLevel);
        if (level == ScriptErrorLevel.Error) throw ex;
        notice?.Invoke(level == ScriptErrorLevel.Warning ? "warning" : "info", ex.Message);
    }
}
