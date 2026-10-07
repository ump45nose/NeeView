// Copyright (c) NeeLaboratory.
using System.Text;
using System.Text.RegularExpressions;
namespace NeeView;

/// <summary>读取 nvjs 头部文档注释的纯引擎模型，不执行脚本。</summary>
public sealed partial class ScriptCommandSource
{
    public const string Extension = ".nvjs";
    public const string OnStartupFilename = "OnStartup";
    public const string OnBookLoadedFilename = "OnBookLoaded";
    public const string OnPageChangedFilename = "OnPageChanged";
    public const string OnPageEndFilename = "OnPageEnd";
    public const string OnWindowStateChangedFilename = "OnWindowStateChanged";
    [GeneratedRegex(@"^\s*/{2,}")] private static partial Regex CommentLine();
    [GeneratedRegex(@"^\s*/{2,}\s*(@\w+)(.*)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)] private static partial Regex DocComment();
    private ScriptCommandSource(string path) { Path = path; }
    public string Path { get; }
    public string Name => System.IO.Path.GetFileNameWithoutExtension(Path);
    public bool IsCloneable { get; private set; }
    public string Text { get; private set; } = "";
    public string Remarks { get; private set; } = "";
    public string ShortCutKey { get; private set; } = "";
    public string MouseGesture { get; private set; } = "";
    public string TouchGesture { get; private set; } = "";
    public string Args { get; private set; } = "";
    public string ArgsDescription { get; private set; } = "";
    public static ScriptCommandSource Create(string path)
    {
        var source = new ScriptCommandSource(path); var filename = source.Name; source.Text = filename;
        source.Remarks = HelpText.GetString(filename switch
        {
            OnStartupFilename => "ScriptOnStartupCommand.Remarks", OnBookLoadedFilename => "ScriptOnBookLoadedCommand.Remarks",
            OnPageChangedFilename => "ScriptOnPageChangedCommand.Remarks", OnPageEndFilename => "ScriptOnPageEndCommand.Remarks",
            OnWindowStateChangedFilename => "ScriptOnWindowStateChangedCommand.Remarks", _ => "ScriptCommand.Remarks"
        });
        source.IsCloneable = filename is not (OnStartupFilename or OnBookLoadedFilename or OnPageChangedFilename or OnPageEndFilename or OnWindowStateChangedFilename);
        var args = new List<string>(); var descriptions = new StringBuilder(); var description = new StringBuilder();
        var mouse = new List<string>(); var shortcuts = new List<string>(); var touch = new List<string>(); var name = ""; var isComment = false;
        foreach (var line in File.ReadLines(path))
        {
            if (CommentLine().IsMatch(line))
            {
                isComment = true; var match = DocComment().Match(line); if (!match.Success) continue;
                var value = match.Groups[2].Value.Trim(); switch (match.Groups[1].Value.ToLowerInvariant())
                {
                    case "@args": if (value.Length > 0) args.Add(value); break;
                    case "@argsdescription": descriptions.AppendLine(ScriptStringEscape.Unescape(value)); break;
                    case "@description": description.AppendLine(ScriptStringEscape.Unescape(value)); break;
                    case "@mousegesture": if (value.Length > 0) mouse.Add(value); break;
                    case "@name": if (value.Length > 0 && name.Length == 0) name = value; break;
                    case "@shortcutkey": if (value.Length > 0) shortcuts.Add(value); break;
                    case "@touchgesture": if (value.Length > 0) touch.Add(value); break;
                }
            }
            else if (isComment && !string.IsNullOrWhiteSpace(line)) break;
        }
        if (name.Length > 0) source.Text = name; var remarks = description.ToString().Trim(); if (remarks.Length > 0) source.Remarks = remarks;
        source.Args = string.Join(' ', args); source.ArgsDescription = descriptions.ToString().Trim(); source.ShortCutKey = string.Join(',', shortcuts); source.MouseGesture = string.Concat(mouse); source.TouchGesture = string.Join(',', touch); return source;
    }
}
