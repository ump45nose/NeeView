// Copyright (c) NeeLaboratory. 原源码沿用仓库 MIT 许可。
// 来源：NeeView/BookSetting/BookSettingSelectMode.cs，基线 c5c398d89；平台改造详见 docs/source-migration.json。
namespace NeeView
{
    public enum BookSettingSelectMode
    {

        Default,

        Continue,

        RestoreOrDefault,

        RestoreOrContinue,

        RestoreOrDefaultReset,
    }

    public enum BookSettingPageSelectMode
    {

        Default,

        RestoreOrDefault,

        RestoreOrDefaultReset,
    }

    public static class BookSettingSelectorForPageExtensions
    {
        public static BookSettingPageSelectMode ToPageSelectMode(this BookSettingSelectMode self)
        {
            return self switch
            {
                BookSettingSelectMode.RestoreOrDefaultReset => BookSettingPageSelectMode.RestoreOrDefaultReset,
                BookSettingSelectMode.RestoreOrDefault or BookSettingSelectMode.RestoreOrContinue => BookSettingPageSelectMode.RestoreOrDefault,
                _ => BookSettingPageSelectMode.Default,
            };
        }

        public static BookSettingSelectMode ToNormalSelectMode(this BookSettingPageSelectMode self)
        {
            return self switch
            {
                BookSettingPageSelectMode.RestoreOrDefaultReset => BookSettingSelectMode.RestoreOrDefaultReset,
                BookSettingPageSelectMode.RestoreOrDefault => BookSettingSelectMode.RestoreOrDefault,
                _ => BookSettingSelectMode.Default,
            };
        }
    }

}
