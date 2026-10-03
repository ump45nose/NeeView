using CommunityToolkit.Mvvm.ComponentModel;
using NeeView;
namespace NeeView.MacOS.ViewModels;

/// <summary>原 Commands 差分键位的编辑副本，取消设置窗口不写入权威数据。</summary>
public sealed class ShortcutEdit(CommandDefinition definition, string value, bool available, string? mouseGesture = null) : ObservableObject
{
    public CommandDefinition Definition { get; } = definition;
    public string Label => Definition.Text + (available ? "" : "（尚未迁移）");
    public string Name => Definition.Name;
    public bool HasParameters => available && CommandParameterEdit.GetParameterType(Name) is not null;
    public string OriginalValue { get; } = value;
    private string _value = value;
    public string Value { get => _value; set => SetProperty(ref _value, value); }
    public string OriginalMouseGesture { get; } = mouseGesture ?? definition.MouseGesture;
    private string _mouseGesture = mouseGesture ?? definition.MouseGesture;
    /// <summary>独立于快捷键逗号语法的方向序列草稿。</summary>
    public string MouseGesture { get => _mouseGesture; set => SetProperty(ref _mouseGesture, value); }
}
