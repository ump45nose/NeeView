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
    public BookshelfConfig Bookshelf { get; set; } = new();
    public HistoryConfig History { get; set; } = new();
    public PlaylistConfig Playlist { get; set; } = new();
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
/// <summary>沿用原滑条页号位置枚举，JSON 数值顺序保持 None/Left/Right。</summary>
public enum SliderIndexLayout { None, Left, Right }
/// <summary>原滑条滚轮动作：直接移动正文帧或交由原命令绑定。</summary>
public enum SliderMouseWheelAction { MovePage, CommandDependent }
/// <summary>原滑条选择/方向/轮滚字段；其他原字段由 JSON 合并保留。</summary>
public sealed class SliderConfig
{
    public bool IsEnabled { get; set; } = true;
    public SliderIndexLayout SliderIndexLayout { get; set; } = SliderIndexLayout.Right;
    private double _thickness = 25;
    /// <summary>原厚度范围 15–50 DIP；拒绝非有限配置，按原精度舍入。</summary>
    public double Thickness { get => _thickness; set => _thickness = double.IsFinite(value) ? Math.Round(Math.Clamp(value, 15, 50), 5) : 25; }
    private double _opacity = 1;
    /// <summary>保存原透明度字段；显示端限制到合法范围，避免损坏配置影响窗口。</summary>
    public double Opacity { get => _opacity; set => _opacity = double.IsFinite(value) ? Math.Round(value, 5) : 1; }
    public SliderDirection SliderDirection { get; set; } = SliderDirection.SyncBookReadDirection;
    public bool IsSliderLinkedFilmStrip { get; set; } = true;
    public bool IsSyncPageMode { get; set; }
    public SliderMouseWheelAction MouseWheelAction { get; set; }
    public bool IsVisiblePlaylistMark { get; set; }
}

/// <summary>来自原 BookConfig 的分页参数及默认值。</summary>
public sealed class BookConfig
{
    public bool IsPrioritizeBookMove { get; set; }
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
/// <summary>原 BookshelfConfig 普通书架默认排序；各路径参数、巡回及搜索后续迁入。</summary>
public sealed class BookshelfConfig
{
    public FolderOrder DefaultFolderOrder { get; set; } = FolderOrder.FileName;
    public FolderSortOrder FolderSortOrder { get; set; } = FolderSortOrder.First;
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
    public bool OpenWithDoubleClick { get; set; }
    public Runtime.LayoutPanel.LayoutPanelManagerMemento? Layout { get; set; }
    public double LeftWidth { get; set; } = 240;
    public double RightWidth { get; set; } = 240;
    public bool IsLeftVisible { get; set; } = true;
    public bool IsRightVisible { get; set; } = true;
    public bool IsLeftAutoHide { get; set; }
    public bool IsRightAutoHide { get; set; }
}
