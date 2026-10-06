using AppKit;
using Foundation;
using CoreGraphics;
using System.Runtime.InteropServices;
using NeeView;
namespace NeeView.Backends;

/// <summary>官方 AppKit 触控板桥接，按原生精确滚动标记区分鼠标滚轮。</summary>
public sealed class MacTrackpadInput : IPlatformInput
{
    private NSObject? _monitor;
    private Func<PlatformGesture, bool>? _handler;
    private MacRelativePointer? _relative;

    /// <summary>Installs an application-local, exact-window relative pointer lease.</summary>
    public IDisposable? BeginRelativePointer(nint sourceWindow, Action<double, double> handler, Action? released = null)
    {
        _relative?.Dispose();
        return _relative = MacRelativePointer.Begin(sourceWindow, handler, released);
    }
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
        _relative?.Dispose(); _relative = null;
        _handler = null;
        if (_monitor is null) return;
        NSEvent.RemoveMonitor(_monitor); _monitor.Dispose(); _monitor = null;
    }
}

/// <summary>应用局部的相对鼠标租约；失焦和关闭立即归还，不安装全局监听。</summary>
internal sealed class MacRelativePointer : IDisposable
{
    private const NSEventMask Mask = NSEventMask.MouseMoved | NSEventMask.LeftMouseDragged | NSEventMask.RightMouseDragged | NSEventMask.OtherMouseDragged;
    private readonly NSWindow _window;
    private readonly Action<double, double> _handler;
    private readonly Action? _released;
    private NSObject? _monitor;
    private readonly List<NSObject> _notifications = [];
    private readonly bool _acceptsMouseMoved;
    private readonly CGPoint _origin;
    private double _dx, _dy;
    private bool _disposed, _associated, _hidden;

    private MacRelativePointer(NSWindow window, Action<double, double> handler, Action? released)
    {
        _window = window; _handler = handler; _released = released;
        _acceptsMouseMoved = window.AcceptsMouseMovedEvents;
        _origin = GetMouseLocation();
        try
        {
            if (CGAssociateMouseAndMouseCursorPosition(0) != 0) throw new InvalidOperationException("无法取得相对鼠标输入。");
            _associated = true;
            window.AcceptsMouseMovedEvents = true;
            NSCursor.Hide(); _hidden = true;
            _monitor = NSEvent.AddLocalMonitorForEventsMatchingMask(Mask, Handle);
            _notifications.Add(NSNotificationCenter.DefaultCenter.AddObserver(NSWindow.DidResignKeyNotification, _ => Dispose(), window));
            _notifications.Add(NSNotificationCenter.DefaultCenter.AddObserver(NSWindow.WillCloseNotification, _ => Dispose(), window));
            _notifications.Add(NSNotificationCenter.DefaultCenter.AddObserver(NSApplication.DidResignActiveNotification, _ => Dispose()));
        }
        catch { Dispose(); throw; }
    }

    /// <summary>只从当前应用的实际关键窗口核对句柄；不能把任意指针转成 Objective-C 对象。</summary>
    public static MacRelativePointer? Begin(nint sourceWindow, Action<double, double> handler, Action? released)
    {
        if (sourceWindow == 0 || sourceWindow == -1) return null;
        ArgumentNullException.ThrowIfNull(handler);
        var window = NSApplication.SharedApplication.KeyWindow;
        if (window is null || (nint)window.Handle != sourceWindow || !window.IsVisible || !window.IsKeyWindow || !NSApplication.SharedApplication.Active) return null;
        return new(window, handler, released);
    }
    private NSEvent Handle(NSEvent e)
    {
        if (_disposed || !_window.IsVisible || !_window.IsKeyWindow || !NSApplication.SharedApplication.Active)
        { Dispose(); return e; }
        if (e.Window?.Handle != _window.Handle) return e;
        // AppKit delta and CG global coordinates are logical screen points, Y increases downwards.
        var dx = e.DeltaX; var dy = e.DeltaY;
        if (!double.IsFinite(dx) || !double.IsFinite(dy)) return e;
        _dx += dx; _dy += dy;
        try { _handler(dx, dy); } catch (Exception ex) { System.Diagnostics.Trace.WriteLine("Loupe: " + ex.GetType().Name); Dispose(); }
        return null!;
    }
    /// <summary>幂等释放全部监视和鼠标关联；切到其他应用时不移动用户指针。</summary>
    public void Dispose()
    {
        if (_disposed) return; _disposed = true;
        try
        {
            if (_monitor is not null) { NSEvent.RemoveMonitor(_monitor); _monitor.Dispose(); _monitor = null; }
            foreach (var notification in _notifications) { NSNotificationCenter.DefaultCenter.RemoveObserver(notification); notification.Dispose(); }
            _notifications.Clear();
            if (_window.Handle != IntPtr.Zero) _window.AcceptsMouseMovedEvents = _acceptsMouseMoved;
        }
        finally
        {
            if (_associated) { CGAssociateMouseAndMouseCursorPosition(1); _associated = false; }
            if (_hidden) { NSCursor.Unhide(); _hidden = false; }
        }
        try
        {
            if (_window.Handle != IntPtr.Zero && _window.IsVisible && _window.IsKeyWindow && NSApplication.SharedApplication.Active)
                CGWarpMouseCursorPosition(new CGPoint(_origin.X + _dx, _origin.Y + _dy));
        }
        finally
        {
            try { _released?.Invoke(); }
            catch (Exception ex) { System.Diagnostics.Trace.WriteLine("Loupe release: " + ex.GetType().Name); }
        }
    }
    private static CGPoint GetMouseLocation()
    {
        var eventRef = CGEventCreate(IntPtr.Zero);
        try { return eventRef == IntPtr.Zero ? default : CGEventGetLocation(eventRef); }
        finally { if (eventRef != IntPtr.Zero) CFRelease(eventRef); }
    }
    // Apple's documented C APIs omitted by the .NET binding. boolean_t is uint32; CGError is int32.
    // Global CG coordinates handle all displays without Cocoa origin conversion or Retina pixel scaling.
    [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
    private static extern int CGAssociateMouseAndMouseCursorPosition(uint connected);
    [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
    private static extern IntPtr CGEventCreate(IntPtr source);
    [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
    private static extern CGPoint CGEventGetLocation(IntPtr @event);
    [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
    private static extern int CGWarpMouseCursorPosition(CGPoint point);
    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    private static extern void CFRelease(IntPtr cf);
}
