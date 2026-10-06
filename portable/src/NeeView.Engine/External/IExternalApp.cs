// Copyright (c) NeeLaboratory. 原 IExternalApp 的四字段系统替换契约。
namespace NeeView;
/// <summary>原命令参数和配置应用共用，执行只读取本次捕获副本。</summary>
public interface IExternalApp
{
    string? Command { get; set; }
    string Parameter { get; set; }
    string? WorkingDirectory { get; set; }
    ArchivePolicy ArchivePolicy { get; set; }
}
