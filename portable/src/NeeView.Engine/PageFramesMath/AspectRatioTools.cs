// Copyright (c) NeeLaboratory. 原源码沿用仓库 MIT 许可。
// 来源：NeeView/PageFramesMath/AspectRatioTools.cs，基线 c5c398d89；平台改造详见 docs/source-migration.json。

namespace NeeView.Maths
{
    public static class AspectRatioTools
    {
        public static bool IsLandscape(Size size)
        {
            return IsLandscape(size.Width, size.Height, Config.Current.Book.WideRatio);
        }

        public static bool IsLandscape(Size size, double wideRatio)
        {
            return IsLandscape(size.Width, size.Height, wideRatio);
        }

        public static bool IsLandscape(double width, double height)
        {
            return IsLandscape(width, height, Config.Current.Book.WideRatio);
        }

        public static bool IsLandscape(double width, double height, double wideRatio)
        {
            return width > height * wideRatio;
        }


        public static bool IsPortrait(Size size)
        {
            return !IsLandscape(size.Width, size.Height, Config.Current.Book.WideRatio);
        }

        public static bool IsPortrait(double width, double height)
        {
            return !IsLandscape(width, height, Config.Current.Book.WideRatio);
        }

        public static bool IsPortrait(double width, double height, double wideRatio)
        {
            return !IsLandscape(width, height, wideRatio);
        }

    }
}
