// Copyright (c) NeeLaboratory. 原BookMementoControl的历史登记规则，去除WPF及全局集合订阅。
namespace NeeView;

/// <summary>每本书的原登记阈值与删除状态；SaveData串行执行实际JSON事务。</summary>
public sealed class BookMementoControl(Book book)
{
    private int _pageChangeCount;
    private bool _historyEntry;
    public bool IsHistoryRemoved { get; private set; }
    public bool IsPageChangeCountEnabled { get; set; } = true;
    /// <summary>仅主Page对象变化计数；半页、尺寸与同页刷新不计数。</summary>
    public void OnTopPageChanged() { if (_pageChangeCount < int.MaxValue) _pageChangeCount++; IsHistoryRemoved = false; }
    /// <summary>原删除回报重置本次访问；下一次真实换页可重新登记。</summary>
    public void OnHistoryRemoved() { _pageChangeCount = 0; _historyEntry = false; IsHistoryRemoved = true; }
    /// <summary>原设置/页尾保存请求允许绕过计数，指定检查时保留删除抑制。</summary>
    public void RequestSaveBookMemento(bool checkHistoryRemoved)
    { if (checkHistoryRemoved && IsHistoryRemoved) return; IsHistoryRemoved = false; IsPageChangeCountEnabled = false; }
    /// <summary>沿原判断顺序：已登记可持续更新，已有书只需一次实际主页面变化。</summary>
    public bool CanHistory(HistoryConfig config) => !IsHistoryRemoved && book.Pages.Count > 0
        && (!IsPageChangeCountEnabled || _historyEntry || _pageChangeCount >= (book.IsNew ? config.HistoryEntryPageCount : 1))
        && (config.IsInnerArchiveHistoryEnabled || book.Source.Parent is null)
        // 包内逻辑目录不新增压缩层，不能误用路径差异当作嵌套。
        && (config.IsUncHistoryEnabled || !book.Path.StartsWith(@"\\", StringComparison.Ordinal));
    /// <summary>仅JSON事务成功后记为已登记，失败仍可重试。</summary>
    internal void CommitHistoryEntry() => _historyEntry = true;
}
