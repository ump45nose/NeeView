// Copyright (c) NeeLaboratory. MIT；原导出参数/命名算法，仅替换WPF元数据与逻辑路径适配。
using CommunityToolkit.Mvvm.ComponentModel;
using NeeView.StringTemplate;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NeeView;

public enum BitmapImageFormat { Jpeg = 0, Png = 1 }
public static class BitmapImageFormatExtensions
{
    public static string GetExtension(this BitmapImageFormat format) => format switch { BitmapImageFormat.Jpeg => ".jpg", BitmapImageFormat.Png => ".png", _ => throw new ArgumentOutOfRangeException(nameof(format)) };
}
public enum ExportImageMode { Original, View }
public enum ExportBookType { Folder, Zip }
public enum ExportImageOverwriteMode { Confirm, AddNumber, Disallow }

public interface IImageExporterOptions
{
    bool HasBackground { get; }
    bool IsOriginalSize { get; }
    bool IsDotKeep { get; }
    int QualityLevel { get; }
}
public interface IExportImageParameter : IImageExporterOptions
{
    ExportImageMode Mode { get; }
    BitmapImageFormat FileFormat { get; }
    string FileNameFormat0 { get; }
    string FileNameFormat1 { get; }
    string FileNameFormat2 { get; }
    ExportImageOverwriteMode OverwriteMode { get; }
    string ExportFolder { get; }
}

/// <summary>原Config.Book的独立导出草稿；字段名和默认值保持JSON兼容。</summary>
public partial class ExportImageParameter : ObservableObject, IExportImageParameter
{
    private ExportImageMode _mode;
    private bool _hasBackground;
    private bool _isOriginalSize = true;
    private bool _isDotKeep;
    private int _qualityLevel = 80;
    private BitmapImageFormat _fileFormat = BitmapImageFormat.Png;
    private string _fileNameFormat0 = DefaultFileNameFormat0;
    private string _fileNameFormat1 = DefaultFileNameFormat1;
    private string _fileNameFormat2 = DefaultFileNameFormat2;
    private ExportImageOverwriteMode _overwriteMode = ExportImageOverwriteMode.Confirm;
    private string _exportFolder = "";
    public const string DefaultFileNameFormat0 = "{Name}";
    public const string DefaultFileNameFormat1 = "{Name}";
    public const string DefaultFileNameFormat2 = "";
    public ExportImageParameter() { }
    /// <summary>复制当前参数，后续编辑不会修改命令或Config中的原对象。</summary>
    /// <param name="parameter">已保存参数或对话框候选。</param>
    public ExportImageParameter(IExportImageParameter parameter)
    {
        _mode = parameter.Mode;
        _hasBackground = parameter.HasBackground;
        _isOriginalSize = parameter.IsOriginalSize;
        _isDotKeep = parameter.IsDotKeep;
        _exportFolder = parameter.ExportFolder;
        _qualityLevel = parameter.QualityLevel;
        _fileFormat = parameter.FileFormat;
        _fileNameFormat0 = parameter.FileNameFormat0;
        _fileNameFormat1 = parameter.FileNameFormat1;
        _fileNameFormat2 = parameter.FileNameFormat2;
        _overwriteMode = parameter.OverwriteMode;
    }
    public bool HasBackground { get => _hasBackground; set => SetProperty(ref _hasBackground, value); }
    public bool IsOriginalSize { get => _isOriginalSize; set => SetProperty(ref _isOriginalSize, value); }
    public bool IsDotKeep { get => _isDotKeep; set => SetProperty(ref _isDotKeep, value); }
    public int QualityLevel { get => _qualityLevel; set => SetProperty(ref _qualityLevel, value); }
    public ExportImageMode Mode { get => _mode; set => SetProperty(ref _mode, value); }
    public BitmapImageFormat FileFormat { get => _fileFormat; set => SetProperty(ref _fileFormat, value); }
    public string FileNameFormat0 { get => _fileNameFormat0; set => SetProperty(ref _fileNameFormat0, value); }
    public string FileNameFormat1 { get => _fileNameFormat1; set => SetProperty(ref _fileNameFormat1, value); }
    public string FileNameFormat2 { get => _fileNameFormat2; set => SetProperty(ref _fileNameFormat2, value); }
    public ExportImageOverwriteMode OverwriteMode { get => _overwriteMode; set => SetProperty(ref _overwriteMode, value); }
    public string ExportFolder { get => _exportFolder ?? ""; set => SetProperty(ref _exportFolder, value); }
}
/// <summary>原整书导出默认ZIP/加编号与顺序命名，和单页参数分开保存。</summary>
public partial class ExportBookParameter : ExportImageParameter
{
    private ExportBookType _bookType = ExportBookType.Zip;
    public const string DefaultBookFileNameFormat0 = "{Index:000}";
    public const string DefaultBookFileNameFormat1 = "{Index:000}";
    public const string DefaultBookFileNameFormat2 = "";
    public ExportBookParameter() { OverwriteMode = ExportImageOverwriteMode.AddNumber; FileNameFormat0 = DefaultBookFileNameFormat0; FileNameFormat1 = DefaultBookFileNameFormat1; FileNameFormat2 = DefaultBookFileNameFormat2; }
    public ExportBookParameter(IExportImageParameter parameter, ExportBookType bookType) : base(parameter) { _bookType = bookType; }
    public ExportBookType BookType { get => _bookType; set => SetProperty(ref _bookType, value); }
}

