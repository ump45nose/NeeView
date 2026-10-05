// Copyright (c) NeeLaboratory. MIT；原WindowTitleConfig默认格式/字段，移除WPF注解及生成器。
namespace NeeView;

/// <summary>原标题配置；未迁媒体格式和未知字段仍由原JSON合并保存。</summary>
public sealed class WindowTitleConfig
{
    public const string DefaultSingle = "{Book} ({Page}{Part: (#)} / {PageMax}) - {EntryPath:/ > }";
    public const string DefaultDouble = "{Book} ({Page}{Part: (#)} / {PageMax}) - {EntryPathL:/ > } | {NameR}";
    private string? _single, _double, _media;
    public string WindowTitleFormat1 { get => _single ?? DefaultSingle; set => _single = string.IsNullOrEmpty(value) ? null : value; }
    public string WindowTitleFormat2 { get => _double ?? DefaultDouble; set => _double = string.IsNullOrEmpty(value) ? null : value; }
    public string WindowTitleFormatMedia { get => _media ?? "{Book}"; set => _media = string.IsNullOrEmpty(value) ? null : value; }
}
