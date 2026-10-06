using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media.Imaging;
using Avalonia.LogicalTree;
using Avalonia.Controls.Templates;
using System.ComponentModel;
using NeeView.MacOS.ViewModels;
namespace NeeView.MacOS.Views;

/// <summary>原800×650、350参数区/预览/底部按钮布局；持久化与目标选择由唯一宿主接入。</summary>
public sealed partial class ExportImageDialog : Window
{
    private Bitmap? _preview;
    private readonly CancellationTokenSource _closed = new();
    private Task _previewTask = Task.CompletedTask;
    private int _previewRevision;
    private bool _isClosed;
    public Task PendingPreview => _previewTask;
    public ExportImageViewModel Model => (ExportImageViewModel)DataContext!;
    public Func<IExportImageParameter, CancellationToken, Task<byte[]?>>? PreviewAsync { get; set; }
    public ExportImageDialog() { AvaloniaXamlLoader.Load(this); foreach (var combo in this.GetLogicalDescendants().OfType<ComboBox>()) combo.ItemTemplate = new FuncDataTemplate<object>((value, _) => new TextBlock { Text = value is null ? "" : CommandParameterEdit.EnumLabel(value) }); }
    public ExportImageDialog(ExportImageViewModel model) : this()
    {
        DataContext = model; Title = model.IsBook ? "导出书籍" : "导出图像";
        model.Draft.PropertyChanged += Draft_Changed;
    }
    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        _ = RefreshPreviewAsync();
    }
    private void Draft_Changed(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ExportImageParameter.Mode) or nameof(ExportImageParameter.HasBackground)
            or nameof(ExportImageParameter.IsOriginalSize) or nameof(ExportImageParameter.IsDotKeep)
            or nameof(ExportImageParameter.FileFormat) or nameof(ExportImageParameter.QualityLevel))
            _ = RefreshPreviewAsync();
    }
    private async void Preview_Click(object? sender, RoutedEventArgs e) { await RefreshPreviewAsync(); }
    /// <summary>合并连续草稿修改；单槽等待旧编码完成，只有最新版本可以发布。</summary>
    public Task RefreshPreviewAsync()
    {
        if (_isClosed) return _previewTask;
        _previewRevision++;
        this.FindControl<Image>("PreviewImage")!.Source = null;
        _preview?.Dispose(); _preview = null;
        return _previewTask.IsCompleted ? _previewTask = PreviewCoreAsync() : _previewTask;
    }
    private async Task PreviewCoreAsync()
    {
        await Task.Yield();
        while (!_isClosed)
        {
            var revision = _previewRevision;
            try
            {
                if (PreviewAsync is null || _closed.IsCancellationRequested) return;
                var bytes = await PreviewAsync(new ExportImageParameter(Model.Draft), _closed.Token);
                _closed.Token.ThrowIfCancellationRequested();
                if (revision != _previewRevision) continue;
                if (bytes is null) { Model.Error = "当前模式没有可用的预览。"; return; }
                using var stream = new MemoryStream(bytes);
                var image = new Bitmap(stream);
                this.FindControl<Image>("PreviewImage")!.Source = image;
                _preview?.Dispose(); _preview = image; Model.Error = "";
            }
            catch (OperationCanceledException) { }
            catch (Exception error) { if (!_isClosed && revision == _previewRevision) Model.Error = error.Message; }
            if (revision == _previewRevision) return;
        }
    }
    private void Export_Click(object? sender, RoutedEventArgs e) => Close(Model.Draft);
    private void Cancel_Click(object? sender, RoutedEventArgs e) => Close(null);
    protected override void OnClosed(EventArgs e)
    {
        _isClosed = true; Model.Draft.PropertyChanged -= Draft_Changed;
        _closed.Cancel(); this.FindControl<Image>("PreviewImage")!.Source = null; _preview?.Dispose(); _preview = null;
        // 晚到预览在原任务中拒绝发布，令牌在任务真正结束后释放。
        _ = DisposeCancellationAsync(); base.OnClosed(e);
    }
    private async Task DisposeCancellationAsync() { try { await _previewTask; } finally { _closed.Dispose(); } }
}
