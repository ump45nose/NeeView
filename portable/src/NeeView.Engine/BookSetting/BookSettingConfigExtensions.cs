// Copyright (c) NeeLaboratory. 原 BookSettingConfigExtensions.CopyTo/ToBookMemento，MIT 许可。
namespace NeeView;

/// <summary>保留原十二字段复制，不受设置恢复策略或JSON忽略Page字段影响。</summary>
public static class BookSettingConfigExtensions
{
    /// <param name="self">原默认或当前设置。</param><param name="target">保持引用的接收设置。</param>
    public static void CopyTo(this BookSettingConfig self, BookSettingConfig target)
    {
        target.Page = self.Page;
        target.PageMode = self.PageMode;
        target.BookReadOrder = self.BookReadOrder;
        target.IsSupportedDividePage = self.IsSupportedDividePage;
        target.IsSupportedSingleFirstPage = self.IsSupportedSingleFirstPage;
        target.IsSupportedSingleLastPage = self.IsSupportedSingleLastPage;
        target.IsSupportedWidePage = self.IsSupportedWidePage;
        target.IsRecursiveFolder = self.IsRecursiveFolder;
        target.SortMode = self.SortMode;
        target.AutoRotate = self.AutoRotate;
        target.BaseScale = self.BaseScale;
        target.EffectProfileId = self.EffectProfileId;
    }
    /// <summary>原设置转换；来源路径和实际条目由书籍控制补齐。</summary>
    /// <returns>原Path/Page/Props关系下的阅读备忘。</returns>
    public static BookMemento ToBookMemento(this BookSettingConfig self) => new()
    {
        Page = self.Page, PageMode = self.PageMode, BookReadOrder = self.BookReadOrder,
        IsSupportedDividePage = self.IsSupportedDividePage, IsSupportedSingleFirstPage = self.IsSupportedSingleFirstPage,
        IsSupportedSingleLastPage = self.IsSupportedSingleLastPage, IsSupportedWidePage = self.IsSupportedWidePage,
        IsRecursiveFolder = self.IsRecursiveFolder, SortMode = self.SortMode, SortSeed = 0,
        AutoRotate = self.AutoRotate, BaseScale = self.BaseScale, EffectProfileId = self.EffectProfileId
    };
}
