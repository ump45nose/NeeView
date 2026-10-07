using Avalonia.Threading;
using NeeView.MacOS.ViewModels;
namespace NeeView.MacOS.Views;
public sealed partial class MainWindow
{
    private IPrintService? _printService;
    private PrintParameters _lastPrintParameters = new();
    private Task _printAction = Task.CompletedTask;
    private PrintWindow? _printWindow;
    /// <summary>唯一启动层装配系统输出；视图只使用Engine契约。</summary>
    public void AttachPrinting(IPrintService service) => _printService = service;
    private Task RunPrintAsync() => _printAction.IsCompleted && !_preparing && !_closedPrepared ? _printAction = PrintCoreAsync() : Task.CompletedTask;
    private async Task PrintCoreAsync()
    {
        await Task.Yield(); if (_printService is null || _model?.Operation.CanExportImage != true) return;
        var op = _model.Operation;
        op.SlideShow.Stop(); Viewer.StopAutoScroll();
        try
        {
            // 选项、预览及原生面板共用一次原导航/退出锁，不能在预览期间换来源。
            await op.PrintCurrentAsync(async token =>
            {
                using var media = Viewer.SuspendPrintMedia();
                var dialog = _printWindow = new PrintWindow(new PrintViewModel { Parameters = _lastPrintParameters });
                dialog.PreviewAsync = (parameters, previewToken) => Viewer.CapturePrintAsync(parameters, previewToken);
                using var registration = token.Register(() => Dispatcher.UIThread.Post(() => dialog.Close(null)));
                var parameters = await dialog.ShowDialog<PrintParameters?>(this);
                _printWindow = null; await dialog.PendingPreview;
                if (parameters is null || _preparing) return false;
                token.ThrowIfCancellationRequested(); _lastPrintParameters = parameters; IsEnabled = false;
                var image = await Viewer.CapturePrintAsync(parameters, token);
                return await _printService.PrintAsync(image, parameters, token);
            });
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { ShowError("打印失败：" + error.Message); }
        finally { _printWindow = null; if (!_closedPrepared) IsEnabled = true; }
    }
}
