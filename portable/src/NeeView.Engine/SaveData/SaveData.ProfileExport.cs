// Copyright (c) NeeLaboratory. 原 Exporter 的 Mac Profile 串流备份适配，基线 c5c398d89。
using System.IO.Compression;

namespace NeeView;

/// <summary>Profile 导出结果，包含提交后的实际目标和 ZIP 条目。</summary>
/// <param name="Path">提交后的实际备份路径。</param><param name="Entries">ZIP 内规范条目名。</param>
public sealed record ProfileExportResult(string Path, IReadOnlyList<string> Entries);

public sealed partial class SaveData
{
    /// <summary>将已由调用方 flush 的 Profile 文件和一级附属材料串流导出为原格式 ZIP。</summary>
    /// <param name="target">目标备份文件；不能是 Profile 权威文件或任一来源文件。</param>
    /// <param name="token">写出期间的取消令牌；取消只删除临时 ZIP。</param>
    /// <returns>提交后的实际路径及条目列表。</returns>
    public async Task<ProfileExportResult> ExportBackupAsync(string target, CancellationToken token = default)
    {
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            return await Task.Run(() => ExportBackupCore(target, token), token).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    private ProfileExportResult ExportBackupCore(string target, CancellationToken token)
    {
        var destination = Path.GetFullPath(target);
        var parent = Path.GetDirectoryName(destination) ?? throw new InvalidDataException("备份目标目录无效。");
        ProfileImportAssets.RejectLink(parent); ProfileImportAssets.RejectLink(destination);
        Directory.CreateDirectory(parent);

        var files = new List<(string Source, string Entry)>();
        AddRoot("UserSetting.json", required: true);
        AddRoot("History.json", false); AddRoot("Bookmark.json", false);
        AddRoot(FolderConfigCollection.FileName, false); AddRoot(QuickAccessCollection.FileName, false);
        AddAssets("Playlists", Config.Current.Playlist.PlaylistFolder, ".nvpls");
        AddAssets("Themes", Config.Current.Theme.CustomThemeFolder, ".json");
        var scriptRoot = _setting["Config"]?["Script"]?["ScriptFolder"]?.GetValue<string>();
        AddAssets("Scripts", string.IsNullOrWhiteSpace(scriptRoot) ? Path.Combine(DirectoryPath, "Scripts") : scriptRoot, ".nvjs");

        if (files.Count > ProfileImportFiles.MaxEntries) throw new InvalidDataException("导出条目数量超限。");
        long total = 0;
        foreach (var file in files)
        {
            var length = new FileInfo(file.Source).Length;
            if (length > ProfileImportFiles.MaxFileBytes || (total += length) > ProfileImportFiles.MaxTotalBytes)
                throw new InvalidDataException("导出文件大小超限。");
            if (file.Source == destination || Path.GetDirectoryName(file.Source) == parent &&
                ProfileImportAssets.CollisionKey(Path.GetFileName(file.Source)) == ProfileImportAssets.CollisionKey(Path.GetFileName(destination)))
                throw new InvalidDataException("备份目标不能覆盖来源文件。");
        }

        var temp = Path.Combine(parent, "." + Path.GetFileName(destination) + "." + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
            {
                using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
                {
                    long copied = 0;
                    var buffer = new byte[64 * 1024];
                    foreach (var file in files)
                    {
                        token.ThrowIfCancellationRequested();
                        ProfileImportAssets.RejectLink(file.Source);
                        var entry = archive.CreateEntry(file.Entry, CompressionLevel.Optimal);
                        using var input = new FileStream(file.Source, FileMode.Open, FileAccess.Read, FileShare.Read);
                        using var output = entry.Open();
                        long fileBytes = 0;
                        int count;
                        while ((count = input.Read(buffer)) != 0)
                        {
                            token.ThrowIfCancellationRequested();
                            if ((fileBytes += count) > ProfileImportFiles.MaxFileBytes || (copied += count) > ProfileImportFiles.MaxTotalBytes)
                                throw new InvalidDataException("导出期间来源增长超过预算。");
                            output.Write(buffer, 0, count);
                        }
                    }
                }
                // ZIP.Dispose写出中心目录后才执行durable flush。
                stream.Flush(true);
            }
            token.ThrowIfCancellationRequested();
            File.Move(temp, destination, true);
            return new ProfileExportResult(destination, files.Select(x => x.Entry).ToArray());
        }
        catch
        {
            try { if (File.Exists(temp)) File.Delete(temp); } catch { }
            throw;
        }

        void AddRoot(string name, bool required)
        {
            var source = ProfileImportAssets.ResolveTarget(DirectoryPath, name);
            if (!Exists(source, directory: false)) { if (required) throw new FileNotFoundException("缺少 UserSetting.json。", source); return; }
            Add(source, name);
        }
        void AddAssets(string folder, string root, string extension)
        {
            var fullRoot = Path.GetFullPath(root);
            ProfileImportAssets.RejectLink(fullRoot);
            if (!Exists(fullRoot, directory: true)) return;
            foreach (var source in Directory.EnumerateFiles(fullRoot, "*", SearchOption.TopDirectoryOnly))
            {
                ProfileImportAssets.RejectLink(source);
                if (!Path.GetExtension(source).Equals(extension, StringComparison.OrdinalIgnoreCase)) continue;
                var name = folder + "/" + Path.GetFileName(source);
                var normalized = ProfileImportAssets.Normalize(name) ?? throw new InvalidDataException("附属文件名无效：" + name);
                Add(source, normalized);
            }
        }
        void Add(string source, string entry)
        {
            ProfileImportAssets.RejectLink(source);
            token.ThrowIfCancellationRequested();
            if (files.Count >= ProfileImportFiles.MaxEntries) throw new InvalidDataException("导出条目数量超限。");
            if (files.Any(x => ProfileImportAssets.CollisionKey(x.Entry) == ProfileImportAssets.CollisionKey(entry)))
                throw new InvalidDataException("导出条目名称冲突：" + entry);
            files.Add((Path.GetFullPath(source), entry));
        }
    }
    /// <summary>仅可靠缺失可以忽略；权限、网络及类型错误不能伪装成未配置材料。</summary>
    private static bool Exists(string path, bool directory)
    {
        try
        {
            var attributes = File.GetAttributes(path);
            if (attributes.HasFlag(FileAttributes.Directory) != directory) throw new InvalidDataException("备份来源类型不匹配：" + path);
            return true;
        }
        catch (FileNotFoundException) { return false; }
        catch (DirectoryNotFoundException) { return false; }
    }
}
