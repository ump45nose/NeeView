using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Avalonia.Platform.Storage;
namespace NeeView.MacOS.Views;

/// <summary>原树上下文及QuickAccess拖放表现；只发出动作，不读文件或写JSON。</summary>
public sealed partial class FolderTreeView
{
    public Func<string, FolderTreeNodeBase?, string?, Task>? ActionRequested { get; set; }
    public Func<FolderTreeNodeBase, QuickAccessDirectoryNode, int, Task>? MoveRequested { get; set; }
    public Action<string>? ErrorRequested { get; set; }
    private FolderTreeNodeBase? _dragNode;
    private Point _dragStart;
    private bool _dragging;
    private IPointer? _dragPointer;
    private static TreeViewItem? Row(object? source) => source is Visual visual ? visual.GetVisualAncestors().Prepend(visual).OfType<TreeViewItem>().FirstOrDefault() : null;
    /// <summary>独立内部拖动只接受QuickAccess目标；普通目录目标不执行文件移动。</summary>
    private void AttachActions()
    {
        AddHandler(PointerPressedEvent, (_, e) =>
        {
            var row = Row(e.Source); if (row?.DataContext is not FolderTreeNodeBase node || e.Source is ToggleButton) return;
            if (e.GetCurrentPoint(this).Properties.IsRightButtonPressed) { if (_model is not null) _model.SelectedItem = node; ShowNodeMenu(node); e.Handled = true; return; }
            if (e.KeyModifiers == KeyModifiers.None && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) { _dragNode = node; _dragStart = e.GetPosition(this); }
        }, RoutingStrategies.Bubble, true);
        AddHandler(PointerMovedEvent, (_, e) =>
        {
            if (_dragNode is null || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
            var delta = e.GetPosition(this) - _dragStart;
            if (!_dragging && delta.X * delta.X + delta.Y * delta.Y > 36) { _dragging = true; _dragPointer = e.Pointer; e.Pointer.Capture(this.FindControl<TreeView>("DirectoryTree")); }
        }, RoutingStrategies.Tunnel);
        AddHandler(PointerReleasedEvent, async (_, e) =>
        {
            if (!_dragging) { _dragNode = null; return; }
            var source = _dragNode; _dragNode = null; _dragging = false; _dragPointer?.Capture(null); _dragPointer = null; e.Handled = true;
            var hit = this.GetVisualAt(e.GetPosition(this)); var row = Row(hit);
            if (source is null || row?.DataContext is not QuickAccessDirectoryNode target || MoveRequested is null) return;
            double y = e.GetPosition(row).Y, headerHeight = Math.Min(28, row.Bounds.Height);
            int delta = y < headerHeight * .25 ? -1 : y > headerHeight * .75 ? 1 : 0;
            try { await MoveRequested(source, target, delta); } catch (Exception ex) { ErrorRequested?.Invoke(ex.Message); }
        }, RoutingStrategies.Tunnel, true);
        PointerCaptureLost += (_, e) => { if (ReferenceEquals(e.Source, this.FindControl<TreeView>("DirectoryTree"))) { _dragNode = null; _dragging = false; _dragPointer = null; } };
        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragOverEvent, (_, e) => { e.DragEffects = Row(e.Source)?.DataContext is QuickAccessDirectoryNode { Value.IsFolder: true } ? DragDropEffects.Copy : DragDropEffects.None; e.Handled = true; });
        AddHandler(DragDrop.DropEvent, async (_, e) =>
        {
            e.Handled = true;
            if (Row(e.Source)?.DataContext is not QuickAccessDirectoryNode { Value.IsFolder: true } target || ActionRequested is null) return;
            try { foreach (var file in e.DataTransfer.TryGetFiles() ?? []) if (file.TryGetLocalPath() is { } path) await ActionRequested("add", target, path); }
            catch (Exception ex) { ErrorRequested?.Invoke(ex.Message); }
        });
    }
    private void ShowNodeMenu(FolderTreeNodeBase node)
    {
        var menu = new ContextMenu();
        void Add(string text, string action, bool enabled = true) { var item = new MenuItem { Header = text, IsEnabled = enabled }; item.Click += async (_, _) => { try { if (ActionRequested is not null) await ActionRequested(action, node, null); } catch (Exception ex) { ErrorRequested?.Invoke(ex.Message); } }; menu.Items.Add(item); }
        Add("打开 / 浏览", "open"); Add("在 Finder 中显示", "reveal", node is DirectoryNode || node is QuickAccessDirectoryNode { Value.IsFolder: false });
        Add("添加到快速访问", "add-current", node is DirectoryNode);
        if (node is QuickAccessDirectoryNode q)
        { Add("添加当前位置", "add", q.Value.IsFolder); Add("新建快速访问文件夹", "folder", q.Value.IsFolder); Add("重命名", "rename", q.Parent is not null); Add("属性", "properties", !q.Value.IsFolder); Add("删除快速访问项", "remove", q.Parent is not null); Add("上移", "up", q.Parent is not null); Add("下移", "down", q.Parent is not null); }
        menu.Items.Add(new Separator()); Add("刷新", "refresh");
        foreach (var text in new[] { "新建目录", "复制文件", "移动文件", "重命名文件", "移至废纸篓" }) menu.Items.Add(new MenuItem { Header = text + "（P4 待迁移）", IsEnabled = false });
        ContextMenu = menu; menu.Open(this);
    }
}
