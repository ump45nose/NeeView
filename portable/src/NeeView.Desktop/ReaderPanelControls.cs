using Avalonia.Controls;

namespace NeeView.Desktop;

/// <summary>面板通用交互适配；错误上报给表现层，不查找全局主窗口。</summary>
internal static class ReaderPanelControls
{
    /// <summary>输入标题和异步动作，返回捕获异常的按钮。</summary>
    public static Button Button(string label, Func<Task> action, ReaderWorkspaceViewModel workspace)
    {
        var button = new Button { Content = label, Margin = new(2) };
        button.Click += async (_, _) => await RunAsync(action, workspace); return button;
    }
    /// <summary>统一观察控件事件的异步任务，避免事件异常逃逸。</summary>
    public static async Task RunAsync(Func<Task> action, ReaderWorkspaceViewModel workspace)
    { try { await action(); } catch (OperationCanceledException) { } catch (Exception error) { workspace.ReportError(error); } }
}
