using Avalonia.Controls;
using NeeView.MacOS.ViewModels;
namespace NeeView.MacOS.Views;
public sealed partial class SettingsWindow
{
    private AnimationSettingsViewModel? _animationSettings;
    private void FillAnimation()
    { _animationSettings=new(Config.Current.Image,Config.Current.Archive.Media);this.FindControl<StackPanel>("AnimationSettings")!.DataContext=_animationSettings; }
}
