namespace NeeView.Application;

public static class CommandCatalog
{
    public static readonly HashSet<string> Supported = new([
        "NextPage", "PrevPage", "NextOnePage", "PrevOnePage", "MoveNext", "MovePrev", "FirstPage", "LastPage",
        "Open", "Bookmark", "UndoDestinationMove", "RedoDestinationMove", "MoveToFolderAs", "Reveal", "Trash", "Rename", "Copy",
        "Paged", "Continuous", "Masonry", "ToggleDouble", "ToggleDirection", "ZoomIn", "ZoomOut", "ActualPixels", "Fit", "Rotate", "Close", "Quit",
        .. Enumerable.Range(1, 9).Select(i => $"MoveToDestinationFolder{i}")], StringComparer.Ordinal);
    /// <summary>返回默认命令键位；旧 Control 导入时不改成 Command。</summary>
    public static IEnumerable<ShortcutBinding> DefaultBindings()
    {
        yield return new("Left", "NextPage"); yield return new("Right", "PrevPage");
        yield return new("Space", "NextPage"); yield return new("Back", "PrevPage");
        yield return new("Home", "FirstPage"); yield return new("End", "LastPage");
        yield return new("Meta+O", "Open"); yield return new("Meta+B", "Bookmark");
        yield return new("Meta+Z", "UndoDestinationMove"); yield return new("Meta+Shift+Z", "RedoDestinationMove");
        yield return new("Meta+W", "Close"); yield return new("Meta+Q", "Quit");
        yield return new("WheelDown", "NextPage"); yield return new("WheelUp", "PrevPage");
        for (var i = 1; i <= 9; i++) yield return new(i.ToString(), $"MoveToDestinationFolder{i}");
    }
    /// <summary>返回冲突手势与命令集合，不静默覆盖。</summary>
    public static IReadOnlyList<string> Conflicts(IEnumerable<ShortcutBinding> bindings) => bindings
        .GroupBy(b => b.Gesture, StringComparer.OrdinalIgnoreCase).Where(g => g.Select(b => b.Command).Distinct().Count() > 1)
        .Select(g => $"{g.Key}: {string.Join(", ", g.Select(b => b.Command))}").ToArray();
}
