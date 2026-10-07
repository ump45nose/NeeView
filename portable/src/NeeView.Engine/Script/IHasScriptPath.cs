namespace NeeView;

/// <summary>当前脚本来源及其相对 include 基准目录。</summary>
public interface IHasScriptPath
{
    string? ScriptPath { get; }
    string? ScriptDirectory { get; }
}
