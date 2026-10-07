using Avalonia.Controls;
namespace NeeView.MacOS.Views;
public sealed partial class MainWindow
{
    /// <summary>原LoadRecentBook打开可选择的最近书籍菜单，不擅自打开第一项。</summary>
    private void OpenRecentBookMenu()
    {
        if (_model is null) return;
        var menu = new ContextMenu();
        foreach (var entry in _model.SaveData.HistoryEntries.OrderByDescending(e=>e.LastAccessTime).Take(Config.Current.History.RecentBookCount))
        {
            var item = new MenuItem { Header = System.IO.Path.GetFileName(entry.Path), IsChecked=entry.Path==_model.Operation.Book?.Path };
            Avalonia.Controls.ToolTip.SetTip(item,entry.Path);
            item.Click += async (_,_) => { menu.Close(); if (!_preparing && !_closedPrepared) await OpenHistoryAsync(entry.Path); };
            menu.Items.Add(item);
        }
        if (menu.Items.Count==0) menu.Items.Add(new MenuItem{Header="无最近书籍",IsEnabled=false});
        Viewer.ContextMenu?.Close(); Viewer.ContextMenu=menu; menu.Closed+=(_,_)=>{if(ReferenceEquals(Viewer.ContextMenu,menu))Viewer.ContextMenu=null;}; menu.Open(Viewer);
    }
}
