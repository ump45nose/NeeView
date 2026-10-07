using Avalonia.Controls;
using NeeView.MacOS.ViewModels;
namespace NeeView.MacOS.Views;
public sealed partial class SettingsWindow
{
    private InformationSettingsViewModel? _informationSettings;
    private void FillInformation()
    {
        _informationSettings = new(Config.Current.Information);
        this.FindControl<ScrollViewer>("InformationSettings")!.DataContext = _informationSettings;
    }
}
