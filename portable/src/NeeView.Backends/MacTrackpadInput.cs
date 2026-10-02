using AppKit;
using Foundation;
using NeeView;
namespace NeeView.Backends;

/// <summary>官方 AppKit 触控板桥接，按原生精确滚动标记区分鼠标滚轮。</summary>
public sealed class MacTrackpadInput : IPlatformInput
{
    private NSObject? _monitor;
    private Func<PlatformGesture, bool>? _handler;
    /// <summary>在 UI 线程安装局部监听，只消费应用内查看器事件。</summary>
    public void Attach(Func<PlatformGesture, bool> handler)
    {
        Dispose(); _handler = handler;
        _monitor = NSEvent.AddLocalMonitorForEventsMatchingMask(NSEventMask.ScrollWheel | NSEventMask.EventMagnify, Handle);
    }
    /// <summary>转换为窗口内容坐标；普通鼠标滚轮留给 Avalonia 命令绑定，避免双重处理。</summary>
    private NSEvent Handle(NSEvent e)
    {
        if (e.Window?.ContentView is not { } content || (e.Type != NSEventType.Magnify && !e.HasPreciseScrollingDeltas)) return e;
        var point = content.ConvertPointFromView(e.LocationInWindow, null);
        var gesture = new PlatformGesture(e.Type == NSEventType.Magnify, point.X, content.IsFlipped ? point.Y : content.Bounds.Height - point.Y,
            e.ScrollingDeltaX, e.ScrollingDeltaY, e.Type == NSEventType.Magnify ? e.Magnification : 0, (nint)e.Window.Handle);
        try { if (_handler?.Invoke(gesture) == true) return null!; }
        catch (Exception ex) { System.Diagnostics.Trace.WriteLine("触控板：" + ex.Message); }
        return e;
    }
    /// <summary>关闭窗口时移除局部监听，不能让旧窗口继续消费新窗口手势。</summary>
    public void Dispose()
    {
        _handler = null;
        if (_monitor is null) return;
        NSEvent.RemoveMonitor(_monitor); _monitor.Dispose(); _monitor = null;
    }
}
