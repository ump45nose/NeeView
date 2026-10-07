// Copyright (c) NeeLaboratory. 原ScriptEventer/Console/CommandHost装配的Mac替换，MIT。
using System.Collections.Concurrent;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using NeeView.MacOS.ViewModels;
using NeeView.Text;
namespace NeeView.MacOS.Views;
public sealed partial class MainWindow
{
    private ScriptManager? _scripts;
    private ScriptConsoleWindow? _scriptConsole;
    private readonly Queue<ScriptLog> _scriptLog = new();
    private Task _scriptFolderAction = Task.CompletedTask;
    private CancellationTokenSource? _scriptFolderCancellation;
    private Book? _scriptDisplayedBook;
    private Page[] _scriptDisplayedPages = [];
    private PagePosition _scriptDisplayedPosition;
    public ScriptManager? Scripts => _scripts;
    /// <summary>唯一启动层提供后端；事件订阅与窗口重建成对释放。</summary>
    public async Task AttachScriptsAsync(IScriptRuntimeFactory factory, Action<string, string?> system, ConcurrentDictionary<string, object?> values)
    {
        if (_model is null) throw new InvalidOperationException("阅读模型未装配。");
        _scripts = new(factory, CreateScriptHost, system, Config.Current.Script, values);
        _scripts.SourcesChanged += ScriptSourcesChanged; _scripts.Log += ScriptLogged;
        _model.Operation.BookLoaded += ScriptBookLoaded; _model.Operation.PageTerminated += ScriptPageEnd;
        Viewer.DisplayCompleted += ScriptPageDisplayed;
        PropertyChanged += ScriptWindowChanged;
        await _scripts.ReloadAsync(); ApplyScriptCommands();
    }
    private object CreateScriptHost(ScriptInvocation run) => Dispatcher.UIThread.InvokeAsync(() =>
    {
        var model = _model ?? throw new ObjectDisposedException(nameof(MainWindow));
        var context = new ScriptAccessContext
        {
            Operation = model.Operation, State = model.SaveData, Commands = model.Commands, Token = run.Token,
            Diagnostics = new ScriptAccessDiagnostics((level, message) => _scripts?.ReportNotice(new(level, message, run.Runtime.ScriptPath))),
            Dispatcher = new ScriptDispatcher(this), CanExecute = IsCommandAvailable,
            InvokeAsync = action => Dispatcher.UIThread.InvokeAsync(action),
            ExecuteCommandAsync = ExecuteScriptCommandAsync,
            WaitForDisplayAsync = async token =>
            { while (model.Operation.IsLoading) await Task.Delay(10, token); token.ThrowIfCancellationRequested(); await Viewer.RefreshAsync().WaitAsync(token); },
            MediaPlayer = page => model.Operation.Book?.CurrentPage == page ? model.Operation.CurrentMediaPlayer : null,
            RefreshPresentation = RefreshScriptPresentation
        };
        return new ScriptApplicationHost(run, context, CreateScriptPanels(context), ReportProfileImport,
            (title, message, count) => ShowScriptDialogAsync(title, message, count, run.Token),
            (title, message, value) => ShowScriptInputAsync(title, message, value, run.Token),
            (initial, folder) => PickScriptPathAsync(initial, folder, run.Token));
    }).GetAwaiter().GetResult();
    private sealed class ScriptDispatcher(MainWindow window) : IPropertyMapDispatcher
    {
        public bool CheckAccess() => Dispatcher.UIThread.CheckAccess();
        public T Invoke<T>(Func<T> action) => CheckAccess() ? action() : Dispatcher.UIThread.InvokeAsync(action).GetAwaiter().GetResult();
        public void Invoke(Action action)
        {
            void Apply() { action(); window.RefreshScriptPresentation(); }
            if (CheckAccess()) Apply(); else Dispatcher.UIThread.InvokeAsync(Apply).GetAwaiter().GetResult();
        }
    }
    private void RefreshScriptPresentation()
    { if (_preparing || _closedPrepared) return; _model?.RefreshPanels(); BuildMenus(); Viewer.InvalidateVisual(); }
    private void ScriptSourcesChanged(object? sender, EventArgs e) => Dispatcher.UIThread.Post(ApplyScriptCommands);
    private void ApplyScriptCommands()
    {
        if (_scripts is null || _model is null || _preparing || _closedPrepared) return;
        _model.SaveData.SetScriptDefaults(_scripts.Sources);
        _model.Commands.SetScriptCommands(_scripts.Sources, source => RunScriptCommandAsync(source)); BuildMenus();
    }
    private Task<object?> RunScriptCommandAsync(ScriptCommandSource source, object?[]? args = null)
    {
        var values = args is { Length: > 0 } ? args : StringTools.SplitArgument((_model!.SaveData.GetCommandParameterObject("Script_" + source.Name) as ScriptCommandParameter)?.Argument).Cast<object?>().ToArray();
        return _scripts!.RunFileAsync(source.Path, values, commandName: "Script_" + source.Name);
    }
    private async Task<bool> ExecuteScriptCommandAsync(string name, object?[] args)
    {
        if (_preparing || _closedPrepared || !IsCommandAvailable(name)) return false;
        if (name.StartsWith("Script_", StringComparison.Ordinal) && _scripts?.Sources.FirstOrDefault(s => "Script_" + s.Name == name) is { } source) await RunScriptCommandAsync(source, args);
        else if (args.Length > 0 && name == "JumpPage") await JumpPageAsync(Convert.ToInt32(args[0]));
        else if (args.Length > 0 && name == "LoadAs") await OpenAsync(Convert.ToString(args[0]) ?? "");
        else if (args.Length > 0 && name == "SetEffectProfile") await _model!.Operation.SetEffectProfileAsync(Convert.ToInt32(args[0]));
        else await ExecuteAsync(name, throwOnError: true);
        return true;
    }
    private void ScriptLogged(object? sender, ScriptLog entry) => Dispatcher.UIThread.Post(() =>
    {
        if (_closedPrepared) return;
        _scriptLog.Enqueue(entry); while (_scriptLog.Count > 256) _scriptLog.Dequeue();
        System.Diagnostics.Trace.WriteLine($"Script [{entry.Level}] {entry.Message} {entry.Path}:{entry.Line}");
    });
    private void ScriptBookLoaded(object? sender, BookLoadedEventArgs e)
    { if (!e.Renamed || Config.Current.Script.OnBookLoadedWhenRenamed) _ = RunScriptEventObservedAsync(ScriptCommandSource.OnBookLoadedFilename); }
    private void ScriptPageEnd(object? sender, int direction) => _ = RunScriptEventObservedAsync(ScriptCommandSource.OnPageEndFilename, [direction]);
    private void ScriptPageDisplayed(object? sender, EventArgs e)
    {
        if (_preparing || _closedPrepared || _model?.Operation.Book is not { } book || book.Pages.Count == 0) return;
        var pages = book.CurrentPages.ToArray(); var position = _model.Operation.Position;
        if (ReferenceEquals(book, _scriptDisplayedBook) && position == _scriptDisplayedPosition && pages.SequenceEqual(_scriptDisplayedPages)) return;
        _scriptDisplayedBook = book; _scriptDisplayedPages = pages; _scriptDisplayedPosition = position;
        _ = RunScriptEventObservedAsync(ScriptCommandSource.OnPageChangedFilename);
    }
    private void ScriptWindowChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property != WindowStateProperty || _preparing || _closedPrepared) return;
        Dispatcher.UIThread.Post(() => _ = RunScriptEventObservedAsync(ScriptCommandSource.OnWindowStateChangedFilename));
    }
    private async Task RunScriptEventObservedAsync(string name, object?[]? args = null)
    {
        if (_preparing || _closedPrepared || _scripts is null) return;
        try { await _scripts.RunEventAsync(name, args); }
        catch (OperationCanceledException) { }
        catch (Exception) { /* 执行器已发布真实错误。 */ }
    }
    public async Task RunStartupScriptsAsync(ScriptLaunchRequest? request)
    {
        if (_scripts is null) return;
        if (request is not null)
        { try { await _scripts.RunFileAsync(request.Path, request.Args); } catch (OperationCanceledException) { } catch (Exception) { } }
        await RunScriptEventObservedAsync(ScriptCommandSource.OnStartupFilename);
    }
    private void OpenScriptConsole()
    {
        if (_scripts is null) return;
        if (_scriptConsole is not null) { _scriptConsole.Activate(); return; }
        var console = new ScriptConsoleWindow(_scripts, () => ScriptCompletion.Create(typeof(ScriptApplicationHost), new ConfigMap(Config.Current), _model!.Commands), () => _ = OpenManualAsync("HelpScript")); _scriptConsole = console;
        foreach (var line in _scriptLog) console.Append(line);
        console.Closed += (_, _) => { if (ReferenceEquals(_scriptConsole, console)) _scriptConsole = null; };
        console.Show(this);
    }
    private async Task ReleaseScriptsAsync()
    {
        if (_scripts is not { } scripts) return;
        Viewer.DisplayCompleted -= ScriptPageDisplayed; PropertyChanged -= ScriptWindowChanged;
        if (_model is not null) { _model.Operation.BookLoaded -= ScriptBookLoaded; _model.Operation.PageTerminated -= ScriptPageEnd; }
        scripts.SourcesChanged -= ScriptSourcesChanged; scripts.Log -= ScriptLogged;
        _scriptConsole?.Close(); await scripts.DisposeAsync(); _scripts = null;
    }
    private Task OpenScriptFolderAsync()
    {
        if (_preparing || _closedPrepared || _platform is null || !_scriptFolderAction.IsCompleted) return _scriptFolderAction;
        return _scriptFolderAction = OpenCoreAsync();
        async Task OpenCoreAsync()
        {
            using var cancel = new CancellationTokenSource(); _scriptFolderCancellation = cancel;
            try
            {
                var path = await ScriptFolderService.PrepareAsync(Config.Current.Script.ScriptFolder, cancel.Token);
                cancel.Token.ThrowIfCancellationRequested(); await _platform.OpenFolderAsync(path, cancel.Token);
                if (_scripts is not null) await _scripts.ReloadAsync(cancel.Token);
            }
            catch (OperationCanceledException) when (cancel.IsCancellationRequested) { }
            finally { if (ReferenceEquals(_scriptFolderCancellation, cancel)) _scriptFolderCancellation = null; }
        }
    }
    private async Task<bool> ShowScriptDialogAsync(string title, string message, int buttons, CancellationToken token)
    {
        var dialog = new Window { Title = title, Width = 420, SizeToContent = SizeToContent.Height, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var panel = new StackPanel { Margin = new(16), Spacing = 12 }; panel.Children.Add(new TextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap });
        var accept = new Button { Content = buttons == 2 ? "是" : "确定" }; accept.Click += (_, _) => dialog.Close(true); panel.Children.Add(accept);
        if (buttons != 0) { var cancel = new Button { Content = buttons == 2 ? "否" : "取消" }; cancel.Click += (_, _) => dialog.Close(false); panel.Children.Add(cancel); }
        dialog.Content = panel; using var cancelRegistration = token.Register(() => Dispatcher.UIThread.Post(() => dialog.Close(false)));
        token.ThrowIfCancellationRequested(); return await dialog.ShowDialog<bool>(this);
    }
    private async Task<string?> ShowScriptInputAsync(string title, string? message, string? value, CancellationToken token)
    {
        var input = new TextBox { Text = value ?? "" }; var panel = new StackPanel { Margin = new(16), Spacing = 12 };
        if (!string.IsNullOrEmpty(message)) panel.Children.Add(new TextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap }); panel.Children.Add(input);
        var dialog = new Window { Title = title, Width = 420, SizeToContent = SizeToContent.Height, Content = panel, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var accept = new Button { Content = "确定" }; accept.Click += (_, _) => dialog.Close(input.Text); panel.Children.Add(accept);
        var cancel = new Button { Content = "取消" }; cancel.Click += (_, _) => dialog.Close(null); panel.Children.Add(cancel);
        dialog.Opened += (_, _) => { input.Focus(); input.SelectAll(); };
        using var registration = token.Register(() => Dispatcher.UIThread.Post(() => dialog.Close(null))); token.ThrowIfCancellationRequested();
        return await dialog.ShowDialog<string?>(this);
    }
    private async Task<string?> PickScriptPathAsync(string? initial, bool folder, CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); var location = initial is null ? null : await StorageProvider.TryGetFolderFromPathAsync(initial);
        var paths = folder ? await StorageProvider.OpenFolderPickerAsync(new() { Title = "选择文件夹", SuggestedStartLocation = location })
            : (IReadOnlyList<IStorageItem>)await StorageProvider.OpenFilePickerAsync(new() { Title = "选择文件", SuggestedStartLocation = location });
        token.ThrowIfCancellationRequested(); return paths.FirstOrDefault()?.TryGetLocalPath();
    }
}
