// Copyright (c) NeeLaboratory. MIT. 迁自 NeeView/NeeView/Media/Imaging/Metadata/ExifPhotometricInterpretation.cs，保留原元数据算法。
namespace NeeView.Media.Imaging.Metadata
{
    public enum ExifPhotometricInterpretation
    {
        WhiteIsZero = 0,
        BlackIsZero = 1,
        RGB = 2,
        PaletteColor = 3,
        TransparencyMask = 4,
        CMYK = 5,
        YCbCr = 6,
        CIELab = 8,
        ICCLab = 9,
        ITULab = 10,
        CFA = 32803,
        PixarLogL = 32844,
        LogLuv = 32845,
        LinearRaw = 34892,
    }

}
