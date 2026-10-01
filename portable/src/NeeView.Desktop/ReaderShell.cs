using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace NeeView.Desktop;

/// <summary>可独立实例化的页面外壳，只定义插槽并发布命令，不持有窗口或应用服务。</summary>
public sealed partial class ReaderShell : UserControl
{
    /// <summary>请求执行稳定字符串命令，由宿主选择实际处理方式。</summary>
    public event Action<string>? CommandRequested;

    /// <summary>装载页面 XAML；无需依赖装配即可用于预览或替换宿主。</summary>
    public ReaderShell() => AvaloniaXamlLoader.Load(this);

    /// <summary>将按钮 Tag 转换为命令事件，保持页面外观与业务执行分离。</summary>
    /// <param name="sender">带稳定命令 Tag 的页面按钮。</param>
    /// <param name="e">点击事件；实际业务由宿主异步处理。</param>
    private void OnToolbarClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string command }) CommandRequested?.Invoke(command);
    }
}
