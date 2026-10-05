// Copyright (c) NeeLaboratory. 适配原 LayoutPanel/Collection/DockPanelContent/Manager，遵循仓库 MIT。
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using NeeView.Windows;
namespace NeeView.Runtime.LayoutPanel;

public enum PanelOrientation { Horizontal, Vertical }
public enum PanelDock { Left, Right, Top, Bottom }

/// <summary>原面板数据；控件、拖影与浮动窗口留在展示端。</summary>
public sealed class LayoutPanel(string key)
{
    public string Key { get; } = key;
    public double Weight { get; set; } = 1;
    public WindowPlacement WindowPlacement { get; set; } = WindowPlacement.None;
}

/// <summary>原面板组：第一项为侧栏图标，组内共享排列方向。</summary>
public sealed class LayoutPanelCollection : List<LayoutPanel>
{
    public PanelOrientation Orientation { get; set; } = PanelOrientation.Vertical;
}

/// <summary>原 Dock 的顺序和选择状态，与视图及书籍无关。</summary>
public sealed class LayoutDockPanelContent
{
    public List<LayoutPanelCollection> Items { get; } = [];
    public LayoutPanelCollection? SelectedItem { get; set; }
}

/// <summary>迁入原组移动、单面板组合及布局快照；不提供通用停靠平台。</summary>
public sealed class LayoutPanelManager
{
    public static readonly string[] LeftDefaults = ["FolderPanel", "PageListPanel", "HistoryPanel"];
    public static readonly string[] RightDefaults = ["FileInformationPanel", "NavigatePanel", "ImageEffectPanel", "BookmarkPanel", "PlaylistPanel", "DestinationFolderPanel"];
    public Dictionary<string, LayoutPanel> Panels { get; } = LeftDefaults.Concat(RightDefaults).ToDictionary(k => k, k => new LayoutPanel(k));
    public Dictionary<string, LayoutDockPanelContent> Docks { get; } = new() { ["Left"] = new(), ["Right"] = new() };
    /// <summary>当前打开的浮动面板键；位置独立保存，关闭后仍可重新浮动。</summary>
    public IReadOnlyCollection<string> Windows => _windows;
    private readonly HashSet<string> _windows = new(StringComparer.Ordinal);
    private readonly LayoutPanelManagerMemento _original;
    public event EventHandler? Changed;

