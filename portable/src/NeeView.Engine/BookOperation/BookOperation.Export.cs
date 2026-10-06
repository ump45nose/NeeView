// Copyright (c) NeeLaboratory. 原ExportImageService/ExportBook，沿唯一来源/页框与JSON。
using System.IO.Compression;
using NeeView.PageFrames;
namespace NeeView;

public sealed partial class BookOperation
{
    private readonly object _exportSync = new();
    private CancellationTokenSource? _exportPending;
    private TaskCompletionSource? _exportCompletion;
    public bool IsExporting => _exportCompletion is not null;
    public IViewImageExporter? ViewImageExporter { get; set; }
    public Func<string, CancellationToken, Task<ExportOverwriteAnswer>>? ConfirmExportOverwriteAsync { get; set; }
    /// <summary>切书和退出取消输出准备，等待原生晚到资源及临时文件清理。</summary>
    public void CancelExportPreparation() { lock (_exportSync) _exportPending?.Cancel(); }
    public bool CanExportImage => !_disposed && !_closing && !IsLoading && !IsExporting && Book is { IsIndexing: false }
        && Frame?.Elements.Count > 0 && IsFrameReading && !IsUsingClipboard && !IsOpeningExternalApplication && !IsRenamingBook
        && !IsTransferringBook && !IsDeletingFile && _destinationMoves?.IsBusy != true;
    /// <summary>单页Original保持首元素语义，不跳过dummy或目录偷偷导出另一页。</summary>
    public bool CanExportOriginalImage => CanExportImage && Frame!.Elements[0] is { IsDummy: false, Page.ArchiveEntry.IsDirectory: false };
    /// <summary>命名捕获原非dummy元素顺序，Original仍仅取原首Page。</summary>
    public ExportPageSource? GetExportPageSource(PageFrame? frame = null) => Book is { } book && (frame ?? Frame) is { } current
        ? new(book.Path, current.Direction, current.Elements.Where(e => !e.IsDummy)
            .Select(e => new PageNameElement(new PageNameSource(e.Page.Index, e.Page.EntryName), e.PagePart)).ToList()) : null;
    /// <summary>单页和整书共用导航锁/代次。Original直接复制条目；View仅由唯一查看器提供。</summary>
    /// <param name="parameter">独立草稿；命令与Config默认不同，不能合并。</param><param name="target">实际文件或整书文件夹路径。</param>
    /// <param name="token">准备、读取和提交前取消。</param><returns>真实落点和完成数量。</returns>
    public async Task<ExportImageResult> ExportImagesAsync(IExportImageParameter parameter, string target, CancellationToken token = default)
    {
        if (!CanExportImage) throw new InvalidOperationException("当前不能导出，请等待加载或文件操作完成。");
        var book = Book!; var generation = _generation;
        var options = new ExportImageParameter(parameter); var bookType = (parameter as ExportBookParameter)?.BookType;
        if (!Enum.IsDefined(options.Mode) || !Enum.IsDefined(options.FileFormat) || !Enum.IsDefined(options.OverwriteMode)
            || bookType is { } kind && !Enum.IsDefined(kind)) throw new ArgumentException("不支持的导出参数。");
        if (bookType is not null && options.OverwriteMode == ExportImageOverwriteMode.Confirm) throw new ArgumentException("原整书导出不支持逐页确认，请使用加编号或禁止覆盖。");
        if (options.Mode == ExportImageMode.View && ViewImageExporter is null) throw new NotSupportedException("当前视图导出尚不可用。");
        if (options.Mode == ExportImageMode.Original && bookType is null && !CanExportOriginalImage)
            throw new NotSupportedException("当前首元素是占位或目录，不能作为原图导出。");
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_exportSync)
        {
            if (IsExporting) throw new InvalidOperationException("已有导出正在执行。");
            _exportPending = cancellation; _exportCompletion = completion;
        }
        int count = 0;
        bool entered = false; var position = Position; var move = MoveDirection;
        var counting = book.MementoControl.IsPageChangeCountEnabled;
        try
        {
            Notify(); await _gate.WaitAsync(cancellation.Token); entered = true;
            CheckExport(); position = Position; move = MoveDirection; counting = book.MementoControl.IsPageChangeCountEnabled;
            _saving?.Cancel(); book.MementoControl.IsPageChangeCountEnabled = false;
            string output;
            var policy = new DefaultExportImageFileNamePolicy(options);
            async Task Write(PageFrame frame, Page? original, Stream stream, CancellationToken ct)
            {
                CheckExport();
                if (options.Mode == ExportImageMode.Original)
                {
                    var page = original ?? frame.Elements.First().Page;
                    if (page.ArchiveEntry.IsDirectory) throw new NotSupportedException("不能将目录作为原图导出。");
                    await using var source = await page.ArchiveEntry.Archive.OpenEntryAsync(page.ArchiveEntry, ct);
                    await source.CopyToAsync(stream, ct); // 修正原流式导出仅打开条目而未写目标流的缺陷。
                }
                else await ViewImageExporter!.ExportViewAsync(frame, options, stream, ct);
                CheckExport();
            }
            async Task Each(Func<string, PageFrame, Page?, Task> export)
            {
                if (options.Mode == ExportImageMode.Original)
                {
                    foreach (var page in book.Pages.Where(p => !p.ArchiveEntry.IsDirectory))
                    {
                        CheckExport(); var source = new ExportPageSource(book.Path, Frame!.Direction, [new(new PageNameSource(page.Index, page.EntryName))]);
                        var name = ExportImageWriter.RelativeName(policy.CreateFileName(source, count + 1));
                        await export(name, Frame!, page); count++;
                    }
                }
                else
                {
                    var next = new PagePosition(0, 0);
                    while (next.Index < book.Pages.Count)
                    {
                        CheckExport(); await ProbeAroundAsync(book, next.Index, cancellation.Token); Position = next; RebuildFrame(1); Notify();
                        var frame = Frame ?? throw new IOException("导出页框创建失败。");
                        var name = ExportImageWriter.RelativeName(policy.CreateFileName(GetExportPageSource(frame)!, count + 1));
                        await export(name, frame, null); count++;
                        // 原ExportBook以包含最后Page终止；循环页框可能包含末页和首图，不能再次导出首图。
                        if (frame.Elements.Any(e => !e.IsDummy && ReferenceEquals(e.Page, book.Pages.Last()))) break;
                        var following = frame.FrameRange.Next(1);
                        if (following <= next) throw new IOException("导出导航未向前移动。");
                        next = following;
                    }
                }
            }
            if (bookType == ExportBookType.Zip)
            {
                output = await ExportImageWriter.WriteFileAsync(target, ExportImageOverwriteMode.Confirm, ConfirmExportOverwriteAsync, async (stream, ct) =>
                {
                    using var zip = new ZipArchive(stream, ZipArchiveMode.Create, true); var names = new HashSet<string>(StringComparer.Ordinal);
                    await Each(async (name, frame, page) =>
                    {
                        var resolved = await ExportImageWriter.ResolveAsync(name, options.OverwriteMode, names.Contains, ConfirmExportOverwriteAsync, ct);
                        if (resolved.Replace) throw new IOException("ZIP内部同名覆盖不受支持，请选择加编号。");
                        names.Add(resolved.Name); var entry = zip.CreateEntry(resolved.Name, CompressionLevel.Fastest);
                        if (page?.ArchiveEntry.LastWriteTime is { Year: >= 1980 and <= 2107 } stamp) entry.LastWriteTime = stamp;
                        await using var entryStream = entry.Open(); await Write(frame, page, entryStream, ct);
                    });
                }, cancellation.Token);
            }
            else if (bookType == ExportBookType.Folder)
            {
                output = Path.TrimEndingDirectorySeparator(Path.GetFullPath(target)); if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any())
                {
                    var answer = ConfirmExportOverwriteAsync is null ? ExportOverwriteAnswer.Cancel : await ConfirmExportOverwriteAsync(output, cancellation.Token);
                    if (answer != ExportOverwriteAnswer.Replace) throw new OperationCanceledException(cancellation.Token);
                }
                Directory.CreateDirectory(output);
                await Each(async (name, frame, page) =>
                {
                    var path = Path.Combine(output, name); ExportImageWriter.CheckChildLinks(output, path);
                    await ExportImageWriter.WriteFileAsync(path, options.OverwriteMode, ConfirmExportOverwriteAsync, (stream, ct) => Write(frame, page, stream, ct), cancellation.Token);
                });
            }
            else
            {
                // 原ViewImageExporter按实际目标扩展名选择PNG/JPEG。
                if (options.Mode == ExportImageMode.View) options.FileFormat = Path.GetExtension(target).Equals(".png", StringComparison.OrdinalIgnoreCase) ? BitmapImageFormat.Png : BitmapImageFormat.Jpeg;
                output = await ExportImageWriter.WriteFileAsync(target, options.OverwriteMode, ConfirmExportOverwriteAsync,
                    (stream, ct) => Write(Frame!, null, stream, ct), cancellation.Token); count = 1;
            }
            return new(output, count, options.Mode == ExportImageMode.Original && bookType is not null ? book.Pages.Where(p => p.ArchiveEntry.IsDirectory).Select(p => p.EntryName).ToArray() : []);
            void CheckExport() { cancellation.Token.ThrowIfCancellationRequested(); if (_closing || generation != _generation || !ReferenceEquals(book, Book)) throw new OperationCanceledException(cancellation.Token); }
        }
        catch (Exception error) when (bookType == ExportBookType.Folder && count > 0)
        { throw new ExportPartialException(target, count, error); }
        finally
        {
            if (entered)
            {
                // 不记录临时导出导航；仍为原书时恢复原Page/Part，不能覆盖新打开请求。
                if (!_disposed && ReferenceEquals(book, Book)) { Position = position; RebuildFrame(move); }
                book.MementoControl.IsPageChangeCountEnabled = counting; _gate.Release();
            }
            lock (_exportSync) { _exportPending = null; _exportCompletion = null; }
            try { Notify(); } finally { completion.TrySetResult(); }
        }
    }
}
