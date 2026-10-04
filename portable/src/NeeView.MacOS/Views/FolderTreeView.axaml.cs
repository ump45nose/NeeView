// Copyright (c) NeeLaboratory. 原FolderTreeView的普通选择/确认Mac适配。
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.VisualTree;
namespace NeeView.MacOS.Views;

/// <summary>原目录树的独立表现适配；仅转交展开绑定、普通点击及Enter确认。</summary>
public sealed partial class FolderTreeView : UserControl
{
    private FolderTreeModel? _model;
    private readonly List<Visual> _ancestors = [];
    /// <summary>构造视图，不创建来源；展开箭头不执行目录确认。</summary>
    public FolderTreeView()
    {
        AvaloniaXamlLoader.Load(this);
        AddHandler(PointerReleasedEvent, Released, RoutingStrategies.Bubble);
        AddHandler(KeyDownEvent, Directory_KeyDown, RoutingStrategies.Tunnel);
        GotFocus += (_, _) => { if (_model is not null) _model.HasKeyboardFocus = true; };
        LostFocus += (_, _) => { if (_model is not null) _model.HasKeyboardFocus = IsKeyboardFocusWithin; };
    }
    /// <summary>接入书架持有的唯一树；创建/绑定不会扫描目录。</summary>
    /// <param name="model">仍由书架所有和释放的原普通树模型。</param>
    public void Attach(FolderTreeModel model)
    {
        if (!ReferenceEquals(_model, model) && _model is not null) _model.IsPresented = false;
        _model = model; DataContext = model; UpdatePresentation();
    }
    /// <summary>原树普通行单击浏览书架；修饰选择、箭头、背景和滚动条不确认旧选项。</summary>
    private async void Released(object? sender, PointerReleasedEventArgs e)
    {
        if (_model is null || e.InitialPressMouseButton != MouseButton.Left || e.KeyModifiers != KeyModifiers.None || e.Source is not Visual visual) return;
        var chain = visual.GetVisualAncestors().Prepend(visual).ToArray();
        if (chain.Any(v => v is ToggleButton or ScrollBar) || chain.OfType<TreeViewItem>().FirstOrDefault()?.DataContext is not DirectoryNode node) return;
        _model.SelectedItem = node; e.Handled = true; await _model.DecideAsync();
    }
    /// <summary>Enter确认目录，方向键留给原生TreeView，不触发查看器翻页。</summary>
    private async void Directory_KeyDown(object? sender, KeyEventArgs e)
    { if (e.KeyModifiers == KeyModifiers.None && e.Key == Key.Enter && _model is not null) { e.Handled = true; await _model.DecideAsync(); } }
    /// <summary>隐藏/脱离宿主取消等待但保留树元数据，浮动重挂载可继续浏览。</summary>
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    { base.OnPropertyChanged(change); if (change.Property == IsVisibleProperty) UpdatePresentation(); }
    /// <summary>组合面板隐藏的是祖先，局部订阅显隐避免仍在后台同步隐藏的目录树。</summary>
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        foreach (var visual in this.GetVisualAncestors()) { _ancestors.Add(visual); visual.PropertyChanged += AncestorChanged; }
        UpdatePresentation();
    }
    private void AncestorChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    { if (e.Property == IsVisibleProperty) UpdatePresentation(); }
    /// <summary>纯表现资格回报，不读来源或改变树选择；实际同步由原模型控制。</summary>
    private void UpdatePresentation()
    { if (_model is not null) _model.IsPresented = TopLevel.GetTopLevel(this) is not null && IsVisible && _ancestors.All(v => v.IsVisible); }
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        foreach (var visual in _ancestors) visual.PropertyChanged -= AncestorChanged; _ancestors.Clear();
        if (_model is not null) _model.IsPresented = false;
    }
}
