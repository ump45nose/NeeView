using CommunityToolkit.Mvvm.ComponentModel;
namespace NeeView.MacOS.ViewModels;
/// <summary>独立脚本表单草稿；取消不更新扫描目录或执行任何脚本。</summary>
public sealed partial class ScriptSettingsViewModel : ObservableObject, IDisposable
{
    public ScriptSettingsViewModel(ScriptConfig source)
    { _enabled = source.IsScriptFolderEnabled; _folder = source.ScriptFolder; _errorLevel = source.ErrorLevel; _renamed = source.OnBookLoadedWhenRenamed; }
    [ObservableProperty] private bool _enabled;
    [ObservableProperty] private string _folder;
    [ObservableProperty] private ScriptErrorLevel _errorLevel;
    [ObservableProperty] private bool _renamed;
    [ObservableProperty] private string _message = "";
    private bool _disposed;
    private CancellationTokenSource? _folderCancellation;
    /// <summary>打开当前目录草稿，失败只显示消息，不保存或应用开关。</summary>
    public async Task OpenFolderAsync(IPlatformService platform)
    {
        if (_disposed || _folderCancellation is not null) return;
        using var cancel = new CancellationTokenSource(); _folderCancellation = cancel;
        try { var path = await ScriptFolderService.PrepareAsync(Folder, cancel.Token); cancel.Token.ThrowIfCancellationRequested(); await platform.OpenFolderAsync(path, cancel.Token); }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested) { }
        catch (Exception ex) { if (!_disposed) Message = ex.Message; }
        finally { _folderCancellation = null; }
    }
    public void Dispose() { _disposed = true; _folderCancellation?.Cancel(); }
    public IReadOnlyList<ScriptErrorLevel> ErrorLevels { get; } = Enum.GetValues<ScriptErrorLevel>();
    public void Apply(ScriptConfig target)
    { target.ScriptFolder = Folder; target.IsScriptFolderEnabled = Enabled; target.ErrorLevel = ErrorLevel; target.OnBookLoadedWhenRenamed = Renamed; }
}
