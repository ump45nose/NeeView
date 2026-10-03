// Copyright (c) NeeLaboratory. MIT；原ViewTransformControl/DragTransform/ViewPropertyControl数值算法。
namespace NeeView.PageFrames;
/// <summary>变换计算与命令规则，不拥有界面或图像资源。</summary>
public static class ViewTransformMath
{
    /// <summary>原start*(1+Scale)或start/(1+Scale)，跨默认比例时吸附到1。</summary>
    public static double Scale(double start, int direction, ViewScaleCommandParameter parameter)
    {
        var result = direction > 0 ? start * (1 + parameter.Scale) : start / (1 + parameter.Scale);
        if (parameter.IsSnapDefaultScale && (direction > 0 ? start < .99 && result > .99 : start > 1.01 && result < 1.01)) result = 1;
        return result;
    }
    /// <summary>原最低角度步长、[-180,180)归一和频率舍入顺序。</summary>
    public static double Rotate(double start, double delta, double frequency)
    {
        if (Math.Abs(delta) < frequency) delta = frequency * Math.Sign(delta);
        var angle = NormalizeAngle(start + delta);
        return frequency > 0 ? Math.Floor((angle + frequency * .5) / frequency) * frequency : angle;
    }
    /// <summary>原循环归一范围。</summary>
    public static double NormalizeAngle(double angle) => ((angle + 180) % 360 + 360) % 360 - 180;
    /// <summary>原旋转中心补偿和导航器正向变换共用。</summary>
    public static Vector RotateVector(Vector value, double angle)
    { var rad = angle * Math.PI / 180; return new(value.X * Math.Cos(rad) - value.Y * Math.Sin(rad), value.X * Math.Sin(rad) + value.Y * Math.Cos(rad)); }
    /// <summary>原循环模式枚举步进，禁用全部或非循环越界保持当前模式。</summary>
    public static PageStretchMode ToggleStretch(PageStretchMode current, int direction, ToggleStretchModeCommandParameter parameter)
    {
        var mode = current; var length = Enum.GetValues<PageStretchMode>().Length; var count = 0;
        do
        {
            var next = (int)mode + direction;
            if (!parameter.IsLoop && (next < 0 || next >= length)) return current;
            mode = (PageStretchMode)((next + length) % length);
            if (parameter.GetStretchModeDictionary()[mode]) return mode;
        } while (count++ < length);
        return current;
    }
}
