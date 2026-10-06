// Copyright (c) NeeLaboratory. 原 ExportBackupCommandParameter，移除 WPF 文件对话框属性。
namespace NeeView;
/// <summary>保留原备份命令文件名；空白时由表现端选择 .nvzip，不保存对话框状态。</summary>
public sealed class ExportBackupCommandParameter
{
    public string FileName { get; set; } = "";
}
