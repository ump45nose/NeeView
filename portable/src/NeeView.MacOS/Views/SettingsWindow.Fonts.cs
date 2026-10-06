using Avalonia.Controls;
using Avalonia.Media;
using NeeView.MacOS.ViewModels;
namespace NeeView.MacOS.Views;

public sealed partial class SettingsWindow
{
    private FontSettingsViewModel? _fontSettings;
    /// <summary>只初始化独立表单及字体族列表，显示资源等待宿主保存成功后应用。</summary>
    private void FillFonts()
    {
        _fontSettings = new(Config.Current.Fonts, FontManager.Current.SystemFonts.Select(f => f.Name));
        this.FindControl<ScrollViewer>("FontSettings")!.DataContext = _fontSettings;
    }
}
