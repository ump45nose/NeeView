// Copyright (c) NeeLaboratory. 原 BookCommandTools/WindowActivator 的 Mac 宿主适配，MIT。
using Avalonia.Controls;
namespace NeeView.MacOS.Views;
public sealed partial class MainWindow
{
    /// <summary>动态报告当前实际归档器；唯一后端不提供空执行的重载或伪造插件选择。</summary>
    private void OpenSelectArchiverMenu()
    {
        if (_model?.Operation.Book is not { } book) return;
        var item = new MenuItem { Header = book.Source.BackendName, IsChecked = true, IsEnabled = false };
        ToolTip.SetTip(item, "此来源使用单一读取后端；Windows 专用插件不适用于 macOS。");
        var menu = new ContextMenu(); menu.Items.Add(item);
        menu.Items.Add(new MenuItem { Header = "此来源没有其他可切换的读取后端", IsEnabled = false });
        Viewer.ContextMenu?.Close(); Viewer.ContextMenu = menu;
        menu.Closed += (_, _) => { if (ReferenceEquals(Viewer.ContextMenu, menu)) Viewer.ContextMenu = null; };
        menu.Open(Viewer);
    }
    /// <summary>原自身窗口顺序：主窗、中央浮窗、侧栏浮窗；跨其他应用交给 macOS。</summary>
    internal IReadOnlyList<Window> GetReaderWindows()
    {
        var windows = new List<Window> { this };
        if (FloatingMainView is { IsVisible: true } viewer) windows.Add(viewer);
        if (_sidePanels is not null) windows.AddRange(_sidePanels.FloatingWindows.Where(w => w.IsVisible));
        return windows.Distinct().ToArray();
    }
    /// <summary>前后轮巡真正已打开的自身窗口；最小化先恢复。仅用户命令触发激活。</summary>
    private void FocusReaderWindow(int direction)
    {
        var windows = GetReaderWindows(); if (windows.Count < 2) return;
        var active = windows.Select((w, i) => (w, i)).FirstOrDefault(x => x.w.IsActive).i;
        var next = windows[(active + Math.Sign(direction) + windows.Count) % windows.Count];
        if (next.WindowState == WindowState.Minimized) next.WindowState = WindowState.Normal;
        next.Activate();
    }
}
