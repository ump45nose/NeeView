// Copyright (c) NeeLaboratory. 原滚动命令参数迁入，移除 WPF 属性编辑注解。
using System.Text.Json.Serialization;
namespace NeeView;

/// <summary>原 NScroll 参数契约；全景开关保留，分页模式下无额外作用。</summary>
public interface IScrollNTypeParameter
{
    NScrollType ScrollType { get; set; }
    double Scroll { get; set; }
    double LineBreakStopTime { get; set; }
    bool PagesAsOne { get; set; }
}

/// <summary>保留原默认值与旧字段映射，用于先滚动、到边界再翻页。</summary>
public sealed class ScrollPageCommandParameter : IScrollNTypeParameter
{
    public NScrollType ScrollType { get; set; } = NScrollType.NType;
    private double _scroll = 1, _endMargin = 10, _stopTime;
    public double Scroll { get => _scroll; set => _scroll = Math.Round(Math.Clamp(value, .1, 1), 5); }
    public double EndMargin { get => _endMargin; set => _endMargin = Math.Round(Math.Max(value, 0), 5); }
    public double LineBreakStopTime { get => _stopTime; set => _stopTime = Math.Round(value, 5); }
    public LineBreakStopMode LineBreakStopMode { get; set; } = LineBreakStopMode.Line;
    public bool PagesAsOne { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool IsNScroll { get => false; set => ScrollType = value ? NScrollType.NType : NScrollType.Diagonal; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public double PageMoveMargin { get => 0; set { LineBreakStopTime = value; LineBreakStopMode = LineBreakStopMode.Page; } }
}

public enum NScrollType { NType, ZType, Diagonal, Horizontal, Vertical }
public enum LineBreakStopMode { Line, Page }
