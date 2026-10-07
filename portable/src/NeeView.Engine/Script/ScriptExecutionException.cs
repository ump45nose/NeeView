namespace NeeView;

/// <summary>脚本解析或执行失败；来源和行号在可用时保留。</summary>
public sealed class ScriptExecutionException : Exception
{
    public ScriptExecutionException(string message, string? source = null, int line = -1, Exception? innerException = null)
        : base(message, innerException) { SourcePath = source; Line = line; }
    public string? SourcePath { get; }
    public int Line { get; }
}