    /// <summary>恢复原三代布局、选择与 GridLength；缺失的原面板补回默认栏。</summary>
    public LayoutPanelManager(LayoutPanelManagerMemento? memento)
    {
        _original = memento ?? new();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var side in Docks.Keys)
        {
            var dock = Docks[side];
            var saved = _original.Docks?.GetValueOrDefault(side);
            foreach (var layout in saved?.PanelLayout ?? [])
            {
                var group = new LayoutPanelCollection { Orientation = layout.Orientation };
                group.AddRange(layout.Panels.Where(k => Panels.ContainsKey(k) && seen.Add(k)).Select(k => Panels[k]));
                if (group.Count > 0) dock.Items.Add(group);
            }
            var defaults = side == "Left" ? LeftDefaults : RightDefaults;
            // 完整旧布局可能把默认栏成员移到另一栏；在读取两边后统一补缺项。
            if (saved is null && _original.Docks is null)
                foreach (var key in defaults.Where(seen.Add)) dock.Items.Add(new() { Panels[key] });
            // 原 Restore 未找到选择成员时保持关闭，未知旧面板不能意外打开首组。
            dock.SelectedItem = saved is null ? dock.Items.FirstOrDefault() :
                dock.Items.FirstOrDefault(g => g.Any(p => p.Key == saved.SelectedItem));
        }
        foreach (var key in Panels.Keys.Where(k => !seen.Contains(k)))
            Docks[LeftDefaults.Contains(key) ? "Left" : "Right"].Items.Add(new() { Panels[key] });
        foreach (var (side, dock) in Docks)
            if (_original.Docks?.GetValueOrDefault(side) is null) dock.SelectedItem ??= dock.Items.FirstOrDefault();
        foreach (var (key, panel) in Panels)
        {
            var length = _original.Panels?.GetValueOrDefault(key)?.GridLength;
            if (length?.EndsWith('*') == true && double.TryParse(length[..^1], NumberStyles.Float, CultureInfo.InvariantCulture, out var weight) && double.IsFinite(weight) && weight > 0) panel.Weight = weight;
            panel.WindowPlacement = _original.Panels?.GetValueOrDefault(key)?.WindowPlacement ?? WindowPlacement.None;
        }
        foreach (var key in (_original.Windows?.Panels ?? []).Where(Panels.ContainsKey).Distinct().ToArray())
        { StandAlone(key); CloseDock(key); _windows.Add(key); }
    }

    /// <summary>找到原面板所属栏与组，跨栏后仍使用同一面板对象。</summary>
    public (string Side, LayoutPanelCollection Group)? Find(string key)
    {
        foreach (var (side, dock) in Docks)
            foreach (var group in dock.Items)
                if (group.Any(p => p.Key == key)) return (side, group);
        return null;
    }

    /// <summary>原 StandAlone：只拆出所选成员，保留所在栏和其余组选择。</summary>
    public bool StandAlone(string key)
    {
        if (Find(key) is not { } found) return false;
        if (found.Group.Count == 1) return true;
        var dock = Docks[found.Side]; var index = dock.Items.IndexOf(found.Group);
        found.Group.Remove(Panels[key]); dock.Items.Insert(index + 1, new() { Panels[key] }); return true;
    }
    /// <summary>原 Open 根据保存位置重新打开浮窗，否则选择停靠组。</summary>
    public void Open(string key)
    { if (!Panels.TryGetValue(key, out var panel)) return; if (IsFloating(key)) OpenWindow(key); else OpenDock(key); }
    /// <summary>原 OpenWindow：拆成单成员、清除该停靠选择、登记浮动窗口。</summary>
    public void OpenWindow(string key, WindowPlacement? placement = null)
    {
        if (!StandAlone(key)) return;
        if (placement?.IsValid() == true) Panels[key].WindowPlacement = placement;
        CloseDock(key); _windows.Add(key); Notify();
    }
    /// <summary>停靠关闭浮窗并选中原组；对应原 dock Snap 清除有效浮动位置。</summary>
    public void OpenDock(string key)
    {
        if (Find(key) is not { } found) return;
        _windows.Remove(key); Panels[key].WindowPlacement = WindowPlacement.None;
        Docks[found.Side].SelectedItem = found.Group; Notify();
    }
    /// <summary>关闭隐藏当前宿主，保留位置；标题关闭先拆组，与原容器命令一致。</summary>
    public void Close(string key, bool standAlone = false)
    { if (!Panels.ContainsKey(key)) return; if (standAlone) StandAlone(key); _windows.Remove(key); CloseDock(key); Notify(); }
    /// <summary>浮动位置和当前打开集合是两个独立的原状态。</summary>
    public bool IsFloating(string key) => Panels.TryGetValue(key, out var panel) && (panel.WindowPlacement.IsValid() || _windows.Contains(key));
    /// <summary>只有打开浮窗或停靠选中组算选中。</summary>
    public bool IsPanelSelected(string key) => _windows.Contains(key) || Find(key) is { } found && ReferenceEquals(Docks[found.Side].SelectedItem, found.Group);
    /// <summary>保存窗口快照只更新布局，不重建阅读或登记访问。</summary>
    public void SetWindowPlacement(string key, WindowPlacement placement)
    { if (Panels.TryGetValue(key, out var panel)) { panel.WindowPlacement = placement; CreateMemento(); } }
    private void CloseDock(string key)
    { foreach (var dock in Docks.Values) if (dock.SelectedItem?.Any(p => p.Key == key) == true) dock.SelectedItem = null; }

    /// <summary>原 MovePanel：leader 携带整组；非 leader 从组分离成独立图标。</summary>
    /// <param name="index">移除源项前的目标栏插入位置。</param>
    public bool MovePanel(string key, string side, int index)
    {
        if (Find(key) is not { } source || !Docks.TryGetValue(side, out var target)) return false;
        var sourceDock = Docks[source.Side]; var group = source.Group; var panel = Panels[key];
        _windows.Remove(key); panel.WindowPlacement = WindowPlacement.None;
        if (ReferenceEquals(group[0], panel))
        {
            var old = sourceDock.Items.IndexOf(group);
            if (ReferenceEquals(sourceDock, target) && old < index) index--;
            sourceDock.Items.Remove(group);
        }
        else { group.Remove(panel); group = new() { panel }; }
        target.Items.Insert(Math.Clamp(index, 0, target.Items.Count), group); target.SelectedItem = group;
        RepairSelection(sourceDock); Notify(); return true;
    }

    /// <summary>原容器 Drop：同组重排；跨组移入单个面板并均分目标权重。</summary>
    public bool CombinePanel(string key, string targetKey, PanelDock edge)
    {
        if (key == targetKey || Find(key) is not { } source || Find(targetKey) is not { } target) return false;
        var panel = Panels[key]; var anchor = Panels[targetKey]; var group = target.Group;
        if (_windows.Contains(targetKey)) return false;
        _windows.Remove(key); panel.WindowPlacement = WindowPlacement.None;
        group.Orientation = edge is PanelDock.Top or PanelDock.Bottom ? PanelOrientation.Vertical : PanelOrientation.Horizontal;
        bool sameGroup = ReferenceEquals(source.Group, group);
        source.Group.Remove(panel);
        if (!sameGroup)
        {
            // 原跨组分割仅均分目标项，不覆盖其余成员比例。
            panel.Weight = anchor.Weight *= .5;
            if (source.Group.Count == 0) Docks[source.Side].Items.Remove(source.Group);
        }
        var index = group.IndexOf(anchor) + (edge is PanelDock.Bottom or PanelDock.Right ? 1 : 0);
        group.Insert(index, panel); Docks[target.Side].SelectedItem = group;
        RepairSelection(Docks[source.Side]); Notify(); return true;
    }

    /// <summary>原 GetLayoutDockFromPos：已有多项组按原方向分半，单项按离中心较远的轴分半。</summary>
    public static PanelDock GetLayoutDockFromPos(double x, double y, double width, double height, LayoutPanelCollection group)
    {
        if (width <= 0 || height <= 0) return PanelDock.Bottom;
        if (group.Count > 1) return group.Orientation == PanelOrientation.Horizontal ? (x < width * .5 ? PanelDock.Left : PanelDock.Right) : (y < height * .5 ? PanelDock.Top : PanelDock.Bottom);
        var xr = x / width; var yr = y / height;
        return Math.Abs(xr - .5) < Math.Abs(yr - .5) ? (yr < .5 ? PanelDock.Top : PanelDock.Bottom) : (xr < .5 ? PanelDock.Left : PanelDock.Right);
    }

    /// <summary>保存原 Docks/PanelLayoutV2/SelectedItem 和 GridLength；保留未支持窗口字段。</summary>
    public LayoutPanelManagerMemento CreateMemento()
    {
        _original.Docks ??= [];
        _original.Panels ??= [];
        foreach (var (side, dock) in Docks)
        {
            if (!_original.Docks.TryGetValue(side, out var saved)) _original.Docks[side] = saved = new();
            // 不让当前已知面板过滤静默删除旧/扩展面板的布局信息；旁路材料不参与运行恢复。
            if (saved.PanelLayout.Any(g => g.Panels.Any(key => !Panels.ContainsKey(key))))
            {
                saved.Extra ??= [];
                if (!saved.Extra.ContainsKey("MacImportedUnrecognizedLayout"))
                    saved.Extra["MacImportedUnrecognizedLayout"] = JsonSerializer.SerializeToElement(saved.PanelLayout);
            }
            saved.PanelLayout = dock.Items.Select(g => new LayoutDockPanelLayout { Orientation = g.Orientation, Panels = g.Select(p => p.Key).ToList() }).ToList();
            saved.SelectedItem = dock.SelectedItem?.FirstOrDefault()?.Key;
        }
        foreach (var (key, panel) in Panels)
        {
            if (!_original.Panels.TryGetValue(key, out var saved)) _original.Panels[key] = saved = new();
            saved.GridLength = panel.Weight.ToString("G17", CultureInfo.InvariantCulture) + "*";
            saved.WindowPlacement = panel.WindowPlacement;
        }
        _original.Windows ??= new();
        _original.Windows.Panels = (_original.Windows.Panels ?? []).Where(k => !Panels.ContainsKey(k)).Concat(_windows).Distinct().ToList();
        return _original;
    }

    /// <summary>源组消失时选中余下首组，空栏没有选择。</summary>
    private static void RepairSelection(LayoutDockPanelContent dock)
    { if (dock.SelectedItem is null || !dock.Items.Contains(dock.SelectedItem)) dock.SelectedItem = dock.Items.FirstOrDefault(); }
    /// <summary>只通知布局改变，不触发阅读状态刷新。</summary>
    private void Notify() => Changed?.Invoke(this, EventArgs.Empty);
}

