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
    private bool _saving;
    private HistorySettingsViewModel? _historySettings;
    private bool _resetInputDefaults;
    /// <summary>独立加载布局，不依赖具体存储或解码后端。</summary>
    public SettingsWindow() { AvaloniaXamlLoader.Load(this); _initialized = true; }
    /// <summary>传入业务表现模型，编辑副本直到用户保存。</summary>
    public SettingsWindow(ReaderWorkspaceViewModel model, Func<string, bool>? available = null) : this()
    {
        _model = model;
        _inputs = model.Commands.Definitions.Select(d => new ShortcutEdit(d, model.SaveData.GetShortcut(d.Name, d.Shortcut), available?.Invoke(d.Name) ?? model.Commands.IsAvailable(d.Name))).ToArray();
        this.FindControl<ListBox>("InputList")!.ItemsSource = _inputs; Fill(); FillFilm(); FillAutoHide();
        this.FindControl<ComboBox>("InputScheme")!.SelectedIndex = (int)Config.Current.Command.PresetInputScheme;
        this.FindControl<ComboBox>("InputReadOrder")!.SelectedIndex = (int)Config.Current.Command.PresetPageReadOrder;
        this.FindControl<CheckBox>("ReversePageMove")!.IsChecked = Config.Current.Command.IsReversePageMove;
        this.FindControl<CheckBox>("ReversePageWheel")!.IsChecked = Config.Current.Command.IsReversePageMoveWheel;
        this.FindControl<CheckBox>("ReverseHorizontalWheel")!.IsChecked = Config.Current.Command.IsReversePageMoveHorizontalWheel;
        this.FindControl<CheckBox>("LimitHorizontalWheel")!.IsChecked = Config.Current.Command.IsHorizontalWheelLimitedOnce;
        this.FindControl<ComboBox>("BookshelfGroup")!.SelectedIndex = (int)Config.Current.Bookshelf.FolderSortOrder;
        this.FindControl<CheckBox>("PrioritizeBookMove")!.IsChecked = Config.Current.Book.IsPrioritizeBookMove;
        _historySettings = new(Config.Current.History);
        this.FindControl<ScrollViewer>("HistorySettings")!.DataContext = _historySettings;
    }
    /// <summary>历史面板的设置入口定位同一设置窗口，不复制第二套表单。</summary>
    public void SelectHistoryPage() => this.FindControl<ListBox>("SettingsNavigation")!.SelectedIndex = 4;
    /// <summary>保持原左导航、右内容结构；只切换设置页面，不应用编辑。</summary>
    private void Navigation_Changed(object? sender, SelectionChangedEventArgs e)
    {
        if (!_initialized) return;
        if (this.FindControl<ScrollViewer>("ReadingSettings") is not { } reading || this.FindControl<Grid>("InputSettings") is not { } input) return;
        var index = (sender as ListBox)?.SelectedIndex ?? 0;
        reading.IsVisible = index == 0; input.IsVisible = index == 1;
        this.FindControl<ScrollViewer>("FilmSettings")!.IsVisible = index == 2;
        this.FindControl<ScrollViewer>("AutoHideSettings")!.IsVisible = index == 3;
        this.FindControl<ScrollViewer>("HistorySettings")!.IsVisible = index == 4;
    }
    /// <summary>按名称及命令标识过滤编辑副本，未展示的键位也保留。</summary>
    private void InputSearch_Changed(object? sender, TextChangedEventArgs e)
    {
        if (!_initialized) return;
        var text = (sender as TextBox)?.Text ?? "";
        if (this.FindControl<ListBox>("InputList") is { } list) list.ItemsSource = _inputs.Where(i => i.Label.Contains(text, StringComparison.CurrentCultureIgnoreCase) || i.Name.Contains(text, StringComparison.OrdinalIgnoreCase)).ToArray();
    }
    /// <summary>从原A/B/C与方向构造默认键位草稿，只有保存成功才成为配置。</summary>
    private void InputDefaults_Click(object? sender, RoutedEventArgs e)
    {
        var candidate = GetInputConfig();
        foreach (var input in _inputs) input.Value = DefaultInputScheme.GetShortcut(input.Name, input.Definition.Shortcut, candidate);
        _resetInputDefaults = true;
    }
    /// <summary>默认方案是应用默认键位按钮的参数，切换下拉框不改现有自定义键位。</summary>
    private CommandConfig GetInputConfig()
    {
        var old = Config.Current.Command;
        return new() { PresetInputScheme = (InputScheme)Math.Max(0, this.FindControl<ComboBox>("InputScheme")!.SelectedIndex),
            PresetPageReadOrder = (PageReadOrder)Math.Max(0, this.FindControl<ComboBox>("InputReadOrder")!.SelectedIndex),
            IsAccessKeyEnabled = old.IsAccessKeyEnabled, IsReversePageMove = this.FindControl<CheckBox>("ReversePageMove")!.IsChecked == true,
            IsReversePageMoveWheel = this.FindControl<CheckBox>("ReversePageWheel")!.IsChecked == true,
            IsReversePageMoveHorizontalWheel = this.FindControl<CheckBox>("ReverseHorizontalWheel")!.IsChecked == true,
            IsHorizontalWheelLimitedOnce = this.FindControl<CheckBox>("LimitHorizontalWheel")!.IsChecked == true };
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
    /// <summary>读取原胶片条、滑条及共享步长参数到编辑控件，取消不修改配置。</summary>
    private void FillFilm()
    {
        var film = Config.Current.FilmStrip; var slider = Config.Current.Slider;
        foreach (var (name, value) in new[] { ("FilmEnabled", film.IsEnabled), ("FilmHide", film.IsHideFilmStrip), ("FilmNumber", film.IsVisibleNumber), ("FilmCenter", film.IsSelectedCenter), ("FilmDetail", film.IsDetailPopupEnabled), ("SliderLinked", slider.IsSliderLinkedFilmStrip), ("SliderSync", slider.IsSyncPageMode), ("SliderHide", slider.IsHidePageSlider), ("SliderModeHide", slider.IsHidePageSliderInAutoHideMode), ("FilmModeHide", film.IsHideFilmStripInAutoHideMode) })
            this.FindControl<CheckBox>(name)!.IsChecked = value;
        this.FindControl<NumericUpDown>("FilmWidth")!.Value = (decimal)Math.Min(film.ImageWidth, (double)decimal.MaxValue / 2);
        this.FindControl<ComboBox>("FilmWheel")!.SelectedIndex = (int)film.MouseWheelAction;
        this.FindControl<ComboBox>("SliderOrder")!.SelectedIndex = (int)slider.SliderDirection;
        this.FindControl<CheckBox>("SliderEnabled")!.IsChecked = slider.IsEnabled;
        this.FindControl<CheckBox>("SliderMarks")!.IsChecked = slider.IsVisiblePlaylistMark;
        this.FindControl<CheckBox>("FilmMarks")!.IsChecked = film.IsVisiblePlaylistMark;
        this.FindControl<ComboBox>("SliderIndexLayout")!.SelectedIndex = (int)slider.SliderIndexLayout;
        this.FindControl<NumericUpDown>("SliderThickness")!.Value = (decimal)slider.Thickness;
        this.FindControl<NumericUpDown>("SliderOpacity")!.Value = (decimal)Math.Clamp(slider.Opacity, 0, 1);
        this.FindControl<ComboBox>("SliderWheel")!.SelectedIndex = (int)slider.MouseWheelAction;
        this.FindControl<NumericUpDown>("MoveSize")!.Value = _model!.SaveData.GetMoveSizeParameter().Size;
    }
    /// <summary>只写当前已迁入的原胶片条与滑条字段，取消时不进入此入口。</summary>
    private void ApplyFilm()
    {
        var film = Config.Current.FilmStrip; var slider = Config.Current.Slider;
        film.IsEnabled = this.FindControl<CheckBox>("FilmEnabled")!.IsChecked == true;
        film.IsHideFilmStrip = this.FindControl<CheckBox>("FilmHide")!.IsChecked == true;
        film.IsHideFilmStripInAutoHideMode = this.FindControl<CheckBox>("FilmModeHide")!.IsChecked == true;
        slider.IsHidePageSlider = this.FindControl<CheckBox>("SliderHide")!.IsChecked == true;
        slider.IsHidePageSliderInAutoHideMode = this.FindControl<CheckBox>("SliderModeHide")!.IsChecked == true;
        film.IsVisibleNumber = this.FindControl<CheckBox>("FilmNumber")!.IsChecked == true;
        film.IsSelectedCenter = this.FindControl<CheckBox>("FilmCenter")!.IsChecked == true;
        film.IsDetailPopupEnabled = this.FindControl<CheckBox>("FilmDetail")!.IsChecked == true;
        film.ImageWidth = (double)(this.FindControl<NumericUpDown>("FilmWidth")!.Value ?? 96);
        film.MouseWheelAction = (FilmStripMouseWheelAction)Math.Max(0, this.FindControl<ComboBox>("FilmWheel")!.SelectedIndex);
        slider.SliderDirection = (SliderDirection)Math.Max(0, this.FindControl<ComboBox>("SliderOrder")!.SelectedIndex);
        slider.IsSliderLinkedFilmStrip = this.FindControl<CheckBox>("SliderLinked")!.IsChecked == true;
        slider.IsSyncPageMode = this.FindControl<CheckBox>("SliderSync")!.IsChecked == true;
        slider.IsEnabled = this.FindControl<CheckBox>("SliderEnabled")!.IsChecked == true;
        slider.IsVisiblePlaylistMark = this.FindControl<CheckBox>("SliderMarks")!.IsChecked == true;
        film.IsVisiblePlaylistMark = this.FindControl<CheckBox>("FilmMarks")!.IsChecked == true;
        slider.SliderIndexLayout = (SliderIndexLayout)Math.Max(0, this.FindControl<ComboBox>("SliderIndexLayout")!.SelectedIndex);
        slider.Thickness = (double)(this.FindControl<NumericUpDown>("SliderThickness")!.Value ?? 25);
        slider.Opacity = (double)(this.FindControl<NumericUpDown>("SliderOpacity")!.Value ?? 1);
        slider.MouseWheelAction = (SliderMouseWheelAction)Math.Max(0, this.FindControl<ComboBox>("SliderWheel")!.SelectedIndex);
        var size = (int)(this.FindControl<NumericUpDown>("MoveSize")!.Value ?? 10);
        if (size != _model!.SaveData.GetMoveSizeParameter().Size) _model.SaveData.SetCommandParameter("PrevSizePage", new MoveSizePageCommandParameter { Size = size });
    }
    /// <summary>原配置填入独立窗口表现表单，配置本身没有 UI 类型。</summary>
    private void FillAutoHide()
    {
        var c = Config.Current; var a = c.AutoHide;
        foreach (var (name, value) in new[] { ("AutoNormal", c.Window.IsAutoHideInNormal), ("AutoMaximized", c.Window.IsAutoHideInMaximized), ("AutoFullScreen", c.Window.IsAutoHideInFullScreen),
            ("AddressEnabled", c.MenuBar.IsAddressBarEnabled), ("SideBarEnabled", c.Panels.IsSideBarEnabled), ("MenuHide", c.MenuBar.IsHideMenu), ("MenuModeHide", c.MenuBar.IsHideMenuInAutoHideMode),
            ("LeftHide", c.Panels.IsHideLeftPanel), ("RightHide", c.Panels.IsHideRightPanel), ("LeftModeHide", c.Panels.IsHideLeftPanelInAutoHideMode), ("RightModeHide", c.Panels.IsHideRightPanelInAutoHideMode), ("KeyDelay", a.IsAutoHideKeyDownDelay) })
            this.FindControl<CheckBox>(name)!.IsChecked = value;
        foreach (var (name, value, maximum) in new[] { ("HideDelay", a.AutoHideDelayTime, 60.0), ("ShowDelay", a.AutoHideDelayVisibleTime, 60.0), ("HorizontalMargin", a.AutoHideHitTestHorizontalMargin, 500.0), ("VerticalMargin", a.AutoHideHitTestVerticalMargin, 500.0) })
            this.FindControl<NumericUpDown>(name)!.Value = (decimal)Math.Clamp(value, 0, maximum);
        this.FindControl<ComboBox>("FocusLock")!.SelectedIndex = (int)a.AutoHideFocusLockMode;
        this.FindControl<ComboBox>("TopConflict")!.SelectedIndex = (int)a.AutoHideConflictTopMargin;
        this.FindControl<ComboBox>("BottomConflict")!.SelectedIndex = (int)a.AutoHideConflictBottomMargin;
    }
    /// <summary>保存用户编辑的原字段；滑条/胶片条资格在各自页面编辑。</summary>
    private void ApplyAutoHide()
    {
        var c = Config.Current; var a = c.AutoHide;
        bool Checked(string name) => this.FindControl<CheckBox>(name)!.IsChecked == true;
        c.Window.IsAutoHideInNormal = Checked("AutoNormal"); c.Window.IsAutoHideInMaximized = Checked("AutoMaximized"); c.Window.IsAutoHideInFullScreen = Checked("AutoFullScreen");
        c.MenuBar.IsAddressBarEnabled = Checked("AddressEnabled"); c.Panels.IsSideBarEnabled = Checked("SideBarEnabled");
        c.MenuBar.IsHideMenu = Checked("MenuHide"); c.MenuBar.IsHideMenuInAutoHideMode = Checked("MenuModeHide");
        c.Panels.IsHideLeftPanel = Checked("LeftHide"); c.Panels.IsHideRightPanel = Checked("RightHide");
        c.Panels.IsHideLeftPanelInAutoHideMode = Checked("LeftModeHide"); c.Panels.IsHideRightPanelInAutoHideMode = Checked("RightModeHide"); a.IsAutoHideKeyDownDelay = Checked("KeyDelay");
        a.AutoHideDelayTime = (double)(this.FindControl<NumericUpDown>("HideDelay")!.Value ?? 1);
        a.AutoHideDelayVisibleTime = (double)(this.FindControl<NumericUpDown>("ShowDelay")!.Value ?? 0);
        a.AutoHideHitTestHorizontalMargin = (double)(this.FindControl<NumericUpDown>("HorizontalMargin")!.Value ?? 32);
        a.AutoHideHitTestVerticalMargin = (double)(this.FindControl<NumericUpDown>("VerticalMargin")!.Value ?? 32);
        a.AutoHideFocusLockMode = (AutoHideFocusLockMode)Math.Max(0, this.FindControl<ComboBox>("FocusLock")!.SelectedIndex);
        a.AutoHideConflictTopMargin = (AutoHideConflictMode)Math.Max(0, this.FindControl<ComboBox>("TopConflict")!.SelectedIndex);
        a.AutoHideConflictBottomMargin = (AutoHideConflictMode)Math.Max(0, this.FindControl<ComboBox>("BottomConflict")!.SelectedIndex);
    }
    /// <summary>设置应用成功并保存 JSON 后关闭；失败留在表单中。</summary>
    private async void Save_Click(object? sender, RoutedEventArgs e)
    {
        if (_model is null || _saving) return;
        _saving = true;
        this.FindControl<Button>("SaveSettings")!.IsEnabled = false;
        this.FindControl<Button>("CancelSettings")!.IsEnabled = false;
        try
        {
            ValidateInputs(_inputs);
            await _model.Operation.ApplyOptionsAsync(() =>
            {
                var inputConfig = GetInputConfig();
                if (!_resetInputDefaults) { inputConfig.PresetInputScheme = Config.Current.Command.PresetInputScheme; inputConfig.PresetPageReadOrder = Config.Current.Command.PresetPageReadOrder; }
                Config.Current.Command = inputConfig;
                if (this.FindControl<ComboBox>("Scope")!.SelectedIndex == 1) Apply(Config.Current.BookSettingDefault);
                else Apply(_model.Operation.Book?.Setting ?? Config.Current.BookSetting);
                // 只写用户修改的差分，避免一次保存就展开全部235条默认命令。
                foreach (var input in _inputs.Where(i => _resetInputDefaults || i.Value.Trim() != i.OriginalValue.Trim()))
                    _model.SaveData.SetShortcutDifference(input.Name, input.Value.Trim(), input.Definition.Shortcut);
                ApplyFilm(); ApplyAutoHide();
                Config.Current.Bookshelf.FolderSortOrder = (FolderSortOrder)Math.Max(0, this.FindControl<ComboBox>("BookshelfGroup")!.SelectedIndex);
                Config.Current.Book.IsPrioritizeBookMove = this.FindControl<CheckBox>("PrioritizeBookMove")!.IsChecked == true;
            }, _historySettings!.GetLimits());
            _saving = false; Close();
        }
        catch (Exception ex)
        {
            this.FindControl<TextBlock>("Message")!.Text = "保存失败：" + ex.Message;
        }
        finally
        {
            _saving = false;
            this.FindControl<Button>("SaveSettings")!.IsEnabled = true;
            this.FindControl<Button>("CancelSettings")!.IsEnabled = true;
        }
    }
    /// <summary>保存中的关闭不能丢弃尚未完成的事务；失败后允许取消或重试。</summary>
    /// <param name="e">系统关闭或父窗口关闭请求。</param>
    protected override void OnClosing(WindowClosingEventArgs e)
    { if (_saving) e.Cancel = true; base.OnClosing(e); }
    /// <summary>保存前校验键位与冲突；不自动将旧 Control 转成 Command。</summary>
    internal static void ValidateInputs(IEnumerable<ShortcutEdit> inputs)
    {
        var seen = new Dictionary<string, ShortcutEdit>(StringComparer.OrdinalIgnoreCase);
        foreach (var input in inputs)
        foreach (var raw in input.Value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var shortcut = raw;
            bool isMouse = MouseGestureSource.TryNormalize(raw, out var mouseShortcut);
            if (isMouse) shortcut = mouseShortcut;
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
