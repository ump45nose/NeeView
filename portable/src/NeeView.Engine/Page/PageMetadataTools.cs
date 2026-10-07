// Copyright (c) NeeLaboratory. MIT. 原PageMetadataTools字段、优先级与格式保留；PictureInfo替换为纯信息。
using NeeView.Media.Imaging.Metadata;
using NeeView.Text;
namespace NeeView;

public static class PageMetadataTools
{
    /// <summary>后台原Searcher同步入口；未带评分的有效图片返回0。</summary>
    public static int GetRating(Page page, CancellationToken token) => Load(page, token)?.Metadata.ElementAt(BitmapMetadataKey.Rating) is ExifRating rating ? rating.ToInteger() : 0;
    public static string GetValueString(Page page, string? name, CancellationToken token) => MetadataValueTools.ToDisplayString(GetValue(page, name, token)) ?? "";
    /// <summary>原标准键先于扩展键；扩展访问名去空格并使用固定小写。</summary>
    public static object? GetValue(Page page, string? name, CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); if (string.IsNullOrEmpty(name)) return null;
        name = name.Replace(" ", "", StringComparison.Ordinal).ToLowerInvariant();
        if (InformationKeyExtensions.TryParse(name, out var key)) return GetDefinedValue(page, key, token);
        return Load(page, token)?.Metadata.LowerExtraMap.TryGetValue(name, out var extra) == true ? extra : null;
    }
    /// <summary>不要求元数据的文件字段无需加载图片；其他字段沿原PictureInfo读取。</summary>
    public static object? GetDefinedValue(Page page, InformationKey key, CancellationToken token) => GetValue(page, key,
        key.ToInformationCategory() == InformationCategory.File ? null : Load(page, token));
    private static PagePictureInfo? Load(Page page, CancellationToken token) => page.LoadPictureInfoAsync(token).GetAwaiter().GetResult();
    /// <summary>已读取信息的纯字段映射；界面在异步加载后调用，不包含文件I/O。</summary>
    public static object? GetValue(Page page, InformationKey key, PagePictureInfo? info)
    {
        var entry = page.ArchiveEntry.TargetArchiveEntry;
        return key.ToInformationCategory() switch
        {
            InformationCategory.File => key switch
            {
                InformationKey.FileName => page.EntryName.Split('/')[^1], InformationKey.FilePath => entry.SystemPath,
                InformationKey.FileSize => entry.Length <= 0 ? null : new FormatValue((entry.Length + 1023) / 1024, "{0:#,0} KB"),
                InformationKey.CreationTime => entry.CreationTime, InformationKey.LastWriteTime => entry.LastWriteTime,
                InformationKey.ArchivePath => entry.Archive.Path, InformationKey.Archiver => entry.Archive.GetType().Name,
                _ => throw new NotSupportedException()
            },
            InformationCategory.Image => info is null ? key == InformationKey.Dimensions && page.Content.HasSize
                ? $"{page.Content.PageDataSource.Size.Width:0} x {page.Content.PageDataSource.Size.Height:0}" : null : key switch
            {
                InformationKey.Dimensions => $"{info.Image.Size.Width:0} x {info.Image.Size.Height:0}",
                InformationKey.BitDepth => new FormatValue(info.Image.BitsPerPixel, "{0}", FormatValue.NotDefaultValueConverter<int>),
                InformationKey.HorizontalResolution => new FormatValue(info.Image.DpiX, "{0:0.# dpi}", FormatValue.NotDefaultValueConverter<double>),
                InformationKey.VerticalResolution => new FormatValue(info.Image.DpiY, "{0:0.# dpi}", FormatValue.NotDefaultValueConverter<double>),
                InformationKey.Decoder => info.Decoder, _ => throw new NotSupportedException()
            },
            InformationCategory.Metadata => info?.Metadata.ElementAt(key.ToBitmapMetadataKey()),
            _ => throw new NotSupportedException()
        };
    }
    /// <summary>标准字段优先，保留所有额外字段；供原脚本与搜索使用。</summary>
    public static Dictionary<string, string> GetValueStringMap(Page page, CancellationToken token)
    {
        var info = Load(page, token);
        var values = InformationKeyExtensions.DefaultKeys.ToDictionary(k => k.ToString(), k => MetadataValueTools.ToDisplayString(GetValue(page, k, info)) ?? "");
        if (info is not null) foreach (var pair in info.Metadata.ExtraMap) values.TryAdd(pair.Key, MetadataValueTools.ToDisplayString(pair.Value) ?? "");
        return values;
    }
}
