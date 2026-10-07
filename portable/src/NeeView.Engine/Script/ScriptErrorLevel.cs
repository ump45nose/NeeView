// Copyright (c) NeeLaboratory.
namespace NeeView;

public enum ScriptErrorLevel
{
    /// <summary>廃止メンバーを情報通知し、スクリプトを続行する。</summary>
    Info = 0,
    /// <summary>廃止メンバーを警告通知し、スクリプトを続行する。</summary>
    Warning = 1,
    /// <summary>廃止メンバーをエラー通知し、スクリプトを停止する。</summary>
    Error = 2,
}

public static class ScriptErrorLevelExtension
{
    public static bool IsOpenConsole(this ScriptErrorLevel self) => ScriptErrorLevel.Warning <= self;
    public static bool IsError(this ScriptErrorLevel self) => ScriptErrorLevel.Error <= self;
}