public interface IPageNameSource { int Index { get; } string EntryName { get; } string EntryLastName { get; } }
public sealed class PageNameSource : IPageNameSource
{
    public PageNameSource(int index, string entryName) { Index = index; EntryName = entryName; }
    public int Index { get; init; }
    public string EntryName { get; init; }
    public string EntryLastName => EntryName.LastIndexOf('/') is var i and >= 0 ? EntryName[(i + 1)..] : EntryName;
}
public class PageNameElement
{
    public PageNameElement(IPageNameSource page) : this(page, PagePart.All) { }
    public PageNameElement(IPageNameSource page, PagePart pagePart) { Page = page; PagePart = pagePart; }
    public IPageNameSource Page { get; init; }
    public PagePart PagePart { get; init; }
}
public interface IExportPageSource { string BookAddress { get; } int Direction { get; } List<PageNameElement> Elements { get; } }
public record ExportPageSource(string BookAddress, int Direction, List<PageNameElement> Elements) : IExportPageSource;
public class ExportIndexPageSource : IExportPageSource
{
    public ExportIndexPageSource(IExportPageSource source, int index) : this(source.BookAddress, source.Direction, source.Elements, index) { }
    public ExportIndexPageSource(string bookAddress, int direction, IEnumerable<PageNameElement> elements, int index) { BookAddress = bookAddress; Direction = direction; Elements = [.. elements]; Index = index; }
    public string BookAddress { get; }
    public int Direction { get; }
    public List<PageNameElement> Elements { get; }
    public int Index { get; }
}

