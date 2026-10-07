namespace NeeView.MacOS.ViewModels;
/// <summary>进程内打印草稿，不写阅读JSON或改变当前书籍。</summary>
public sealed class PrintViewModel
{
    public PrintParameters Parameters { get; set; } = new();
}
