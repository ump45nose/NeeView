using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Input;
using NeeView;
using NeeView.MacOS.ViewModels;
namespace NeeView.MacOS.Views;

/// <summary>原设置窗口的阅读和输入页面；布局独立于配置恢复规则。</summary>
public sealed partial class SettingsWindow : Window
{
    private ReaderWorkspaceViewModel? _model;
    private IReadOnlyList<ShortcutEdit> _inputs = [];
    private bool _initialized;
    /// <summary>独立加载布局，不依赖具体存储或解码后端。</summary>
    public SettingsWindow() { AvaloniaXamlLoader.Load(this); _initialized = true; }
    /// <summary>传入业务表现模型，编辑副本直到用户保存。</summary>
    public SettingsWindow(ReaderWorkspaceViewModel model, Func<string, bool>? available = null) : this()
    {
        _model = model;
        _inputs = model.Commands.Definitions.Select(d => new ShortcutEdit(d, model.SaveData.GetShortcut(d.Name, d.Shortcut), available?.Invoke(d.Name) ?? model.Commands.IsAvailable(d.Name))).ToArray();
        this.FindControl<ListBox>("InputList")!.ItemsSource = _inputs; Fill(); FillFilm();
        this.FindControl<ComboBox>("BookshelfGroup")!.SelectedIndex = (int)Config.Current.Bookshelf.FolderSortOrder;
        this.FindControl<CheckBox>("PrioritizeBookMove")!.IsChecked = Config.Current.Book.IsPrioritizeBookMove;
    }
    /// <summary>保持原左导航、右内容结构；只切换设置页面，不应用编辑。</summary>
    private void Navigation_Changed(object? sender, SelectionChangedEventArgs e)
    {
        if (!_initialized) return;
        if (this.FindControl<ScrollViewer>("ReadingSettings") is not { } reading || this.FindControl<Grid>("InputSettings") is not { } input) return;
        var index = (sender as ListBox)?.SelectedIndex ?? 0;
        reading.IsVisible = index == 0; input.IsVisible = index == 1;
        this.FindControl<ScrollViewer>("FilmSettings")!.IsVisible = index == 2;
    }
    /// <summary>按名称及命令标识过滤编辑副本，未展示的键位也保留。</summary>
    private void InputSearch_Changed(object? sender, TextChangedEventArgs e)
    {
        if (!_initialized) return;
        var text = (sender as TextBox)?.Text ?? "";
        if (this.FindControl<ListBox>("InputList") is { } list) list.ItemsSource = _inputs.Where(i => i.Label.Contains(text, StringComparison.CurrentCultureIgnoreCase) || i.Name.Contains(text, StringComparison.OrdinalIgnoreCase)).ToArray();
    }
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
    /// <summary>只调整胶片条/滑条时不重建原正文帧，保持表现设置与阅读规则分离。</summary>
    /// <param name="setting">当前书籍真实阅读设置。</param>
    /// <returns>表单内任一已迁入阅读字段是否改变。</returns>
    private bool HasReadingChanges(BookSettingConfig setting) =>
        (int)setting.PageMode != this.FindControl<ComboBox>("Mode")!.SelectedIndex ||
        (int)setting.BookReadOrder != this.FindControl<ComboBox>("Order")!.SelectedIndex ||
        setting.IsSupportedDividePage != (this.FindControl<CheckBox>("Divide")!.IsChecked == true) ||
        setting.IsSupportedWidePage != (this.FindControl<CheckBox>("Wide")!.IsChecked == true) ||
        setting.IsSupportedSingleFirstPage != (this.FindControl<CheckBox>("First")!.IsChecked == true) ||
        setting.IsSupportedSingleLastPage != (this.FindControl<CheckBox>("Last")!.IsChecked == true);
    /// <summary>读取原胶片条、滑条及共享步长参数到编辑控件，取消不修改配置。</summary>
    private void FillFilm()
    {
        var film = Config.Current.FilmStrip; var slider = Config.Current.Slider;
        foreach (var (name, value) in new[] { ("FilmEnabled", film.IsEnabled), ("FilmHide", film.IsHideFilmStrip), ("FilmNumber", film.IsVisibleNumber), ("FilmCenter", film.IsSelectedCenter), ("FilmDetail", film.IsDetailPopupEnabled), ("SliderLinked", slider.IsSliderLinkedFilmStrip), ("SliderSync", slider.IsSyncPageMode) })
            this.FindControl<CheckBox>(name)!.IsChecked = value;
        this.FindControl<NumericUpDown>("FilmWidth")!.Value = (decimal)Math.Min(film.ImageWidth, (double)decimal.MaxValue / 2);
        this.FindControl<ComboBox>("FilmWheel")!.SelectedIndex = (int)film.MouseWheelAction;
        this.FindControl<ComboBox>("SliderOrder")!.SelectedIndex = (int)slider.SliderDirection;
        this.FindControl<CheckBox>("SliderEnabled")!.IsChecked = slider.IsEnabled;
        this.FindControl<ComboBox>("SliderIndexLayout")!.SelectedIndex = (int)slider.SliderIndexLayout;
        this.FindControl<NumericUpDown>("SliderThickness")!.Value = (decimal)slider.Thickness;
        this.FindControl<NumericUpDown>("SliderOpacity")!.Value = (decimal)Math.Clamp(slider.Opacity, 0, 1);
        this.FindControl<ComboBox>("SliderWheel")!.SelectedIndex = (int)slider.MouseWheelAction;
        this.FindControl<NumericUpDown>("MoveSize")!.Value = _model!.SaveData.GetMoveSizeParameter().Size;
    }
    /// <summary>只写当前已迁入字段；播放列表标记、全局自动隐藏等原字段保持原值。</summary>
    private void ApplyFilm()
    {
        var film = Config.Current.FilmStrip; var slider = Config.Current.Slider;
        film.IsEnabled = this.FindControl<CheckBox>("FilmEnabled")!.IsChecked == true;
        film.IsHideFilmStrip = this.FindControl<CheckBox>("FilmHide")!.IsChecked == true;
        film.IsVisibleNumber = this.FindControl<CheckBox>("FilmNumber")!.IsChecked == true;
        film.IsSelectedCenter = this.FindControl<CheckBox>("FilmCenter")!.IsChecked == true;
        film.IsDetailPopupEnabled = this.FindControl<CheckBox>("FilmDetail")!.IsChecked == true;
        film.ImageWidth = (double)(this.FindControl<NumericUpDown>("FilmWidth")!.Value ?? 96);
        film.MouseWheelAction = (FilmStripMouseWheelAction)Math.Max(0, this.FindControl<ComboBox>("FilmWheel")!.SelectedIndex);
        slider.SliderDirection = (SliderDirection)Math.Max(0, this.FindControl<ComboBox>("SliderOrder")!.SelectedIndex);
        slider.IsSliderLinkedFilmStrip = this.FindControl<CheckBox>("SliderLinked")!.IsChecked == true;
        slider.IsSyncPageMode = this.FindControl<CheckBox>("SliderSync")!.IsChecked == true;
        slider.IsEnabled = this.FindControl<CheckBox>("SliderEnabled")!.IsChecked == true;
        slider.SliderIndexLayout = (SliderIndexLayout)Math.Max(0, this.FindControl<ComboBox>("SliderIndexLayout")!.SelectedIndex);
        slider.Thickness = (double)(this.FindControl<NumericUpDown>("SliderThickness")!.Value ?? 25);
        slider.Opacity = (double)(this.FindControl<NumericUpDown>("SliderOpacity")!.Value ?? 1);
        slider.MouseWheelAction = (SliderMouseWheelAction)Math.Max(0, this.FindControl<ComboBox>("SliderWheel")!.SelectedIndex);
        var size = (int)(this.FindControl<NumericUpDown>("MoveSize")!.Value ?? 10);
        if (size != _model!.SaveData.GetMoveSizeParameter().Size) _model.SaveData.SetCommandParameter("PrevSizePage", new MoveSizePageCommandParameter { Size = size });
    }
    /// <summary>设置应用成功并保存 JSON 后关闭；失败留在表单中。</summary>
    private async void Save_Click(object? sender, RoutedEventArgs e)
    {
        if (_model is null) return;
        try
        {
            ValidateInputs(_inputs);
            if (this.FindControl<ComboBox>("Scope")!.SelectedIndex == 1) Apply(Config.Current.BookSettingDefault);
            else if (_model.Operation.Book is { } book)
            { if (HasReadingChanges(book.Setting)) await _model.Operation.ApplySettingAsync(Apply); }
            else Apply(Config.Current.BookSetting);
            // 只写用户修改的差分，避免一次保存就展开全部 235 条默认命令。
            foreach (var input in _inputs.Where(i => i.Value.Trim() != i.OriginalValue.Trim())) _model.SaveData.SetShortcut(input.Name, input.Value.Trim());
            ApplyFilm();
            Config.Current.Bookshelf.FolderSortOrder = (FolderSortOrder)Math.Max(0, this.FindControl<ComboBox>("BookshelfGroup")!.SelectedIndex);
            Config.Current.Book.IsPrioritizeBookMove = this.FindControl<CheckBox>("PrioritizeBookMove")!.IsChecked == true;
            // 分组设置只重排已有元数据，不能重扫来源或意外重新洗牌。
            _model.Operation.Bookshelf.Reorder();
            await _model.Operation.SaveAsync(); Close();
        }
        catch (Exception ex) { this.FindControl<TextBlock>("Message")!.Text = "保存失败：" + ex.Message; }
    }
    /// <summary>保存前校验键位与冲突；不自动将旧 Control 转成 Command。</summary>
    internal static void ValidateInputs(IEnumerable<ShortcutEdit> inputs)
    {
        var seen = new Dictionary<string, ShortcutEdit>(StringComparer.OrdinalIgnoreCase);
        foreach (var input in inputs)
        foreach (var raw in input.Value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var shortcut = raw;
            var mouseTokens = raw.Split('+');
            bool isMouse = mouseTokens[^1] is "LeftClick" or "RightClick" or "MiddleClick" or "WheelUp" or "WheelDown"
                && mouseTokens[..^1].All(t => t is "Ctrl" or "Control" or "Meta" or "Command" or "Alt" or "Shift" or "LeftButton" or "RightButton" or "MiddleButton");
            if (isMouse) shortcut = raw.Replace("Control+", "Ctrl+").Replace("Command+", "Meta+");
            else
            {
                try
                {
                    var tokens = raw.Replace("Control+", "Ctrl+").Replace("Command+", "Meta+").Split('+');
                    if (tokens[^1].Length == 1 && char.IsAsciiDigit(tokens[^1][0])) tokens[^1] = "D" + tokens[^1];
                    shortcut = KeyGesture.Parse(string.Join('+', tokens)).ToString();
                }
                catch (ArgumentException)
                {
                    // 原配置中未迁入的复杂手势原样保留；仅拒绝用户新写入的不可识别值。
                    if (!input.OriginalValue.Split(',', StringSplitOptions.TrimEntries).Contains(raw))
                        throw new ArgumentException($"{input.Label} 的输入“{raw}”无法识别。");
                }
            }
            if (seen.TryGetValue(shortcut, out var previous) && previous.Name != input.Name)
            {
                // 原命令具有不同输入作用域，已有绑定不能阻止无关设置保存；新增冲突仍需处理。
                bool unchanged = previous.Value.Trim() == previous.OriginalValue.Trim() && input.Value.Trim() == input.OriginalValue.Trim();
                if (!unchanged) throw new ArgumentException($"输入冲突：{shortcut} 同时绑定到 {previous.Label}、{input.Label}。");
            }
            seen[shortcut] = input;
        }
    }
    /// <summary>取消不写入配置。</summary>
    private void Cancel_Click(object? sender, RoutedEventArgs e) => Close();
}
