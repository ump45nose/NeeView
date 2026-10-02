// Copyright (c) NeeLaboratory. 原源码沿用仓库 MIT 许可。
// 来源：NeeView/Book/WidePageStretch.cs，基线 c5c398d89；平台改造详见 docs/source-migration.json。
namespace NeeView
{
    public enum WidePageStretch
    {
        /// <summary>
        /// ストレッチなし
        /// </summary>
        None = 0,

        /// <summary>
        /// 縦幅を揃える
        /// </summary>
        UniformHeight,

        /// <summary>
        /// 横幅を揃える
        /// </summary>
        UniformWidth,
    }
}
