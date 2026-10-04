// Copyright (c) NeeLaboratory. 原 CopyToFolderAsCommandParameter 字段与默认值，基线 c5c398d89。
namespace NeeView;

/// <summary>原固定复制命令的目标与当前页组选取策略；不读取面板移动/复制模式。</summary>
public sealed class CopyToFolderAsCommandParameter
{
    private int _index;
    public int Index { get => _index; set => _index = Math.Max(0, value); }
    public MultiPagePolicy MultiPagePolicy { get; set; } = MultiPagePolicy.Once;
}
