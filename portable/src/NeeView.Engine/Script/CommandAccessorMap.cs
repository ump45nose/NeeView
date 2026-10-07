// Copyright (c) NeeLaboratory. 原CommandAccessorMap/CommandTable旧命令解析，MIT。
using System.Collections;
namespace NeeView;
/// <summary>实时原命令表；替代名共享真实命令，废弃名明确报错。</summary>
public sealed class CommandAccessorMap(ScriptAccessContext context) : IEnumerable<KeyValuePair<string, CommandAccessor>>
{
    // 固定Windows基线CommandTable第451–474行；不是额外执行表。
    private static readonly IReadOnlyDictionary<string, string> Alternatives = new Dictionary<string, string>
    {
        ["AutoScrollOn"] = "ToggleAutoScroll", ["ToggleVisibleThumbnailList"] = "ToggleVisibleFilmStrip", ["ToggleHideThumbnailList"] = "ToggleHideFilmStrip"
    };
    private static readonly IReadOnlyDictionary<string, string?> Obsolete = new Dictionary<string, string?>
    {
        ["ToggleVisibleTitleBar"] = null, ["ToggleVisiblePagemarkList"] = "ToggleVisiblePlaylist", ["TogglePagemark"] = "TogglePlaylistMark",
        ["PrevPagemark"] = "PrevPlaylistItem", ["NextPagemark"] = "NextPlaylistItem", ["PrevPagemarkInBook"] = "PrevPlaylistItemInBook", ["NextPagemarkInBook"] = "NextPlaylistItemInBook"
    };
    public CommandAccessor? this[string key] => context.Read(() =>
    {
        if (context.Commands.Definitions.Any(d => d.Name == key)) return new CommandAccessor(context, key);
        if (Obsolete.TryGetValue(key, out var replacement)) return context.Diagnostics.Throw<CommandAccessor>(new NotSupportedException($"命令{key}已于39废弃。" + (replacement is null ? "" : $"请使用{replacement}。")));
        // 原CommandNameSource保留冒号数字后缀；没有实际clone时仍返回null。
        var separator = key.IndexOf(':'); var name = separator < 0 ? key : key[..separator];
        if (!Alternatives.TryGetValue(name, out var alternative)) return null;
        context.Diagnostics.Throw(new NotSupportedException($"命令{name}已于46改名为{alternative}。"), ScriptErrorLevel.Info);
        var target = alternative + (separator < 0 ? "" : key[separator..]);
        return context.Commands.Definitions.Any(d => d.Name == target) ? new CommandAccessor(context, target) : null;
    });
    public IEnumerator<KeyValuePair<string, CommandAccessor>> GetEnumerator() => context.Read(() => context.Commands.Definitions.Select(d => new KeyValuePair<string, CommandAccessor>(d.Name, new(context, d.Name)))
        .Concat(Alternatives.Where(a => context.Commands.Definitions.Any(d => d.Name == a.Value)).Select(a => new KeyValuePair<string, CommandAccessor>(a.Key, new(context, a.Value)))).ToArray()).AsEnumerable().GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
