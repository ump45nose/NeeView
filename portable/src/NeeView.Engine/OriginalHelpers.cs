namespace NeeLaboratory
{
    /// <summary>迁入 Runtime/MathUtility 的纯范围算法，不引用 Windows Runtime 工程。</summary>
    public static class MathUtility
    {
        /// <summary>返回闭区间中的值，保留原比较顺序。</summary>
        public static T Clamp<T>(T val, T min, T max) where T : IComparable<T> => val.CompareTo(min) < 0 ? min : val.CompareTo(max) > 0 ? max : val;
        /// <summary>将索引归一到闭区间；对应原 NormalizeLoopRange。</summary>
        public static int NormalizeLoopRange(int val, int min, int max)
        {
            if (min > max) throw new ArgumentException("need min <= max");
            if (val >= max) return min + (val - min) % (max - min + 1);
            if (val < min) return max - (min - val - 1) % (max - min + 1);
            return val;
        }
        /// <summary>返回索引在闭区间循环中的周期编号。</summary>
        public static int CycleLoopRange(int val, int min, int max)
        {
            if (min > max) throw new ArgumentException("need min <= max");
            if (val >= max) return (val - min) / (max - min + 1);
            if (val < min) return (val - min + 1) / (max - min + 1) - 1;
            return 0;
        }
    }
}
namespace NeeView
{
    /// <summary>原 Direction 枚举顺序算法。</summary>
    internal static class DirectionExtensions
    {
        /// <summary>负方向逆序，正方向保持顺序。</summary>
        public static IEnumerable<T> ToDirection<T>(this IEnumerable<T> items, int direction) => direction < 0 ? items.Reverse() : items;
    }
}
