using Avalonia.Controls;
using Avalonia.Input;
using NeeView;
namespace NeeView.MacOS.Views;

/// <summary>原菜单树的 Avalonia 适配；布局与命令实现解耦，禁用节点仍可见。</summary>
public static class MenuPresenter
{
    /// <summary>由完整树、命令元数据及执行能力创建菜单；宿主回调负责执行。</summary>
    public static void Populate(Menu menu, MenuNode root, CommandTable commands, SaveData state, Func<string, bool> available, Func<string, Task> execute, Func<string, bool?>? isChecked = null)
    {
        menu.Items.Clear();
        foreach (var node in root.Children ?? []) menu.Items.Add(Create(node));
        // 递归转换原节点；能力仅决定禁用状态，绝不删除条目。
        Control Create(MenuNode node)
        {
            if (node.MenuElementType == MenuElementType.Separator) return new Separator();
            var item = new MenuItem { Header = node.Name };
            if (node.CommandName is { } name)
            {
                var definition = commands.Definitions.FirstOrDefault(d => d.Name == name);
                bool enabled = available(name);
                item.Header = node.Name ?? definition?.MenuText ?? definition?.Text ?? name;
                item.Tag = name; item.IsEnabled = enabled;
                if (isChecked?.Invoke(name) is { } check) { item.ToggleType = MenuItemToggleType.CheckBox; item.IsChecked = check; }
                ToolTip.SetTip(item, enabled ? name : "尚未迁移 · " + (definition?.Stage ?? "待确认"));
                var shortcut = name == "LoadAs" ? "Meta+O" : name == "CloseWindow" ? "Meta+W" : name == "CloseApplication" ? "Meta+Q" : state.GetShortcut(name, definition?.Shortcut ?? "").Split(',')[0];
                try { if (shortcut.Length > 0) item.InputGesture = KeyGesture.Parse(shortcut.Replace("Control+", "Ctrl+").Replace("Command+", "Meta+")); }
                catch (ArgumentException) { /* 鼠标手势仍由命令输入层解析，不能当作键盘提示。 */ }
                item.Click += async (_, e) => { e.Handled = true; await execute(name); };
            }
            foreach (var child in node.Children ?? []) item.Items.Add(Create(child));
            return item;
        }
    }
    /// <summary>业务或面板改变后刷新勾选状态，不重建菜单或改写业务设置。</summary>
    public static void RefreshChecks(Menu menu, Func<string, bool?> isChecked)
    {
        foreach (var item in menu.Items.OfType<MenuItem>()) Refresh(item);
        // 子菜单沿原层级更新，原暂未迁入节点仍保持可见。
        void Refresh(MenuItem item)
        {
            if (item.Tag is string name && isChecked(name) is { } check) item.IsChecked = check;
            foreach (var child in item.Items.OfType<MenuItem>()) Refresh(child);
        }
    }
    /// <summary>刷新已迁入命令的运行时可用性，能力占位与边界禁用仍可识别。</summary>
    public static void RefreshAvailability(Menu menu, Func<string, bool> canExecute)
    {
        foreach (var item in menu.Items.OfType<MenuItem>()) Refresh(item);
        // 保持原菜单树，不重建或删除条目。
        void Refresh(MenuItem item)
        {
            if (item.Tag is string name) item.IsEnabled = canExecute(name);
            foreach (var child in item.Items.OfType<MenuItem>()) Refresh(child);
        }
    }
}
