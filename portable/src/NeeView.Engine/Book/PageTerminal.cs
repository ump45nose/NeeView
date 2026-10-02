// Copyright (c) NeeLaboratory. 原源码沿用仓库 MIT 许可。
// 来源：NeeView/Book/PageTerminal.cs，基线 c5c398d89；平台改造详见 docs/source-migration.json。
using System;

namespace NeeView
{
    [Flags]
    public enum PageTerminal
    {
        None = 0,
        First = 1 << 0,
        Last = 1 << 1
    }
}
