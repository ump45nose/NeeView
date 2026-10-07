// Copyright (c) NeeLaboratory. 原 MainViewConfig 字段、默认值与 JSON；WPF Size 替换为纯值。
using CommunityToolkit.Mvvm.ComponentModel;
using NeeView.Windows;
using System.Text.Json.Serialization;
namespace NeeView;

/// <summary>唯一中央查看器的宿主配置；不持有窗口、控件或图像资源。</summary>
public sealed class MainViewConfig : ObservableObject
{
    private bool _isFloating, _isTopmost, _isFrontAsPossible, _isHideTitleBar, _isAutoStretch, _isFloatingEndWhenClosed;
    private bool _isAutoHide = true, _isAutoShow = true;
    private AlternativeContent _alternativeContent = AlternativeContent.PageList;
    private Size _referenceSize;
    public bool IsFloating { get => _isFloating; set => SetProperty(ref _isFloating, value); }
    public bool IsFloatingEndWhenClosed { get => _isFloatingEndWhenClosed; set => SetProperty(ref _isFloatingEndWhenClosed, value); }
    public AlternativeContent AlternativeContent { get => _alternativeContent; set => SetProperty(ref _alternativeContent, value); }
    public bool IsTopmost { get => _isTopmost; set => SetProperty(ref _isTopmost, value); }
    public bool IsFrontAsPossible { get => _isFrontAsPossible; set => SetProperty(ref _isFrontAsPossible, value); }
    public bool IsHideTitleBar { get => _isHideTitleBar; set => SetProperty(ref _isHideTitleBar, value); }
    public bool IsAutoStretch { get => _isAutoStretch; set => SetProperty(ref _isAutoStretch, value); }
    public bool IsAutoHide { get => _isAutoHide; set => SetProperty(ref _isAutoHide, value); }
    public bool IsAutoShow { get => _isAutoShow; set => SetProperty(ref _isAutoShow, value); }
    [PropertyMapIgnore] public WindowStateEx LastState { get; set; } = WindowStateEx.Normal;
    [PropertyMapIgnore] public WindowPlacement WindowPlacement { get; set; } = WindowPlacement.None;
    [PropertyMapIgnore, JsonConverter(typeof(JsonSizeConverter))]
    public Size ReferenceSize { get => _referenceSize; set => SetProperty(ref _referenceSize, value); }
}
/// <summary>沿原枚举顺序：空白或借用同一页面列表。</summary>
public enum AlternativeContent { Blank, PageList }
