using NeeView;
namespace NeeView.Backends;
/// <summary>原MediaArchive单实体来源；只持元数据，请求流按请求释放。</summary>
public sealed class MediaArchiveSource(string path) : MediaArchive(path)
{
    public override Task<IReadOnlyList<ArchiveEntry>> GetEntriesAsync(CancellationToken token)=>SourceIo.RunAsync<IReadOnlyList<ArchiveEntry>>(()=>
    {
        ObjectDisposedException.ThrowIf(IsDisposed,this);token.ThrowIfCancellationRequested();var file=new FileInfo(Path);
        if(!file.Exists)throw new FileNotFoundException("视频不存在。",Path);
        return [new ArchiveEntry(this){Id=0,RawEntryName=file.Name,FilePath=file.FullName,Length=file.Length,LastWriteTime=file.LastWriteTime,CreationTime=file.CreationTime}];
    },token);
    public override Task<Stream> OpenEntryAsync(ArchiveEntry entry,CancellationToken token)=>SourceIo.RunAsync<Stream>(()=>
    {ObjectDisposedException.ThrowIf(IsDisposed,this);token.ThrowIfCancellationRequested();return new FileStream(Path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete);},token);
    public override ValueTask DisposeAsync(){IsDisposed=true;return ValueTask.CompletedTask;}
}
