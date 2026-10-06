using Avalonia.Threading;
namespace NeeView.MacOS.Views;

public sealed partial class MainWindow
{
    /// <summary>后台来源输入转交UI线程；关闭或切书关闭本次弹窗，迟到文本不提交。</summary>
    private async Task<string?> AskArchiveKeyAsync(ArchiveKeyRequest request, CancellationToken token)
    {
        return await Dispatcher.UIThread.InvokeAsync(ShowAsync);
        async Task<string?> ShowAsync()
        {
            token.ThrowIfCancellationRequested(); if (_preparing || _closedPrepared) return null;
            var dialog = new PasswordDialog(request);
            using var canceled = token.Register(() => Dispatcher.UIThread.Post(() => { if (dialog.IsVisible) dialog.Close(null); }));
            var result = await dialog.ShowDialog<string?>(this);
            token.ThrowIfCancellationRequested(); return _preparing || _closedPrepared ? null : result;
        }
    }
}
