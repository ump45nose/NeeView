// Copyright (c) NeeLaboratory. 转换自原 SliderTextBox/SliderValueConverter，不模拟 WPF 绑定层。
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
namespace NeeView.MacOS.Views;

/// <summary>底部页号的独立表现控件；只提交原始索引，正文定位由宿主处理。</summary>
public sealed partial class SliderTextBox : UserControl
{
    public static readonly StyledProperty<double> ValueProperty = AvaloniaProperty.Register<SliderTextBox, double>(nameof(Value));
    public static readonly StyledProperty<int> MaximumProperty = AvaloniaProperty.Register<SliderTextBox, int>(nameof(Maximum));
    public static readonly StyledProperty<object?> SourceKeyProperty = AvaloniaProperty.Register<SliderTextBox, object?>(nameof(SourceKey));
    public double Value { get => GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public int Maximum { get => GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }
    public object? SourceKey { get => GetValue(SourceKeyProperty); set => SetValue(SourceKeyProperty, value); }
    public Func<object?, int, Task>? RequestPageAsync { get; set; }
    public event EventHandler? ReturnFocusRequested;
    public event EventHandler<Exception>? NavigationFailed;
    private bool _editing;
    private object? _editSource;
    private double _wheel;
    private TextBox Input => this.FindControl<TextBox>("NumberInput")!;

    /// <summary>加载正式模板，不创建来源、配置或引擎实例。</summary>
    public SliderTextBox() { AvaloniaXamlLoader.Load(this); RefreshDisplay(); }

    /// <summary>仅刷新显示；切书撤销旧输入，普通保存/刷新不覆盖正在编辑的文本。</summary>
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == SourceKeyProperty) CancelEdit();
        if (change.Property == ValueProperty || change.Property == MaximumProperty) RefreshDisplay();
    }

    /// <summary>同步原一起始页号显示，最大页索引与正文方向分离。</summary>
    private void RefreshDisplay()
    {
        if (this.FindControl<TextBlock>("NumberDisplay") is { } display)
            display.Text = $"{(int)Value + 1} / {Maximum + 1}";
    }

    /// <summary>点击或 Tab 获焦进入编辑，并全选现有原始页号。</summary>
    public void BeginEdit()
    {
        if (!IsEffectivelyEnabled || SourceKey is null || _editing) return;
        _editing = true; _editSource = SourceKey; _wheel = 0;
        Input.Text = ((int)Value + 1).ToString(System.Globalization.CultureInfo.CurrentCulture);
        Input.IsVisible = true; Input.Focus(); Input.SelectAll();
    }

    /// <summary>来源切换或关闭取消尚未提交的输入，隐藏不能触发旧书定位。</summary>
    public void CancelEdit()
    {
        _editing = false; _editSource = null;
        if (this.FindControl<TextBox>("NumberInput") is { } input) input.IsVisible = false;
    }

    /// <summary>沿原转换器解析一起始数字并按原绑定舍入；非法文本保持当前临时选择。</summary>
    /// <param name="text">页号文本，允许原转换器支持的小数及科学计数。</param>
    /// <param name="maximum">当前书籍最大零起始索引。</param>
    /// <param name="index">范围校正后的原始页索引。</param>
    /// <returns>是否解析成功；NaN 不进入索引转换。</returns>
    internal static bool TryConvertBack(string? text, int maximum, out int index)
    {
        index = 0;
        if (!double.TryParse(text, out var number) || double.IsNaN(number)) return false;
        // 原 converter 先限制一起始值，再减一；WPF 默认 double→int 绑定按 Convert.ToInt32 舍入。
        number = Math.Clamp(number, 1, int.MaxValue) - 1;
        index = Convert.ToInt32(Math.Clamp(number, 0, Math.Max(0, maximum))); return true;
    }

    /// <summary>Enter/失焦强制提交原始页选择；等待期间编辑新文本时保留新草稿。</summary>
    /// <returns>是否为有效文本且来源仍匹配；无效文本仍按原 ValueChanged 确认已有选择。</returns>
    public async Task<bool> CommitAsync()
    {
        if (!_editing || !ReferenceEquals(_editSource, SourceKey) || RequestPageAsync is null) return false;
        var source = _editSource; var text = Input.Text;
        var valid = TryConvertBack(text, Maximum, out var index);
        if (!valid) index = (int)Math.Clamp(Value, 0, Maximum);
        await RequestPageAsync(source, index);
        if (ReferenceEquals(source, SourceKey) && _editing && Input.Text == text)
        { Input.Text = ((int)Value + 1).ToString(System.Globalization.CultureInfo.CurrentCulture); Input.SelectAll(); }
        return valid && ReferenceEquals(source, SourceKey);
    }

    /// <summary>单击激活；事件由输入框接管，避免正文手势重复处理。</summary>
    private void Number_Pressed(object? sender, PointerPressedEventArgs e)
    { if (!_editing && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) { BeginEdit(); e.Handled = true; } }
    /// <summary>键盘进入根容器时切换到原编辑模式。</summary>
    private void Number_Focused(object? sender, FocusChangedEventArgs e) { if (ReferenceEquals(e.Source, sender)) BeginEdit(); }
    /// <summary>获焦时全选，符合原 SliderTextBox 输入反馈。</summary>
    private void Input_Focused(object? sender, FocusChangedEventArgs e) => Input.SelectAll();
    /// <summary>Enter 保持编辑；Escape 转移焦点，其失焦提交与原项目一致。</summary>
    private async void Input_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyModifiers != KeyModifiers.None) return;
        if (e.Key == Key.Escape) { e.Handled = true; ReturnFocusRequested?.Invoke(this, EventArgs.Empty); }
        else if (e.Key == Key.Enter)
        { e.Handled = true; try { await CommitAsync(); } catch (Exception ex) { NavigationFailed?.Invoke(this, ex); } }
    }
    /// <summary>普通失焦提交并退出编辑；提交时已抓取来源和文本，不会写入新书。</summary>
    private async void Input_LostFocus(object? sender, FocusChangedEventArgs e)
    {
        if (!_editing) return;
        var request = CommitAsync(); _editing = false; Input.IsVisible = false;
        try { await request; } catch (Exception ex) { NavigationFailed?.Invoke(this, ex); }
    }
    /// <summary>原页号框滚轮每格改变一页并立即确认，不采用双页同步步长。</summary>
    private async void Input_Wheel(object? sender, PointerWheelEventArgs e)
    {
        e.Handled = true; _wheel += e.Delta.Y; var steps = (int)_wheel; _wheel -= steps;
        if (steps == 0 || !_editing) return;
        Input.Text = ((int)Math.Clamp(Value - steps, 0, Maximum) + 1).ToString(System.Globalization.CultureInfo.CurrentCulture);
        try { await CommitAsync(); } catch (Exception ex) { NavigationFailed?.Invoke(this, ex); }
    }
}
