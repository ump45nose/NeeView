// Copyright (c) NeeLaboratory. 原 MoveMediaPageCommandParameter.cs，MIT 许可。
namespace NeeView;

/// <summary>原媒体位置命令的独立参数，不与相反方向共享。</summary>
public sealed class MoveMediaPositionCommandParameter
{
    private double _delta;
    /// <summary>按原五位小数及非负约束；零值使用媒体全局步进秒数。</summary>
    public double Delta
    {
        get => _delta;
        set => _delta = double.IsFinite(value) ? Math.Max(Math.Round(value, 5), 0) : 0;
    }
}
