using NeeView.Text;
namespace NeeView;
/// <summary>Mac 使用原自然比较算法；Windows 原生字符比较在迁入处替换。</summary>
public static class NaturalSort
{
    public static IComparer<string> Comparer { get; } = new NaturalComparer();
    /// <summary>比较名称中的数字、全半角及日文字符；不用于判断路径身份。</summary>
    public static int Compare(string? x, string? y) => Comparer.Compare(x, y);
}
