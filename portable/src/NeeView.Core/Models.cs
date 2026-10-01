using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace NeeView.Core;

/// <summary>原始来源与归档内部定位；临时解压路径不参与身份。</summary>
public sealed record SourceLocator(string Path, string? Entry = null);
public readonly record struct BookId(string Value);
public readonly record struct ContentId(string Value);
public readonly record struct ContentVersion(long Length, long ModifiedTicks);
public readonly record struct PixelSize(int Width, int Height)
{
    public bool IsLandscape => Width > Height;
    public double Aspect => Height > 0 ? (double)Width / Height : 0.75;
}
public sealed record PageDescriptor(ContentId Id, string Name, SourceLocator Locator,
    ContentVersion Version, int EntryIndex, PixelSize? Size = null);
public enum ReaderMode { Paged, Continuous, Masonry }
public enum ReadDirection { RightToLeft, LeftToRight }
public enum SortMode { Entry, EntryDescending, FileName, FileNameDescending, FileType, FileTypeDescending, TimeStamp, TimeStampDescending, Size, SizeDescending, Random }
public enum ScaleMode { Fit, FitWidth, ActualPixels }
public enum RestorePolicy { Default, Continue, RestoreOrDefault, RestoreOrContinue }
public sealed record ReaderOptions
{
    public ReaderMode Mode { get; init; }
    public ReadDirection Direction { get; init; } = ReadDirection.RightToLeft;
    public bool DoublePage { get; init; }
    public bool DivideWide { get; init; }
    public bool SingleFirst { get; init; }
    public bool SingleLast { get; init; }
    public bool WidePage { get; init; } = true;
    public SortMode Sort { get; init; } = SortMode.Entry;
    public ScaleMode Scale { get; init; }
    public double Zoom { get; init; } = 1;
    public int Rotation { get; init; }
    public int RandomSeed { get; init; } = 1;
    public double ColumnWidth { get; init; } = 320;
    public double Gap { get; init; } = 8;
}
public sealed record ReadingAnchor(ContentId Content, int Part = 0, double RelativeY = 0);
public sealed record ReadingState(BookId Book, SourceLocator Locator, ReadingAnchor? Anchor,
    ReaderOptions Options, DateTimeOffset LastAccess, string? LegacyPage = null);
public sealed record Bookmark(string Id, string? ParentId, int Order, string Name, BookId? Book,
    SourceLocator? Locator, ReadingAnchor? Anchor, string? LegacyPage = null);

/// <summary>按字段恢复原版设置选择策略。</summary>
public static class OptionRestore
{
    /// <summary>输入默认、当前、历史和字段策略，返回新的不可变配置。</summary>
    public static ReaderOptions Mix(ReaderOptions defaults, ReaderOptions current, ReaderOptions? history,
        IReadOnlyDictionary<string, RestorePolicy>? policies = null)
    {
        var values = new Dictionary<string, object?>();
        foreach (var property in typeof(ReaderOptions).GetProperties())
        {
            var policy = policies?.GetValueOrDefault(property.Name) ?? RestorePolicy.RestoreOrDefault;
            var source = policy switch
            {
                RestorePolicy.Default => defaults,
                RestorePolicy.Continue => current,
                RestorePolicy.RestoreOrContinue => history ?? current,
                _ => history ?? defaults
            };
            values[property.Name] = property.GetValue(source);
        }
        // 反射只处理固定模型的可写属性，不读取导入数据中的任意类型。
        var result = new ReaderOptions();
        foreach (var property in typeof(ReaderOptions).GetProperties()) property.SetValue(result, values[property.Name]);
        return result;
    }
}

