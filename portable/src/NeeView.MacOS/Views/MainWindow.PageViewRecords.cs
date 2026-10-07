using Avalonia.Threading;
namespace NeeView.MacOS.Views;
public sealed partial class MainWindow
{
    private PageViewRecorder? _pageViewRecorder;
    private void InitializePageViewRecorder()
    {
        _pageViewRecorder = new(error: error => Dispatcher.UIThread.Post(() => ShowError("页面记录写入失败：" + error.Message)));
        _pageViewRecorder.Attach(_model!.Operation);
    }
}
