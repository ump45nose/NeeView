namespace NeeView;

/// <summary>原 Config 的 P1 设置分支；其余分支在原 JSON 中完整保留。</summary>
public sealed class Config
{
    public static Config Current { get; private set; } = new();
    public BookSettingConfig BookSetting { get; set; } = new();
    public BookSettingConfig BookSettingDefault { get; set; } = new();
    public BookSettingPolicyConfig BookSettingPolicy { get; set; } = new();
    public BookConfig Book { get; set; } = new();
    public ViewConfig View { get; set; } = new();
    public PanelsConfig Panels { get; set; } = new();
    public FilmStripConfig FilmStrip { get; set; } = new();
    public SliderConfig Slider { get; set; } = new();
    public bool IsAddressBarEnabled { get; set; } = true;
    /// <summary>启动时装配唯一配置，读取前不初始化具体窗口。</summary>
    public static void SetCurrent(Config config) => Current = config;
}

/// <summary>来自原 FilmStripConfig 的胶片条显示与尺寸默认值；未迁入字段由原 JSON 保留。</summary>
public sealed class FilmStripConfig
{
    public bool IsEnabled { get; set; }
    public bool IsHideFilmStrip { get; set; }
    private double _imageWidth = 96;
    public double ImageWidth { get => _imageWidth; set => _imageWidth = double.IsFinite(value) ? Math.Round(Math.Max(value, 32), 5) : 96; }
    public bool IsVisibleNumber { get; set; }
    public bool IsSelectedCenter { get; set; }
    public bool IsHideFilmStripInAutoHideMode { get; set; } = true;
    public bool IsManipulationBoundaryFeedbackEnabled { get; set; } = true;
    public bool IsVisiblePlaylistMark { get; set; }
    public FilmStripMouseWheelAction MouseWheelAction { get; set; }
    public bool IsDetailPopupEnabled { get; set; } = true;
}
public enum FilmStripMouseWheelAction { MoveSelection, MovePage, CommandDependent }
public enum SliderDirection { LeftToRight, RightToLeft, SyncBookReadDirection }
/// <summary>原滑条选择/方向/轮滚字段；其他原字段由 JSON 合并保留。</summary>
public sealed class SliderConfig
{
    public SliderDirection SliderDirection { get; set; } = SliderDirection.SyncBookReadDirection;
    public bool IsSliderLinkedFilmStrip { get; set; } = true;
    public bool IsSyncPageMode { get; set; }
}

/// <summary>来自原 BookConfig 的分页参数及默认值。</summary>
public sealed class BookConfig
{
    public double WideRatio { get; set; } = 1;
    public double DividePageRate { get; set; } = .5;
    public bool IsStaticWidePage { get; set; }
    public bool IsInsertDummyPage { get; set; }
    public bool IsInsertDummyFirstPage { get; set; }
    public bool IsInsertDummyLastPage { get; set; } = true;
    public FolderSortOrder FolderSortOrder { get; set; } = FolderSortOrder.First;
    public WidePageStretch WidePageStretch { get; set; } = WidePageStretch.UniformHeight;
    public double ContentsSpace { get; set; } = -1;
}
/// <summary>原查看器基础缩放选项。</summary>
public sealed class ViewConfig
{
    public PageStretchMode StretchMode { get; set; } = PageStretchMode.Uniform;
    public bool AllowStretchScaleUp { get; set; } = true;
    public bool AllowStretchScaleDown { get; set; } = true;
}
/// <summary>窗口级布局状态，独立于书籍和阅读规则。</summary>
public sealed class PanelsConfig
{
    public Runtime.LayoutPanel.LayoutPanelManagerMemento? Layout { get; set; }
    public double LeftWidth { get; set; } = 240;
    public double RightWidth { get; set; } = 240;
    public bool IsLeftVisible { get; set; } = true;
    public bool IsRightVisible { get; set; } = true;
    public bool IsLeftAutoHide { get; set; }
    public bool IsRightAutoHide { get; set; }
}
