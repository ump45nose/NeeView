// Copyright (c) NeeLaboratory. 原 CopyFileCommandParameter 的当前页组策略，基线 c5c398d89。
namespace NeeView;

/// <summary>复制原当前页组；旧弃用字段仍由唯一JSON合并保留。</summary>
public sealed class CopyFileCommandParameter
{
    public MultiPagePolicy MultiPagePolicy { get; set; } = MultiPagePolicy.Once;
}

/// <summary>原 TextCopyPolicy 数值及默认值，不把路径文本当成文件对象。</summary>
public enum TextCopyPolicy { None, CopyFilePath, OriginalPath }
