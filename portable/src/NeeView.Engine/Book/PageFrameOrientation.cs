// Copyright (c) NeeLaboratory. MIT；原页帧方向及切换算法。
namespace NeeView;
public enum PageFrameOrientation { Horizontal, Vertical }
public static class PageFrameOrientationExtension
{
    public static PageFrameOrientation GetToggle(this PageFrameOrientation mode) => (PageFrameOrientation)(((int)mode + 1) % Enum.GetNames<PageFrameOrientation>().Length);
}
