// Copyright (c) NeeLaboratory. 原源码沿用仓库 MIT 许可。
// 来源：NeeView/Book/PageSortMode.cs，基线 c5c398d89；平台改造详见 docs/source-migration.json。
using System;
using System.Collections.Generic;
using System.Linq;

namespace NeeView
{
    // ページ整列
    public enum PageSortMode
    {

        FileName,

        FileNameDescending,

        FileType,

        FileTypeDescending,

        TimeStamp,

        TimeStampDescending,

        Size,

        SizeDescending,

        Entry,

        EntryDescending,

        Random,
    }

    public static class PageSortModeExtension
    {
        public static PageSortMode GetToggle(this PageSortMode mode)
        {
            return (PageSortMode)(((int)mode + 1) % Enum.GetNames(typeof(PageSortMode)).Length);
        }

        public static bool IsDescending(this PageSortMode mode)
        {
            return mode switch
            {
                PageSortMode.FileNameDescending or PageSortMode.FileTypeDescending or PageSortMode.TimeStampDescending or PageSortMode.SizeDescending or PageSortMode.EntryDescending => true,
                _ => false,
            };
        }

        public static bool IsFileNameCategory(this PageSortMode mode)
        {
            return mode switch
            {
                PageSortMode.FileName or PageSortMode.FileNameDescending => true,
                _ => false,
            };
        }

        public static bool IsEntryCategory(this PageSortMode mode)
        {
            return mode switch
            {
                PageSortMode.Entry or PageSortMode.EntryDescending => true,
                _ => false,
            };
        }
    }

    /// <summary>原来源排序资格：普通书籍排除登记顺序，播放列表保留完整枚举。</summary>
    public enum PageSortModeClass { None, Normal, WithEntry, Full }
    public static class PageSortModeClassExtension
    {
        /// <summary>原排序集合判定；仅将文案字典改为纯枚举集合，不依赖界面资源。</summary>
        /// <param name="self">来源能力类别。</param><param name="mode">原排序值。</param><returns>该来源是否支持。</returns>
        public static bool Contains(this PageSortModeClass self, PageSortMode mode) => Enum.IsDefined(mode) && self switch
        { PageSortModeClass.Full or PageSortModeClass.WithEntry => true, PageSortModeClass.Normal => !mode.IsEntryCategory(), _ => mode == PageSortMode.FileName };
        /// <summary>原无效模式保持升降方向并回退文件名排序。</summary>
        public static PageSortMode ValidatePageSortMode(this PageSortModeClass self, PageSortMode mode) => self.Contains(mode) ? mode
            : mode.IsDescending() ? PageSortMode.FileNameDescending : PageSortMode.FileName;
        /// <summary>原循环顺序逐个跳过来源不支持的排序；不改变配置枚举顺序。</summary>
        public static PageSortMode GetTogglePageSortMode(this PageSortModeClass self, PageSortMode mode)
        {
            do { mode = mode.GetToggle(); } while (!self.Contains(mode));
            return mode;
        }
    }

}
