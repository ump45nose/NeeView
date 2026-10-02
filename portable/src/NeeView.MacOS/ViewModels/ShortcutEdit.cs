using CommunityToolkit.Mvvm.ComponentModel;
using NeeView;
namespace NeeView.MacOS.ViewModels;

/// <summary>原 Commands 差分键位的编辑副本，取消设置窗口不写入权威数据。</summary>
public sealed class ShortcutEdit(CommandDefinition definition, string value, bool available) : ObservableObject
{
    public CommandDefinition Definition { get; } = definition;
    public string Label => Definition.Text + (available ? "" : "（尚未迁移）");
    public string Name => Definition.Name;
    public string OriginalValue { get; } = value;
    private string _value = value;
    public string Value { get => _value; set => SetProperty(ref _value, value); }
}
