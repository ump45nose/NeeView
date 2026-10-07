using Avalonia.Controls;
using NeeView.MacOS.ViewModels;
namespace NeeView.MacOS.Views;
public sealed partial class SettingsWindow
{
    private VideoSettingsViewModel? _videoSettings;
    private void FillVideo()
    {_videoSettings=new(Config.Current.Archive.Media);this.FindControl<StackPanel>("VideoSettings")!.DataContext=_videoSettings;}
}
