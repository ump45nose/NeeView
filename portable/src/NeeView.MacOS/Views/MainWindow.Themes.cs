using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
namespace NeeView.MacOS.Views;

public sealed partial class MainWindow
{
    private ThemePresenter? _themePresenter;
    /// <summary>启动层注入唯一主题适配；窗口关闭退订，系统/导入无需重载书籍。</summary>
    public void AttachTheme(ThemePresenter presenter)
    {
        _themePresenter = presenter; presenter.Applied += ThemeApplied;
        Closed += (_, _) => { presenter.Applied -= ThemeApplied; presenter.Dispose(); _themePresenter = null; };
    }
    private void ThemeApplied(ThemeLoadResult result)
    {
        foreach (var control in this.GetVisualDescendants().OfType<Control>().Prepend(this)) control.InvalidateVisual();
        if (result.Error is not null) ShowError("主题加载失败，已使用默认配色：" + result.Error);
    }
}
