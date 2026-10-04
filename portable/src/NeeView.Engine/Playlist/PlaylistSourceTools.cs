// Copyright (c) NeeLaboratory. 原 PlaylistSourceTools/BookHubTools 临时列表及既有格式解析，基线 c5c398d89。
using System.Text.Json;
namespace NeeView;

/// <summary>原列表格式的唯一解析入口；全局面板与来源共用，不创建第二个Hub。</summary>
public static class PlaylistSourceTools
{
    public const string Extension = ".nvpls";
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true, AllowTrailingCommas = true, ReadCommentHandling = JsonCommentHandling.Skip };
    /// <summary>识别原列表来源扩展名，大小写只作用于扩展名。</summary>
    public static bool IsPlaylist(string path) => System.IO.Path.GetExtension(path).Equals(Extension, StringComparison.OrdinalIgnoreCase);
    /// <summary>接受原两种格式；未知格式/新版本明确拒绝，不保存成当前版本破坏源。</summary>
    public static PlaylistSource Deserialize(byte[] bytes)
    {
        using var document = JsonDocument.Parse(bytes, new() { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
        var root = document.RootElement;
        var format = root.GetProperty("Format").GetString();
        if (format == "NeeViewPlaylist.1")
        {
            var source = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(bytes, Options)!;
            var result = new PlaylistSource { Items = root.GetProperty("Items").EnumerateArray().Select(item => new PlaylistSourceItem { Path = item.GetString() ?? throw new JsonException("播放列表路径为空。") }).ToList() };
            source.Remove("Format"); source.Remove("Items"); result.ExtensionData = source; return result;
        }
        const string prefix = "NeeView.Playlist/";
        if (format == "NeeView.Playlist/45.0.3981") format = "NeeView.Playlist/2.0.0"; // 原 45 alpha.4 错版本修正规则。
        if (format is null || !format.StartsWith(prefix, StringComparison.Ordinal) || !Version.TryParse(format[prefix.Length..], out var version) || version > new Version(2, 0, 1))
            throw new NotSupportedException("不支持的播放列表格式：" + format);
        var playlist = JsonSerializer.Deserialize<PlaylistSource>(bytes, Options) ?? throw new JsonException("播放列表为空。");
        if (playlist.Items is null || playlist.Items.Any(item => item is null || string.IsNullOrEmpty(item.Path))) throw new JsonException("播放列表条目必须包含 Path。");
        return playlist;
    }

    /// <summary>原new PlaylistSource(files)语义：保留顺序、重复项与目录，不预展开或登记全局列表。</summary>
    /// <param name="paths">已校验的绝对定位。</param><returns>原v2 JSON，只保存默认Path字段。</returns>
    public static byte[] CreateTemporarySource(IEnumerable<string> paths)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject(); writer.WriteString("Format", PlaylistSource.CurrentFormat); writer.WriteStartArray("Items");
            foreach (var path in FileClipboardCodec.ValidatePaths(paths))
            { writer.WriteStartObject(); writer.WriteString("Path", path); writer.WriteEndObject(); }
            writer.WriteEndArray(); writer.WriteEndObject();
        }
        return stream.ToArray();
    }
}

/// <summary>替换原Temporary/PlaylistSourceTools落盘能力；成功生成文件由进程持有，关闭窗口不删除。</summary>
public interface ITemporaryPlaylistService
{
    /// <summary>在应用进程临时目录建立独立原格式列表，取消不返回部分文件。</summary>
    /// <param name="paths">不去重/不过滤/不预展开的接收顺序。</param><param name="token">准备取消。</param>
    /// <returns>进入原加载链的.nvpls地址，所有权由进程服务持有直至退出。</returns>
    Task<string> CreateAsync(IReadOnlyList<string> paths, CancellationToken token);
}
