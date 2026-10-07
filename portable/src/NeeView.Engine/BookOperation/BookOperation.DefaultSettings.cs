// Copyright (c) NeeLaboratory. 原 SetDefaultPageSetting/TogglePermitFile 的 Mac 可等待适配。
namespace NeeView;

public sealed partial class BookOperation
{
    /// <summary>原默认十二字段复制；普通改变保持来源/主Page，递归变化按原DirtyBook重收集。</summary>
    /// <returns>设置提交或来源重新收集完成的任务；失败保持旧书/设置并可重试。</returns>
    public async Task SetDefaultPageSettingAsync()
    {
        Book? reloadBook = null; BookMemento? reloadMemento = null; long generation = 0;
        await _gate.WaitAsync();
        try
        {
            if (_disposed || _closing || IsLoading) return;
            if (Book is { } book && book.Setting.IsRecursiveFolder != Config.Current.BookSettingDefault.IsRecursiveFolder)
            {
                // 先在独立候选中准备；旧来源在新索引可用前继续保留，不先写错递归能力。
                reloadBook = book; generation = _generation; reloadMemento = Config.Current.BookSettingDefault.ToBookMemento();
                reloadMemento.Path = book.Path; reloadMemento.Page = book.CurrentPage?.EntryName ?? ""; reloadMemento.SortSeed = book.SortSeed;
            }
            else
            {
                _saving?.Cancel(); await saveData.SynchronizeWritesAsync();
                var target = Config.Current.BookSetting; var before = (BookSettingConfig)target.Clone();
                var position = Position; var seed = Book?.SortSeed; var historyState = Book?.MementoControl.CaptureState();
                var anchor = Book?.CurrentPage;
                try
                {
                    Config.Current.BookSettingDefault.CopyTo(target);
                    if (Book is { } current)
                    {
                        // 原PageFrameBoxContext订阅实际设置变化，允许重新登记移除的历史；相同值不触发。
                        if (before.Page != target.Page || System.Text.Json.JsonSerializer.Serialize(before) != System.Text.Json.JsonSerializer.Serialize(target))
                            current.MementoControl.RequestSaveBookMemento(false);
                        await current.SortAsync(CancellationToken.None);
                        Position = new(anchor?.Index ?? position.Index, position.Part); RebuildFrame(MoveDirection);
                    }
                    await saveData.SaveAsync(Book);
                }
                catch
                {
                    before.CopyTo(target);
                    if (Book is { } retained)
                    {
                        retained.SortSeed = seed!.Value; await retained.SortAsync(CancellationToken.None);
                        Position = position; RebuildFrame(MoveDirection); retained.MementoControl.RestoreState(historyState!.Value);
                    }
                    throw;
                }
                RecordPageHistory(); Notify();
            }
        }
        finally { _gate.Release(); }
        if (reloadBook is not null && reloadMemento is not null)
        {
            // 更晚的打开或退出拥有优先级，已失效的默认重载不覆盖新上下文。
            if (_closing || _disposed || generation != _generation || !ReferenceEquals(reloadBook, Book)) return;
            var loaded = await OpenCoreAsync(reloadBook.Path, CancellationToken.None, entryName: string.IsNullOrEmpty(reloadMemento.Page) ? null : reloadMemento.Page,
                startupMemento: reloadMemento, pageSearchKeyword: reloadBook.Pages.SearchKeyword, expectedGeneration: generation);
            if (!loaded && ReferenceEquals(reloadBook, Book) && _generation == generation + 1 && !_closing && !_disposed)
                throw new IOException("重置页面设置的来源重新收集失败：" + Error);
        }
    }
    /// <summary>原全局文件写权限与菜单/Toggle参数；不导航，不改变已授权实体操作的结果。</summary>
    /// <param name="fromMenu">菜单取反，快捷键采用原On/Off/Toggle。</param>
    public async Task ToggleFileWriteAccessAsync(bool fromMenu = false)
    {
        await _gate.WaitAsync();
        try
        {
            if (_disposed || _closing) return;
            _saving?.Cancel(); await saveData.SynchronizeWritesAsync();
            var before = Config.Current.System.IsFileWriteAccessEnabled;
            try
            {
                Config.Current.System.IsFileWriteAccessEnabled = saveData.GetCommandParameter<ToggleCommandParameter>("TogglePermitFile").GetState(before, fromMenu);
                await SaveConfigurationAsync();
            }
            catch { Config.Current.System.IsFileWriteAccessEnabled = before; throw; }
        }
        finally { _gate.Release(); }
    }
}
