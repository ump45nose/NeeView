// Copyright (c) NeeLaboratory. 原 PageHistory/BookHubHistory 的无控件适配；沿用原 100 项历史及路径/条目语义。
using NeeView.Collections;
namespace NeeView;

/// <summary>原页面历史以书籍路径和条目名比较，分割页不另建持久化身份。</summary>
public readonly record struct PageHistoryUnit(string BookAddress, string PageName)
{
    public static readonly PageHistoryUnit Empty = new("", "");
    /// <summary>原空白记录判断。</summary>
    public bool IsEmpty() => string.IsNullOrEmpty(BookAddress) && string.IsNullOrEmpty(PageName);
}
/// <summary>原页面历史；加载由 BookOperation 协调，失败不提交游标。</summary>
public sealed class PageHistory
{
    private readonly HistoryLimitedCollection<PageHistoryUnit> _history = new(100);
    /// <summary>去除末尾空记录，仅在书籍/条目变化时追加。</summary>
    public void Add(PageHistoryUnit unit) { _history.TrimEnd(PageHistoryUnit.Empty); if (unit != _history.GetCurrent()) _history.Add(unit); }
    /// <summary>判断原上一页历史能力。</summary>
    public bool CanMoveToPrevious() => _history.CanPrevious();
    /// <summary>判断原下一页历史能力。</summary>
    public bool CanMoveToNext() => _history.CanNext();
    /// <summary>取得待加载历史；不存在时返回 null，不改变游标。</summary>
    public PageHistoryUnit? GetTarget(int direction) => direction < 0 ? (_history.CanPrevious() ? _history.GetPrevious() : null) : (_history.CanNext() ? _history.GetNext() : null);
    /// <summary>成功显示后提交游标；异步加载失败可以重试原项。</summary>
    public void CommitMove(int direction) => _history.Move(Math.Sign(direction));
    /// <summary>保留原前进/后退菜单列表的索引和顺序。</summary>
    public IReadOnlyList<KeyValuePair<int, PageHistoryUnit>> GetHistory(int direction, int size) => _history.GetHistory(direction, size);
}
/// <summary>原打开顺序历史，不以访问时间排序的 History.json 替代。</summary>
public sealed class BookHubHistory
{
    private readonly HistoryLimitedCollection<string> _history = new(100);
    /// <summary>普通打开追加新分支；来自本历史的重放由调用者跳过追加。</summary>
    public void Add(string path) { _history.TrimEnd(null); if (path != _history.GetCurrent()) _history.Add(path); }
    /// <summary>判断是否可以后退。</summary>
    public bool CanMoveToPrevious() => _history.CanPrevious();
    /// <summary>判断是否可以前进。</summary>
    public bool CanMoveToNext() => _history.CanNext();
    /// <summary>取得相邻打开记录，不修改游标。</summary>
    public string? GetTarget(int direction) => direction < 0 ? (_history.CanPrevious() ? _history.GetPrevious() : null) : (_history.CanNext() ? _history.GetNext() : null);
    /// <summary>成功打开后提交原游标。</summary>
    public void CommitMove(int direction) => _history.Move(Math.Sign(direction));
}
