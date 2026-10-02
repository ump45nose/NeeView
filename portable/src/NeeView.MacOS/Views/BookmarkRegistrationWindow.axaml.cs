using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using NeeView;
namespace NeeView.MacOS.Views;

/// <summary>登记表现适配，只返回用户选择；原编辑规则由 BookmarkPopupEdit/SaveData 执行。</summary>
public sealed partial class BookmarkRegistrationWindow : Window
{
    private readonly BookmarkPopupEdit? _edit;
    /// <summary>设计器入口，加载唯一正式 XAML。</summary>
    public BookmarkRegistrationWindow() => AvaloniaXamlLoader.Load(this);
    /// <summary>以原编辑上下文和当前树构建选择列表，不访问文件系统。</summary>
    public BookmarkRegistrationWindow(BookmarkPopupEdit edit, BookmarkNode root, BookmarkNode parent) : this()
    {
        _edit = edit; Title = edit.IsEdit ? "编辑书签" : "添加书签";
        this.FindControl<TextBox>("BookmarkName")!.Text = edit.Name;
        var choices = new List<FolderChoice>();
        void Collect(BookmarkNode folder, string path)
        {
            choices.Add(new(folder, path));
            foreach (var child in folder.Children!.Where(e => e.IsFolder)) Collect(child, path + " / " + child.DisplayName);
        }
        Collect(root, "书签根目录");
        var picker = this.FindControl<ComboBox>("BookmarkParent")!; picker.ItemsSource = choices;
        picker.SelectedItem = choices.First(e => ReferenceEquals(e.Node, parent));
        this.FindControl<Button>("AddButton")!.IsVisible = edit.IsEdit;
        Opened += (_, _) => { var input = this.FindControl<TextBox>("BookmarkName")!; input.Focus(); input.SelectAll(); };
    }
    /// <summary>完成在编辑/新增模式中保持原不同含义。</summary>
    private void Done(object? sender, RoutedEventArgs e) => Complete(_edit?.IsEdit == true ? BookmarkPopupResult.Edit : BookmarkPopupResult.Add);
    /// <summary>取消、追加或移除仅返回选择，不提前修改权威树。</summary>
    private void Decide(object? sender, RoutedEventArgs e) { if (sender is Control { Tag: string action }) Complete(Enum.Parse<BookmarkPopupResult>(action)); }
    /// <summary>规范名称并返回父级引用；Engine 提交时重新核验目标有效性。</summary>
    private void Complete(BookmarkPopupResult action)
    {
        if (action == BookmarkPopupResult.None) { Close(null); return; }
        if (_edit is null || this.FindControl<ComboBox>("BookmarkParent")!.SelectedItem is not FolderChoice folder) return;
        _edit.Name = this.FindControl<TextBox>("BookmarkName")!.Text ?? "";
        Close(new BookmarkRegistrationChoice(folder.Node, action));
    }
    /// <summary>目标文件夹的界面投影，只持有原树引用和显示文案。</summary>
    private sealed record FolderChoice(BookmarkNode Node, string Label)
    {
        /// <summary>向选择器提供层级文案，不作为持久化路径。</summary>
        public override string ToString() => Label;
    }
}
/// <summary>登记弹窗的用户决定，引用当前原树节点。</summary>
public sealed record BookmarkRegistrationChoice(BookmarkNode Parent, BookmarkPopupResult Result);
