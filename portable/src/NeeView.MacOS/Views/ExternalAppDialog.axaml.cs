using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using NeeView.MacOS.ViewModels;
namespace NeeView.MacOS.Views;

/// <summary>原外部应用集合的布局适配，业务与持久化由父设置/Engine负责。</summary>
public sealed partial class ExternalAppDialog : Window
{
    public ExternalApplicationsViewModel Model => (ExternalApplicationsViewModel)DataContext!;
    public ExternalAppDialog() { AvaloniaXamlLoader.Load(this); DataContext = new ExternalApplicationsViewModel([]); }
    public ExternalAppDialog(IEnumerable<ExternalApp> source) : this() => DataContext = new ExternalApplicationsViewModel(source);
    private void Add_Click(object? sender, RoutedEventArgs e) => Model.Add();
    private void Remove_Click(object? sender, RoutedEventArgs e) => Model.Remove();
    private void Up_Click(object? sender, RoutedEventArgs e) => Model.Move(-1);
    private void Down_Click(object? sender, RoutedEventArgs e) => Model.Move(1);
    private void Ok_Click(object? sender, RoutedEventArgs e) => Close(Model.ToCollection());
    private void Cancel_Click(object? sender, RoutedEventArgs e) => Close(null);
    protected override void OnKeyDown(KeyEventArgs e) { if (e.Key == Key.Escape) { Close(null); e.Handled = true; } else base.OnKeyDown(e); }
}
