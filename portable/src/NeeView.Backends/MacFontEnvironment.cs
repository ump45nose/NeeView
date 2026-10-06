using AppKit;
namespace NeeView.Backends;

/// <summary>原系统字体度量的AppKit替换点；必须在启动UI线程调用。</summary>
public static class MacFontEnvironment
{
    /// <summary>消息/菜单字体的默认点大小与DIP一致，不再次乘Retina比例。</summary>
    /// <param name="defaultFontName">Avalonia字体后端可解析的系统默认字体族。</param>
    /// <returns>只含值的Engine快照，原生字体包装在本次调用释放。</returns>
    public static FontEnvironment Read(string defaultFontName)
    {
        using var message = NSFont.MessageFontOfSize(0)
            ?? throw new InvalidOperationException("无法读取 macOS 默认消息字体。");
        using var menu = NSFont.MenuFontOfSize(0)
            ?? throw new InvalidOperationException("无法读取 macOS 默认菜单字体。");
        return new(defaultFontName, (double)message.PointSize, (double)menu.PointSize);
    }
}