/// <summary>原布局 JSON 节点；Windows/AlternativePanelSource 原样保留。</summary>
public sealed class LayoutPanelManagerMemento
{
    public Dictionary<string, LayoutPanelMemento>? Panels { get; set; }
    public Dictionary<string, LayoutDockPanelContentMemento>? Docks { get; set; }
    public LayoutPanelWindowManagerMemento? Windows { get; set; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; set; }
}
public sealed class LayoutPanelMemento
{
    public string GridLength { get; set; } = "1*";
    public WindowPlacement WindowPlacement { get; set; } = WindowPlacement.None;
    [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; set; }
}
/// <summary>原 Windows.Panels 格式；未识别字段和面板保留供后续迁移。</summary>
public sealed class LayoutPanelWindowManagerMemento
{
    public List<string> Panels { get; set; } = [];
    [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; set; }
}
public sealed class LayoutDockPanelContentMemento : IJsonOnDeserialized
{
    [JsonPropertyName("PanelLayoutV2")] public List<LayoutDockPanelLayout> PanelLayout { get; set; } = [];
    /// <summary>Mac原JSON合并必须显式写null清除旧选择，否则关闭后保存会重新打开。</summary>
    public string? SelectedItem { get; set; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; set; }
    /// <summary>普通配置载入同样恢复旧布局；无需先经过导入窗口。</summary>
    public void OnDeserialized()
    {
        PanelLayout ??= [];
        if (PanelLayout.Count != 0 || Extra is null) return;
        var raw = new System.Text.Json.Nodes.JsonObject();
        foreach (var pair in Extra) raw[pair.Key] = System.Text.Json.Nodes.JsonNode.Parse(pair.Value.GetRawText());
        if (LayoutPanelCompatibility.ReadLegacy(raw) is { } groups) PanelLayout = groups;
    }
}

