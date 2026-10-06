// Copyright (c) NeeLaboratory. Adapted from LoupeDragTransformContext and LoupeTransformContext.
namespace NeeView.PageFrames;

/// <summary>原开启基点和设备像素舍入；不携带窗口或图像资源。</summary>
public static class LoupeTransform
{
    /// <summary>开启时捕获一次基点，改变倍率时不重新定位鼠标锚点。</summary>
    /// <param name="first">相对视口中心的初始指针。</param><param name="scale">原 FixedScale。</param>
    /// <param name="center">是否将鼠标下的内容移到视口中心。</param><returns>缩放前位移。</returns>
    public static Vector GetBasePoint(Vector first, double scale, bool center)
    {
        scale = double.IsFinite(scale) && scale > 0 ? scale : 1;
        return center ? new(-first.X, -first.Y) : new(-first.X + first.X / scale, -first.Y + first.Y / scale);
    }
    /// <summary>原 point=base-(Last-First)*Speed；先按设备像素舍入，再由展示层平移和缩放。</summary>
    /// <param name="basePoint">开启时固定的基点。</param><param name="delta">累计相对鼠标移动。</param>
    /// <param name="speed">原配置速度。</param><param name="deviceScale">当前设备像素/DIP 比例。</param>
    /// <returns>独立于普通平移的 Loupe 位移。</returns>
    public static Vector GetPoint(Vector basePoint, Vector delta, double speed, double deviceScale = 1)
    {
        speed = double.IsFinite(speed) ? speed : 0;
        deviceScale = double.IsFinite(deviceScale) && deviceScale > 0 ? deviceScale : 1;
        var x = basePoint.X - delta.X * speed; var y = basePoint.Y - delta.Y * speed;
        return new(double.IsFinite(x) ? Math.Round(x * deviceScale) / deviceScale : 0,
            double.IsFinite(y) ? Math.Round(y * deviceScale) / deviceScale : 0);
    }
}
