namespace NeeView;
public sealed partial class BookOperation
{
    /// <summary>原页面搜索重建可读集合；来源页保留，失败/取消不提交，空结果保存原位置。</summary>
    /// <param name="keyword">原Searcher表达式。</param><param name="expectedBook">拒绝已经切换的面板动作。</param><param name="token">输入或关闭取消。</param>
    /// <returns>是否提交到同一书籍。</returns>
    public async Task<bool> SearchPagesAsync(string keyword, Book? expectedBook, CancellationToken token = default)
    {
        keyword = keyword.Trim(); PageSearchProfile.Analyze(keyword); await _gate.WaitAsync(token);
        try
        {
            if (_disposed || _closing || Book is not { } book || !ReferenceEquals(book, expectedBook) || IsLoading) return false;
            var generation = _generation; var current = book.CurrentPage; RefreshMarkers();
            var mode = book.EffectiveSortMode; var result = await Task.Run(() => BookPageSort.Sort(PageSearchProfile.Search(keyword, book.Pages.SourcePages, token), mode, book.SortSeed, token), token);
            var index = current is not null ? result.Pages.IndexOf(current) : -1;
            await ProbePagesAsync(result.Pages.Skip(Math.Max(0, index - 1)).Take(4), token);
            token.ThrowIfCancellationRequested(); if (_disposed || _closing || generation != _generation || !ReferenceEquals(book, Book)) return false;
            book.Pages.SearchKeyword = keyword; book.ApplySort(result);
            Position = new(current is not null && book.Pages.Contains(current) ? current.Index : 0, Position.Part);
            RebuildFrame(MoveDirection); Notify(); return true;
        }
        finally { _gate.Release(); }
    }
}
