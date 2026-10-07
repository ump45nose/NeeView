namespace NeeView;

/// <summary>宿主无关的 JavaScript 执行运行时。</summary>
public interface IScriptRuntime : IHasScriptPath, IDisposable
{
    /// <summary>执行脚本并返回 JavaScript 结果。</summary>
    object? Evaluate(string script, string? path = null);
    /// <summary>按当前脚本目录解析并执行 UTF-8 脚本文件。</summary>
    object? ExecuteFile(string path);
    /// <summary>向脚本全局设置值。</summary>
    void SetValue(string name, object? value);
    /// <summary>读取脚本全局值。</summary>
    object? GetValue(string name);
}

/// <summary>创建一个独立的脚本运行时实例。</summary>
public interface IScriptRuntimeFactory
{
    IScriptRuntime Create(Action<object?> log, Action<string, string?> system, CancellationToken token);
}
