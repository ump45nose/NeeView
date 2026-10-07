// Copyright (c) NeeLaboratory. 原ConsoleHost/ConsoleWindow执行、历史及补全，Avalonia适配，MIT。
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Threading;
namespace NeeView.MacOS.Views;
/// <summary>独立控制台表现；只有本窗口的执行由本窗口取消，不影响其他事件或命令脚本。</summary>
public sealed class ScriptConsoleWindow : Window
{
    private readonly TextBox _input = new() { Name = "ScriptInput", AcceptsReturn = true, MinHeight = 110, TextWrapping = Avalonia.Media.TextWrapping.Wrap };
    private readonly TextBox _output = new() { Name = "ScriptOutput", IsReadOnly = true, AcceptsReturn = true, TextWrapping = Avalonia.Media.TextWrapping.Wrap };
    private readonly ScriptManager _manager;
    private readonly Button _run = new() { Content = "执行" };
    private readonly List<string> _history = [];
    private readonly ListBox _suggestions = new() { MinWidth = 320, MaxHeight = 200 };
    private readonly Popup _completion;
    private readonly Func<IReadOnlyList<string>> _words;
    private readonly Action? _help;
    private CancellationTokenSource? _execution;
    private bool _closed;
    private int _historyIndex;
    private string _draft = "";
    public ScriptConsoleWindow(ScriptManager manager, Func<IReadOnlyList<string>>? words = null, Action? help = null)
    {
        _manager = manager; _words = words ?? (() => []); _help = help; Title = "NeeView · 脚本控制台"; Width = 720; Height = 540;
        _completion = new() { PlacementTarget = _input, Child = _suggestions, IsLightDismissEnabled = true };
        var cancel = new Button { Content = "停止全部脚本" }; cancel.Click += (_, _) => manager.CancelAll();
        var clear = new Button { Content = "清空输出" }; clear.Click += (_, _) => _output.Text = "";
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { _run, cancel, clear } };
        var grid = new Grid { RowDefinitions = new("*,Auto,Auto,Auto"), Margin = new(12), RowSpacing = 8 };
        Grid.SetRow(_input, 1); Grid.SetRow(buttons, 2);
        var hint = new TextBlock { Text = "Enter 执行 · Shift+Enter 换行 · ↑↓ 历史 · Tab 补全" }; Grid.SetRow(hint, 3);
        grid.Children.Add(_output); grid.Children.Add(_input); grid.Children.Add(buttons); grid.Children.Add(hint); grid.Children.Add(_completion); Content = grid;
        _run.Click += async (_, _) => await ExecuteInputAsync();
        _input.KeyDown += InputKeyDown;
        _suggestions.DoubleTapped += (_, _) => AcceptCompletion();
        manager.Log += ManagerLog;
        Closed += (_, _) => { _closed = true; _completion.IsOpen = false; manager.Log -= ManagerLog; _execution?.Cancel(); };
    }
    private async void InputKeyDown(object? sender, KeyEventArgs e)
    {
        if (_completion.IsOpen && e.Key is Key.Up or Key.Down)
        { e.Handled = true; _suggestions.SelectedIndex = Math.Clamp(_suggestions.SelectedIndex + (e.Key == Key.Up ? -1 : 1), 0, _suggestions.ItemCount - 1); return; }
        if (e.Key == Key.Escape) { _completion.IsOpen = false; return; }
        if (e.Key == Key.Tab || e.Key == Key.Space && e.KeyModifiers == KeyModifiers.Control)
        { e.Handled = true; if (_completion.IsOpen) AcceptCompletion(); else Complete(); return; }
        if (e.Key == Key.Enter && !e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        { e.Handled = true; if (_completion.IsOpen) AcceptCompletion(); else await ExecuteInputAsync(); return; }
        if (e.Key is Key.Up or Key.Down && e.KeyModifiers == KeyModifiers.None && !(_input.Text ?? "").Contains('\n'))
        {
            e.Handled = true;
            if (_historyIndex == _history.Count) _draft = _input.Text ?? "";
            _historyIndex = Math.Clamp(_historyIndex + (e.Key == Key.Up ? -1 : 1), 0, _history.Count);
            _input.Text = _historyIndex == _history.Count ? _draft : _history[_historyIndex]; _input.CaretIndex = _input.Text.Length;
        }
    }
    private void Complete()
    {
        var prefix = ScriptCompletion.Prefix(_input.Text ?? "", _input.CaretIndex).Prefix;
        var candidates = _words().Where(w => w.StartsWith(prefix, StringComparison.Ordinal)).Take(100).ToArray();
        _suggestions.ItemsSource = candidates; _suggestions.SelectedIndex = candidates.Length > 0 ? 0 : -1; _completion.IsOpen = candidates.Length > 0;
    }
    private void AcceptCompletion()
    {
        if (_suggestions.SelectedItem is not string word) return;
        var text = _input.Text ?? ""; var caret = _input.CaretIndex; var prefix = ScriptCompletion.Prefix(text, caret);
        _input.Text = text[..prefix.Start] + word + text[caret..]; _input.CaretIndex = prefix.Start + word.Length;
        _completion.IsOpen = false; _input.Focus();
    }
    /// <summary>原cls/help/exit语义与真实JavaScript共享入口；历史最多100项，输出最多64Ki字符。</summary>
    public async Task ExecuteInputAsync()
    {
        if (_closed || _execution is not null) return;
        var code = _input.Text ?? ""; if (string.IsNullOrWhiteSpace(code)) return;
        if (_history.LastOrDefault() != code) { _history.Add(code); if (_history.Count > 100) _history.RemoveAt(0); }
        _historyIndex = _history.Count; _draft = ""; _input.Text = "";
        switch (code.Trim())
        {
            case "cls": _output.Text = ""; return;
            case "?": case "help": _help?.Invoke(); return;
            case "exit": Close(); return;
        }
        using var cancel = new CancellationTokenSource(); _execution = cancel; _run.IsEnabled = false;
        Append(new("input", code));
        try { var result = await _manager.EvaluateAsync(code, cancel.Token); Append(new("result", Convert.ToString(result) ?? "undefined")); }
        catch (OperationCanceledException) { Append(new("info", "脚本已停止。")); }
        catch (Exception) { /* Manager已发布实际来源和行号；不重复记录。 */ }
        finally { _execution = null; if (!_closed) _run.IsEnabled = true; }
    }
    private void ManagerLog(object? sender, ScriptLog entry) => Dispatcher.UIThread.Post(() => { if (!_closed) Append(entry); });
    public void Append(ScriptLog entry)
    {
        if (_closed) return;
        var text = (_output.Text ?? "") + $"[{entry.Level}] {entry.Message}" + (entry.Path is null ? "" : $" ({entry.Path}:{entry.Line})") + "\n";
        _output.Text = text.Length > 65536 ? text[^65536..] : text; _output.CaretIndex = _output.Text.Length;
    }
}
