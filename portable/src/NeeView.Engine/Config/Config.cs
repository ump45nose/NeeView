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
    public BookmarkConfig Bookmark { get; set; } = new();
    public SystemConfig System { get; set; } = new();
    public PlaylistConfig Playlist { get; set; } = new();
    public AutoHideConfig AutoHide { get; set; } = new();
    public WindowConfig Window { get; set; } = new();
    public MenuBarConfig MenuBar { get; set; } = new();
    public MouseConfig Mouse { get; set; } = new();
    public CommandConfig Command { get; set; } = new();
    public StartUpConfig StartUp { get; set; } = new();
    /// <summary>早期 Mac 字段兼容入口；真实配置沿用原 MenuBar 分支。</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsAddressBarEnabled { get => MenuBar.IsAddressBarEnabled; set => MenuBar.IsAddressBarEnabled = value; }
    /// <summary>启动时装配唯一配置，读取前不初始化具体窗口。</summary>
    public static void SetCurrent(Config config) => Current = config;
}

/// <summary>原 SystemConfig 搜索分支；其余系统设置仍在原 JSON 中保留。</summary>
public sealed class SystemConfig
{
    public BookPageCollectMode BookPageCollectMode { get; set; } = BookPageCollectMode.ImageAndBook;
    public ArchiveEntryCollectionMode ArchiveRecursiveMode { get; set; } = ArchiveEntryCollectionMode.IncludeSubArchives;
    public bool IsIncrementalSearchEnabled { get; set; } = true;
    private int _searchHistorySize = 8;
    /// <summary>沿用原非负上限，零表示不保留搜索历史。</summary>
    public int SearchHistorySize { get => _searchHistorySize; set => _searchHistorySize = Math.Max(0, value); }
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
    public bool IsHidePageSlider { get; set; }
    public bool IsHidePageSliderInAutoHideMode { get; set; } = true;
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
    public PageEndAction PageEndAction { get; set; }
    public ResetNextBookPageMode ResetNextBookPageMode { get; set; } = ResetNextBookPageMode.Continue;
    public bool IsNotifyPageLoop { get; set; }
    public PageFrameOrientation Orientation { get; set; } = PageFrameOrientation.Horizontal;
    private int _bookThumbnailDepth = 2;
    public int BookThumbnailDepth { get => _bookThumbnailDepth; set => _bookThumbnailDepth = Math.Max(1, value); }
    public string BookThumbnailRegex { get; set; } = @"^folder\.jpg$";
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
    public PanelListItemStyle PanelListItemStyle { get; set; } = PanelListItemStyle.Content;
    public FolderOrder DefaultFolderOrder { get; set; } = FolderOrder.FileName;
    public FolderOrder PlaylistFolderOrder { get; set; }
    public FolderSortOrder FolderSortOrder { get; set; } = FolderSortOrder.First;
}
/// <summary>原查看器基础缩放选项。</summary>
public sealed class ViewConfig
{
    private PageStretchMode _stretchMode = PageStretchMode.Uniform;
    public PageStretchMode StretchMode { get => _stretchMode; set { _stretchMode = value; if (value != PageStretchMode.None) ValidStretchMode = value; } }
    [System.Text.Json.Serialization.JsonIgnore]
    public PageStretchMode ValidStretchMode { get; private set; } = PageStretchMode.Uniform;
    /// <summary>原表单回滚恢复有效适配模式，JSON忽略的运行状态不能由序列化快照推测。</summary>
    internal void RestoreStretchMode(PageStretchMode mode, PageStretchMode valid) { _stretchMode = mode; ValidStretchMode = valid; }
    public bool AllowStretchScaleUp { get; set; } = true;
    public bool AllowStretchScaleDown { get; set; } = true;
    public bool IsBaseScaleEnabled { get; set; } = true;
    public DragControlCenter ScaleCenter { get; set; }
    public DragControlCenter RotateCenter { get; set; }
    public DragControlCenter FlipCenter { get; set; }
    public bool IsKeepScale { get; set; }
    public bool IsKeepAngle { get; set; }
    public bool IsKeepFlip { get; set; }
    public bool IsKeepScaleBooks { get; set; }
    public bool IsKeepAngleBooks { get; set; }
    public bool IsKeepFlipBooks { get; set; }
    public bool IsKeepPageTransform { get; set; }
    public bool IsScaleStretchTracking { get; set; }
    public ViewHorizontalOrigin ViewHorizontalOrigin { get; set; } = ViewHorizontalOrigin.CenterOrDirectionDependent;
    public ViewVerticalOrigin ViewVerticalOrigin { get; set; } = ViewVerticalOrigin.CenterOrDirectionDependent;
    public double ViewOriginCenterRatio { get; set; } = 1;
    private double _angleFrequency;
    public double AngleFrequency { get => _angleFrequency; set => _angleFrequency = double.IsFinite(value) ? Math.Round(Math.Max(0, value), 5) : 0; }
    public MovementConstraint MovementConstraint { get; set; } = MovementConstraint.LockUntilResized;
    public double ScrollDuration { get; set; } = .2;
    /// <summary>旧原字段只作读取转换，保存继续使用两个独立方向。</summary>
    [System.Text.Json.Serialization.JsonPropertyName("ViewOrigin"), System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public int ViewOriginLegacy
    {
        get => 0;
        set { ViewHorizontalOrigin = value == 0 ? ViewHorizontalOrigin.Center : ViewHorizontalOrigin.CenterOrDirectionDependent;
            ViewVerticalOrigin = value == 0 ? ViewVerticalOrigin.Center : value == 2 ? ViewVerticalOrigin.CenterOrTop : ViewVerticalOrigin.CenterOrDirectionDependent; }
    }
}
/// <summary>窗口级布局状态，独立于书籍和阅读规则。</summary>
public sealed class PanelsConfig
{
    public PanelListItemProfile NormalItemProfile { get; set; } = PanelListItemProfile.Create(PanelListItemStyle.Normal);
    public PanelListItemProfile ContentItemProfile { get; set; } = PanelListItemProfile.Create(PanelListItemStyle.Content);
    public PanelListItemProfile BannerItemProfile { get; set; } = PanelListItemProfile.Create(PanelListItemStyle.Banner);
    public PanelListItemProfile ThumbnailItemProfile { get; set; } = PanelListItemProfile.Create(PanelListItemStyle.Thumbnail);
    /// <summary>模板读取唯一原Panels分支，未知枚举回退Content但保留配置原值。</summary>
    public PanelListItemProfile GetProfile(PanelListItemStyle style) => style switch
    { PanelListItemStyle.Normal => NormalItemProfile, PanelListItemStyle.Banner => BannerItemProfile, PanelListItemStyle.Thumbnail => ThumbnailItemProfile, _ => ContentItemProfile };
    public bool OpenWithDoubleClick { get; set; }
    public Runtime.LayoutPanel.LayoutPanelManagerMemento? Layout { get; set; }
    public double LeftWidth { get; set; } = 240;
    public double RightWidth { get; set; } = 240;
    public bool IsLeftVisible { get; set; } = true;
    public bool IsRightVisible { get; set; } = true;
    public bool IsHideLeftPanel { get; set; }
    public bool IsHideRightPanel { get; set; }
    public bool IsHideLeftPanelInAutoHideMode { get; set; } = true;
    public bool IsHideRightPanelInAutoHideMode { get; set; } = true;
    public bool IsSideBarEnabled { get; set; } = true;
    private double _conflictTopMargin = 32, _conflictBottomMargin = 20;
    /// <summary>原覆盖菜单与侧栏的内容安全余量，非负并保留五位精度。</summary>
    public double ConflictTopMargin { get => _conflictTopMargin; set => _conflictTopMargin = double.IsFinite(value) ? Math.Round(Math.Max(0, value), 5) : 32; }
    /// <summary>原覆盖滑条与侧栏的内容安全余量，正文区域不受它影响。</summary>
    public double ConflictBottomMargin { get => _conflictBottomMargin; set => _conflictBottomMargin = double.IsFinite(value) ? Math.Round(Math.Max(0, value), 5) : 20; }
    /// <summary>兼容旧 Mac 调用；不再写入第二个自动隐藏字段。</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsLeftAutoHide { get => IsHideLeftPanel; set => IsHideLeftPanel = value; }
    /// <summary>右侧兼容入口，同样委托原字段。</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsRightAutoHide { get => IsHideRightPanel; set => IsHideRightPanel = value; }
}