/// <summary>原 NeeView 半页位置语义，保留 MIT 版权；参照 Book/PagePosition.cs。</summary>
public readonly record struct PagePosition(int Value)
{
    public PagePosition(int index, int part) : this(checked(index * 2 + part)) { }
    public int Index => Value >= 0 ? Value / 2 : (int)(((long)Value - 1) / 2);
    public int Part => (int)(Math.Abs((long)Value) % 2);
    public static PagePosition Empty => new(int.MinValue);
    public static PagePosition operator +(PagePosition value, int offset) => new(value.Value + offset);
}
public readonly record struct PageRange
{
    /// <summary>输入起点及有符号半页数量，生成闭区间。</summary>
    public PageRange(PagePosition position, int partSize)
    {
        Min = partSize >= 0 ? position : position + (partSize + 1);
        Max = partSize >= 0 ? position + (partSize - 1) : position;
        if (partSize == 0 || position == PagePosition.Empty) Min = Max = PagePosition.Empty;
    }
    public PagePosition Min { get; }
    public PagePosition Max { get; }
    public int PartSize => Min == PagePosition.Empty ? 0 : Max.Value - Min.Value + 1;
    /// <summary>方向须为 -1/1，返回范围之外的相邻半页。</summary>
    public PagePosition Next(int direction) => direction switch { 1 => Max + 1, -1 => Min + -1, _ => throw new ArgumentOutOfRangeException(nameof(direction)) };
}

/// <summary>数字段自然比较，避免将 10 排在 2 前面。</summary>
public sealed class NaturalNameComparer : IComparer<string>
{
    public static NaturalNameComparer Instance { get; } = new();
    /// <summary>输入两个名字，返回确定且不依赖系统路径大小写规则的排序值。</summary>
    public int Compare(string? x, string? y)
    {
        var left = Regex.Split(x ?? "", "([0-9]+)");
        var right = Regex.Split(y ?? "", "([0-9]+)");
        for (var i = 0; i < Math.Min(left.Length, right.Length); i++)
        {
            var a = left[i]; var b = right[i];
            int compare;
            if (a.Length > 0 && b.Length > 0 && char.IsAsciiDigit(a[0]) && char.IsAsciiDigit(b[0]))
            {
                var na = a.TrimStart('0'); var nb = b.TrimStart('0');
                compare = na.Length.CompareTo(nb.Length);
                if (compare == 0) compare = string.CompareOrdinal(na, nb);
                if (compare == 0) compare = a.Length.CompareTo(b.Length);
            }
            else compare = StringComparer.OrdinalIgnoreCase.Compare(a, b);
            if (compare != 0) return compare;
        }
        var count = left.Length.CompareTo(right.Length);
        return count != 0 ? count : StringComparer.Ordinal.Compare(x, y);
    }
}
public static class PageOrdering
{
    /// <summary>按设置排序；资源身份不随位置变化。</summary>
    public static IReadOnlyList<PageDescriptor> Sort(IEnumerable<PageDescriptor> pages, ReaderOptions options)
    {
        var input = pages.ToArray();
        IEnumerable<PageDescriptor> result = options.Sort switch
        {
            SortMode.FileName => input.OrderBy(p => p.Name, NaturalNameComparer.Instance),
            SortMode.FileNameDescending => input.OrderByDescending(p => p.Name, NaturalNameComparer.Instance),
            SortMode.FileType => input.OrderBy(p => Path.GetExtension(p.Name)).ThenBy(p => p.Name, NaturalNameComparer.Instance),
            SortMode.FileTypeDescending => input.OrderByDescending(p => Path.GetExtension(p.Name)).ThenBy(p => p.Name, NaturalNameComparer.Instance),
            SortMode.TimeStamp => input.OrderBy(p => p.Version.ModifiedTicks).ThenBy(p => p.Name, NaturalNameComparer.Instance),
            SortMode.TimeStampDescending => input.OrderByDescending(p => p.Version.ModifiedTicks).ThenBy(p => p.Name, NaturalNameComparer.Instance),
            SortMode.Size => input.OrderBy(p => p.Version.Length).ThenBy(p => p.Name, NaturalNameComparer.Instance),
            SortMode.SizeDescending => input.OrderByDescending(p => p.Version.Length).ThenBy(p => p.Name, NaturalNameComparer.Instance),
            SortMode.EntryDescending => input.OrderByDescending(p => p.EntryIndex),
            SortMode.Random => input.OrderBy(p => StableRandom(p.Id.Value, options.RandomSeed)),
            _ => input.OrderBy(p => p.EntryIndex)
        };
        return result.ToArray();
    }
    /// <summary>输入身份和种子，生成跨进程稳定的随机排序键。</summary>
    private static uint StableRandom(string value, int seed)
    {
        var hash = (uint)seed ^ 2166136261;
        foreach (var character in value) hash = (hash ^ character) * 16777619;
        return hash;
    }
}
