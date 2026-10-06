// Copyright (c) NeeLaboratory. 原三个外部应用参数，仅移除WPF属性编辑元数据。
namespace NeeView;
/// <summary>原直接外部打开参数，缺失字段保持原默认值。</summary>
public sealed class OpenExternalAppCommandParameter : IExternalApp
{
    public const string DefaultParameter = "\"{File}\"";
    private string _parameter = DefaultParameter;
    private string? _workingDirectory;
    public string? Command { get; set; }
    public string Parameter { get => _parameter; set => _parameter = string.IsNullOrWhiteSpace(value) ? DefaultParameter : value; }
    public ArchivePolicy ArchivePolicy { get; set; } = ArchivePolicy.SendExtractFile;
    public MultiPagePolicy MultiPagePolicy { get; set; } = MultiPagePolicy.Once;
    public string? WorkingDirectory { get => _workingDirectory; set => _workingDirectory = string.IsNullOrWhiteSpace(value) ? null : value.Trim(); }
}
/// <summary>原一开始应用索引；零由宿主打开选择菜单。</summary>
public sealed class OpenExternalAppAsCommandParameter
{
    private int _index;
    public int Index { get => _index; set => _index = Math.Max(0, value); }
    public MultiPagePolicy MultiPagePolicy { get; set; } = MultiPagePolicy.Once;
}
/// <summary>整书固定单次，应用索引与原配置顺序一致。</summary>
public sealed class OpenBookExternalAppAsCommandParameter
{
    private int _index;
    public int Index { get => _index; set => _index = Math.Max(0, value); }
}
