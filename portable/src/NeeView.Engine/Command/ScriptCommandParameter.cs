// Copyright (c) NeeLaboratory. 原ScriptCommandParameter，MIT。
using CommunityToolkit.Mvvm.ComponentModel;
namespace NeeView;
public sealed class ScriptCommandParameter : ObservableObject
{
    private string? _argument;
    private bool _isChecked;
    public string? Argument { get => _argument; set => SetProperty(ref _argument, string.IsNullOrWhiteSpace(value) ? null : value.Trim()); }
    public bool IsChecked { get => _isChecked; set => SetProperty(ref _isChecked, value); }
}
