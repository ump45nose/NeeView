namespace NeeView;

/// <summary>平台文件调用的退出边界；中断不等于回滚，实际结果不明时必须保留恢复记录。</summary>
public interface IInterruptibleFileOperations
{
    /// <summary>取消已排队/正在执行的请求；调用方仍等待明确完成或带恢复说明的失败。</summary>
    void InterruptPendingOperations();
}
