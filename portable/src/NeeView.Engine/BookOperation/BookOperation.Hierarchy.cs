// Copyright (c) NeeLaboratory. 原BookHub.RequestLoadParent及BookOperation.MoveToChildBook。
namespace NeeView;

public sealed partial class BookOperation
{
    public bool CanMoveToParentBook => !_disposed && !_closing && !IsLoading && Book?.BookAddress.Place is not null;
    public bool CanMoveToChildBook => !_disposed && !_closing && !IsLoading && Book?.CurrentPage?.PageType.IsFolder() == true;
    /// <summary>进入父书并明确定位当前真实子项；失败保留旧来源/位置，书架浏览状态不作为目标。</summary>
    public async Task MoveToParentBookAsync()
    {
        if (!CanMoveToParentBook) return;
        await _historyGate.WaitAsync();
        try
        {
            if (!CanMoveToParentBook || Book?.BookAddress is not { Place: { } parent, ParentEntryName: { } entry }) return;
            if (parent == Book.Path) return;
            // Mac普通文件名可以含反斜杠；宿主相对路径已经使用'/'，不得二次归一。
            await OpenCoreAsync(parent, CancellationToken.None, entryName: entry);
        }
        finally { _historyGate.Release(); }
    }
    /// <summary>原当前主页面必须为Folder或Archive；从其SystemPath进入，与书架选中项无关。</summary>
    public async Task MoveToChildBookAsync()
    {
        if (!CanMoveToChildBook) return;
        await OpenChildBookAsync(Book!.CurrentPage!, Book);
    }
    /// <summary>原书籍封面按钮打开实际页面；先核对所属书籍，旧画面不能打开新书的同索引项。</summary>
    public async Task OpenChildBookAsync(Page page, Book? expectedBook = null)
    {
        await _historyGate.WaitAsync();
        try
        {
            if (_disposed || _closing || IsLoading || Book is null || expectedBook is not null && !ReferenceEquals(expectedBook, Book)
                || !page.PageType.IsFolder() || !Book.Pages.Contains(page) || page.EntryFullName == Book.Path) return;
            await OpenCoreAsync(page.EntryFullName, CancellationToken.None);
        }
        finally { _historyGate.Release(); }
    }
    /// <summary>原递归设置重新收集来源，明确保留当前实体条目；失败不改变当前书籍或配置。</summary>
    public async Task ToggleRecursiveFolderAsync()
    {
        if (_disposed || _closing || IsLoading || Book is null) return;
        var book = Book; var memento = book.CreateMemento();
        memento.IsRecursiveFolder = !book.Setting.IsRecursiveFolder;
        await OpenCoreAsync(book.Path, CancellationToken.None, entryName: string.IsNullOrEmpty(memento.Page) ? null : memento.Page, startupMemento: memento, startupPart: Position.Part);
    }
}