/// <summary>沿用原 Orientation:key1,key2 字符串格式。</summary>
[JsonConverter(typeof(JsonLayoutDockPanelLayoutConverter))]
public sealed class LayoutDockPanelLayout
{
    public PanelOrientation Orientation { get; set; } = PanelOrientation.Vertical;
    public List<string> Panels { get; set; } = [];
}
public sealed class JsonLayoutDockPanelLayoutConverter : JsonConverter<LayoutDockPanelLayout>
{
    /// <summary>按原冒号/逗号读取排列方向与面板键；非法格式明确返回解析错误。</summary>
    public override LayoutDockPanelLayout Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var tokens = (reader.GetString() ?? "Vertical:").Split(':', 2);
        if (!Enum.TryParse<PanelOrientation>(tokens[0], out var orientation) || !Enum.IsDefined(orientation)) throw new JsonException("无效的面板排列方向。");
        return new() { Orientation = orientation, Panels = tokens.Length > 1 ? tokens[1].Split(',', StringSplitOptions.RemoveEmptyEntries).ToList() : [] };
    }
    /// <summary>序列化为原 PanelLayoutV2 字符串，布局不绑定 Avalonia 类型。</summary>
    public override void Write(Utf8JsonWriter writer, LayoutDockPanelLayout value, JsonSerializerOptions options) => writer.WriteStringValue(value.Orientation + ":" + string.Join(',', value.Panels));
}
