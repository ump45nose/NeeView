// Copyright (c) NeeLaboratory. 迁入原 ScrollResult，遵循仓库 MIT 许可。
namespace NeeView.PageFrames;

/// <summary>原滚动结果：零向量终止；非斜向双轴移动视为换行。</summary>
public sealed class ScrollResult(NScrollType scrollType, Vector vector)
{
    public NScrollType ScrollType { get; } = scrollType;
    public Vector Vector { get; } = vector;
    public bool IsTerminated => Vector.IsZero();
    public bool IsLineBreak => Vector.IsZero() || ScrollType != NScrollType.Diagonal && Vector.X != 0 && Vector.Y != 0;
}
