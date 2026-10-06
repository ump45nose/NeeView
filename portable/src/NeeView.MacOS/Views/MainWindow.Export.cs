using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using NeeView.MacOS.ViewModels;
namespace NeeView.MacOS.Views;

public sealed partial class MainWindow
{
    private Task _exportAction = Task.CompletedTask;
    private CancellationTokenSource? _exportCancellation;
    private ExportImageDialog? _exportDialog;
    /// <summary>原三个导出命令共用宿主单槽；退出取消选择/预览并等待实际输出。</summary>
    private Task RunExportAsync(string command) => _exportAction.IsCompleted && !_preparing && !_closedPrepared
        ? _exportAction = ExportCoreAsync(command) : Task.CompletedTask;
    private async Task ExportCoreAsync(string command)
    {
        await Task.Yield(); if (_model is null || !_model.Operation.CanExportImage) return;
        var operation = _model.Operation; var book = operation.Book;
        using var cancellation = new CancellationTokenSource(); _exportCancellation = cancellation;
        bool wasPlaying = operation.SlideShow.IsPlaying;
        if (wasPlaying) operation.SlideShow.Stop();
        try
        {
            IExportImageParameter options;
            if (command == "ExportImage") options = _model.SaveData.GetCommandParameter<ExportImageCommandParameter>(command);
            else
            {
                var wholeBook = command == "ExportBookAs";
                var current = wholeBook ? Config.Current.Book.ExportBookParameter : Config.Current.Book.ExportImageParameter;
                var source = operation.GetExportPageSource() ?? throw new IOException("没有可导出的页框。");
                var dialog = _exportDialog = new ExportImageDialog(new ExportImageViewModel(current, source, wholeBook));
                dialog.PreviewAsync = async (parameter, token) =>
                {
                    if (!ReferenceEquals(book, operation.Book)) throw new OperationCanceledException(token);
                    if (parameter.Mode == ExportImageMode.Original) return Viewer.CanCopyImage ? await Viewer.CaptureCopyImageAsync(token) : null;
                    using var stream = new ImageCopyEncoder.LimitedPngStream();
                    var preview = new ExportImageParameter(parameter) { FileFormat = BitmapImageFormat.Png };
                    await Viewer.ExportViewAsync(operation.Frame!, preview, stream, token); return stream.ToArray();
                };
                var result = await dialog.ShowDialog<ExportImageParameter?>(this);
                _exportDialog = null; await dialog.PendingPreview;
                cancellation.Token.ThrowIfCancellationRequested(); if (result is null) return; options = result;
            }
            if (!ReferenceEquals(book, operation.Book)) throw new OperationCanceledException(cancellation.Token);
            var target = await ChooseExportTargetAsync(options, command == "ExportImage", cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            if (target is null || !ReferenceEquals(book, operation.Book) || _preparing) return;
            IsEnabled = false;
            var exported = await operation.ExportImagesAsync(options, target, cancellation.Token);
            // 对话框草稿沿唯一JSON保存，失败原地回滚配置；已导出的文件不会被保存失败撤销。
            if (command != "ExportImage")
            {
                var oldImage = Config.Current.Book.ExportImageParameter; var oldBook = Config.Current.Book.ExportBookParameter;
                var saved = new ExportImageParameter(options) { ExportFolder = Path.GetDirectoryName(exported.Path)! };
                if (options is ExportBookParameter bp) Config.Current.Book.ExportBookParameter = new(saved, bp.BookType);
                else Config.Current.Book.ExportImageParameter = saved;
                try { await operation.SaveAllAsync(); }
                catch (Exception error)
                {
                    Config.Current.Book.ExportImageParameter = oldImage; Config.Current.Book.ExportBookParameter = oldBook;
                    ShowError("已导出 " + exported.Count + " 项：" + exported.Path + "；导出参数保存失败：" + error.Message);
                    return;
                }
            }
            if (command != "ExportImage" || ((ExportImageCommandParameter)options).IsShowToast)
                ShowError("已导出 " + exported.Count + " 项：" + exported.Path + (exported.SkippedDirectories?.Count > 0 ? "；跳过目录项 " + exported.SkippedDirectories.Count : ""));
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { if (!_preparing) ShowError("导出失败：" + error.Message); }
        finally
        {
            _exportDialog = null; _exportCancellation = null;
            if (!_closedPrepared) IsEnabled = true;
            if (!_preparing && wasPlaying && ReferenceEquals(book, operation.Book)) operation.SlideShow.Play();
            if (!_closedPrepared) RefreshHistoryCommandStates();
        }
    }
    /// <summary>选择器只回报本地目标；源/模板和实际写入由Engine校验。</summary>
    private async Task<string?> ChooseExportTargetAsync(IExportImageParameter options, bool direct, CancellationToken token)
    {
        var source = _model!.Operation.GetExportPageSource()!;
        var filename = new DefaultExportImageFileNamePolicy(options).CreateFileName(source, 1);
        if (direct && !string.IsNullOrWhiteSpace(options.ExportFolder))
            return await ExportImageParameterTools.PrepareTargetAsync(options.ExportFolder, filename, token);
        using var start = string.IsNullOrWhiteSpace(options.ExportFolder) ? null : await StorageProvider.TryGetFolderFromPathAsync(options.ExportFolder);
        token.ThrowIfCancellationRequested();
        if (options is ExportBookParameter { BookType: ExportBookType.Folder })
        {
            var folders = await StorageProvider.OpenFolderPickerAsync(new() { Title = "选择书籍导出父目录", SuggestedStartLocation = start, AllowMultiple = false });
            try { token.ThrowIfCancellationRequested(); return folders.FirstOrDefault()?.TryGetLocalPath() is { } parent ? Path.Combine(parent, Path.GetFileNameWithoutExtension(source.BookAddress)) : null; }
            finally { foreach (var folder in folders) folder.Dispose(); }
        }
        bool zip = options is ExportBookParameter;
        return await AwaitBackupPathAsync(StorageProvider.SaveFilePickerAsync(new()
        {
            Title = zip ? "导出书籍 ZIP" : "导出图像", SuggestedStartLocation = start,
            SuggestedFileName = zip ? Path.GetFileNameWithoutExtension(source.BookAddress) + ".zip" : Path.GetFileName(filename),
            DefaultExtension = zip ? "zip" : Path.GetExtension(filename).TrimStart('.'), ShowOverwritePrompt = false,
            FileTypeChoices = zip ? [new("ZIP") { Patterns = ["*.zip"] }] : options.Mode == ExportImageMode.View
                ? [new("PNG") { Patterns = ["*.png"] }, new("JPEG") { Patterns = ["*.jpg", "*.jpeg"] }]
                : [new("原始条目") { Patterns = ["*" + Path.GetExtension(filename)] }]
        }), token);
    }
    /// <summary>原覆盖/加编号/取消三选；只回报授权，不在UI修改文件。</summary>
    private async Task<ExportOverwriteAnswer> ConfirmExportOverwriteAsync(string path, CancellationToken token)
    {
        return await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            token.ThrowIfCancellationRequested(); if (_preparing) return ExportOverwriteAnswer.Cancel;
            var dialog = new Window { Title = "导出目标已存在", Width = 620, SizeToContent = SizeToContent.Height, WindowStartupLocation = WindowStartupLocation.CenterOwner };
            var replace = new Button { Content = "覆盖", IsDefault = true }; var number = new Button { Content = "加编号" }; var cancel = new Button { Content = "取消", IsCancel = true };
            replace.Click += (_, _) => dialog.Close(ExportOverwriteAnswer.Replace); number.Click += (_, _) => dialog.Close(ExportOverwriteAnswer.AddNumber); cancel.Click += (_, _) => dialog.Close(ExportOverwriteAnswer.Cancel);
            dialog.Content = new StackPanel { Margin = new(16), Spacing = 12, Children =
                { new TextBlock { Text = path, TextWrapping = Avalonia.Media.TextWrapping.Wrap }, new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { replace, number, cancel } } } };
            using var registration = token.Register(() => Dispatcher.UIThread.Post(() => dialog.Close(ExportOverwriteAnswer.Cancel)));
            return await dialog.ShowDialog<ExportOverwriteAnswer>(this);
        });
    }
}
