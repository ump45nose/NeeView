using Avalonia.Controls;
namespace NeeView.MacOS.Views;

public sealed partial class MainWindow
{
    private ContextMenu? _externalMenu;
    /// <summary>原Index0表示选择应用，直接命令与配置索引进入同一实际业务。</summary>
    private async Task ExecuteExternalApplicationAsync(string name, bool book)
    {
        if (_model is null || _preparing || _closedPrepared) return;
        if (name == "OpenExternalApp" || _model.Operation.GetExternalApplicationIndex(name) > 0)
        { await _model.Operation.OpenExternalApplicationCommandAsync(name); return; }
        _externalMenu?.Close();
        var menu = new ContextMenu(); _externalMenu = menu;
        var items = Config.Current.System.ExternalAppCollection.Select((app, index) =>
        {
            var item = new MenuItem { Header = app.DisplayName, IsEnabled = _model.Operation.CanOpenExternalApplication(_model.Operation.GetExternalApplicationPolicy(name), book) };
            item.Click += async (_, _) =>
            {
                if (_model is null || _preparing || _closedPrepared) return;
                try { await _model.Operation.OpenExternalApplicationCommandAsync(name, index + 1); }
                catch (Exception ex) { ShowError(ex.Message); }
                if (!_preparing && !_closedPrepared) RefreshHistoryCommandStates();
            };
            return item;
        }).ToArray();
        menu.ItemsSource = items.Length == 0 ? [new MenuItem { Header = "尚未配置外部应用（设置 → 文件操作）", IsEnabled = false }] : items;
        menu.Closed += (_, _) => { if (ReferenceEquals(_externalMenu, menu)) _externalMenu = null; };
        menu.Open(Viewer);
    }
}
