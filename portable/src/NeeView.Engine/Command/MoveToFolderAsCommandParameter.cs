// Copyright (c) NeeLaboratory. 原 MoveToFolderAsCommandParameter 默认索引/单图策略，基线 c5c398d89。
namespace NeeView;
public enum MultiPagePolicy { Once, All, AllLeftToRight }
public sealed class MoveToFolderAsCommandParameter
{
    private int _index;
    public int Index { get => _index; set => _index = Math.Max(0, value); }
    public MultiPagePolicy MultiPagePolicy { get; set; } = MultiPagePolicy.Once;
}
