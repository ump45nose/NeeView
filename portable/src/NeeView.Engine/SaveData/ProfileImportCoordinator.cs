namespace NeeView;
/// <summary>新窗口无法释放时禁止恢复磁盘，避免存活对象随后覆盖恢复文件。</summary>
public sealed class ProfileImportRecoveryBlockedException(string message, Exception inner) : Exception(message, inner);


/// <summary>导入只替换实际系统边界：先关闭唯一阅读上下文，再提交和重新装配，不广播配置刷新。</summary>
public sealed class ProfileImportCoordinator(SaveData state, Func<Task> close, Func<Task> reopen)
{
    private readonly SemaphoreSlim _gate = new(1);
    /// <summary>提交失败重开旧状态；新窗口装配失败时先恢复持久备份再重开。</summary>
    /// <param name="request">已经确认的独立候选。</param><param name="token">关闭前和提交准备阶段可取消；窗口重建/回滚不可取消。</param>
    /// <returns>成功导入结果；失败抛出，保留原错误与恢复错误。</returns>
    public async Task<ProfileImportResult> ApplyAsync(ProfileImportRequest request, CancellationToken token = default)
    {
        await _gate.WaitAsync(token);
        try
        {
            await state.ValidateProfileImportAsync(request, token);
            token.ThrowIfCancellationRequested();
            await close(); // 保存失败不提交任何候选；旧窗口负责恢复自身交互。
            ProfileImportResult? result = null;
            try
            {
                result = await state.ApplyProfileImportAsync(request, token);
                await reopen(); return result;
            }
            catch (ProfileImportRecoveryBlockedException failure)
            { throw new ProfileImportRecoveryBlockedException("新窗口尚未释放，导入前备份：" + result?.BackupDirectory, failure); }
            catch (Exception failure)
            {
                try
                {
                    if (result is not null) await state.RestoreProfileImportAsync(result.BackupDirectory);
                    await reopen();
                }
                catch (Exception recovery) { throw new AggregateException("导入失败且恢复未完成，请保留 Profile 备份与事务标记。", failure, recovery); }
                throw;
            }
        }
        finally { _gate.Release(); }
    }
}
