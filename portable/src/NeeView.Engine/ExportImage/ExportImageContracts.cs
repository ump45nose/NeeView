// Copyright (c) NeeLaboratory. 原导出链的可等待替换点；不含控件、位图或平台类型。
using NeeView.PageFrames;
namespace NeeView;

/// <summary>原覆盖对话框的三个实际结果。</summary>
public enum ExportOverwriteAnswer { Cancel, Replace, AddNumber }
/// <summary>实际完成的目标及输出数；文件夹取消/失败已完成项由异常报告，不伪称整批回滚。</summary>
public sealed record ExportImageResult(string Path, int Count, IReadOnlyList<string>? SkippedDirectories = null);
/// <summary>文件夹已提交的输出保留；失败或取消报告真实完成数。</summary>
public sealed class ExportPartialException(string path, int completedCount, Exception error) : IOException($"已导出{completedCount}项到{path}；其余" + (error is OperationCanceledException ? "已取消。" : "失败：" + error.Message), error)
{ public int CompletedCount => completedCount; public bool Canceled => InnerException is OperationCanceledException; }
/// <summary>唯一查看器将已加载原页框绘制到请求级输出；资源归展示端管理。</summary>
public interface IViewImageExporter
{
    /// <param name="frame">当前导航锁保护的原页框。</param><param name="options">独立导出参数快照。</param>
    /// <param name="stream">调用方持有的目标流。</param><param name="token">等待/绘制前后取消，原生编码可能不能即时中断。</param>
    Task ExportViewAsync(PageFrame frame, IExportImageParameter options, Stream stream, CancellationToken token);
}
/// <summary>原直接导出参数与默认提示；FileFormat无原初始化，必须保持Jpeg=0。</summary>
public sealed class ExportImageCommandParameter : ExportImageParameter
{
    public ExportImageCommandParameter() { FileFormat = BitmapImageFormat.Jpeg; }
    public bool IsShowToast { get; set; } = true;
}
/// <summary>原废弃ExportImageAs参数只读兼容，实际运行沿Config.Book。</summary>
public sealed class ExportImageAsCommandParameter
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? ExportFolder { get => null; set => ExportFolderLegacy = value; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public int? QualityLevel { get => null; set => QualityLevelLegacy = value ?? 0; }
    [System.Text.Json.Serialization.JsonIgnore] public string? ExportFolderLegacy { get; private set; }
    [System.Text.Json.Serialization.JsonIgnore] public int QualityLevelLegacy { get; private set; }
}
