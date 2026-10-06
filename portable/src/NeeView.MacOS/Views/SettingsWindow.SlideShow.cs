using Avalonia.Controls;
using NeeView.MacOS.ViewModels;
namespace NeeView.MacOS.Views;
public sealed partial class SettingsWindow
{
    private SlideShowSettingsViewModel? _slideShowSettings;
    private void FillSlideShow(Func<string, bool>? available)
    {
        _slideShowSettings = new(Config.Current.SlideShow, Config.Current.StartUp, _model!.Commands, available ?? _model.Commands.IsAvailable);
        this.FindControl<ScrollViewer>("SlideShowSettings")!.DataContext = _slideShowSettings;
    }
}
