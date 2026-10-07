// Copyright (c) NeeLaboratory. 原 ImageEffectView 六分支结构；只生成编辑控件，不持有阅读或文件业务。
using System.Globalization;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using NeeView.Effects;
using NeeView.MacOS.ViewModels;
namespace NeeView.MacOS.Views;
public sealed partial class ImageEffectView : UserControl, IDisposable
{
    private ImageEffectPanelViewModel? _model;
    private bool _building;
    private int _editorSequence;
    private string _editorScope = "";
    private readonly Dictionary<string, Control> _editors = new();
    public Func<string, string, Task<string?>>? AskNameAsync { get; set; }
    public event EventHandler<string>? Failed;
    public ImageEffectView() { AvaloniaXamlLoader.Load(this); }
    public void Attach(BookOperation operation)
    {
        _model?.Dispose(); _model = new(operation); _model.Changed += (_, _) => Dispatcher.UIThread.Post(Build);
        _model.Failed += (_, message) => Failed?.Invoke(this, message);
        var profiles = this.FindControl<ComboBox>("Profiles")!;
        profiles.ItemTemplate = new FuncDataTemplate<EffectProfile>((value, _) => new TextBlock { Text = value?.DisplayName });
        profiles.SelectionChanged += async (_, _) => { if (!_building && profiles.SelectedItem is EffectProfile profile) await _model.SelectAsync(profile.Id); };
        Build();
    }
    private void Build()
    {
        if (_model is null) return;
        var focused = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() as Control;
        var focusedControls = focused is null ? [] : new[] { focused }.Concat(focused.GetVisualAncestors().OfType<Control>()).ToArray();
        var focusedEditor = _editors.FirstOrDefault(pair => focusedControls.Contains(pair.Value)).Key;
        _building = true; _editorSequence = 0; _editors.Clear();
        try
        {
            var profiles = this.FindControl<ComboBox>("Profiles")!; profiles.ItemsSource = _model.Profiles; profiles.SelectedItem = _model.Profiles.FirstOrDefault(p => p.Id == _model.SelectedId);
            var menu = new ContextMenu();
            MenuItem Item(string text, Func<Task> action, bool enabled = true) { var item = new MenuItem { Header = text, IsEnabled = enabled }; item.Click += async (_, _) => await action(); return item; }
            menu.Items.Add(Item("新建预设", () => _model.CreateAsync(false))); menu.Items.Add(Item("克隆预设", () => _model.CreateAsync(true)));
            menu.Items.Add(Item("重命名", async () => { if (AskNameAsync is null) return; var name = await AskNameAsync("重命名预设", (profiles.SelectedItem as EffectProfile)?.DisplayName ?? ""); if (name is not null) await _model.RenameAsync(name); }, _model.SelectedId != 0));
            menu.Items.Add(Item("删除预设", _model.DeleteAsync, _model.SelectedId != 0 && _model.Profiles.Count > 1));
            var more = this.FindControl<Button>("More")!; more.ContextMenu = menu; more.Click -= OpenMore; more.Click += OpenMore;
            var sections = this.FindControl<StackPanel>("Sections")!; sections.Children.Clear(); var draft = _model.Draft;
            var custom = Section(sections, "自定义尺寸", draft.ImageCustomSize.IsEnabled, value => draft.ImageCustomSize.IsEnabled = value);
            Number(custom, "宽度", draft.ImageCustomSize.Width, value => draft.ImageCustomSize.Width = (int)value, 16, 4096);
            Number(custom, "高度", draft.ImageCustomSize.Height, value => draft.ImageCustomSize.Height = (int)value, 16, 4096);
            Choice(custom, "宽高比", draft.ImageCustomSize.AspectRatio, value => draft.ImageCustomSize.AspectRatio = value);
            Number(custom, "应用比例", draft.ImageCustomSize.ApplicabilityRate, value => draft.ImageCustomSize.ApplicabilityRate = value, 0, 1);
            Check(custom, "长边对齐", draft.ImageCustomSize.IsAlignLongSide, value => draft.ImageCustomSize.IsAlignLongSide = value);
            var trim = Section(sections, "裁剪", draft.ImageTrim.IsEnabled, value => draft.ImageTrim.IsEnabled = value);
            foreach (var edge in new[] { "Left", "Right", "Top", "Bottom" })
            { var property = typeof(ImageTrimConfig).GetProperty(edge)!; Number(trim, edge, (double)property.GetValue(draft.ImageTrim)!, value => property.SetValue(draft.ImageTrim, value), 0, .9); }
            var dot = Section(sections, "像素保持", draft.ImageDotKeep.IsEnabled, value => draft.ImageDotKeep.IsEnabled = value);
            Number(dot, "阈值", draft.ImageDotKeep.Threshold, value => draft.ImageDotKeep.Threshold = value, .01, 100);
            var resize = Section(sections, "缩放滤镜", draft.ImageResizeFilter.IsEnabled, value => draft.ImageResizeFilter.IsEnabled = value);
            Choice(resize, "插值", draft.ImageResizeFilter.ResizeInterpolation, value => draft.ImageResizeFilter.ResizeInterpolation = value);
            Check(resize, "锐化", draft.ImageResizeFilter.IsUnsharpMaskEnabled, value => draft.ImageResizeFilter.IsUnsharpMaskEnabled = value);
            var mask = draft.ImageResizeFilter.UnsharpMask;
            Number(resize, "锐化量", mask.Amount, value => mask.Amount = WholeNumber(value), Math.Min(25, mask.Amount), Math.Max(200, mask.Amount));
            Number(resize, "半径", mask.Radius, value => mask.Radius = value, Math.Min(.3, mask.Radius), Math.Max(3, mask.Radius));
            Number(resize, "阈值", mask.Threshold, value => mask.Threshold = WholeNumber(value), Math.Min(0, mask.Threshold), Math.Max(10, mask.Threshold));
            var grid = Section(sections, "网格", draft.ImageGrid.IsEnabled, value => draft.ImageGrid.IsEnabled = value);
            Choice(grid, "目标", draft.ImageGrid.Target, value => draft.ImageGrid.Target = value);
            Text(grid, "颜色", draft.ImageGrid.Color.ToString(), value => draft.ImageGrid.Color = ThemeRgba.Parse(value));
            Number(grid, "横向分格", draft.ImageGrid.DivX, value => draft.ImageGrid.DivX = (int)value, 1, 50);
            Number(grid, "纵向分格", draft.ImageGrid.DivY, value => draft.ImageGrid.DivY = (int)value, 1, 50);
            Check(grid, "方格", draft.ImageGrid.IsSquare, value => draft.ImageGrid.IsSquare = value);
            var effect = Section(sections, "图像效果", draft.ImageEffect.IsEnabled, value => draft.ImageEffect.IsEnabled = value);
            Button(effect, "新增效果层", () => _model.EditAsync((d, _) => d.ImageEffect.Layers.CreateNew()), draft.ImageEffect.Layers.CanCreateNew());
            for (int index = 0; index < draft.ImageEffect.Layers.Count; index++)
            {
                var layer = draft.ImageEffect.Layers[index]; var row = new StackPanel { Spacing = 5, Margin = new Thickness(0,6) };
                _editorScope = "ImageEffect.Layer." + index;
                effect.Children.Add(new Border { BorderBrush = Brushes.Gray, BorderThickness = new Thickness(0,1,0,0), Child = row });
                Check(row, $"层 {index + 1}", layer.IsEnabled, value => layer.IsEnabled = value);
                Choice(row, "类型", layer.EffectType, value => layer.ChangeType(value, draft.ImageEffect, _model.Cache));
                var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 }; row.Children.Add(actions);
                Button(actions,"↑",()=>_model.EditAsync((d,_)=>d.ImageEffect.Layers.MoveUp(layer)),draft.ImageEffect.Layers.CanMoveUp(layer));
                Button(actions,"↓",()=>_model.EditAsync((d,_)=>d.ImageEffect.Layers.MoveDown(layer)),draft.ImageEffect.Layers.CanMoveDown(layer));
                Button(actions,"删除",()=>_model.EditAsync((d,c)=>d.ImageEffect.Layers.Delete(layer,c)),draft.ImageEffect.Layers.CanDelete(layer));
                Button(actions,"重置",()=>_model.EditAsync((_,_)=>layer.Reset()),layer.Effect is not null and not UnknownEffectUnit);
                if (!ImageEffectRenderer.IsSupported(layer.Effect)) row.Children.Add(new TextBlock { Text = "此效果待迁移，参数保留。", TextWrapping = TextWrapping.Wrap, Opacity = .65 });
                if (layer.Effect is { } unit && unit is not UnknownEffectUnit) Parameters(row, unit);
            }
        }
        finally { _building = false; }
        // 编辑事务重建参数表单后，把焦点交还同一编辑位置；后续方向键仍属于控件。
        if (focusedEditor is not null && _editors.TryGetValue(focusedEditor, out var replacement)) replacement.Focus();
    }
    private void OpenMore(object? sender, Avalonia.Interactivity.RoutedEventArgs args) => (sender as Button)?.ContextMenu?.Open(sender as Control);
    private StackPanel Section(StackPanel parent, string title, bool enabled, Action<bool> set)
    {
        _editorScope = title;
        var body = new StackPanel { Spacing = 5 }; Check(body, title, enabled, set); parent.Children.Add(body); return body;
    }
    private void Button(StackPanel parent, string title, Func<Task> action, bool enabled)
    { var button = new Button { Content = title, IsEnabled = enabled, Padding = new Thickness(6,2) }; RegisterEditor(button, title); button.Click += async (_, _) => await action(); parent.Children.Add(button); }
    private void Check(StackPanel parent, string title, bool value, Action<bool> set)
    { var control = new CheckBox { Content = title, IsChecked = value }; RegisterEditor(control, title); control.IsCheckedChanged += async (_, _) => { if (!_building && _model is not null) await _model.EditAsync((_,_) => set(control.IsChecked == true)); }; parent.Children.Add(control); }
    private void Choice<T>(StackPanel parent, string title, T value, Action<T> set) where T : struct, Enum
    {
        var combo = new ComboBox { ItemsSource = Enum.GetValues<T>(), SelectedItem = value, HorizontalAlignment = HorizontalAlignment.Stretch };
        var committed = value;
        async Task Commit()
        {
            if (_building || _model is null || combo.IsDropDownOpen || combo.SelectedItem is not T item || EqualityComparer<T>.Default.Equals(item, committed)) return;
            committed = item;
            await _model.EditAsync((_, _) => set(item));
        }
        // 下拉方向导航只选择候选；关闭后一次提交，不能在popup交互期间删除其宿主。
        combo.SelectionChanged += async (_, _) => await Commit();
        combo.DropDownClosed += async (_, _) => await Commit();
        Row(parent, title, combo);
    }
    private void Number(StackPanel parent, string title, double value, Action<double> set, double min = -100000, double max = 100000) =>
        Text(parent, title, value.ToString(CultureInfo.InvariantCulture), text => { if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) || !double.IsFinite(number) || number < min || number > max) throw new ArgumentException($"{title} 范围为 {min}–{max}。"); set(number); });
    private static int WholeNumber(double value) => value == Math.Truncate(value) ? checked((int)value) : throw new ArgumentException("参数必须为整数。");
    private void Text(StackPanel parent, string title, string value, Action<string> set)
    {
        var input = new TextBox { Text = value, MinWidth = 70 }; string last = value;
        async Task Commit() { if (_building || _model is null || input.Text == last) return; last = input.Text ?? ""; await _model.EditAsync((_,_) => set(last)); }
        input.LostFocus += async (_, _) => await Commit(); input.KeyDown += async (_, e) => { if (e.Key == Key.Enter) { e.Handled = true; await Commit(); } };
        Row(parent, title, input);
    }
    /// <summary>登记有界表单内的稳定编辑位置，用于同一事务完成后的焦点归还。</summary>
    private void RegisterEditor(Control control, string title)
    {
        var key = _editorScope + "/" + title;
        // Colorize控制点同名，但位置有序；其他层参数数目变化不改变后续层的编辑身份。
        var index = 0; var unique = key; while (_editors.ContainsKey(unique)) unique = key + "/" + ++index;
        control.Name = "EffectEditor" + _editorSequence++; _editors.Add(unique, control);
    }
    private void Row(StackPanel parent, string title, Control control)
    { RegisterEditor(control, title); var grid = new Grid { ColumnDefinitions = new("*,*"), ColumnSpacing = 4 }; grid.Children.Add(new TextBlock { Text = title, VerticalAlignment = VerticalAlignment.Center }); Grid.SetColumn(control,1); grid.Children.Add(control); parent.Children.Add(grid); }
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2075", Justification = "正式Mac项目LinkMode=None；表单仅反射固定十四类EffectUnit公开参数，未知类型不进入表单。")]
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "正式Mac项目LinkMode=None保留原参数JSON；Colorize点只在编辑边界转换，不启用AOT裁剪。")]
    private void Parameters(StackPanel parent, EffectUnit unit)
    {
        foreach (var property in unit.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(x => x.CanRead && x.CanWrite && x.Name is not "ExtensionData" and not "BlackRaw" and not "WhiteRaw"))
        {
            if (property.PropertyType == typeof(double)) Number(parent, property.Name, (double)property.GetValue(unit)!, value => property.SetValue(unit,value));
            else if (property.PropertyType == typeof(ThemeRgba)) Text(parent,property.Name,property.GetValue(unit)!.ToString()!,text=>property.SetValue(unit,ThemeRgba.Parse(text)));
            else if (property.PropertyType == typeof(EffectPoint)) Text(parent,property.Name, System.Text.Json.JsonSerializer.Serialize((EffectPoint)property.GetValue(unit)!).Trim('"'),text=>property.SetValue(unit,System.Text.Json.JsonSerializer.Deserialize<EffectPoint>(System.Text.Json.JsonSerializer.Serialize(text))));
        }
        if (unit is ColorizeEffectUnit colorize)
        {
            foreach (var point in colorize.Points) { Text(parent,"控制点颜色",point.Color.ToString(),value=>point.Color=ThemeRgba.Parse(value)); Number(parent,"强度",point.Strength,value=>point.Strength=value,0,100); }
        }
    }
    public void Dispose() { _model?.Dispose(); _model = null; _editors.Clear(); }
}
