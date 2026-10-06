// Copyright (c) NeeLaboratory. 原Archive.CanDelete/DeleteAsync、PageFileIO分组规则，基线c5c398d89。
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using NeeView;
using SharpCompress.Readers;
namespace NeeView.Backends;

public sealed partial class FolderArchive
{
    public override bool CanDelete(IReadOnlyList<ArchiveEntry> entries) => !IsDisposed && entries.Count > 0
        && entries.All(e => ReferenceEquals(e.Archive, this) && e.FilePath is not null);
    /// <summary>目录也作为一个实体进入废纸篓；不遍历链接目标，逐项回报成功。</summary>
    public override async Task<PageDeleteResult> DeleteAsync(IReadOnlyList<ArchiveEntry> entries, IPlatformService platform, CancellationToken token)
    {
        if (!CanDelete(entries)) throw new NotSupportedException("条目不能由此目录删除。");
        var removed = new List<ArchiveEntry>();
        foreach (var entry in entries.DistinctBy(e => e.FilePath))
        {
            if (removed.Any(e => e.IsDirectory && !e.IsShortcut && entry.FilePath!.StartsWith(e.FilePath!.TrimEnd('/') + "/", StringComparison.Ordinal))) continue;
            try { await platform.TrashAsync(entry.FilePath!, token).ConfigureAwait(false); removed.Add(entry); }
            catch (OperationCanceledException) { return new(removed); }
            catch (Exception ex) { return new(removed, ex.Message); }
        }
        return new(removed);
    }
}

public sealed partial class PlaylistArchive
{
    [JsonSerializable(typeof(PlaylistSource))]
    [JsonSourceGenerationOptions(WriteIndented = true)]
    private partial class PlaylistWriteContext : JsonSerializerContext { }
    public override bool CanDelete(IReadOnlyList<ArchiveEntry> entries) => !IsDisposed && entries.Count > 0
        && entries.All(e => ReferenceEquals(e.Archive, this) && _sourceItems.ContainsKey(e));
    /// <summary>只移除选中的列表登记，保留别名/重复项及未知字段，不删除引用实体。</summary>
    public override async Task<PageDeleteResult> DeleteAsync(IReadOnlyList<ArchiveEntry> entries, IPlatformService platform, CancellationToken token)
    {
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (!CanDelete(entries) || _sourceBytes is null || _source is null) throw new NotSupportedException("列表条目不能删除。");
            var selected = entries.Distinct().ToArray(); var items = selected.Select(e => _sourceItems[e]).ToHashSet();
            var prepared = new PlaylistSource { Items = _source.Items.Where(e => !items.Contains(e)).ToList(), ExtensionData = _source.ExtensionData };
            var bytes = JsonSerializer.SerializeToUtf8Bytes(prepared, PlaylistWriteContext.Default.PlaylistSource);
            await Task.Run(() => ContentWrite.Replace(Path, bytes, _sourceBytes, token), CancellationToken.None).ConfigureAwait(false);
            _source = prepared; _sourceBytes = bytes;
            _entries = _entries!.Where(e => !selected.Contains(e)).ToArray(); foreach (var e in selected) _sourceItems.Remove(e);
            return new(selected);
        }
        finally { _gate.Release(); }
    }
}

