using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using NeeView;

namespace NeeView.Backends;

/// <summary>同一正式exe的短生命周期文件请求；复用原事务，将不可取消的挂载调用隔离在子进程。</summary>
public sealed class FileOperationWorkerBackend : IFileOperationBackend, IBookRenameBackend, IBookTransferBackend, IInterruptibleFileOperations, IDisposable
{
    private static readonly SemaphoreSlim Slot = new(1, 1);
    private readonly object _sync = new();
    private readonly string _recoveryDirectory;
    private readonly Func<ProcessStartInfo> _startInfo;
    private readonly TimeSpan _idleLimit, _cancelGrace;
    private CancellationTokenSource _generation = new();
    private bool _disposed;
    private const string UnknownResult = "文件操作结果未确认；恢复记录已保留，请恢复连接后重新检查。";

    /// <param name="recoveryDirectory">沿用原随机事务日志目录。</param>
    /// <param name="startInfoFactory">测试可注入无窗口宿主；产品固定当前exe，不经过Shell。</param>
    /// <param name="idleLimit">真实I/O进展的最长间隔，默认15秒。</param>
    /// <param name="cancelGrace">取消后允许原协议回滚的时间，默认3秒。</param>
    public FileOperationWorkerBackend(string recoveryDirectory, Func<ProcessStartInfo>? startInfoFactory = null,
        TimeSpan? idleLimit = null, TimeSpan? cancelGrace = null)
    {
        _recoveryDirectory = recoveryDirectory;
        _startInfo = startInfoFactory ?? CreateStartInfo;
        _idleLimit = idleLimit ?? TimeSpan.FromSeconds(15);
        _cancelGrace = cancelGrace ?? TimeSpan.FromSeconds(3);
        if (_idleLimit <= TimeSpan.Zero || _cancelGrace <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(idleLimit));
    }

