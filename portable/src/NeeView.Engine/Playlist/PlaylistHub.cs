// Copyright (c) NeeLaboratory. 原 PlaylistHub/PlaylistSourceTools 的单进程 Mac 替换子集。
using System.Security.Cryptography;
using System.Text.Json;
namespace NeeView;

/// <summary>唯一全局播放列表选择、串行编辑和 .nvpls 保存；没有窗口或具体后端依赖。</summary>
public sealed class PlaylistHub(PlaylistConfig config)
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true, AllowTrailingCommas = true, ReadCommentHandling = JsonCommentHandling.Skip };
    private readonly SemaphoreSlim _gate = new(1);
    private string? _fingerprint;
    public PlaylistConfig Config { get; } = config;
    public Playlist? Current { get; private set; }
    public IReadOnlyList<string> PlaylistFiles { get; private set; } = [];
    public string? Error { get; private set; }
    public PlaylistItem? SelectedItem { get; set; }
    public event EventHandler? Changed;

    /// <summary>惰性初始化；失败保持文件只读和可重试，不以空列表覆盖损坏源。</summary>
    public async Task InitializeAsync(CancellationToken token = default)
    {
        if (Current is not null) return;
        await _gate.WaitAsync(token);
        try { if (Current is null) await LoadCoreAsync(Config.CurrentPlaylist, token); }
        catch (Exception ex) when (ex is not OperationCanceledException) { Error = ex.Message; }
        finally { _gate.Release(); Changed?.Invoke(this, EventArgs.Empty); }
    }
    /// <summary>切换先完整加载并校验；失败保留当前集合、选择配置与删除恢复批次。</summary>
    public async Task SwitchAsync(string path, CancellationToken token = default)
    {
        await _gate.WaitAsync(token);
        try { await LoadCoreAsync(System.IO.Path.GetFullPath(path), token); }
        catch (Exception ex) { Error = ex.Message; throw; }
        finally { _gate.Release(); Changed?.Invoke(this, EventArgs.Empty); }
    }
    /// <summary>读取原 v1/v2 源与未知字段；旧版本只在真实编辑后升级保存。</summary>
    private async Task LoadCoreAsync(string path, CancellationToken token)
    {
        var loaded = await Task.Run(() =>
        {
            token.ThrowIfCancellationRequested();
            byte[]? bytes = File.Exists(path) ? File.ReadAllBytes(path) : null;
            if (bytes is null && path != Config.DefaultPlaylist && path != Config.PagemarkPlaylist) throw new FileNotFoundException("播放列表不存在。", path);
            var source = bytes is null ? new PlaylistSource() : Deserialize(bytes);
            var playlist = new Playlist(path, source);
            foreach (var item in playlist.Items) item.Place = GetPlace(item.Path);
            var files = GetFiles(path); token.ThrowIfCancellationRequested();
            return (playlist, files, fingerprint: Fingerprint(bytes));
        }, token);
        Current = loaded.playlist; PlaylistFiles = loaded.files; _fingerprint = loaded.fingerprint;
        SelectedItem = Current.Items.FirstOrDefault();
        Config.CurrentPlaylist = path; Error = null;
    }
    /// <summary>保留原 Default 首项、实际文件自然排序和外部选择追加，不凭空增加 Pagemark 文件。</summary>
    private IReadOnlyList<string> GetFiles(string selected)
    {
        var files = Directory.Exists(Config.PlaylistFolder) ? Directory.EnumerateFiles(Config.PlaylistFolder, "*.nvpls").Where(path => path != Config.DefaultPlaylist).OrderBy(path => path, NaturalSort.Comparer).ToList() : [];
        files.Insert(0, Config.DefaultPlaylist);
        if (!files.Contains(selected)) files.Add(selected); return files;
    }
    /// <summary>沿原 GetExistEntryName 找到目录或归档实体，归档内部目录不充当真实文件夹。</summary>
    private static string GetPlace(string path)
    {
        if (File.Exists(path) || Directory.Exists(path)) return System.IO.Path.GetDirectoryName(path) ?? "";
        var parent = System.IO.Path.GetDirectoryName(path);
        while (!string.IsNullOrEmpty(parent))
        { if (File.Exists(parent) || Directory.Exists(parent)) return parent; parent = System.IO.Path.GetDirectoryName(parent); }
        return "";
    }
    /// <summary>接受原两种格式；未知格式/新版本明确拒绝，不保存成当前版本破坏源。</summary>
    private static PlaylistSource Deserialize(byte[] bytes)
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
    /// <summary>注册按原 IsFirstIn 决定首尾，重复条目返回原引用。</summary>
    public async Task<PlaylistItem?> AddAsync(string path, CancellationToken token = default)
    {
        PlaylistItem? result = null;
        await EditAsync(list => { result = list.Insert(Config.IsFirstIn ? 0 : list.Items.Count, new(new() { Path = path })); }, token);
        return result;
    }
    /// <summary>标记开/关操作在同一锁内查找和保存，防止并发切换的检查/修改竞争。</summary>
    public Task SetMarkAsync(string path, bool? enabled, CancellationToken token = default) => EditAsync(list =>
    {
        var item = list.Items.FirstOrDefault(item => item.Path == path);
        bool add = enabled ?? item is null;
        if (add && item is null) list.Insert(Config.IsFirstIn ? 0 : list.Items.Count, new(new() { Path = path }));
        else if (!add && item is not null) list.Remove([item]);
    }, token);
    /// <summary>移除当前列表中实际存在的选中批次；不删除图片和列表文件。</summary>
    public Task RemoveAsync(IEnumerable<PlaylistItem> items, CancellationToken token = default) { var batch = items.ToArray(); return EditAsync(list => list.Remove(batch), token); }
    /// <summary>恢复最后成功移除批次，保存失败仍允许原引用重试。</summary>
    public Task RestoreAsync(CancellationToken token = default) => EditAsync(list => list.Restore(), token);
    /// <summary>更名只改列表别名，空或原文件名按原规则省略 Name。</summary>
    public Task RenameAsync(PlaylistItem item, string name, CancellationToken token = default) => EditAsync(list => { if (list.Items.Contains(item)) item.Name = name; }, token);
    /// <summary>原单项 Move；目标对象失效时不改变其他列表。</summary>
    public Task MoveAsync(PlaylistItem item, PlaylistItem? target, CancellationToken token = default) => EditAsync(list => list.Move(item, target), token);
    /// <summary>原 Path 自然排序，显式操作改变注册顺序。</summary>
    public Task SortAsync(CancellationToken token = default) => EditAsync(list => list.Sort(), token);

    /// <summary>原当前书过滤使用页面映射；分组将同 Place 聚拢，导航与界面共用此顺序。</summary>
    public IReadOnlyList<PlaylistItem> GetViewItems(Book? book)
    {
        IEnumerable<PlaylistItem> items = Current?.Items ?? [];
        if (Config.IsCurrentBookFilterEnabled && book is not null)
        {
            var pages = book.Pages.Select(page => page.EntryFullName).ToHashSet(StringComparer.Ordinal);
            items = items.Where(item => pages.Contains(item.Path));
        }
        return (Config.IsGroupBy ? items.GroupBy(item => item.Place).SelectMany(group => group) : items).ToArray();
    }
    /// <summary>等待现有编辑落盘；用于退出，不将未完成 I/O 当成已保存。</summary>
    public async Task FlushAsync() { await _gate.WaitAsync(); _gate.Release(); }
    /// <summary>串行变更与原子提交；失败原地恢复条目名称、顺序及删除恢复记录。</summary>
    private async Task EditAsync(Action<Playlist> edit, CancellationToken token)
    {
        await InitializeAsync(token); await _gate.WaitAsync(token);
        try
        {
            var list = Current ?? throw new IOException(Error ?? "播放列表尚未加载。");
            var items = list.Items.ToArray(); var names = items.Select(item => item.Source.NameRaw).ToArray(); var removed = list.Removed; var selected = SelectedItem;
            try
            {
                edit(list);
                // 原已存在登记返回原对象；无变化不升级旧格式、不触碰外部文件。
                if (items.SequenceEqual(list.Items) && items.Select(item => item.Source.NameRaw).SequenceEqual(names)) return;
                if (SelectedItem is not null && !list.Items.Contains(SelectedItem)) SelectedItem = null;
                foreach (var item in list.Items.Where(item => item.Place.Length == 0)) item.Place = await Task.Run(() => GetPlace(item.Path), token);
                var prepared = new PlaylistSource { Items = list.Items.Select(item => item.Source).ToList(), ExtensionData = list.Source.ExtensionData };
                var bytes = JsonSerializer.SerializeToUtf8Bytes(prepared, Options);
                // 读写在一个后台任务中真实完成；取消只发生在提交前，不能失败后晚到写入。
                await Task.Run(() => WriteCore(list.Path, bytes, token), CancellationToken.None);
                list.Source.Items = prepared.Items; list.Source.Format = PlaylistSource.CurrentFormat; Error = null;
            }
            catch
            { list.Replace(items); for (int i = 0; i < items.Length; i++) items[i].Source.NameRaw = names[i]; list.Removed = removed; SelectedItem = selected; throw; }
        }
        catch (Exception ex) { Error = ex.Message; throw; }
        finally { _gate.Release(); Changed?.Invoke(this, EventArgs.Empty); }
    }
    /// <summary>临时文件 Flush 后同目录原子替换；提交前校验外部变化，失败只清理自己的临时文件。</summary>
    private void WriteCore(string path, byte[] bytes, CancellationToken token)
    {
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            token.ThrowIfCancellationRequested(); Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { stream.Write(bytes); stream.Flush(true); }
            var existing = File.Exists(path) ? File.ReadAllBytes(path) : null;
            if (Fingerprint(existing) != _fingerprint) throw new IOException("播放列表已被外部修改，请重新打开后再编辑。");
            token.ThrowIfCancellationRequested(); File.Move(temporary, path, true); _fingerprint = Fingerprint(bytes);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    /// <summary>内容指纹用于外部修改保护，缺失文件与空文件严格区分。</summary>
    private static string? Fingerprint(byte[]? bytes) => bytes is null ? null : Convert.ToHexString(SHA256.HashData(bytes));
    /// <summary>原前后文件循环算法；切换失败保留原配置。</summary>
    public async Task MovePlaylistAsync(int direction)
    {
        await InitializeAsync(); await _gate.WaitAsync();
        try
        {
            var files = PlaylistFiles; if (files.Count == 0) return;
            var index = files.ToList().IndexOf(Current?.Path ?? Config.CurrentPlaylist);
            await LoadCoreAsync(files[(index + files.Count + direction) % files.Count], CancellationToken.None);
        }
        catch (Exception ex) { Error = ex.Message; throw; }
        finally { _gate.Release(); Changed?.Invoke(this, EventArgs.Empty); }
    }
    /// <summary>新建必须是未占用 .nvpls 文件；成功写入后才切换当前源。</summary>
    public async Task CreateAsync(string path, CancellationToken token = default)
    {
        path = System.IO.Path.GetFullPath(path);
        if (System.IO.Path.GetExtension(path) != ".nvpls") throw new ArgumentException("文件必须使用 .nvpls 扩展名。");
        await _gate.WaitAsync(token);
        try
        {
            await Task.Run(() =>
            {
                var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    token.ThrowIfCancellationRequested(); Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
                    using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    { stream.Write(JsonSerializer.SerializeToUtf8Bytes(new PlaylistSource(), Options)); stream.Flush(true); }
                    token.ThrowIfCancellationRequested(); File.Move(temporary, path, false);
                }
                finally { if (File.Exists(temporary)) File.Delete(temporary); }
            }, token);
            // 目标文件已原子提交；后续完成当前源装配，不把已成功创建误报为取消。
            await LoadCoreAsync(path, CancellationToken.None);
        }
        finally { _gate.Release(); Changed?.Invoke(this, EventArgs.Empty); }
    }
    /// <summary>面板新建用例校验原列表名称，前端无需承担业务路径规则。</summary>
    public Task CreateNamedAsync(string name, CancellationToken token = default)
    {
        if (string.IsNullOrWhiteSpace(name) || name.IndexOfAny(['/', '\\', '\0']) >= 0 || name is "." or "..") throw new ArgumentException("请输入有效的列表名称。");
        return CreateAsync(System.IO.Path.Combine(Config.PlaylistFolder, name.EndsWith(".nvpls", StringComparison.Ordinal) ? name : name + ".nvpls"), token);
    }
}
