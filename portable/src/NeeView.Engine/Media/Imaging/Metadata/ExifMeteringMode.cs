// Copyright (c) NeeLaboratory. MIT. 迁自 NeeView/NeeView/Media/Imaging/Metadata/ExifMeteringMode.cs，保留原元数据算法。
namespace NeeView.Media.Imaging.Metadata
{
    public enum ExifMeteringMode
    {
        Unknown = 0,
        Average = 1,
        CenterWeightedAverage = 2,
        Spot = 3,
        MultiSpot = 4,
        Pattern = 5,
        Partial = 6,
        Other = 255,
    }
}
