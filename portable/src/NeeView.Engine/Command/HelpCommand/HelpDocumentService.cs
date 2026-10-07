namespace NeeView;

public enum HelpDocumentKind { MainMenu, SearchOptions, Script }

/// <summary>只管理随机临时帮助目录和平台打开；不保存Profile、不执行网页脚本。</summary>
public sealed class HelpDocumentService(IPlatformService platform, string? temporaryRoot = null) : IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1);
    private readonly string _directory = Path.Combine(temporaryRoot ?? Path.Combine(Path.GetTempPath(), "NeeView.Mac", "Help"), Guid.NewGuid().ToString("N"));
    private bool _disposed;
    /// <summary>后台原子写入固定原文件名，再提交唯一平台file URI打开；失败传播，可重试。</summary>
    /// <param name="kind">原两种帮助文档。</param><param name="html">纯生成器输出，不包含用户文件内容。</param>
    /// <param name="token">窗口关闭或请求取消；已提交系统动作等待真实结果。</param><returns>系统打开入口已接受文档。</returns>
    public async Task OpenAsync(HelpDocumentKind kind, string html, CancellationToken token = default)
    {
        var filename = kind switch { HelpDocumentKind.MainMenu => "MainMenuList.html", HelpDocumentKind.SearchOptions => "SearchOptions.html", HelpDocumentKind.Script => "ScriptManual.html", _ => throw new ArgumentOutOfRangeException(nameof(kind)) };
        await _gate.WaitAsync(token);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this); token.ThrowIfCancellationRequested();
            var path = Path.Combine(_directory, filename); var temporary = path + ".tmp";
            try
            {
                await Task.Run(async () =>
                {
                    token.ThrowIfCancellationRequested(); Directory.CreateDirectory(_directory);
                    await File.WriteAllTextAsync(temporary, html, new System.Text.UTF8Encoding(false), token);
                    token.ThrowIfCancellationRequested(); File.Move(temporary, path, overwrite: true);
                }, token);
                token.ThrowIfCancellationRequested(); await platform.OpenUriAsync(new Uri(path), token);
            }
            finally
            {
                // 临时清理失败不能覆盖真实写入或系统打开错误；关闭时还会清理独占目录。
                try { File.Delete(temporary); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
        finally { _gate.Release(); }
    }
    /// <summary>成功关闭后等待在途打开，只清理本实例生成的随机目录。</summary>
    public async ValueTask DisposeAsync()
    {
        await _gate.WaitAsync();
        try { if (_disposed) return; if (Directory.Exists(_directory)) Directory.Delete(_directory, true); _disposed = true; }
        finally { _gate.Release(); }
    }
}
