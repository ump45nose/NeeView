// Copyright (c) NeeLaboratory. 原 PlaylistConfig 的 Mac 路径/JSON 适配。
using System.Text.Json.Serialization;
namespace NeeView;

/// <summary>原播放列表配置与短路径保存；Mac 精确比较路径，不统一转小写。</summary>
public sealed class PlaylistConfig
{
    [JsonIgnore] internal string DefaultFolder { get; set; } = "";
    public int PanelListItemStyle { get; set; }
    public bool IsGroupBy { get; set; }
    public bool IsCurrentBookFilterEnabled { get; set; }
    public bool IsFirstIn { get; set; }
    [JsonPropertyName("PlaylistFolder")] public string? PlaylistFolderRaw { get; set; }
    [JsonPropertyName("CurrentPlaylist")] public string? CurrentPlaylistRaw { get; set; }
    [JsonIgnore] public string PlaylistFolder => System.IO.Path.GetFullPath(PlaylistFolderRaw ?? DefaultFolder);
    [JsonIgnore] public string DefaultPlaylist => System.IO.Path.Combine(PlaylistFolder, "Default.nvpls");
    [JsonIgnore] public string PagemarkPlaylist => System.IO.Path.Combine(PlaylistFolder, "Pagemark.nvpls");
    [JsonIgnore] public string CurrentPlaylist
    {
        get => CurrentPlaylistRaw is null ? DefaultPlaylist : System.IO.Path.GetFullPath(System.IO.Path.IsPathFullyQualified(CurrentPlaylistRaw) ? CurrentPlaylistRaw : System.IO.Path.Combine(PlaylistFolder, CurrentPlaylistRaw));
        set => CurrentPlaylistRaw = value == DefaultPlaylist ? null : System.IO.Path.GetDirectoryName(value) == PlaylistFolder ? System.IO.Path.GetFileName(value) : value;
    }
}
