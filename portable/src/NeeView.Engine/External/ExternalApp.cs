// Copyright (c) NeeLaboratory. 原 ExternalApp 的配置和表现字段；基线 c5c398d89。
using CommunityToolkit.Mvvm.ComponentModel;
using System.Text.Json.Serialization;
namespace NeeView;

/// <summary>原可配置外部应用；不持有窗口或平台对象。</summary>
public sealed class ExternalApp : ObservableObject, IExternalApp, ICloneable, IEquatable<ExternalApp>
{
    private string? _name, _command, _workingDirectory;
    private string _parameter = OpenExternalAppCommandParameter.DefaultParameter;
    private ArchivePolicy _archivePolicy = ArchivePolicy.SendExtractFile;
    public string? Name { get => _name; set { if (SetProperty(ref _name, string.IsNullOrWhiteSpace(value) ? null : value.Trim())) OnPropertyChanged(nameof(DisplayName)); } }
    public string? Command { get => _command; set { if (SetProperty(ref _command, string.IsNullOrWhiteSpace(value) ? null : value.Trim())) OnPropertyChanged(nameof(DisplayName)); } }
    public string Parameter { get => _parameter; set => SetProperty(ref _parameter, string.IsNullOrWhiteSpace(value) ? OpenExternalAppCommandParameter.DefaultParameter : value); }
    public ArchivePolicy ArchivePolicy { get => _archivePolicy; set => SetProperty(ref _archivePolicy, value); }
    public string? WorkingDirectory { get => _workingDirectory; set => SetProperty(ref _workingDirectory, string.IsNullOrWhiteSpace(value) ? null : value.Trim()); }
    [JsonExtensionData] public Dictionary<string, System.Text.Json.JsonElement>? ExtensionData { get; set; }
    [JsonIgnore] public string DisplayName => _name ?? (string.IsNullOrWhiteSpace(_command) ? "系统默认应用" : Path.GetFileNameWithoutExtension(_command));
    /// <summary>独立字段草稿，不复制表现事件订阅。</summary>
    public object Clone() => new ExternalApp { Name = Name, Command = Command, Parameter = Parameter, ArchivePolicy = ArchivePolicy, WorkingDirectory = WorkingDirectory, ExtensionData = ExtensionData?.ToDictionary(pair => pair.Key, pair => pair.Value.Clone()) };
    /// <summary>沿原五字段相等性，默认配置差分不会展开空集合。</summary>
    public bool Equals(ExternalApp? other) => other is not null && Name == other.Name && Command == other.Command && Parameter == other.Parameter && ArchivePolicy == other.ArchivePolicy && WorkingDirectory == other.WorkingDirectory && (ExtensionData?.Count ?? 0) == (other.ExtensionData?.Count ?? 0)
        && (ExtensionData is null || ExtensionData.All(pair => other.ExtensionData is not null && other.ExtensionData.TryGetValue(pair.Key, out var value) && value.GetRawText() == pair.Value.GetRawText()));
    public override bool Equals(object? other) => Equals(other as ExternalApp);
    public override int GetHashCode() => HashCode.Combine(Name, Command, Parameter, ArchivePolicy, WorkingDirectory);
}
