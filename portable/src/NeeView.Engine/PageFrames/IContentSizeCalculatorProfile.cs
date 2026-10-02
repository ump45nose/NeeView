// Copyright (c) NeeLaboratory. MIT；来源 NeeView/PageFrames/IContentSizeCalculatorProfile.cs，c5c398d89。

namespace NeeView.PageFrames
{
    public interface IContentSizeCalculatorProfile
    {
        public double ContentsSpace { get; }
        public PageStretchMode StretchMode { get; }
        public AutoRotateType AutoRotate { get; }
        public AutoRotatePolicy AutoRotatePolicy { get; }
        public bool AllowFileContentAutoRotate { get; }
        public bool AllowEnlarge { get; }
        public bool AllowReduce { get; }
        public Size ReferenceSize { get; }
        public Size CanvasSize { get; }
        public double DeviceScale { get; }
        public WidePageStretch WidePageStretch { get; }
    }
}
