// Copyright (c) NeeLaboratory. 原集合/DestinationFolderAccessor，MIT。
namespace NeeView;
/// <summary>集合增删与唯一配置共享，返回原对象的脚本包装。</summary>
public sealed class DestinationFolderCollectionAccessor(ScriptAccessContext context)
{
    public DestinationFolderAccessor[] Items => context.Read(() => Config.Current.System.DestinationFolderCollection.Select(x => new DestinationFolderAccessor(context, x)).ToArray());
    public DestinationFolderAccessor CreateNew() => context.Write(() => new DestinationFolderAccessor(context, Config.Current.System.DestinationFolderCollection.CreateNew()));
    public void Remove(DestinationFolderAccessor item) => context.Write(() => { Config.Current.System.DestinationFolderCollection.Remove(item.Source); });
}
/// <summary>原显式路径/页面操作；移动仍过滤归档内部条目，复制使用原实体化策略。</summary>
public sealed class DestinationFolderAccessor(ScriptAccessContext context, DestinationFolder source)
{
    internal DestinationFolder Source { get; } = source;
    public string Name { get => context.Read(() => Source.Name); set => context.Write(() => Source.Name = value); }
    public string Path { get => context.Read(() => Source.Path); set => context.Write(() => Source.Path = value); }
    public void CopyPage(PageAccessor page) => CopyPage([page]);
    public void CopyPage(PageAccessor[] pages) => context.Run(() => context.Operation.TransferScriptPagesAsync(pages.Select(p => p.Source).ToArray(), Path, false, context.Token));
    public void Copy(string path) => Copy([path]);
    public void Copy(string[] paths) => context.Run(() => context.Operation.TransferScriptPathsAsync(paths.ToArray(), Path, false, true, context.Token));
    public void MovePage(PageAccessor page) => MovePage([page]);
    public void MovePage(PageAccessor[] pages) => Move(pages.Select(p => p.Source.ArchiveEntry.TargetArchiveEntry).Where(e => e.FilePath is not null).Select(e => e.FilePath!).ToArray());
    public void Move(string path) => Move([path]);
    public void Move(string[] paths) => context.Run(() => context.Operation.TransferScriptPathsAsync(paths.ToArray(), Path, true, true, context.Token));
}
