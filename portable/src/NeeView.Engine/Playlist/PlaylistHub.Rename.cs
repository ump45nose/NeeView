// Copyright (c) NeeLaboratory. 原 PlaylistHub.RenameItemPathRecursive 的可等待文件保存适配。
using System.Text.Json;
namespace NeeView;

public sealed partial class PlaylistHub
{
    private bool _renamedPlaylistDirty;
    /// <summary>递归更改当前和其他已登记.nvpls的Path；别名、顺序和未知字段保留，失败明确回报。</summary>
    /// <param name="source">已改名的原书籍地址。</param><param name="destination">实体操作返回的实际新地址。</param>
    /// <returns>逐文件原子保存完成的任务；任一失败抛出汇总错误以便继续恢复。</returns>
    public async Task RenameItemPathRecursiveAsync(string source, string destination)
    {
        await _gate.WaitAsync();
        var errors = new List<string>();
        try
        {
            if (Current is { } list)
            {
                list.Path = BookMementoTools.RenamePath(list.Path, source, destination);
                foreach (var item in list.Items.Concat(list.Removed.Select(e => e.Item)).Distinct())
                {
                    var next = BookMementoTools.RenamePath(item.Path, source, destination);
                    if (next == item.Path) continue;
                    item.Source.Path = next; item.Place = await Task.Run(() => GetPlace(next)); _renamedPlaylistDirty = true;
                }
                if (_renamedPlaylistDirty)
                {
                    try
                    {
                        var prepared = new PlaylistSource { Items = list.Items.Select(item => item.Source).ToList(), ExtensionData = list.Source.ExtensionData };
                        await Task.Run(() => WriteCore(list.Path, JsonSerializer.SerializeToUtf8Bytes(prepared, Options), CancellationToken.None));
                        list.Source.Items = prepared.Items; list.Source.Format = PlaylistSource.CurrentFormat; _renamedPlaylistDirty = false;
                    }
                    catch (Exception ex) { errors.Add(list.Path + ": " + ex.Message); }
                }
            }
            foreach (var path in await Task.Run(() => GetFiles(Config.CurrentPlaylist)))
            {
                if (path == Current?.Path || !File.Exists(path)) continue;
                try
                {
                    await Task.Run(() =>
                    {
                        var bytes = File.ReadAllBytes(path); var data = Deserialize(bytes); bool changed = false;
                        foreach (var item in data.Items)
                        {
                            var next = BookMementoTools.RenamePath(item.Path, source, destination);
                            if (next != item.Path) { item.Path = next; changed = true; }
                        }
                        if (changed) WriteFileCore(path, JsonSerializer.SerializeToUtf8Bytes(data, Options), Fingerprint(bytes), CancellationToken.None);
                    });
                }
                catch (Exception ex) { errors.Add(path + ": " + ex.Message); }
            }
            Error = errors.Count == 0 ? null : string.Join("\n", errors);
            PlaylistFiles = await Task.Run(() => GetFiles(Config.CurrentPlaylist));
            if (errors.Count > 0) throw new IOException(Error);
        }
        finally { _gate.Release(); Changed?.Invoke(this, EventArgs.Empty); }
    }
}
