namespace NeeView;
public sealed partial class BookOperation
{
    /// <summary>手动/启动清理共用原历史集合事务，不加载书籍或影响正文。</summary>
    public Task<int> RemoveUnlinkedHistoryAsync(CancellationToken token = default) => saveData.RemoveUnlinkedHistoryAsync(archives.ExistsAsync, token);
    /// <summary>原ClearHistoryInPlace只删除当前书架真实目标集合；包内/书签别名按原路径精确比较。</summary>
    public Task<int> ClearHistoryInPlaceAsync(CancellationToken token = default) => saveData.RemoveHistoryAsync(
        Bookshelf.Items.Where(item => item.Bookmark?.IsFolder != true && !item.Path.StartsWith("bookmark:", StringComparison.Ordinal))
            .Select(item => item.Bookmark?.Path ?? item.Path), token);
}
