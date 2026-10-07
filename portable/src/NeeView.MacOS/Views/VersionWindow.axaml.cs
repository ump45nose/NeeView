using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using NeeView.MacOS.ViewModels;
namespace NeeView.MacOS.Views;

/// <summary>原版本窗口的纯表现转换；系统动作由VM契约执行，不访问文件或阅读状态。</summary>
public sealed partial class VersionWindow : Window
{
    private VersionWindowViewModel? Model => DataContext as VersionWindowViewModel;
    /// <summary>加载唯一正式XAML，关闭释放表现动作。</summary>
    public VersionWindow() { AvaloniaXamlLoader.Load(this); Closed += (_, _) => Model?.Dispose(); }
    /// <summary>使用实际平台和系统剪贴板创建原版本表现模型。</summary>
    public VersionWindow(IPlatformService platform, IApplicationReleaseService? releases = null) : this() => DataContext = new VersionWindowViewModel(platform, CopyTextAsync, releases: releases);
    private async Task CopyTextAsync(string text, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (Clipboard is not { } clipboard) throw new NotSupportedException("系统剪贴板不可用。");
        await clipboard.SetTextAsync(text);
    }
    private async void CopyVersion(object? sender, RoutedEventArgs e) { if (Model is { } model) await model.CopyVersionAsync(); }
    private async void OpenLicense(object? sender, RoutedEventArgs e) { if (Model is { } model) await model.OpenLicenseAsync(); }
    private async void OpenProject(object? sender, RoutedEventArgs e) { if (Model is { } model) await model.OpenProjectAsync(); }
    private async void OpenOriginalProject(object? sender, RoutedEventArgs e) { if (Model is { } model) await model.OpenOriginalProjectAsync(); }
    private async void CheckRelease(object? sender, RoutedEventArgs e) { if (Model is { } model) await model.CheckReleaseAsync(); }
    private async void OpenRelease(object? sender, RoutedEventArgs e) { if (Model is { } model) await model.OpenReleaseAsync(); }
    private async void OpenDownload(object? sender, RoutedEventArgs e) { if (Model is { } model) await model.OpenDownloadAsync(); }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { Close(); e.Handled = true; }
        else if (e.Key == Key.C && e.KeyModifiers is KeyModifiers.Control or KeyModifiers.Meta) { CopyVersion(this, new()); e.Handled = true; }
        else base.OnKeyDown(e);
    }
}
