// Copyright (c) NeeLaboratory. 原源码沿用仓库 MIT 许可。
// 来源：NeeView/Book/PageReadOrder.cs，基线 c5c398d89；平台改造详见 docs/source-migration.json。
using System;

namespace NeeView
{
    // 見開き時のページ並び
    public enum PageReadOrder
    {

        RightToLeft,

        LeftToRight,
    }

    public static class PageReadOrderExtensions
    {
        public static PageReadOrder GetToggle(this PageReadOrder mode)
        {
            return (PageReadOrder)(((int)mode + 1) % Enum.GetNames(typeof(PageReadOrder)).Length);
        }

        public static int ToSign(this PageReadOrder self)
        {
            return self == PageReadOrder.LeftToRight ? 1 : -1;
        }
    }
}
