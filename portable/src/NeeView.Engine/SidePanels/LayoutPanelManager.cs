// Copyright (c) NeeLaboratory. 适配原 LayoutPanel/Collection/DockPanelContent/Manager，遵循仓库 MIT。
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
namespace NeeView.Runtime.LayoutPanel;

public enum PanelOrientation { Horizontal, Vertical }
public enum PanelDock { Left, Right, Top, Bottom }

/// <summary>原面板数据；控件、拖影与浮动窗口留在展示端。</summary>
public sealed class LayoutPanel(string key)
{
    public string Key { get; } = key;
    public double Weight { get; set; } = 1;
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
    private readonly LayoutPanelManagerMemento _original;
    public event EventHandler? Changed;

    /// <summary>恢复原 PanelLayoutV2、选择与 GridLength；缺失的原面板补回默认栏。</summary>
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
            dock.SelectedItem = dock.Items.FirstOrDefault(g => g.Any(p => p.Key == saved?.SelectedItem)) ?? dock.Items.FirstOrDefault();
        }
        foreach (var key in Panels.Keys.Where(k => !seen.Contains(k)))
            Docks[LeftDefaults.Contains(key) ? "Left" : "Right"].Items.Add(new() { Panels[key] });
        foreach (var (key, panel) in Panels)
        {
            var length = _original.Panels?.GetValueOrDefault(key)?.GridLength;
            if (length?.EndsWith('*') == true && double.TryParse(length[..^1], NumberStyles.Float, CultureInfo.InvariantCulture, out var weight) && double.IsFinite(weight) && weight > 0) panel.Weight = weight;
        }
        foreach (var dock in Docks.Values) dock.SelectedItem ??= dock.Items.FirstOrDefault();
    }

    /// <summary>找到原面板所属栏与组，跨栏后仍使用同一面板对象。</summary>
    public (string Side, LayoutPanelCollection Group)? Find(string key)
    {
        foreach (var (side, dock) in Docks)
            foreach (var group in dock.Items)
                if (group.Any(p => p.Key == key)) return (side, group);
        return null;
    }

    /// <summary>原 MovePanel：leader 携带整组；非 leader 从组分离成独立图标。</summary>
    /// <param name="index">移除源项前的目标栏插入位置。</param>
    public bool MovePanel(string key, string side, int index)
    {
        if (Find(key) is not { } source || !Docks.TryGetValue(side, out var target)) return false;
        var sourceDock = Docks[source.Side]; var group = source.Group; var panel = Panels[key];
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
            saved.PanelLayout = dock.Items.Select(g => new LayoutDockPanelLayout { Orientation = g.Orientation, Panels = g.Select(p => p.Key).ToList() }).ToList();
            saved.SelectedItem = dock.SelectedItem?.FirstOrDefault()?.Key;
        }
        foreach (var (key, panel) in Panels)
        {
            if (!_original.Panels.TryGetValue(key, out var saved)) _original.Panels[key] = saved = new();
            saved.GridLength = panel.Weight.ToString("G17", CultureInfo.InvariantCulture) + "*";
        }
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
    [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; set; }
}
public sealed class LayoutPanelMemento
{
    public string GridLength { get; set; } = "1*";
    [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; set; }
}
public sealed class LayoutDockPanelContentMemento
{
    [JsonPropertyName("PanelLayoutV2")] public List<LayoutDockPanelLayout> PanelLayout { get; set; } = [];
    public string? SelectedItem { get; set; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; set; }
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
