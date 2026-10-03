using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using NeeView;
namespace NeeView.MacOS.Views;

/// <summary>书签表现适配：对话框、选择和拖动反馈；集合规则与保存全部进入 Engine。</summary>
public sealed partial class MainWindow
{
    private BookmarkNode? _bookmarkDrag;
    private Avalonia.Point _bookmarkDragStart;
    private IPointer? _bookmarkPointer;
    private bool _bookmarkDragging;
    private bool _bookmarkTreeSelection;
    /// <summary>书签列表和编辑树共享原节点，删除读取实际操作区域的批次。</summary>
    private BookmarkNode[] SelectedBookmarkNodes() => _bookmarkTreeSelection
        ? this.FindControl<TreeView>("BookmarkTree")!.SelectedItems.OfType<BookmarkNode>().ToArray()
        : this.FindControl<BookmarkListView>("BookmarkPanelList")!.SelectedNodes.ToArray();
    private const string BookmarkDragHelp = "拖到文件夹中间移入，行边缘调整顺序；空白移至根。";

    /// <summary>挂接树的内部指针输入，不引入跨进程文件拖放或第二套树模型。</summary>
    private void AttachBookmarkInput()
    {
        var tree = this.FindControl<TreeView>("BookmarkTree")!;
        // 原生树项目先完成选择/捕获，随后统一接管内部拖动，避免默认处理覆盖本树捕获。
        tree.AddHandler(PointerPressedEvent, Bookmark_Pressed, RoutingStrategies.Bubble, handledEventsToo: true);
        tree.AddHandler(PointerMovedEvent, Bookmark_Moved, RoutingStrategies.Tunnel);
        tree.AddHandler(PointerReleasedEvent, Bookmark_Released, RoutingStrategies.Tunnel, handledEventsToo: true);
        tree.PointerCaptureLost += (_, e) =>
        {
            // 子控件的捕获转交会冒泡到树；仍由本树持有时不是取消。
            if (!ReferenceEquals(e.Source, tree)) return;
            _bookmarkDrag = null; _bookmarkDragging = false; _bookmarkPointer = null; SetBookmarkHint(BookmarkDragHelp);
        };
        tree.SelectionChanged += (_, _) =>
        {
            if (_model is null || !tree.IsKeyboardFocusWithin) return;
            _bookmarkTreeSelection = true; _model.BookmarkSelectionCount = tree.SelectedItems.Count;
            this.FindControl<BookmarkListView>("BookmarkPanelList")!.SyncTreeSelection(tree.SelectedItem as BookmarkNode);
        };
        tree.KeyDown += (_, e) => { if (e.Key == Key.Escape && _bookmarkDrag is not null) { e.Handled = true; CancelBookmarkDrag(); } };
    }

    /// <summary>取消拖动必须同时释放指针捕获；关闭窗口也共用此入口。</summary>
    private void CancelBookmarkDrag()
    {
        _bookmarkDrag = null; _bookmarkDragging = false; _bookmarkPointer?.Capture(null); _bookmarkPointer = null;
        SetBookmarkHint(BookmarkDragHelp);
    }

    /// <summary>从命中控件查找实际节点容器，不使用列表下标表示持久身份。</summary>
    private static TreeViewItem? BookmarkRow(object? source) => source is Visual visual
        ? (visual as TreeViewItem ?? visual.GetVisualAncestors().OfType<TreeViewItem>().FirstOrDefault()) : null;

