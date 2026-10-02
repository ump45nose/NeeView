// Copyright (c) NeeLaboratory. 原源码沿用仓库 MIT 许可。
// 来源：NeeView/Book/IBookSetting.cs，基线 c5c398d89；平台改造详见 docs/source-migration.json。
namespace NeeView
{
    public interface IBookSetting : IBookPageViewSetting
    {
        public string Page { get; set; }
        public bool IsRecursiveFolder { get; set; }
        public PageSortMode SortMode { get; set; }

        bool IsEquals(IBookSetting? other)
        {
            return other is not null &&
                ((IBookPageViewSetting)this).IsEquals(other) &&
                Page == other.Page &&
                IsRecursiveFolder == other.IsRecursiveFolder &&
                SortMode == other.SortMode;
        }

        bool IsSettingEquals(IBookSetting? other)
        {
            return other is not null &&
                ((IBookPageViewSetting)this).IsEquals(other) &&
                IsRecursiveFolder == other.IsRecursiveFolder &&
                SortMode == other.SortMode;
        }

    }
}
