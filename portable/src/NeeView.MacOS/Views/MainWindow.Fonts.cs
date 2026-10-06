namespace NeeView.MacOS.Views;

public sealed partial class MainWindow
{
    private FontPresenter? _fontPresenter;
    /// <summary>启动层接入同一配置的字体资源；关闭释放引用，无全局字体监听。</summary>
    public void AttachFonts(FontPresenter presenter)
    { _fontPresenter = presenter; RefreshFonts(); Closed += (_, _) => _fontPresenter = null; }
    private void RefreshFonts()
    {
        _fontPresenter?.Apply();
        if (_fontPresenter?.FallbackMessage is { } message) ShowError(message);
    }
}