    /// <summary>左键记录拖动候选；普通点击、展开和双击继续交给树控件。</summary>
    private void Bookmark_Pressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not TreeView tree || !e.GetCurrentPoint(tree).Properties.IsLeftButtonPressed) return;
        _bookmarkTreeSelection = true;
        // 展开箭头要自行持有按下/释放，树级捕获会让按钮丢失点击；只从节点正文开始拖动。
        if (e.Source is Visual source && source.GetVisualAncestors().Prepend(source).OfType<Button>().Any()) return;
        _bookmarkDrag = BookmarkRow(e.Source)?.DataContext as BookmarkNode;
        _bookmarkDragStart = e.GetPosition(tree); _bookmarkDragging = false;
        // 按下即捕获，树外释放也能结束；Mac 移动回报不必再次提供按键标志。
        if (_bookmarkDrag is not null) { _bookmarkPointer = e.Pointer; e.Pointer.Capture(tree); }
    }

    /// <summary>超过阈值才进入拖动态；通过实际行位置展示移入或顺序移动提示。</summary>
    private void Bookmark_Moved(object? sender, PointerEventArgs e)
    {
        if (sender is not TreeView tree || _bookmarkDrag is null) return;
        var point = e.GetPosition(tree);
        if (!_bookmarkDragging && Math.Abs(point.X - _bookmarkDragStart.X) + Math.Abs(point.Y - _bookmarkDragStart.Y) < 6) return;
        _bookmarkDragging = true; e.Handled = true;
        var row = BookmarkRow(tree.InputHitTest(e.GetPosition(tree)));
        if (row?.DataContext is BookmarkNode target)
        {
            var y = e.GetPosition(row).Y;
            SetBookmarkHint(target.IsFolder && y > 6 && y < row.Bounds.Height - 6 ? $"移入：{target.DisplayName}" : $"调整顺序：{target.DisplayName}");
        }
        else SetBookmarkHint("移至书签根目录；释放到面板外取消。");
    }

    /// <summary>释放时只提交树内有效目标；取消及保存失败不更改选择和阅读状态。</summary>
    private async void Bookmark_Released(object? sender, PointerReleasedEventArgs e)
    {
        if (sender is not TreeView tree || _bookmarkDrag is not { } node || !_bookmarkDragging) { CancelBookmarkDrag(); return; }
        var point = e.GetPosition(tree); var row = BookmarkRow(tree.InputHitTest(point));
        var target = row?.DataContext as BookmarkNode;
        var y = row is null ? 0 : e.GetPosition(row).Y;
        var into = target?.IsFolder == true && y > 6 && y < row!.Bounds.Height - 6;
        var after = row is not null && y > row.Bounds.Height / 2;
        _bookmarkDrag = null; _bookmarkDragging = false; e.Pointer.Capture(null); e.Handled = true; SetBookmarkHint(BookmarkDragHelp);
        if (_preparing || _closedPrepared || !new Avalonia.Rect(tree.Bounds.Size).Contains(point)) return;
        try { await ApplyBookmarkDropAsync(node, target, into, after); } catch (Exception ex) { ShowError(ex.Message); }
    }

    /// <summary>把拖动意图转换为原 Move/MoveToChild，重排索引以移除源后的最终位置为准。</summary>
    /// <param name="target">null 表示根目录空白。</param><param name="into">移入文件夹中间。</param>
    public async Task ApplyBookmarkDropAsync(BookmarkNode node, BookmarkNode? target, bool into, bool after)
    {
        if (_model is null || _preparing || _closedPrepared || ReferenceEquals(node, target)) return;
        try
        {
            var collection = _model.SaveData.Bookmarks;
            if (target is null || into)
            {
                var moved = await _model.SaveData.MoveBookmarkAsync(node, target ?? collection.Items);
                if (moved is not null) SelectBookmark(moved);
            }
            else
            {
                var parent = collection.ParentOf(target) ?? throw new InvalidOperationException("目标书签已失效。");
                var index = parent.Children!.IndexOf(target) + (after ? 1 : 0);
                if (ReferenceEquals(collection.ParentOf(node), parent) && parent.Children.IndexOf(node) < index) index--;
                SelectBookmark(await _model.SaveData.MoveBookmarkAsync(node, parent, index));
            }
        }
        catch { if (_model.SaveData.BookmarkRoot.Walk().Contains(node)) SelectBookmark(node); throw; }
    }

    /// <summary>保存成功后保留实际节点选择，并展开其祖先；主题和布局均可独立调整。</summary>
    private void SelectBookmark(BookmarkNode? node)
    {
        if (_model is null || _preparing || _closedPrepared) return;
        var tree = this.FindControl<TreeView>("BookmarkTree")!;
        // 集合迁移会暂时移除容器；成功或回滚后重新选定同一节点，清除遗留多选。
        tree.SelectedItems.Clear(); _model.SelectedBookmark = node;
        var parents = new Stack<BookmarkNode>();
        for (var parent = node is null ? null : _model.SaveData.Bookmarks.ParentOf(node); parent is not null; parent = _model.SaveData.Bookmarks.ParentOf(parent)) parents.Push(parent);
        while (parents.TryPop(out var parent)) if (tree.ContainerFromItem(parent) is TreeViewItem row) { row.IsExpanded = true; tree.UpdateLayout(); }
        tree.SelectedItem = node;
        this.FindControl<BookmarkListView>("BookmarkPanelList")!.Reveal(node);
    }

    /// <summary>更新拖动提示，不发布阅读刷新或解码请求。</summary>
    private void SetBookmarkHint(string text) => this.FindControl<TextBlock>("BookmarkDropHint")!.Text = text;

    /// <summary>上/下移动所选节点；集合钳制端点，保持节点和原字段。</summary>
    private async void Bookmark_Reorder(object? sender, RoutedEventArgs e)
    {
        if (_preparing || _closedPrepared || _model?.SelectedBookmark is not { } node || sender is not Control { Tag: string step }) return;
        try
        {
            var parent = _model.SaveData.Bookmarks.ParentOf(node) ?? throw new InvalidOperationException("书签节点已失效。");
            SelectBookmark(await _model.SaveData.MoveBookmarkAsync(node, parent, parent.Children!.IndexOf(node) + int.Parse(step)));
        }
        catch (Exception ex) { if (_model.SaveData.BookmarkRoot.Walk().Contains(node)) SelectBookmark(node); ShowError(ex.Message); }
    }

    /// <summary>以当前树生成可选目标列表；自身和后代排除，执行时仍在 Engine 重新校验。</summary>
    private async void Bookmark_Move(object? sender, RoutedEventArgs e)
    {
        if (_preparing || _closedPrepared || _model?.SelectedBookmark is not { } node) return;
        var choices = new List<BookmarkFolderChoice>();
        void Collect(BookmarkNode folder, string path)
        {
            if (node.Walk().Contains(folder)) return;
            choices.Add(new(folder, path));
            foreach (var child in folder.Children!.Where(e => e.IsFolder)) Collect(child, path + " / " + child.DisplayName);
        }
        Collect(_model.SaveData.BookmarkRoot, "书签根目录");
        var picker = new ComboBox { ItemsSource = choices, SelectedIndex = 0, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch };
        var cancel = new Button { Content = "取消" }; var accept = new Button { Content = "移动" };
        var dialog = new Window { Title = "移动书签", Width = 420, Height = 180, WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new StackPanel { Margin = new Thickness(16), Spacing = 12, Children = { picker, new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 8, Children = { cancel, accept } } } } };
        cancel.Click += (_, _) => dialog.Close(null); accept.Click += (_, _) => dialog.Close(picker.SelectedItem as BookmarkFolderChoice);
        var selected = await dialog.ShowDialog<BookmarkFolderChoice?>(this);
        if (selected is null || _preparing || _closedPrepared) return;
        try { var moved = await _model.SaveData.MoveBookmarkAsync(node, selected.Node); if (moved is not null) SelectBookmark(moved); }
        catch (Exception ex) { if (_model.SaveData.BookmarkRoot.Walk().Contains(node)) SelectBookmark(node); ShowError(ex.Message); }
    }

    /// <summary>文件夹颜色采用原 #AARRGGBB 存储，取消不写入；书籍不支持文件夹颜色。</summary>
    private async void Bookmark_Color(object? sender, RoutedEventArgs e)
    {
        if (_model?.SelectedBookmark is not { IsFolder: true } node) { ShowError("请先选择书签文件夹。"); return; }
        var color = await AskNameAsync("文件夹颜色（默认或 #AARRGGBB）", node.Color ?? "默认");
        if (color is null || _preparing || _closedPrepared) return;
        try { await _model.SaveData.SetBookmarkColorAsync(node, color == "默认" ? null : color); }
        catch (Exception ex) { if (_model.SaveData.BookmarkRoot.Walk().Contains(node)) SelectBookmark(node); ShowError(ex.Message); }
    }

    /// <summary>恢复上一批删除至仍有效的原父级，成功后选中恢复项。</summary>
    private async void Bookmark_Restore(object? sender, RoutedEventArgs e)
    {
        if (_model is null || _preparing || _closedPrepared) return;
        var selected = _model.SelectedBookmark;
        try { SelectBookmark(await _model.SaveData.RestoreBookmarksAsync()); }
        catch (Exception ex) { if (selected is not null && _model.SaveData.BookmarkRoot.Walk().Contains(selected)) SelectBookmark(selected); ShowError(ex.Message); }
    }

    /// <summary>目标选择器的表现文案，不作为书签身份或存储路径。</summary>
    private sealed record BookmarkFolderChoice(BookmarkNode Node, string Label)
    {
        /// <summary>返回界面层级文案，不改变节点标识。</summary>
        public override string ToString() => Label;
    }
}
