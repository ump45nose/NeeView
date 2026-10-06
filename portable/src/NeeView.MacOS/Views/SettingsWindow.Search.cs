using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using NeeView.MacOS.ViewModels;
namespace NeeView.MacOS.Views;

public sealed partial class SettingsWindow
{
    private static readonly string[] SearchPageNames = ["ReadingSettings", "InputSettings", "FilmSettings", "AutoHideSettings", "HistorySettings", "NavigationSettings", "FileSettings", "ThemeSettings", "FontSettings", "ArchiveSettings", "SlideShowSettings", "BackgroundSettings", "LoupeSettings"];
    private SettingsSearchPresenter? _settingsSearchPresenter;
    private NavigationSearchViewModel? _settingsSearch;
    private bool _showingSearch;
    /// <summary>连接原搜索语法及仅窗口内历史，所有结果沿现有表单保存事务。</summary>
    private void InitializeSettingsSearch()
    {
        var navigation = this.FindControl<ListBox>("SettingsNavigation")!;
        var pages = SearchPageNames.Select((name, i) => (this.FindControl<Control>(name)!, ((ListBoxItem)navigation.Items[i]!).Content!.ToString()!));
        _settingsSearchPresenter = new(pages, _inputs, this.FindControl<StackPanel>("SettingsSearchResults")!, this.FindControl<ListBox>("InputList")!.ItemTemplate);
        var history = new HistoryStringCollection();
        _settingsSearch = new(() => _settingsSearchPresenter.Index, _settingsSearchPresenter.Index.Analyze, SearchSettingsAsync, history,
            (keyword, remove, _) => { if (remove) history.Remove(keyword); else history.Append(keyword); return Task.CompletedTask; });
        this.FindControl<StackPanel>("SettingsSearchInput")!.DataContext = _settingsSearch;
        Closed += (_, _) => { _settingsSearch.Dispose(); _settingsSearchPresenter.Dispose(); };
    }
    /// <summary>先完整匹配再替换结果；错误/取消保留上次有效结果，空查询恢复普通页。</summary>
    private Task<bool> SearchSettingsAsync(string keyword, object? expected, CancellationToken token)
    {
        if (_saving || _settingsSearchPresenter is null || !ReferenceEquals(expected, _settingsSearchPresenter.Index)) return Task.FromResult(false);
        var results = _settingsSearchPresenter.Index.Search(keyword, token); token.ThrowIfCancellationRequested();
        _showingSearch = keyword.Length != 0;
        _settingsSearchPresenter.Restore();
        var navigation = this.FindControl<ListBox>("SettingsNavigation")!;
        SetSettingsPageVisibility(navigation.SelectedIndex);
        if (_showingSearch)
        {
            _settingsSearchPresenter.Show(results);
            this.FindControl<TextBlock>("SettingsSearchSummary")!.Text = results.Count == 0 ? "没有匹配的设置" : $"搜索结果 · {results.Count} 项";
            this.FindControl<ScrollViewer>("SettingsSearchPage")!.Offset = default;
        }
        return Task.FromResult(true);
    }
    /// <summary>只控制现有页面显隐，搜索不会应用草稿或改变普通页选择。</summary>
    private void SetSettingsPageVisibility(int index)
    {
        for (var i = 0; i < SearchPageNames.Length; i++) this.FindControl<Control>(SearchPageNames[i])!.IsVisible = !_showingSearch && i == index;
        this.FindControl<ScrollViewer>("SettingsSearchPage")!.IsVisible = _showingSearch;
    }
    private async void SettingsSearch_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || e.KeyModifiers != KeyModifiers.None || _saving || _settingsSearch is null) return;
        e.Handled = true; await _settingsSearch.SearchAsync();
    }
    private async void SettingsSearch_Clear(object? sender, RoutedEventArgs e)
    { if (_settingsSearch is not null && !_saving) { _settingsSearch.Keyword = ""; await _settingsSearch.SearchAsync(false); } }
    /// <summary>手动删空表达式立即返回上一个普通页，即使全局逐次搜索关闭。</summary>
    private async void SettingsSearch_TextChanged(object? sender, TextChangedEventArgs e)
    {
        if (_settingsSearch is null || !_showingSearch || _saving || !string.IsNullOrWhiteSpace((sender as TextBox)?.Text)) return;
        _settingsSearch.Keyword = ""; await _settingsSearch.SearchAsync(false);
    }
    /// <summary>原历史只存于本设置窗口，不进入SaveData搜索历史或History.json。</summary>
    private void SettingsSearch_History(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button button || _settingsSearch is not { } search || _saving) return;
        var menu = new ContextMenu();
        foreach (var keyword in search.History.ToArray())
        {
            var delete = new Button { Content = "×", Padding = new Thickness(4, 0) };
            var row = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Children = { new TextBlock { Text = keyword, MaxWidth = 150 }, delete } };
            var item = new MenuItem { Header = row };
            item.Click += async (_, _) => { search.Keyword = keyword; await search.SearchAsync(); };
            delete.Click += async (_, args) => { args.Handled = true; menu.Close(); await search.RemoveHistoryAsync(keyword); }; menu.Items.Add(item);
        }
        if (menu.Items.Count == 0) menu.Items.Add(new MenuItem { Header = "无搜索历史", IsEnabled = false });
        button.ContextMenu = menu; menu.Open(button);
    }
}
