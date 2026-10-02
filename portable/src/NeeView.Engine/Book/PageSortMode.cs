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

}