public sealed partial class CompressedArchive
{
    private readonly HashSet<int> _deletedIds = [];
    public override bool CanDelete(IReadOnlyList<ArchiveEntry> entries) => !IsDisposed && Source is null && Config.Current.Archive.Zip.IsFileWriteAccessEnabled && _archive.Type == SharpCompress.Common.ArchiveType.Zip
        && !_entries.Any(e => e.IsEncrypted)
        && entries.Count > 0 && entries.All(e => ReferenceEquals(e.Archive, this) && (e.Id >= 0 && e.Id < _entries.Count && !_deletedIds.Contains(e.Id)
            || e.Id < 0 && e.IsDirectory && ContainsDirectory(e.EntryName)));
    /// <summary>ZIP根来源按原条目ID删除并展开目录子项。暂存完整副本后原子替换；RAR/7z保持只读。</summary>
    public override async Task<PageDeleteResult> DeleteAsync(IReadOnlyList<ArchiveEntry> entries, IPlatformService platform, CancellationToken token)
    {
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (!CanDelete(entries)) throw new NotSupportedException("此归档不支持删除条目。");
            var all = await GetEntriesAsync(token).ConfigureAwait(false);
            var removed = all.Where(e => entries.Any(s => s.Id == e.Id || s.IsDirectory && e.EntryName.StartsWith(s.EntryName.TrimEnd('/') + "/", StringComparison.Ordinal))).ToArray();
            var ids = removed.Select(e => e.Id).ToHashSet(); var activeIds = Enumerable.Range(0, _entries.Count).Where(i => !_deletedIds.Contains(i)).ToArray();
            await Task.Run(() =>
            {
                ContentWrite.RejectLinkedPath(Path); token.ThrowIfCancellationRequested();
                var stamp = ContentWrite.Hash(Path); var temporary = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Path)!, ".neeview-" + Guid.NewGuid().ToString("N") + ".tmp");
                try
                {
                    // Update模式会把大量压缩数据载入内存；Create流式重建只保留一个有界复制缓冲。
                    using (var input = System.IO.Compression.ZipFile.OpenRead(Path))
                    using (var output = System.IO.Compression.ZipFile.Open(temporary, System.IO.Compression.ZipArchiveMode.Create))
                    {
                        output.Comment = input.Comment;
                        var physical = input.Entries.ToArray();
                        if (physical.Length != activeIds.Length || physical.Where((e, i) => e.FullName != _entries[activeIds[i]].Key).Any())
                            throw new IOException("归档索引已改变，请重新加载后删除。");
                        byte[] buffer = new byte[81920];
                        for (int i = 0; i < physical.Length; i++)
                        {
                            token.ThrowIfCancellationRequested(); if (ids.Contains(activeIds[i])) continue;
                            var entry = physical[i]; var copy = output.CreateEntry(entry.FullName, System.IO.Compression.CompressionLevel.Fastest);
                            copy.LastWriteTime = entry.LastWriteTime; copy.ExternalAttributes = entry.ExternalAttributes;
                            copy.Comment = entry.Comment;
                            using var source = entry.Open(); using var target = copy.Open(); int read;
                            while ((read = source.Read(buffer)) > 0) { token.ThrowIfCancellationRequested(); target.Write(buffer, 0, read); }
                        }
                    }
                    token.ThrowIfCancellationRequested(); ContentWrite.RejectLinkedPath(Path);
                    if (!stamp.AsSpan().SequenceEqual(ContentWrite.Hash(Path))) throw new IOException("归档在删除准备期间已被外部修改。");
                    _archive.Dispose();
                    try { File.Move(temporary, Path, true); }
                    catch { _archive = SharpCompress.Archives.ArchiveFactory.OpenArchive(Path, new ReaderOptions()); _entries = Remap(_archive); throw; }
                    _deletedIds.UnionWith(ids);
                    _archive = SharpCompress.Archives.ArchiveFactory.OpenArchive(Path, new ReaderOptions()); _entries = Remap(_archive);
                }
                finally { if (File.Exists(temporary)) File.Delete(temporary); }
            }, CancellationToken.None).ConfigureAwait(false);
            return new(removed.Concat(entries.Where(e => e.Id < 0)).ToArray());
        }
        finally { _gate.Release(); }
    }
    /// <summary>删除后保留现有Page的条目ID，不能用压缩包新下标读取另一图片。</summary>
    private List<SharpCompress.Archives.IArchiveEntry> Remap(SharpCompress.Archives.IArchive archive)
    {
        var result = _entries.ToList(); using var fresh = archive.Entries.GetEnumerator();
        for (int i = 0; i < result.Count; i++) if (!_deletedIds.Contains(i)) { if (!fresh.MoveNext()) throw new IOException("归档提交后索引不完整。"); result[i] = fresh.Current; }
        return result;
    }
}

/// <summary>列表/归档局部写入保护，共用.NET文件能力；不是第二套传输服务。</summary>
internal static class ContentWrite
{
    internal static byte[] Hash(string path) { using var input = File.OpenRead(path); return SHA256.HashData(input); }
    internal static void RejectLinkedPath(string path)
    {
        for (FileSystemInfo? info = new FileInfo(path); info is not null; info = new DirectoryInfo(System.IO.Path.GetDirectoryName(info.FullName)!))
        {
            if (info.LinkTarget is not null && !(info.FullName == "/var" && info.ResolveLinkTarget(true)?.FullName == "/private/var")) throw new NotSupportedException("不经链接路径修改来源。");
            if (System.IO.Path.GetDirectoryName(info.FullName) is null) break;
        }
    }
    /// <summary>完整准备、指纹复核后同目录原子替换；提交后不抛晚取消。</summary>
    internal static void Replace(string path, byte[] bytes, byte[] expected, CancellationToken token)
    {
        RejectLinkedPath(path); token.ThrowIfCancellationRequested();
        if (!Hash(path).AsSpan().SequenceEqual(SHA256.HashData(expected))) throw new IOException("列表已被外部修改，请重新加载。");
        var temporary = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(path)!, ".neeview-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { output.Write(bytes); output.Flush(true); }
            token.ThrowIfCancellationRequested(); RejectLinkedPath(path);
            if (!Hash(path).AsSpan().SequenceEqual(SHA256.HashData(expected))) throw new IOException("列表已被外部修改，请重新加载。");
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
