using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using NeeView;
using NeeView.MacOS.ViewModels;
namespace NeeView.MacOS.Views;

/// <summary>原设置窗口的 P1 阅读页面；布局独立于配置恢复规则。</summary>
public sealed partial class SettingsWindow : Window
{
    private ReaderWorkspaceViewModel? _model;
    /// <summary>独立加载布局，不依赖具体存储或解码后端。</summary>
    public SettingsWindow() => AvaloniaXamlLoader.Load(this);
    /// <summary>传入业务表现模型，编辑副本直到用户保存。</summary>
    public SettingsWindow(ReaderWorkspaceViewModel model) : this() { _model = model; Fill(); }
    /// <summary>切换当前书籍和默认设置的编辑作用域。</summary>
    private void Scope_Changed(object? sender, SelectionChangedEventArgs e) { if (_model is not null) Fill(); }
    /// <summary>从原设置类型填入表单，页面模式和方向枚举值保持原顺序。</summary>
    private void Fill()
    {
        var setting = this.FindControl<ComboBox>("Scope")!.SelectedIndex == 1 ? Config.Current.BookSettingDefault : _model!.Operation.Book?.Setting ?? Config.Current.BookSetting;
        this.FindControl<ComboBox>("Mode")!.SelectedIndex = (int)setting.PageMode;
        this.FindControl<ComboBox>("Order")!.SelectedIndex = (int)setting.BookReadOrder;
        this.FindControl<CheckBox>("Divide")!.IsChecked = setting.IsSupportedDividePage;
        this.FindControl<CheckBox>("Wide")!.IsChecked = setting.IsSupportedWidePage;
        this.FindControl<CheckBox>("First")!.IsChecked = setting.IsSupportedSingleFirstPage;
        this.FindControl<CheckBox>("Last")!.IsChecked = setting.IsSupportedSingleLastPage;
    }
    /// <summary>将当前表单写回选定原设置对象，不改写未迁移字段。</summary>
    private void Apply(BookSettingConfig setting)
    {
        setting.PageMode = (PageMode)this.FindControl<ComboBox>("Mode")!.SelectedIndex;
        setting.BookReadOrder = (PageReadOrder)this.FindControl<ComboBox>("Order")!.SelectedIndex;
        setting.IsSupportedDividePage = this.FindControl<CheckBox>("Divide")!.IsChecked == true;
        setting.IsSupportedWidePage = this.FindControl<CheckBox>("Wide")!.IsChecked == true;
        setting.IsSupportedSingleFirstPage = this.FindControl<CheckBox>("First")!.IsChecked == true;
        setting.IsSupportedSingleLastPage = this.FindControl<CheckBox>("Last")!.IsChecked == true;
    }
    /// <summary>设置应用成功并保存 JSON 后关闭；失败留在表单中。</summary>
    private async void Save_Click(object? sender, RoutedEventArgs e)
    {
        if (_model is null) return;
        try
        {
            if (this.FindControl<ComboBox>("Scope")!.SelectedIndex == 1) Apply(Config.Current.BookSettingDefault);
            else if (_model.Operation.Book is not null) await _model.Operation.ApplySettingAsync(Apply);
            else Apply(Config.Current.BookSetting);
            await _model.Operation.SaveAsync(); Close();
        }
        catch (Exception ex) { this.FindControl<TextBlock>("Message")!.Text = "保存失败：" + ex.Message; }
    }
    /// <summary>取消不写入配置。</summary>
    private void Cancel_Click(object? sender, RoutedEventArgs e) => Close();
}
