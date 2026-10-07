// Copyright (c) NeeLaboratory. 原窗口/面板脚本访问的Avalonia适配，MIT。
using Avalonia.Controls;
namespace NeeView.MacOS.ViewModels;
/// <summary>访问现有面板与宿主；不拥有阅读、布局或文件模型。</summary>
public sealed class ScriptPanelAccessors
{
    public ScriptPanelAccessors(ScriptAccessContext context, ReaderWorkspaceViewModel model, Window window, Func<string, Window?> resolve, ScriptPanelBindings? bindings = null, MainViewPanelAccessor? mainView = null)
    {
        Window = new(context, () => window, () => { window.Show(); window.Activate(); }, window.Close);
        MainView = mainView ?? new(context);
        Bookshelf = new BookshelfPanelAccessor(context, model, resolve, bindings); PageList = new PageListPanelAccessor(context, model, resolve, bindings);
        Bookmark = new BookmarkPanelAccessor(context, model, resolve, bindings); Playlist = new PlaylistPanelAccessor(context, model, resolve, bindings);
        History = new HistoryPanelAccessor(context, model, resolve, bindings); Information = new(context, model, "FileInformationPanel", resolve);
        Effect = new(context, model, "ImageEffectPanel", resolve); Navigator = new(context, model, "NavigatePanel", resolve);
    }
    public WindowAccessor Window { get; }
    public MainViewPanelAccessor MainView { get; }
    public BookshelfPanelAccessor Bookshelf { get; }
    public PageListPanelAccessor PageList { get; }
    public BookmarkPanelAccessor Bookmark { get; }
    public PlaylistPanelAccessor Playlist { get; }
    public HistoryPanelAccessor History { get; }
    public LayoutPanelAccessor Information { get; }
    public LayoutPanelAccessor Effect { get; }
    public LayoutPanelAccessor Navigator { get; }
}
public class LayoutPanelAccessor
{
    protected readonly ScriptAccessContext Context;
    protected readonly ReaderWorkspaceViewModel Model;
    private readonly string _key;
    private readonly Func<string, Window?> _resolve;
    public LayoutPanelAccessor(ScriptAccessContext context, ReaderWorkspaceViewModel model, string key, Func<string, Window?> resolve)
    { Context = context; Model = model; _key = key; _resolve = resolve; }
    public bool IsSelected { get => Context.Read(() => Model.Layout.IsPanelSelected(_key)); set { if (value) Open(); else Close(); } }
    public bool IsVisible => Context.Read(() => Model.IsPanelVisible(_key));
    public bool IsFloating => Context.Read(() => Model.Layout.IsFloating(_key));
    public WindowAccessor Window => new(Context, () => _resolve(_key), () => Model.Layout.OpenWindow(_key), () => Model.Layout.OpenDock(_key));
    public void Open() => Context.Write(() => Model.ShowPanel(_key));
    public void OpenDock() => Context.Write(() => { Model.Layout.OpenDock(_key); Model.ShowPanel(_key); });
    public void OpenFloat() => Context.Write(() => Model.Layout.OpenWindow(_key));
    public void Close() => Context.Write(() => Model.Layout.Close(_key));
}
public sealed class WindowAccessor(ScriptAccessContext context, Func<Window?> resolve, Action open, Action close)
{
    public bool IsOpen { get => context.Read(() => resolve() is not null); set { if (value) Open(); else Close(); } }
    public double Left { get => context.Read(() => (double)(resolve()?.Position.X ?? 0)); set => context.Write(() => { if (resolve() is { } w) w.Position = new((int)value, w.Position.Y); }); }
    public double Top { get => context.Read(() => (double)(resolve()?.Position.Y ?? 0)); set => context.Write(() => { if (resolve() is { } w) w.Position = new(w.Position.X, (int)value); }); }
    public double Width { get => context.Read(() => resolve()?.Width ?? 0); set => context.Write(() => { if (resolve() is { } w) w.Width = value; }); }
    public double Height { get => context.Read(() => resolve()?.Height ?? 0); set => context.Write(() => { if (resolve() is { } w) w.Height = value; }); }
    public string State
    {
        get => context.Read(() => resolve()?.WindowState.ToString() ?? "None");
        set => context.Write(() =>
        {
            var state = Enum.Parse<NeeView.Windows.WindowStateEx>(value);
            if (!Enum.IsDefined(state)) throw new ArgumentException("Unknown window state.");
            if (state == NeeView.Windows.WindowStateEx.None || resolve() is not { } w) return;
            if (state == NeeView.Windows.WindowStateEx.FullDesktop) throw new NotSupportedException("跨屏全桌面宿主尚未迁移。");
            w.WindowState = Enum.Parse<WindowState>(state.ToString());
        });
    }
    public void Open() => context.Write(open);
    public void Close() => context.Write(close);
    public void Focus() => context.Write(() => resolve()?.Activate());
}
/// <summary>中央查看器专属浮动代理；未接入时明确报错，绝不关闭主窗口。</summary>
public sealed class MainViewPanelAccessor
{
    public MainViewPanelAccessor(ScriptAccessContext context, Func<Window?>? resolve = null, Action? open = null, Action? close = null)
    {
        Window = new(context, resolve ?? (() => null), open ?? (() => throw new NotSupportedException("中央查看器浮动宿主尚未接入。")), close ?? (() => { }));
    }
    public WindowAccessor Window { get; }
    public void Open() => Window.Open();
    public void Close() => Window.Close();
}
