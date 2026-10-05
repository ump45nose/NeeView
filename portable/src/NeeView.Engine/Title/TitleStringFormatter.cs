// Copyright (c) NeeLaboratory. MIT；迁入原TitleStringFormatter的内容选择及已迁标题字段。
using NeeView.PageFrames;
using NeeView.StringTemplate;
namespace NeeView;

/// <summary>原标题字段以唯一Book/PageFrame为源，不持有控件或另一套阅读位置。</summary>
public static class TitleStringFormatter
{
    private sealed record TitleSource(Book Book, PageFrame? Frame, IReadOnlyList<PageFrameElement> Contents);
    private static readonly Dictionary<string, KeyInfo<TitleSource>> Words = new(StringComparer.Ordinal)
    {
        ["Book"] = new((s, f, _) => StringFormatTools.FormatValue(f, LoosePath.GetFileName(s.Book.Path))),
        ["PageMax"] = new((s, f, _) => StringFormatTools.FormatValue(f, s.Book.Pages.Count)),
        ["Page"] = new((s, f, suffix) => GetContent(s, suffix) is { } e ? StringFormatTools.FormatValue(f, e.Page.Index + 1) : ""),
        ["Part"] = new((s, f, suffix) => GetContent(s, suffix) is { } e ? StringFormatTools.FormatValue(f, e.PagePart.ToSuffix()) : ""),
        ["FullPath"] = new((s, f, suffix) => GetContent(s, suffix) is { } e ? StringFormatTools.FormatValue(f, e.Page.EntryFullName) : ""),
        ["EntryPath"] = new((s, f, suffix) => GetContent(s, suffix) is { } e ? StringFormatTools.FormatValue(f, e.Page.EntryName) : ""),
        ["Name"] = new((s, f, suffix) => GetContent(s, suffix) is { } e ? StringFormatTools.FormatValue(f, e.Page.EntryName.Split('/')[^1]) : ""),
        ["Size"] = new((s, f, suffix) => GetContent(s, suffix) is { Page.Content.HasSize: true } e
            ? StringFormatTools.FormatValue(f, (int)e.Page.Content.PageDataSource.Size.Width) + " x " + StringFormatTools.FormatValue(f, (int)e.Page.Content.PageDataSource.Size.Height) : ""),
    };
    // 每窗口持有解析结果，普通翻页只替换源，不重复解析同一格式。
    public sealed class WindowFormatter
    {
        private string? _format;
        private StringFormat<TitleSource>? _parsed;
        /// <summary>原WindowTitle按非dummy内容数量选单双页格式；半页来源于PageFrameElement.PagePart。</summary>
        /// <param name="book">当前原书籍。</param><param name="frame">原当前页框。</param>
        /// <param name="isFrameReading">瀑布/连续模式只为当前主页显示标题。</param><returns>无书籍显示应用名，损坏格式回退原默认且不修改配置。</returns>
        public string Format(Book? book, PageFrame? frame, bool isFrameReading = true)
        {
            if (book is null) return "NeeView";
            var contents = frame?.Elements.Where(e => !e.IsDummy && (isFrameReading || e.Page == book.CurrentPage)).ToArray() ?? [];
            var config = Config.Current.WindowTitle;
            var format = contents.Length >= 2 ? config.WindowTitleFormat2 : config.WindowTitleFormat1;
            try
            {
                if (_format != format || _parsed is null) { _parsed = Formatter.CreateStringFormat(format, Words); _format = format; }
                return Formatter.Format(_parsed, new(book, frame, contents));
            }
            catch (FormatException)
            {
                var fallback = contents.Length >= 2 ? WindowTitleConfig.DefaultDouble : WindowTitleConfig.DefaultSingle;
                return Formatter.Format(Formatter.CreateStringFormat(fallback, Words), new(book, frame, contents));
            }
        }
    }
    /// <summary>原1/2/L/R后缀按页框方向选择内容，不能用Position.Part直接猜左右。</summary>
    private static PageFrameElement? GetContent(TitleSource source, string suffix)
    {
        if (source.Contents.Count == 0) return null;
        return suffix switch
        {
            "" or "1" => source.Contents.First(), "2" => source.Contents.Last(),
            "L" => source.Frame?.Direction > 0 ? source.Contents.First() : source.Contents.Last(),
            "R" => source.Frame?.Direction > 0 ? source.Contents.Last() : source.Contents.First(),
            _ => throw new NotSupportedException($"Invalid suffix: {suffix}"),
        };
    }
}
