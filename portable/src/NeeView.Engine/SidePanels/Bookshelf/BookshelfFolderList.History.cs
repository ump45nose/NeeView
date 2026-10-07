// Copyright (c) NeeLaboratory. 原BookshelfFolderHistory/HistoryLimitedCollection，MIT。
using NeeView.Collections;
namespace NeeView;
public sealed partial class BookshelfFolderList
{
    private readonly HistoryLimitedCollection<string> _placeHistory = new(100);
    private void RecordFolderHistory()
    { _placeHistory.TrimEnd(null); if (Place != _placeHistory.GetCurrent()) _placeHistory.Add(Place); }
    public IReadOnlyList<string> PreviousHistory => _placeHistory.GetHistory(-1, 10).Select(p => p.Value).ToArray();
    public IReadOnlyList<string> NextHistory => _placeHistory.GetHistory(1, 10).Select(p => p.Value).ToArray();
    /// <summary>原目录前进/后退只导航书架，不打开书籍；失败和晚到结果不移动游标。</summary>
    public async Task MoveFolderHistoryAsync(int direction, CancellationToken token = default)
    {
        var target = direction < 0 ? _placeHistory.GetPrevious() : _placeHistory.GetNext(); if (target is null) return;
        var expected = _revision + 1;
        if (await SetPlaceAsync(target, token: token, searchKeyword: "", recordHistory: false) && _revision == expected && Place == target)
            _placeHistory.Move(direction < 0 ? -1 : 1);
    }
}
