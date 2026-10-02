// Copyright (c) NeeLaboratory. 原源码沿用仓库 MIT 许可。
// 来源：NeeView/Book/BookMementoExtensions.cs，基线 c5c398d89；平台改造详见 docs/source-migration.json。
namespace NeeView
{
    public static class BookMementoExtensions
    {
        public static BookSettingConfig ToBookSetting(this BookMemento memento)
        {
            var setting = new BookSettingConfig();

            setting.Page = memento.Page ?? "";
            setting.PageMode = memento.PageMode;
            setting.BookReadOrder = memento.BookReadOrder;
            setting.IsSupportedDividePage = memento.IsSupportedDividePage;
            setting.IsSupportedSingleFirstPage = memento.IsSupportedSingleFirstPage;
            setting.IsSupportedSingleLastPage = memento.IsSupportedSingleLastPage;
            setting.IsSupportedWidePage = memento.IsSupportedWidePage;
            setting.IsRecursiveFolder = memento.IsRecursiveFolder;
            setting.SortMode = memento.SortMode;
            setting.AutoRotate = memento.AutoRotate;
            setting.BaseScale = memento.BaseScale;
            setting.EffectProfileId = memento.EffectProfileId;

            return setting;
        }
    }

}