public static class ExportFileNameFormat
{
    public const string BookKey = "Book", PartKey = "Part", PageKey = "Page", EntryPathKey = "EntryPath", NameKey = "Name", IndexKey = "Index";
    private static readonly Dictionary<string, KeyInfo<ExportIndexPageSource>> Map = new(StringComparer.Ordinal) { [BookKey] = new(GetBook), [PartKey] = new(GetPart), [PageKey] = new(GetPage), [EntryPathKey] = new(GetEntryPath), [NameKey] = new(GetName), [IndexKey] = new(GetIndex) };
    public static ExportPageSource CreateDummyFileNameSource(int pageCount, int direction) => pageCount == 1 ? new("BookName", 1, [new(new PageNameSource(1, "Dir/Foo.jpg"))]) : new("BookName", direction, [new(new PageNameSource(1, "Dir/Foo.jpg")), new(new PageNameSource(2, "Dir/Bar.jpg"))]);
    public static string Format(string format, ExportIndexPageSource source) => Formatter.Format(Formatter.CreateStringFormat(format, Map), source);
    public static string Format(string format, IExportPageSource source, int index) => Format(format, new ExportIndexPageSource(source, index));
    public static string Format(string format, IExportPageSource source, int index, ExportImageMode mode, BitmapImageFormat imageFormat) => Format(format, source, index) + (mode == ExportImageMode.Original ? Extension(source.Elements.First().Page.EntryName) : imageFormat.GetExtension());
    private static string Extension(string s) => Path.GetExtension(s);
    public static PageNameElement GetContent(ExportIndexPageSource s, string x) => x switch { "" or "1" => s.Elements.First(), "2" => s.Elements.Last(), "L" => s.Direction > 0 ? s.Elements.First() : s.Elements.Last(), "R" => s.Direction > 0 ? s.Elements.Last() : s.Elements.First(), _ => throw new NotSupportedException() };
    private static string GetBook(ExportIndexPageSource s, string f, string _) => StringFormatTools.FormatValue(f, WithoutExtension(s.BookAddress.Split('/').Last()));
    private static string GetPage(ExportIndexPageSource s, string f, string x) => StringFormatTools.FormatValue(f, (GetContent(s, x)?.Page.Index ?? -1) + 1);
    private static string GetPart(ExportIndexPageSource s, string f, string x) => StringFormatTools.FormatValue(f, GetContent(s, x).PagePart.ToSuffix());
    private static string GetEntryPath(ExportIndexPageSource s, string f, string x) => StringFormatTools.FormatValue(f, WithoutExtension(GetContent(s, x)?.Page.EntryName ?? ""));
    private static string GetName(ExportIndexPageSource s, string f, string x) => StringFormatTools.FormatValue(f, WithoutExtension(GetContent(s, x)?.Page.EntryLastName ?? ""));
    private static string GetIndex(ExportIndexPageSource s, string f, string _) => StringFormatTools.FormatValue(f, s.Index);
    private static string WithoutExtension(string s) => s[..(s.Length - Path.GetExtension(s).Length)];
}
public interface IExportImageFileNamePolicy { string CreateFileName(IExportPageSource source, int index); }
public sealed class DefaultExportImageFileNamePolicy : IExportImageFileNamePolicy
{
    private readonly IExportImageParameter p;
    private readonly string f0, f1, f2;
    /// <summary>保留原空值沿用与格式错误的两组回退；Mac逻辑路径仅按/拆分。</summary>
    /// <param name="parameter">请求级不再修改的导出参数快照。</param>
    public DefaultExportImageFileNamePolicy(IExportImageParameter parameter)
    {
        p = parameter;
        f0 = string.IsNullOrWhiteSpace(parameter.FileNameFormat0) ? ExportImageParameter.DefaultFileNameFormat0 : parameter.FileNameFormat0;
        f1 = string.IsNullOrWhiteSpace(parameter.FileNameFormat1) ? ExportImageParameter.DefaultFileNameFormat1 : parameter.FileNameFormat1;
        f2 = string.IsNullOrWhiteSpace(parameter.FileNameFormat2) ? f1 : parameter.FileNameFormat2;
        // 原策略在格式错误时回退默认，不让旧配置阻止导出。
        try { ExportFileNameFormat.Format(parameter.FileNameFormat0, ExportFileNameFormat.CreateDummyFileNameSource(1, 1), 1); }
        catch { f0 = ExportImageParameter.DefaultFileNameFormat0; }
        try { ExportFileNameFormat.Format(parameter.FileNameFormat1, ExportFileNameFormat.CreateDummyFileNameSource(2, 1), 0); ExportFileNameFormat.Format(parameter.FileNameFormat2, ExportFileNameFormat.CreateDummyFileNameSource(2, 1), 0); }
        catch { f1 = ExportImageParameter.DefaultFileNameFormat1; f2 = ExportImageParameter.DefaultFileNameFormat2; }
    }
    /// <summary>按原首元素/双页模板及真实格式生成相对文件名，不在命名阶段操作文件。</summary>
    /// <param name="source">原帧顺序、方向、页号与分割区域。</param>
    /// <param name="index">导出成功序号，从1开始。</param><returns>包含来源或输出格式扩展名的相对名字。</returns>
    public string CreateFileName(IExportPageSource source, int index)
    {
        var format = p.Mode == ExportImageMode.Original ? f0 : source.Elements.Count >= 2 ? f2 : f1;
        var name = ExportFileNameFormat.Format(format, source, index);
        return name + (p.Mode == ExportImageMode.Original ? Extension(source.Elements[0].Page.EntryLastName).ToLowerInvariant() : p.FileFormat.GetExtension());
    }
    private static string Extension(string s) => Path.GetExtension(s);
}
public static class ExportImageParameterTools
{
    /// <summary>模板可能含条目路径；创建真实目标前保留子目录但拒绝越界路径。</summary>
    public static string ValidateRelativeFileName(string name) => ExportImageWriter.RelativeName(name);
    /// <summary>直接命令的模板目标保留子目录；检查沿途链接，避免模板写出用户指定根。</summary>
    /// <param name="folder">用户明确配置的导出根目录；根自身可以是用户选择的链接。</param>
    /// <param name="name">原命名策略生成的相对文件名。</param>
    /// <param name="token">后台路径检查的取消。</param>
    /// <returns>已规范化并校验的绝对目标路径。</returns>
    public static async Task<string> PrepareTargetAsync(string folder, string name, CancellationToken token)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
        var path = Path.Combine(root, ValidateRelativeFileName(name));
        await Task.Run(() => ExportImageWriter.CheckChildLinks(root, path), token).ConfigureAwait(false);
        return path;
    }
    public static string GetDefaultFileNameFormat(ExportImageMode mode, int pageCount) => mode == ExportImageMode.Original ? ExportImageParameter.DefaultFileNameFormat0 : pageCount >= 2 ? ExportImageParameter.DefaultFileNameFormat2 : ExportImageParameter.DefaultFileNameFormat1;
}
