using System.Text.Json;
using System.Text.Json.Serialization;
using NeeView;
namespace NeeView.Backends;

/// <summary>同一正式可执行文件的无界面文件请求模式；复用原事务，未另建传输内核。</summary>
public static class FileOperationWorkerHost
{
    public const string Argument = "--neeview-file-worker";
    internal const int MaximumMessage = 256 * 1024;

    /// <summary>只从标准输入接收一个请求，后续cancel/EOF取消；结果与进展通过独立JSON行返回。</summary>
    /// <returns>协议/未处理失败返回非零；业务失败仍返回可解释的结果，不删除恢复材料。</returns>
    public static async Task<int> RunAsync(TextReader input, TextWriter output)
    {
        using var lifetime = new CancellationTokenSource();
        var lines = new FileWorkerLineReader(input);
        string? first;
        try { first = await lines.ReadAsync(); } catch (IOException) { return 2; }
        if (first is null || first.Length > MaximumMessage) return 2;
        FileWorkerRequest request;
        try { request = JsonSerializer.Deserialize(first, FileWorkerJson.Default.FileWorkerRequest) ?? throw new IOException("无效文件请求。"); }
        catch (Exception error) when (error is JsonException or IOException) { return 2; }
        var last = long.MinValue; var sync = new object();
        void Write(FileWorkerResponse response)
        {
            var line = JsonSerializer.Serialize(response, FileWorkerJson.Default.FileWorkerResponse);
            if (line.Length > MaximumMessage) throw new IOException("文件请求响应超过限制。");
            lock (sync) { output.WriteLine(line); output.Flush(); }
        }
        using var progress = FileOperationProgress.Observe(() =>
        {
            var now = Environment.TickCount64;
            lock (sync) { if (last != long.MinValue && now - last < 200) return; last = now; Write(new() { Progress = true }); }
        });
        // 监听不阻止退出；进程退出后才结束stdin读，CTS晚到取消安全地忽略释放。
        // Console.In的同步包装会阻塞ReadAsync本身；独立监听线程不能阻止事务开始。
        var monitoring = Task.Run(() => MonitorCancellationAsync(lines, lifetime));
        try
        {
            var backend = new FileOperationBackend(request.RecoveryDirectory);
            var response = await ExecuteAsync(backend, request, lifetime.Token);
            response.Completed = true; Write(response); return 0;
        }
        catch (Exception error)
        {
            Write(new() { Completed = true, Error = error.Message, ErrorKind = error is OperationCanceledException ? "canceled" : error is FileNotFoundException ? "missing" : error is UnauthorizedAccessException ? "permission" : error is ArgumentException ? "argument" : error is TimeoutException ? "timeout" : "io" });
            return 0;
        }
        finally
        {
            // 不等待仍持有stdin的父进程；子进程退出结束监听，不捕获正式窗口或平台对象。
            _ = monitoring;
        }
    }
    private static async Task MonitorCancellationAsync(FileWorkerLineReader input, CancellationTokenSource lifetime)
    {
        try { var line = await input.ReadAsync(); if (line is null || line == "cancel") lifetime.Cancel(); }
        catch (ObjectDisposedException) { }
        catch (IOException) { try { lifetime.Cancel(); } catch (ObjectDisposedException) { } }
    }
    private static T Required<T>(T? value) where T : class => value ?? throw new ArgumentException("文件请求缺少参数。");
    private static async Task<FileWorkerResponse> ExecuteAsync(FileOperationBackend backend, FileWorkerRequest r, CancellationToken token)
    {
        FileOperationProgress.Report();
        switch (r.Operation)
        {
            case "exists": return new() { Flag = await backend.FileExistsAsync(r.Path, token) };
            case "transfer": return new() { Transfer = await backend.TransferAsync(Required(r.Transfer), token) };
            case "release": await backend.ReleaseAsync(Required(r.Result)); return new();
            case "recover": return new() { Warnings = (await backend.RecoverAsync(token)).ToArray() };
            case "mkdir": await backend.CreateDirectoryAsync(r.Path, r.Name, token); return new();
            case "target": return new() { Target = await backend.GetRenameTargetAsync(r.Path, token) };
            case "plan-rename": return new() { Rename = await backend.PlanRenameAsync(Required(r.Target), r.Name, token) };
            case "rename": await backend.RenameAsync(Required(r.Rename), token); return new();
            case "was-renamed": return new() { Flag = await backend.WasRenamedAsync(Required(r.Rename), token) };
            case "plan-book": return new() { Book = await backend.PlanBookTransferAsync(r.Path, r.Name, token) };
            case "plan-path": return new() { Book = await backend.PlanPathTransferAsync(r.Path, r.Name, token) };
            case "transfer-book": return new() { Transfer = await backend.TransferBookAsync(Required(r.Book), r.Move, token) };
            case "was-moved": return new() { Flag = await backend.WasBookMovedAsync(Required(r.Rename), token) };
            default: throw new ArgumentException("未知文件请求。");
        }
    }
}

/// <summary>固定操作协议，只携带原文件契约；路径和配置不进入命令行或Shell。</summary>
internal sealed class FileWorkerRequest
{
    public string RecoveryDirectory { get; set; } = "";
    public string Operation { get; set; } = "";
    public string Path { get; set; } = "";
    public string Name { get; set; } = "";
    public bool Move { get; set; }
    public FileTransferRequest? Transfer { get; set; }
    public FileTransferResult? Result { get; set; }
    public BookRenameTarget? Target { get; set; }
    public BookRenamePlan? Rename { get; set; }
    public BookTransferPlan? Book { get; set; }
}
internal sealed class FileWorkerResponse
{
    public bool Progress { get; set; }
    public bool Completed { get; set; }
    public string? Error { get; set; }
    public string? ErrorKind { get; set; }
    public bool? Flag { get; set; }
    public FileTransferResult? Transfer { get; set; }
    public BookRenameTarget? Target { get; set; }
    public BookRenamePlan? Rename { get; set; }
    public BookTransferPlan? Book { get; set; }
    public string[]? Warnings { get; set; }

}
[JsonSerializable(typeof(FileWorkerRequest))]
[JsonSerializable(typeof(FileWorkerResponse))]
internal sealed partial class FileWorkerJson : JsonSerializerContext { }

/// <summary>有界JSON行读取；先限制内存再解析，不等待任意长度ReadLine分配。</summary>
internal sealed class FileWorkerLineReader(TextReader reader)
{
    // TextReader（尤其Console.In）可能等待填满请求；逐字符读取只在行边界等待，不吞掉进展/取消。
    private readonly char[] _buffer = new char[1];
    private int _position, _count;
    public async Task<string?> ReadAsync()
    {
        var line = new System.Text.StringBuilder();
        while (true)
        {
            if (_position == _count)
            {
                _count = await reader.ReadAsync(_buffer).ConfigureAwait(false); _position = 0;
                if (_count == 0) return line.Length == 0 ? null : line.ToString();
            }
            int end = Array.IndexOf(_buffer, '\n', _position, _count - _position);
            int length = (end < 0 ? _count : end) - _position;
            if (line.Length + length > FileOperationWorkerHost.MaximumMessage) throw new IOException("文件协议消息超过限制。");
            line.Append(_buffer, _position, length); _position += length;
            if (end >= 0) { _position++; return line.ToString().TrimEnd('\r'); }
        }
    }
}
