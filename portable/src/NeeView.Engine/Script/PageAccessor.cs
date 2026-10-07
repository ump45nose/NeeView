namespace NeeView;
public record class PageAccessor
{
    protected ScriptAccessContext Context { get; }
    internal Page Source { get; }
    public PageAccessor(ScriptAccessContext context, Page source) { Context = context; Source = source; }
    public int Index => Context.Read(() => Source.Index);
    public string Path => Context.Read(() => Source.ArchiveEntry.TargetArchiveEntry.SystemPath);
    public string RawPath => Context.Read(() => Source.EntryFullName);
    public long Size => Context.Read(() => Source.ArchiveEntry.Length);
    public DateTime LastWriteTime => Context.Read(() => Source.ArchiveEntry.LastWriteTime);
    public DateTime CreationTime => Context.Read(() => Source.ArchiveEntry.CreationTime);
    public bool IsBook => Context.Read(() => Source.Content.IsFileContent);
    /// <summary>同步JavaScript在后台读取同一Page的有界元数据；取消贯穿原来源读取。</summary>
    public string GetMetaValue(string key) => PageMetadataTools.GetValueString(Source, key, Context.Token);
    public Dictionary<string,string> GetMetaValueMap() => PageMetadataTools.GetValueStringMap(Source, Context.Token);
    public void Open() => Context.Run(async () =>
    { if (Context.Operation.Book?.Pages.Contains(Source) == true) await Context.Operation.JumpAsync(Source.Index); else await Context.Operation.OpenAsync(Source.EntryFullName, Context.Token); });
    public void OpenAsBook() => Context.Run(() => Context.Operation.OpenAsync(Source.EntryFullName, Context.Token));
}
