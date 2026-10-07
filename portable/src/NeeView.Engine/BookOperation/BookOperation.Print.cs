namespace NeeView;
public sealed partial class BookOperation
{
    /// <summary>打印共享原导出忙碌/导航/关闭等待；原生系统操作开始后等待真实结果。</summary>
    public async Task<bool> PrintCurrentAsync(Func<CancellationToken,Task<bool>> print,CancellationToken token=default)
    {
        if(!CanExportImage)throw new InvalidOperationException("当前阅读器不能打印。");
        var book=Book;var generation=_generation;using var cancel=CancellationTokenSource.CreateLinkedTokenSource(token);
        var completion=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock(_exportSync){if(IsExporting)throw new InvalidOperationException("已有输出任务。");_exportPending=cancel;_exportCompletion=completion;}
        var entered=false;
        try
        {
            Notify();await _gate.WaitAsync(cancel.Token);entered=true;
            if(generation!=_generation||!ReferenceEquals(book,Book))throw new OperationCanceledException(cancel.Token);
            return await print(cancel.Token);
        }
        finally
        {
            if(entered)_gate.Release();lock(_exportSync){_exportPending=null;_exportCompletion=null;}
            completion.TrySetResult();Notify();
        }
    }
}
