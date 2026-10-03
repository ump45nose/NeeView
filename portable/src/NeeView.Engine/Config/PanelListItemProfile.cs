// Copyright (c) NeeLaboratory. 原 PanelListItemProfile 的配置/形状部分；WPF测量和绘制移至Mac表现。
namespace NeeView;

/// <summary>原四种列表模板，数值顺序保持旧JSON兼容。</summary>
public enum PanelListItemStyle { Normal, Content, Banner, Thumbnail }
/// <summary>原封面形状；不携带界面颜色、控件或属性系统。</summary>
public enum PanelListItemImageShape { Original, Square, BookShape, Banner }

/// <summary>原侧栏共享模板参数；字体高度和裁剪由展示端解释。</summary>
public sealed class PanelListItemProfile
{
    private int _imageWidth;
    public PanelListItemImageShape ImageShape { get; set; }
    public int ImageWidth { get => _imageWidth; set => _imageWidth = Math.Max(0, value); }
    public bool IsDetailPopupEnabled { get; set; } = true;
    public bool IsImagePopupEnabled { get; set; }
    public bool IsTextVisible { get; set; }
    public bool IsTextWrapped { get; set; }
    public bool IsTagVisible { get; set; }
    public bool IsIconOverlay { get; set; }
    [System.Text.Json.Serialization.JsonIgnore]
    public int ShapeWidth => ImageShape == PanelListItemImageShape.BookShape ? (int)(ImageWidth * .7071) : ImageWidth;
    [System.Text.Json.Serialization.JsonIgnore]
    public int ShapeHeight => ImageShape == PanelListItemImageShape.Banner ? ImageWidth / 4 : ImageWidth;
    /// <summary>沿原四个派生Profile构造默认值，不将Content误置为纯文本。</summary>
    public static PanelListItemProfile Create(PanelListItemStyle style) => new()
    {
        ImageShape = style == PanelListItemStyle.Thumbnail ? PanelListItemImageShape.Original : style == PanelListItemStyle.Banner ? PanelListItemImageShape.Banner : PanelListItemImageShape.Square,
        ImageWidth = style switch { PanelListItemStyle.Normal => 0, PanelListItemStyle.Content => 64, PanelListItemStyle.Banner => 200, _ => 128 },
        IsDetailPopupEnabled = true, IsImagePopupEnabled = style == PanelListItemStyle.Content,
        IsTextVisible = true, IsTextWrapped = style == PanelListItemStyle.Thumbnail, IsTagVisible = true
    };
}
