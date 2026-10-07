using Avalonia.Controls;
using NeeView.MacOS.ViewModels;
namespace NeeView.MacOS.Views;
public sealed partial class MainWindow
{
    /// <summary>将真实列表与选区装配到独立脚本表现适配，不复制业务状态。</summary>
    public ScriptPanelAccessors CreateScriptPanels(ScriptAccessContext context)
    {
        if (_model is null) throw new InvalidOperationException("MainWindow is not initialized.");
        var bookmark = this.FindControl<BookmarkListView>("BookmarkPanelList")!;
        var playlist = this.FindControl<PlaylistView>("PlaylistPanelView")!;
        var bindings = new ScriptPanelBindings(key => key switch
        {
            "PageListPanel" => this.FindControl<ListBox>("PageList"), "HistoryPanel" => this.FindControl<ListBox>("HistoryList"),
            "FolderPanel" => this.FindControl<ListBox>("FolderList"), "BookmarkPanel" => bookmark.FindControl<ListBox>("BookmarkItems"),
            "PlaylistPanel" => playlist.FindControl<ListBox>("PlaylistItems"), _ => null
        }, (key, style) => key switch
        {
            "PageListPanel" => SetPageListStyleAsync(style), "HistoryPanel" => SetListStyleAsync(true, style),
            "FolderPanel" => SetListStyleAsync(false, style), "BookmarkPanel" => bookmark.SetListStyleAsync(style),
            "PlaylistPanel" => playlist.SetListStyleAsync(style), _ => throw new NotSupportedException("未知面板。")
        }, bookmark.ScriptModel, key => this.FindControl<TreeView>(key));
        return new(context, _model, this, key => _sidePanels?.FloatingWindows.FirstOrDefault(w => w.PanelKey == key), bindings,
            new(context, () => _mainView?.Window, () => _mainView?.SetFloating(true, true), () => _mainView?.SetFloating(false)));
    }
}
