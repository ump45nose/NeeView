using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using NeeView.MacOS.ViewModels;
using System.Text.Json.Serialization;
namespace NeeView.MacOS.Views;

/// <summary>从现有设置布局建立索引并展示同一编辑控件，主题/布局调整不增加第二配置表。</summary>
internal sealed class SettingsSearchPresenter : IDisposable
{
    private sealed record Field(Control Control, Panel Parent, int Position, string Label);
    private sealed record AttachedField(Field Field, bool InheritedContext);
    private readonly Dictionary<string, Field> _fields = [];
    private readonly Dictionary<string, ShortcutEdit> _commands = [];
    private readonly List<AttachedField> _attached = [];
    private readonly StackPanel _results;
    private readonly IDataTemplate? _commandTemplate;
    public SettingsSearchIndex Index { get; }

    /// <summary>索引实际表单和全部原命令；禁用能力也保留，命令参数文案来自现有编辑器。</summary>
    /// <param name="pages">现有页面根及标题。</param><param name="commands">窗口内全部原命令草稿。</param>
    /// <param name="results">搜索结果宿主。</param><param name="commandTemplate">原命令行模板。</param>
    public SettingsSearchPresenter(IEnumerable<(Control Root, string Title)> pages, IReadOnlyList<ShortcutEdit> commands,
        StackPanel results, IDataTemplate? commandTemplate)
    {
        _results = results; _commandTemplate = commandTemplate;
        var items = new List<SettingsSearchItem>();
        foreach (var page in pages) Scan(page.Root, page.Title, page.Title, items);
        foreach (var command in commands)
        {
            var key = "command:" + command.Name; _commands.Add(key, command);
            var parameters = CommandParameterEdit.GetParameterType(command.Name)?.GetProperties()
                .Where(p => p.CanRead && p.CanWrite && !Attribute.IsDefined(p, typeof(JsonIgnoreAttribute)))
                .Select(p => CommandParameterEdit.Label(p.Name) + " " + p.Name) ?? [];
            items.Add(new(key, "命令 / 键鼠", "命令", command.Label + " " + command.Name
                + " 快捷键 鼠标 手势 触控 参数 " + string.Join(" ", parameters)));
        }
        Index = new(items);
    }
    /// <summary>只遍历布局容器，不进入下拉项或虚拟命令行；section类标记独立于实际字号。</summary>
    private void Scan(Control root, string page, string section, List<SettingsSearchItem> items)
    {
        if (root is ScrollViewer { Content: Control content }) { Scan(content, page, section, items); return; }
        if (root is not Panel panel) return;
        string? label = null;
        for (var i = 0; i < panel.Children.Count; i++)
        {
            var child = panel.Children[i];
            if (child is TextBlock text)
            {
                if (text.Classes.Contains("settings-section")) { section = text.Text ?? page; label = null; }
                else label = text.Text;
                continue;
            }
            if (child is Panel or ScrollViewer) { Scan(child, page, section, items); continue; }
            if (child.Name is not { } key || child is not (CheckBox or ComboBox or NumericUpDown or TextBox or Button)) continue;
            var caption = Avalonia.Automation.AutomationProperties.GetName(child)
                ?? (child as ContentControl)?.Content as string ?? label ?? key;
            var tip = ToolTip.GetTip(child)?.ToString() ?? "";
            _fields.Add(key, new(child, panel, i, caption));
            items.Add(new(key, page, section, caption + " " + tip));
            // 同一标签可说明相邻的两个数值控件，例如宽/高或灵敏度/时长。
        }
    }
    /// <summary>还原上一批结果后，按原页面/分区分组；编辑始终使用相同草稿及事件。</summary>
    /// <param name="items">Engine返回的完整匹配结果。</param>
    public void Show(IReadOnlyList<SettingsSearchItem> items)
    {
        Restore();
        foreach (var group in items.GroupBy(i => (i.Page, i.Section)))
        {
            _results.Children.Add(new TextBlock { Text = group.Key.Page + (group.Key.Section == group.Key.Page ? "" : " / " + group.Key.Section),
                Classes = { "settings-search-heading" }, FontWeight = Avalonia.Media.FontWeight.SemiBold, Margin = new Thickness(0, 12, 0, 4) });
            foreach (var item in group)
            {
                if (_commands.TryGetValue(item.Target, out var command))
                { _results.Children.Add(new ContentControl { Content = command, ContentTemplate = _commandTemplate }); continue; }
                var field = _fields[item.Target]; var control = field.Control;
                // 在离开旧父级前固定继承的DataContext，避免短暂null回写TwoWay草稿。
                var inherited = !control.IsSet(StyledElement.DataContextProperty);
                if (inherited) control.SetCurrentValue(StyledElement.DataContextProperty, control.DataContext);
                field.Parent.Children.Remove(control); _attached.Add(new(field, inherited));
                var row = new StackPanel { Spacing = 4, Margin = new Thickness(0, 0, 0, 8) };
                if (control is not (CheckBox or Button)) row.Children.Add(new TextBlock { Text = field.Label });
                row.Children.Add(control); _results.Children.Add(row);
            }
        }
    }
    /// <summary>导航、清空或关闭时恢复原父级和顺序，然后恢复原DataContext继承。</summary>
    public void Restore()
    {
        foreach (var attachment in _attached) ((Panel)attachment.Field.Control.Parent!).Children.Remove(attachment.Field.Control);
        _results.Children.Clear();
        foreach (var attachment in _attached.OrderBy(a => a.Field.Position))
        {
            var field = attachment.Field; field.Parent.Children.Insert(Math.Min(field.Position, field.Parent.Children.Count), field.Control);
            if (attachment.InheritedContext) field.Control.ClearValue(StyledElement.DataContextProperty);
        }
        _attached.Clear();
    }
    /// <summary>关闭窗口归还控件并释放搜索器，不触碰配置。</summary>
    public void Dispose() { Restore(); Index.Dispose(); }
}
