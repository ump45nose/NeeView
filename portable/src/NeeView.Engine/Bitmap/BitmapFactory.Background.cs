namespace NeeView;

public sealed partial class BitmapFactory
{
    /// <summary>原自定义背景只读取指定图片；共享解码槽、主缓存及显示租约预算。</summary>
    /// <param name="path">原自定义文件路径或file URL。</param><param name="archives">唯一来源工厂。</param><param name="token">当前背景需求的取消。</param>
    public async Task<BitmapLease> GetBackgroundAsync(string path, IArchiveFactory archives, CancellationToken token)
    {
        if (Uri.TryCreate(path, UriKind.Absolute, out var uri) && uri.IsFile) path = uri.LocalPath;
        var metadata = await archives.GetFileMetadataAsync(path, token);
        if (metadata is null || metadata.IsDirectory || !ImageFormats.IsImage(path)) throw new IOException("背景不是可读取的图片文件。");
        var key = new Key(path, 0, metadata.Length, metadata.LastWriteTime, int.MaxValue, int.MaxValue, false, "Background", Interlocked.Read(ref _coverRevision));
        return await GetCoreAsync(key, new(int.MaxValue, int.MaxValue), token, true, async cancellation =>
        {
            var cover = new ArchivePageCover();
            try
            {
                var source = await archives.OpenAsync(path, cancellation); cover.Own(source);
                cover.Entry = (await source.GetEntriesAsync(cancellation)).FirstOrDefault(e => e.IsImage() && e.EntryName == source.RequestedEntryName);
                if (cover.Entry is null) throw new IOException("背景图片不存在。");
                return cover;
            }
            catch { await cover.DisposeAsync(); throw; }
        });
    }
}
