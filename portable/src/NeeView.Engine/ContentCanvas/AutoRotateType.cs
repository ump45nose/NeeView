// Copyright (c) NeeLaboratory. 原源码沿用仓库 MIT 许可。
// 来源：NeeView/ContentCanvas/AutoRotateType.cs，基线 c5c398d89；平台改造详见 docs/source-migration.json。
namespace NeeView
{
    // 自動回転タイプ
    public enum AutoRotateType
    {

        None,

        Left,

        Right,

        ForcedLeft,

        ForcedRight,
    }

    public static class AutoRotateTypeExtensions
    {
        public static double ToAngle(this AutoRotateType self)
        {
            return self switch
            {
                AutoRotateType.Left => -90.0,
                AutoRotateType.Right => 90.0,
                AutoRotateType.ForcedLeft => -90.0,
                AutoRotateType.ForcedRight => 90.0,
                _ => 0.0,
            };
        }

        public static bool IsForced(this AutoRotateType self)
        {
            return self switch
            {
                AutoRotateType.ForcedLeft => true,
                AutoRotateType.ForcedRight => true,
                _ => false
            };
        }
    }

}
