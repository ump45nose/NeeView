// Copyright (c) NeeLaboratory. 原源码沿用仓库 MIT 许可。
// 来源：NeeView/Config/AutoRotatePolicy.cs，基线 c5c398d89；平台改造详见 docs/source-migration.json。
namespace NeeView
{
    public enum AutoRotatePolicy
    {
        /// <summary>
        /// 表示領域に合わせる
        /// </summary>
        FitToViewArea,

        /// <summary>
        /// 横長にする
        /// </summary>
        ToLandscape,

        /// <summary>
        /// 縦長にする
        /// </summary>
        ToPortrait,
    }
}
