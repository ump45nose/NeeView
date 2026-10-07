// Copyright (c) NeeLaboratory. 原 SaveSetting/ReloadSetting/Exporter 的可等待 Mac 边界。
using System.Reflection;
using System.Text.Json;
namespace NeeView;

public sealed partial class BookOperation
{
    /// <summary>设置文件动作不能与加载、实体操作或已关闭上下文并发。</summary>
    public bool CanManageProfile => !_disposed && !_closing && !IsLoading && !IsRenamingBook && !IsTransferringBook
        && _bookDeleteCompletion is null && !IsDeletingFile && !IsUsingClipboard && _destinationMoves?.IsBusy != true;

    /// <summary>原 SaveAll(false)：取消防抖、等待已确认列表编辑，立即保存阅读、设置及集合。</summary>
    /// <param name="token">等待与保存准备可取消。</param><returns>全部权威写入完成任务，失败传播。</returns>
    public async Task SaveAllAsync(CancellationToken token = default)
    {
        await _gate.WaitAsync(token);
        try { EnsureProfileAvailable(); await SaveAllCoreAsync(token); }
        finally { _gate.Release(); }
    }
    /// <summary>先保存原所有状态，再沿唯一 SaveData 串流导出；导航在整个动作内串行。</summary>
    /// <param name="target">原 .nvzip 目标或命令明确文件名。</param><param name="token">准备及导出取消。</param>
    /// <returns>实际完成的备份路径和条目。</returns>
    public async Task<ProfileExportResult> ExportBackupAsync(string target, CancellationToken token = default)
    {
        await _gate.WaitAsync(token);
        try { EnsureProfileAvailable(); await SaveAllCoreAsync(token); return await saveData.ExportBackupAsync(target, token); }
        finally { _gate.Release(); }
    }
    private void EnsureProfileAvailable()
    { if (!CanManageProfile) throw new InvalidOperationException("请等待书籍加载或文件操作完成后再操作设置。"); }
    private async Task SaveAllCoreAsync(CancellationToken token)
    {
        _saving?.Cancel();
        if (_playlistHub is not null) await _playlistHub.FlushAsync();
        await saveData.SynchronizeWritesAsync();
        SaveLastBookshelf();
        await SaveCurrentReadingAsync(token);
    }

    /// <summary>原地重载唯一 UserSetting；仅来源索引规则变化按原 DirtyBook 重新收集，不重载历史或书签。</summary>
    /// <param name="token">排队及候选读取取消；成功应用后不把晚到取消误报失败。</param>
    /// <returns>所有已迁运行配置和命令完成恢复的任务。</returns>
    public async Task ReloadSettingAsync(CancellationToken token = default)
    {
        Book? reloadBook = null; BookMemento? reloadMemento = null; long reloadGeneration = 0;
        await _gate.WaitAsync(token);
        try
        {
            EnsureProfileAvailable();
            _saving?.Cancel(); await saveData.SynchronizeWritesAsync();
            var generation = _generation;
            var snapshot = JsonSerializer.Deserialize<Config>(JsonSerializer.Serialize(Config.Current))!;
            var stretch = Config.Current.View.ValidStretchMode;
            var reading = Book is { } book ? (BookSettingConfig)book.Setting.Clone() : null;
            var previousPosition = Position;
            try
            {
                await saveData.ReloadSettingAsync(candidate =>
                {
                    if (generation != _generation) throw new OperationCanceledException("打开请求已改变，请重试重载设置。");
                    EnsureProfileAvailable();
                    if (candidate is null) return; // 原缺失UserSetting返回空memento，不重置当前Config。
                    foreach (var branch in typeof(Config).GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.PropertyType.IsClass && p.CanWrite))
                        CopySettingFields(branch.GetValue(candidate)!, branch.GetValue(Config.Current)!);
                    if (Book is { } current)
                    {
                        var anchor = current.CurrentPage;
                        // Config.BookSetting 与当前 Book.Setting 共享对象，分支复制已应用候选；显式使用候选避免隐含依赖。
                        var recollect = reading!.IsRecursiveFolder != candidate.BookSetting.IsRecursiveFolder || snapshot.System.BookPageCollectMode != Config.Current.System.BookPageCollectMode
                            || snapshot.System.ArchiveRecursiveMode != Config.Current.System.ArchiveRecursiveMode
                            || JsonSerializer.Serialize(snapshot.Archive.Pdf) != JsonSerializer.Serialize(Config.Current.Archive.Pdf)
                            || MediaFormats.IndexChanged(snapshot.Archive.Media,Config.Current.Archive.Media);
                        if (reading.SortMode != current.Setting.SortMode) current.Sort(token);
                        Position = new(anchor?.Index ?? previousPosition.Index, previousPosition.Part);
                        RebuildFrame(MoveDirection);
                        if (recollect)
                        {
                            // 必须在排序提交后捕获 EffectiveSortMode，否则同时改变递归/排序会重开为旧排序。
                            reloadBook = current; reloadMemento = current.CreateMemento(); reloadGeneration = generation;
                            // 当前索引在新来源真正成功前继续使用旧递归规则，避免失败后保留错误的读取能力。
                            current.Setting.IsRecursiveFolder = reading.IsRecursiveFolder;
                        }
                    }
                }, token);
            }
            catch
            {
                foreach (var branch in typeof(Config).GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.PropertyType.IsClass && p.CanWrite))
                    CopySettingFields(branch.GetValue(snapshot)!, branch.GetValue(Config.Current)!);
                Config.Current.View.RestoreStretchMode(snapshot.View.StretchMode, stretch);
                if (reading is not null && Book is { } current)
                { CopySettingFields(reading, current.Setting); current.Setting.Page = reading.Page; current.Sort(CancellationToken.None); Position = previousPosition; RebuildFrame(MoveDirection); }
                throw;
            }
            Bookshelf.Reorder(); _destinationFolders?.RefreshManaged(); Notify();
        }
        finally { _gate.Release(); }
        if (reloadBook is not null && reloadMemento is not null && ReferenceEquals(reloadBook, Book) && reloadGeneration == _generation)
        {
            var loaded = await OpenCoreAsync(reloadBook.Path, token, entryName: string.IsNullOrEmpty(reloadMemento.Page) ? null : reloadMemento.Page,
                startupMemento: reloadMemento, pageSearchKeyword: reloadBook.Pages.SearchKeyword, expectedGeneration: reloadGeneration);
            if (!loaded)
            {
                token.ThrowIfCancellationRequested();
                if (ReferenceEquals(reloadBook, Book) && _generation == reloadGeneration + 1) throw new IOException("设置已恢复，但来源重新收集失败：" + Error);
            }
        }
    }
}
