using CommunityToolkit.Mvvm.ComponentModel;
namespace NeeView.MacOS.ViewModels;

public sealed record SlideShowCommandChoice(string Name, string Label);
public sealed record SlideShowOption<T>(T Value, string Label);
/// <summary>原幻灯表单独立草稿，颜色/模板与定时、导航及JSON业务分离。</summary>
public sealed class SlideShowSettingsViewModel : ObservableObject
{
    private decimal _interval, _duration;
    private bool _timer, _priority, _wait, _scroll, _startup;
    private SlideShowCommandChoice? _command;
    private SlideShowTimerResetGesture _reset;
    private PageEndAction _end;
    private PageMoveType _move;
    public decimal Interval { get => _interval; set => SetProperty(ref _interval, value); }
    public decimal Duration { get => _duration; set => SetProperty(ref _duration, value); }
    public bool Timer { get => _timer; set => SetProperty(ref _timer, value); }
    public bool Priority { get => _priority; set => SetProperty(ref _priority, value); }
    public bool WaitAnimation { get => _wait; set => SetProperty(ref _wait, value); }
    public bool AutoScroll { get => _scroll; set => SetProperty(ref _scroll, value); }
    public bool AutoStart { get => _startup; set => SetProperty(ref _startup, value); }
    public SlideShowCommandChoice? Command { get => _command; set => SetProperty(ref _command, value); }
    public SlideShowTimerResetGesture Reset { get => _reset; set => SetProperty(ref _reset, value); }
    public PageEndAction EndAction { get => _end; set => SetProperty(ref _end, value); }
    public PageMoveType Move { get => _move; set => SetProperty(ref _move, value); }
    public IReadOnlyList<SlideShowCommandChoice> Commands { get; }
    public IReadOnlyList<SlideShowOption<SlideShowTimerResetGesture>> ResetGestures { get; }
    public IReadOnlyList<SlideShowOption<PageEndAction>> EndActions { get; }
    public IReadOnlyList<SlideShowOption<PageMoveType>> MoveTypes { get; }
    public decimal IntervalMaximum { get; }
    public decimal DurationMaximum { get; }
    public SlideShowSettingsViewModel(SlideShowConfig config, StartUpConfig startup, CommandTable commands, Func<string, bool> available)
    {
        _interval = (decimal)config.SlideShowInterval; _duration = (decimal)config.PageMoveDuration;
        IntervalMaximum = Math.Max(30, _interval); DurationMaximum = Math.Max(1, _duration);
        _timer = config.IsTimerVisible; _priority = config.IsPrioritizeTime; _wait = config.IsWaitAnimation;
        _scroll = config.IsAutoScroll; _startup = startup.IsAutoPlaySlideShow;
        _reset = config.TimerResetGesture; _end = config.PageEndAction; _move = config.PageMoveType;
        ResetGestures = Options(_reset, value => value switch { SlideShowTimerResetGesture.None => "不重置", SlideShowTimerResetGesture.InputAction => "键盘、鼠标按下及滚轮", SlideShowTimerResetGesture.MouseMove => "包括鼠标移动", _ => null });
        EndActions = Options(_end, value => value switch { PageEndAction.None => "停止播放", PageEndAction.Loop => "回到首尾页", PageEndAction.SeamlessLoop => "无缝循环", PageEndAction.NextBook => "打开下一本书", PageEndAction.Dialog => "询问书尾动作", _ => null });
        MoveTypes = Options(_move, value => value switch { PageMoveType.Scroll => "滚动", PageMoveType.Fade => "淡入淡出", _ => null });
        var choices = commands.Definitions.Select(d => new SlideShowCommandChoice(d.Name, d.Text + (available(d.Name) ? "" : "（尚未迁移）"))).ToList();
        _command = choices.FirstOrDefault(c => c.Name == config.NextPageCommandName);
        if (_command is null) { _command = new(config.NextPageCommandName, config.NextPageCommandName + "（未知命令，执行时回退到下一页）"); choices.Add(_command); }
        Commands = choices;
    }
    /// <summary>显示文案独立于原枚举值；未来值仍留在草稿中，不被默认选择覆盖。</summary>
    private static IReadOnlyList<SlideShowOption<T>> Options<T>(T current, Func<T, string?> label) where T : struct, Enum
    {
        var values = Enum.GetValues<T>().ToList();
        if (!values.Contains(current)) values.Add(current);
        return values.Select(value => new SlideShowOption<T>(value, label(value) ?? $"{value}（尚不支持）")).ToArray();
    }
    /// <summary>只在唯一设置事务提交时应用；取消不触碰原配置。</summary>
    public void Apply(SlideShowConfig config, StartUpConfig startup)
    {
        config.SlideShowInterval = (double)Interval; config.PageMoveDuration = (double)Duration;
        config.IsTimerVisible = Timer; config.IsPrioritizeTime = Priority; config.IsWaitAnimation = WaitAnimation;
        config.IsAutoScroll = AutoScroll; config.TimerResetGesture = Reset; config.PageEndAction = EndAction;
        config.PageMoveType = Move; config.NextPageCommandName = Command?.Name ?? config.NextPageCommandName;
        startup.IsAutoPlaySlideShow = AutoStart;
    }
}
