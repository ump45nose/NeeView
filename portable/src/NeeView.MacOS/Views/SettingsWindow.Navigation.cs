using Avalonia.Controls;
namespace NeeView.MacOS.Views;
public sealed partial class SettingsWindow
{
    /// <summary>读取原列表字段到独立表单草稿，关闭/取消不会应用。</summary>
    private void FillNavigation()
    {
        var p = Config.Current.PageList; var b = Config.Current.Bookshelf;
        this.FindControl<ComboBox>("PageNameFormat")!.SelectedIndex = (int)p.Format;
        this.FindControl<ComboBox>("PageListStyle")!.SelectedIndex = (int)p.PanelListItemStyle;
        this.FindControl<ComboBox>("PageTreeLayout")!.SelectedIndex = (int)p.FolderTreeLayout;
        this.FindControl<ComboBox>("FolderTreeLayout")!.SelectedIndex = (int)b.FolderTreeLayout;
        foreach (var (name, value) in new[] { ("PageShowBookTitle", p.ShowBookTitle), ("PageGroupBy", p.IsGroupBy), ("PageShowSearch", p.IsVisibleSearchBox), ("PageShowCount", p.IsVisibleItemsCount), ("PageFocusMain", p.FocusMainView), ("PageTreeVisible", p.IsFolderTreeVisible), ("FolderShowSearch", b.IsVisibleSearchBox), ("FolderSearchRecursive", b.IsSearchIncludeSubdirectories), ("FolderTreeVisible", b.IsFolderTreeVisible), ("FolderTreeSync", b.IsSyncFolderTree), ("FolderTreeAutoSync", b.IsSyncFolderTreeAuto), ("IncrementalSearch", Config.Current.System.IsIncrementalSearchEnabled), ("KeepSearchHistory", Config.Current.History.IsKeepSearchHistory) }) this.FindControl<CheckBox>(name)!.IsChecked = value;
        FillNumber("PageTreeWidth", p.FolderTreeAreaWidth, 48, 2048); FillNumber("PageTreeHeight", p.FolderTreeAreaHeight, 40, 2048);
        FillNumber("FolderTreeWidth", b.FolderTreeAreaWidth, 48, 2048); FillNumber("FolderTreeHeight", b.FolderTreeAreaHeight, 40, 2048);
    }
    /// <summary>保存时写入原分支，沿BookOperation.ApplyOptions锁与事务统一失败回滚。</summary>
    private void ApplyNavigation()
    {
        var p = Config.Current.PageList; var b = Config.Current.Bookshelf; bool Check(string name) => this.FindControl<CheckBox>(name)!.IsChecked == true;
        p.Format = (PageNameFormat)Math.Max(0, this.FindControl<ComboBox>("PageNameFormat")!.SelectedIndex);
        p.PanelListItemStyle = (PanelListItemStyle)Math.Max(0, this.FindControl<ComboBox>("PageListStyle")!.SelectedIndex);
        p.FolderTreeLayout = (FolderTreeLayout)Math.Max(0, this.FindControl<ComboBox>("PageTreeLayout")!.SelectedIndex);
        b.FolderTreeLayout = (FolderTreeLayout)Math.Max(0, this.FindControl<ComboBox>("FolderTreeLayout")!.SelectedIndex);
        p.ShowBookTitle = Check("PageShowBookTitle"); p.IsGroupBy = Check("PageGroupBy"); p.IsVisibleSearchBox = Check("PageShowSearch"); p.IsVisibleItemsCount = Check("PageShowCount"); p.FocusMainView = Check("PageFocusMain"); p.IsFolderTreeVisible = Check("PageTreeVisible");
        b.IsVisibleSearchBox = Check("FolderShowSearch"); b.IsSearchIncludeSubdirectories = Check("FolderSearchRecursive"); b.IsFolderTreeVisible = Check("FolderTreeVisible"); b.IsSyncFolderTree = Check("FolderTreeSync"); b.IsSyncFolderTreeAuto = Check("FolderTreeAutoSync");
        p.FolderTreeAreaWidth = (double)(this.FindControl<NumericUpDown>("PageTreeWidth")!.Value ?? 128); p.FolderTreeAreaHeight = (double)(this.FindControl<NumericUpDown>("PageTreeHeight")!.Value ?? 72);
        b.FolderTreeAreaWidth = (double)(this.FindControl<NumericUpDown>("FolderTreeWidth")!.Value ?? 128); b.FolderTreeAreaHeight = (double)(this.FindControl<NumericUpDown>("FolderTreeHeight")!.Value ?? 72);
        Config.Current.System.IsIncrementalSearchEnabled = Check("IncrementalSearch"); Config.Current.History.IsKeepSearchHistory = Check("KeepSearchHistory");
    }
}
