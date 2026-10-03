using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using NeeView;
namespace NeeView.MacOS.Views;
public sealed partial class MainWindow
{
    private Window? _pageEndDialog;
    /// <summary>原页尾三按钮选择；只返回选择，不在弹窗改当前书籍。</summary>
    private async Task<PageEndAction> ShowPageEndDialogAsync(int direction, CancellationToken token)
    {
        if (_preparing || _closedPrepared || token.IsCancellationRequested) return PageEndAction.None;
        var dialog = new Window { Title = direction < 0 ? "已到首页" : "已到末页", Width = 470, Height = 180, CanResize = false, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        _pageEndDialog = dialog;
        var scope = new NameScope(); NameScope.SetNameScope(dialog, scope);
        var panel = new StackPanel { Margin = new Thickness(20), Spacing = 18 };
        panel.Children.Add(new TextBlock { Text = "接下来如何继续阅读？" });
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        foreach (var (name, text, action) in new[] { ("NextBook", direction < 0 ? "上一本书" : "下一本书", PageEndAction.NextBook), ("Loop", "循环本书", PageEndAction.Loop), ("None", "保持当前位置", PageEndAction.None) })
        {
            var button = new Button { Name = name, Content = text }; scope.Register(name, button); button.Click += (_, _) => dialog.Close(action); buttons.Children.Add(button);
        }
        panel.Children.Add(buttons); dialog.Content = panel;
        try { return await dialog.ShowDialog<PageEndAction>(this); }
        finally { if (ReferenceEquals(_pageEndDialog, dialog)) _pageEndDialog = null; }
    }
}
