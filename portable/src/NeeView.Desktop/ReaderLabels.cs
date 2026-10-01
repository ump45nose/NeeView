namespace NeeView.Desktop;

/// <summary>界面名称集中维护；稳定命令和枚举存储不依赖翻译文本。</summary>
public sealed record ReaderChoice<T>(T Value, string Label)
{
    public override string ToString() => Label;
}
public static class ReaderLabels
{
    /// <summary>输入业务值，返回阅读设置和命令的中文显示名称。</summary>
    public static string Text(object value)
    {
        var name = value.ToString() ?? "";
        if (name.StartsWith("MoveToDestinationFolder", StringComparison.Ordinal)) return "分类至目标 " + name[23..];
        return name switch
        {
            "Paged" => "分页", "Continuous" => "连续", "Masonry" => "瀑布流", "RightToLeft" => "从右向左", "LeftToRight" => "从左向右",
            "Fit" => "适应窗口", "FitWidth" => "适应宽度", "ActualPixels" => "实际像素 100%",
            "Entry" => "来源顺序", "EntryDescending" => "来源倒序", "FileName" => "文件名", "FileNameDescending" => "文件名倒序", "FileType" => "文件类型", "FileTypeDescending" => "类型倒序",
            "TimeStamp" => "修改时间", "TimeStampDescending" => "时间倒序", "Size" => "文件大小", "SizeDescending" => "大小倒序", "Random" => "随机",
            "Default" => "使用默认值", "Continue" => "沿用当前值", "RestoreOrDefault" => "恢复历史，否则使用默认值", "RestoreOrContinue" => "恢复历史，否则沿用当前值",
            "NextPage" => "下一阅读页", "PrevPage" => "上一阅读页", "NextOnePage" => "下一张图片", "PrevOnePage" => "上一张图片", "MoveNext" => "前进", "MovePrev" => "后退", "FirstPage" => "第一张", "LastPage" => "最后一张",
            "Open" => "打开", "Bookmark" => "添加书签", "UndoDestinationMove" => "撤销移动", "RedoDestinationMove" => "重做移动", "MoveToFolderAs" => "移动到目录", "Reveal" => "在 Finder 显示", "Trash" => "移至废纸篓", "Rename" => "重命名", "Copy" => "复制到目录",
            "ToggleDouble" => "切换单双页", "ToggleDirection" => "切换阅读方向", "ZoomIn" => "放大", "ZoomOut" => "缩小", "Rotate" => "顺时针旋转", "Close" => "关闭窗口", "Quit" => "退出应用", _ => name
        };
    }
    /// <summary>将固定值序列转换为可替换翻译的选项。</summary>
    public static ReaderChoice<T>[] Choices<T>(IEnumerable<T> values) => values.Select(v => new ReaderChoice<T>(v, Text(v!))).ToArray();
    /// <summary>从 ComboBox 选项读取稳定值，界面翻译不会改变保存含义。</summary>
    public static T Value<T>(object? selected) => ((ReaderChoice<T>)selected!).Value;
}