    /// <summary>中断当前代次（包括排队请求），后续窗口仍可使用同一进程级服务。</summary>
    public void InterruptPendingOperations()
    {
        CancellationTokenSource previous;
        lock (_sync) { if (_disposed) return; previous = _generation; _generation = new(); }
        try { previous.Cancel(); } finally { previous.Dispose(); }
    }
    /// <summary>关闭服务；共享槽不提前释放，仍在运行的请求负责终止和回收自己的进程。</summary>
    public void Dispose()
    {
        CancellationTokenSource previous;
        lock (_sync) { if (_disposed) return; _disposed = true; previous = _generation; }
        try { previous.Cancel(); } finally { previous.Dispose(); }
    }
    private static ProcessStartInfo CreateStartInfo()
    {
        var file = Environment.ProcessPath ?? throw new InvalidOperationException("当前进程路径不可用。");
        var info = new ProcessStartInfo(file);
        if (Path.GetFileNameWithoutExtension(file).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            info.ArgumentList.Add(Assembly.GetEntryAssembly()?.Location ?? throw new InvalidOperationException("当前入口程序集不可用。"));
        info.ArgumentList.Add(FileOperationWorkerHost.Argument);
        return info;
    }

    /// <summary>请求只经stdin传递；成功结果优先于晚取消，未知结果不冒充取消回滚。</summary>
    private async Task<FileWorkerResponse> CallAsync(FileWorkerRequest request, CancellationToken token)
    {
        CancellationTokenSource linked;
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            linked = CancellationTokenSource.CreateLinkedTokenSource(token, _generation.Token);
        }
        using (linked)
        {
            if (!await Slot.WaitAsync(_idleLimit, linked.Token).ConfigureAwait(false))
                throw new IOException("文件操作队列暂不可用；前一请求仍在等待系统终止，恢复记录已保留。");
            Process? process = null; Task<FileWorkerResponse>? responseTask = null; Task? stderrTask = null;
            bool releaseSlot = true;
            try
            {
                linked.Token.ThrowIfCancellationRequested();
                request.RecoveryDirectory = _recoveryDirectory;
                var json = JsonSerializer.Serialize(request, FileWorkerJson.Default.FileWorkerRequest);
                if (json.Length > FileOperationWorkerHost.MaximumMessage) throw new IOException("文件请求超过限制。");
                var info = _startInfo();
                info.UseShellExecute = false; info.RedirectStandardInput = true; info.RedirectStandardOutput = true;
                info.RedirectStandardError = true; info.CreateNoWindow = true;
                process = Process.Start(info) ?? throw new IOException("无法启动文件请求。");
                stderrTask = DrainErrorAsync(process.StandardError);
                long lastProgress = Stopwatch.GetTimestamp();
                responseTask = ReadResponseAsync(process.StandardOutput, () => Interlocked.Exchange(ref lastProgress, Stopwatch.GetTimestamp()));
                // 管道写入同样有界；所有用户路径都保留为JSON字面值。
                await process.StandardInput.WriteLineAsync(json).WaitAsync(_idleLimit).ConfigureAwait(false);
                await process.StandardInput.FlushAsync().WaitAsync(_idleLimit).ConfigureAwait(false);
                bool stopping = false; long stopStarted = 0;
                while (!responseTask.IsCompleted)
                {
                    if (!stopping && (linked.IsCancellationRequested || Stopwatch.GetElapsedTime(Interlocked.Read(ref lastProgress)) >= _idleLimit))
                    {
                        stopping = true; stopStarted = Stopwatch.GetTimestamp();
                        await SendCancelAsync(process).ConfigureAwait(false);
                    }
                    if (stopping && Stopwatch.GetElapsedTime(stopStarted) >= _cancelGrace)
                        throw new IOException(UnknownResult);
                    await Task.WhenAny(responseTask, Task.Delay(25)).ConfigureAwait(false);
                }
                var response = await responseTask.ConfigureAwait(false);
                // 收到结果但进程不退出也不能无限等待；持久化成功的结果保留其真实语义。
                await process.WaitForExitAsync().WaitAsync(_cancelGrace).ConfigureAwait(false);
                if (process.ExitCode != 0) throw new IOException(UnknownResult);
                if (response.Error is not null) ThrowBusinessError(response, linked.Token);
                if (!response.Completed) throw new IOException(UnknownResult);
                return response;
            }
            catch (Exception error) when (process is not null && error is not OperationCanceledException && error is not FileWorkerBusinessException)
            {
                throw new IOException(UnknownResult, error);
            }
            finally
            {
                if (process is not null)
                {
                    // 只终止本次自己创建的请求进程，绝不影响挂载/其他NeeView窗口或系统进程。
                    try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { } catch (System.ComponentModel.Win32Exception) { }
                    try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false); }
                    catch (TimeoutException) { System.Diagnostics.Trace.WriteLine("FileWorker: terminated process still awaiting OS exit."); }
                    catch (InvalidOperationException) { }
                    if (responseTask is not null) _ = ObserveAsync(responseTask);
                    if (stderrTask is not null) _ = ObserveAsync(stderrTask);
                    if (process.HasExited) process.Dispose();
                    else
                    {
                        // OS尚未确认终止时，保留进程与槽的所有权；不能让新写请求和旧原生调用并发。
                        releaseSlot = false; _ = DrainTerminatedAsync(process);
                    }
                }
                if (releaseSlot) Slot.Release();
            }
        }
    }
    private static async Task DrainTerminatedAsync(Process process)
    {
        try { await process.WaitForExitAsync().ConfigureAwait(false); }
        catch (Exception error) { System.Diagnostics.Trace.WriteLine("FileWorker: delayed process wait failed: " + error.GetType().Name); }
        finally { process.Dispose(); Slot.Release(); }
    }
    private static async Task SendCancelAsync(Process process)
    {
        try
        {
            await process.StandardInput.WriteLineAsync("cancel").WaitAsync(TimeSpan.FromMilliseconds(250)).ConfigureAwait(false);
            await process.StandardInput.FlushAsync().WaitAsync(TimeSpan.FromMilliseconds(250)).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or TimeoutException or ObjectDisposedException) { }
    }
    private static async Task ObserveAsync(Task work) { try { await work.ConfigureAwait(false); } catch { } }
    /// <summary>持续排空stderr以免管道堵塞；不在错误中回显可能包含用户路径的任意输出。</summary>
    private static async Task DrainErrorAsync(TextReader reader)
    { var buffer = new char[1024]; while (await reader.ReadAsync(buffer).ConfigureAwait(false) != 0) { } }
    private static async Task<FileWorkerResponse> ReadResponseAsync(TextReader reader, Action progress)
    {
        var lines = new FileWorkerLineReader(reader);
        while (await lines.ReadAsync().ConfigureAwait(false) is { } line)
        {
            var response = JsonSerializer.Deserialize(line, FileWorkerJson.Default.FileWorkerResponse) ?? throw new IOException("文件响应无效。");
            if (response.Progress)
            {
                if (response.Completed || response.Error is not null) throw new IOException("文件响应状态冲突。");
                progress(); continue;
            }
            if (!response.Completed) throw new IOException("文件响应不完整。");
            return response;
        }
        throw new IOException(UnknownResult);
    }
    private static void ThrowBusinessError(FileWorkerResponse response, CancellationToken token)
    {
        if (response.ErrorKind == "canceled") throw new OperationCanceledException(response.Error, token);
        // 业务异常与协议/进程中断分开；保留原调用方的失败提示，未知结果不会当成不存在。
        throw new FileWorkerBusinessException(response.Error!);
    }
    private sealed class FileWorkerBusinessException(string message) : IOException(message);
    private async Task<T> CallAsync<T>(FileWorkerRequest request, Func<FileWorkerResponse, T> result, CancellationToken token)
        => result(await CallAsync(request, token).ConfigureAwait(false));
    private async Task CallVoidAsync(FileWorkerRequest request, CancellationToken token)
        => _ = await CallAsync(request, token).ConfigureAwait(false);
    /// <inheritdoc/>
    public Task<bool> FileExistsAsync(string path, CancellationToken token) => CallAsync(new() { Operation = "exists", Path = path }, r => r.Flag ?? throw new IOException("缺少检查结果。"), token);
    /// <inheritdoc/>
    public Task<FileTransferResult> TransferAsync(FileTransferRequest request, CancellationToken token) => CallAsync(new() { Operation = "transfer", Transfer = request }, r => r.Transfer ?? throw new IOException("缺少传输结果。"), token);
    /// <inheritdoc/>
    public Task ReleaseAsync(FileTransferResult result) => CallVoidAsync(new() { Operation = "release", Result = result }, CancellationToken.None);
    /// <inheritdoc/>
    public Task<IReadOnlyList<string>> RecoverAsync(CancellationToken token = default) => CallAsync<IReadOnlyList<string>>(new() { Operation = "recover" }, r => r.Warnings ?? throw new IOException("缺少恢复结果。"), token);
    /// <inheritdoc/>
    public Task CreateDirectoryAsync(string parent, string name, CancellationToken token) => CallVoidAsync(new() { Operation = "mkdir", Path = parent, Name = name }, token);
    /// <inheritdoc/>
    public Task<BookRenameTarget> GetRenameTargetAsync(string path, CancellationToken token) => CallAsync(new() { Operation = "target", Path = path }, r => r.Target ?? throw new IOException("缺少目标。"), token);
    /// <inheritdoc/>
    public Task<BookRenamePlan> PlanRenameAsync(BookRenameTarget target, string name, CancellationToken token) => CallAsync(new() { Operation = "plan-rename", Target = target, Name = name }, r => r.Rename ?? throw new IOException("缺少改名计划。"), token);
    /// <inheritdoc/>
    public Task RenameAsync(BookRenamePlan plan, CancellationToken token) => CallVoidAsync(new() { Operation = "rename", Rename = plan }, token);
    /// <inheritdoc/>
    public Task<bool?> WasRenamedAsync(BookRenamePlan plan, CancellationToken token) => CallAsync(new() { Operation = "was-renamed", Rename = plan }, r => r.Flag, token);
    /// <inheritdoc/>
    public Task<BookTransferPlan> PlanBookTransferAsync(string source, string folder, CancellationToken token) => CallAsync(new() { Operation = "plan-book", Path = source, Name = folder }, r => r.Book ?? throw new IOException("缺少传输计划。"), token);
    /// <inheritdoc/>
    public Task<BookTransferPlan> PlanPathTransferAsync(string source, string destination, CancellationToken token) => CallAsync(new() { Operation = "plan-path", Path = source, Name = destination }, r => r.Book ?? throw new IOException("缺少传输计划。"), token);
    /// <inheritdoc/>
    public Task<FileTransferResult> TransferBookAsync(BookTransferPlan plan, bool move, CancellationToken token) => CallAsync(new() { Operation = "transfer-book", Book = plan, Move = move }, r => r.Transfer ?? throw new IOException("缺少传输结果。"), token);
    /// <inheritdoc/>
    public Task<bool?> WasBookMovedAsync(BookRenamePlan plan, CancellationToken token) => CallAsync(new() { Operation = "was-moved", Rename = plan }, r => r.Flag, token);
}
