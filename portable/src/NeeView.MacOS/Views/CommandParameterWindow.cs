using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using NeeView.MacOS.ViewModels;
namespace NeeView.MacOS.Views;

/// <summary>独立参数弹窗；布局和编辑控件可调整，不实现原命令业务。</summary>
public sealed class CommandParameterWindow : Window
{
    public CommandParameterEdit Draft { get; }
    /// <summary>候选只在应用按钮确认后交给父设置草稿；父设置保存失败仍可重试。</summary>
    public CommandParameterWindow(CommandParameterEdit draft, string title)
    {
        Draft = draft; Title = title + " · 参数"; Width = 460; Height = 520; MinWidth = 360;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var scope = new Avalonia.Controls.NameScope(); Avalonia.Controls.NameScope.SetNameScope(this, scope);
        var fields = new StackPanel { Spacing = 12, Margin = new Thickness(20) };
        fields.Children.Add(new TextBlock { Text = title, FontSize = 18 });
        foreach (var field in draft.Fields)
        {
            var label = CommandParameterEdit.Label(field.Name);
            if (field.PropertyType == typeof(bool))
            {
                var check = new CheckBox { Name = field.Name, Content = label, IsChecked = (bool)field.GetValue(draft.Value)! };
                check.IsEnabled = field.Name != "PagesAsOne";
                scope.Register(field.Name, check);
                check.IsCheckedChanged += (_, _) => field.SetValue(draft.Value, check.IsChecked == true); fields.Children.Add(check);
            }
            else if (field.PropertyType.IsEnum)
            {
                fields.Children.Add(new TextBlock { Text = label }); var choices = Enum.GetValues(field.PropertyType).Cast<object>().ToArray();
                var combo = new ComboBox { Name = field.Name, ItemsSource = choices.Select(CommandParameterEdit.EnumLabel).ToArray(), SelectedIndex = Array.IndexOf(choices, field.GetValue(draft.Value)), HorizontalAlignment = HorizontalAlignment.Stretch };
                scope.Register(field.Name, combo);
                combo.SelectionChanged += (_, _) => { if (combo.SelectedIndex >= 0) field.SetValue(draft.Value, choices[combo.SelectedIndex]); }; fields.Children.Add(combo);
            }
            else
            {
                fields.Children.Add(new TextBlock { Text = label });
                var value = (decimal)Convert.ToDouble(field.GetValue(draft.Value));
                var maximum = field.Name == "Angle" ? 180 : field.Name is "Scale" or "Scroll" ? 1 : 1000;
                // 原参数的合法范围由setter决定；表单不能截断未编辑的旧值。
                var number = new NumericUpDown { Name = field.Name, Minimum = Math.Min(0, value), Maximum = Math.Max(maximum, value), Value = value,
                    Increment = field.PropertyType == typeof(int) ? 1 : .05m, FormatString = field.PropertyType == typeof(int) ? "0" : "0.#####" };
                scope.Register(field.Name, number);
                number.ValueChanged += (_, _) => { if (number.Value is { } value) field.SetValue(draft.Value, Convert.ChangeType(value, field.PropertyType)); }; fields.Children.Add(number);
            }
        }
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Margin = new Thickness(20) };
        var cancel = new Button { Name = "CancelParameter", Content = "取消" }; cancel.Click += (_, _) => Close(null);
        var apply = new Button { Name = "ApplyParameter", Content = "应用" }; apply.Click += (_, _) => Close(Draft);
        scope.Register(cancel.Name!, cancel); scope.Register(apply.Name!, apply);
        buttons.Children.Add(cancel); buttons.Children.Add(apply);
        var grid = new Grid { RowDefinitions = new("*,Auto") }; grid.Children.Add(new ScrollViewer { Content = fields }); Grid.SetRow(buttons, 1); grid.Children.Add(buttons); Content = grid;
    }
}
