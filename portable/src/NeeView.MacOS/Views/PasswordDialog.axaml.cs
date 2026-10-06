using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
namespace NeeView.MacOS.Views;

/// <summary>纯口令输入表现，不读取来源或保存配置；原业务状态仍归ArchiveKey。</summary>
public sealed partial class PasswordDialog : Window
{
    public PasswordDialog() : this(new("", false)) { }
    public PasswordDialog(ArchiveKeyRequest request)
    {
        AvaloniaXamlLoader.Load(this);
        this.FindControl<TextBlock>("SourceName")!.Text = "请输入密码：" + System.IO.Path.GetFileName(request.ArchivePath);
        this.FindControl<TextBlock>("IncorrectPassword")!.IsVisible = request.IsRetry;
        var input = this.FindControl<TextBox>("PasswordInput")!;
        input.TextChanged += (_, _) => this.FindControl<Button>("AcceptPassword")!.IsEnabled = !string.IsNullOrEmpty(input.Text);
        input.KeyDown += (_, e) => { if (e.Key == Key.Enter) { e.Handled = true; Accept(this, new()); } else if (e.Key == Key.Escape) { e.Handled = true; Close(null); } };
        Opened += (_, _) => input.Focus();
        Closed += (_, _) => input.Text = "";
    }
    private void Accept(object? sender, RoutedEventArgs e)
    { if (this.FindControl<TextBox>("PasswordInput")!.Text is { Length: > 0 } value) Close(value); }
    private void Cancel(object? sender, RoutedEventArgs e) => Close(null);
}
