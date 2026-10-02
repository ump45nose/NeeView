// Copyright (c) NeeLaboratory. 原源码沿用仓库 MIT 许可。
// 来源：NeeView/Book/IBookPageViewSetting.cs，基线 c5c398d89；平台改造详见 docs/source-migration.json。
namespace NeeView
{
    public interface IBookPageViewSetting
    {
        PageReadOrder BookReadOrder { get; set; }
        bool IsSupportedDividePage { get; set; }
        bool IsSupportedSingleFirstPage { get; set; }
        bool IsSupportedSingleLastPage { get; set; }
        bool IsSupportedWidePage { get; set; }
        PageMode PageMode { get; set; }
        AutoRotateType AutoRotate { get; set; }
        double BaseScale { get; set; }
        int EffectProfileId { get; set; }

        bool IsEquals(IBookPageViewSetting? other)
        {
            return other is not null &&
                   PageMode == other.PageMode &&
                   BookReadOrder == other.BookReadOrder &&
                   IsSupportedDividePage == other.IsSupportedDividePage &&
                   IsSupportedSingleFirstPage == other.IsSupportedSingleFirstPage &&
                   IsSupportedSingleLastPage == other.IsSupportedSingleLastPage &&
                   IsSupportedWidePage == other.IsSupportedWidePage &&
                   AutoRotate == other.AutoRotate &&
                   BaseScale == other.BaseScale &&
                   EffectProfileId == other.EffectProfileId;
        }
    }
}
