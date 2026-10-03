// Copyright (c) NeeLaboratory. 候选值来自原 SettingPageHistory 的两张保留限制表。
using NeeView;
namespace NeeView.MacOS.ViewModels;

/// <summary>历史限制的独立编辑副本；不执行裁剪、访问来源或写入配置。</summary>
public sealed class HistorySettingsViewModel
{
    public IReadOnlyList<HistoryLimitChoice<int>> SizeChoices { get; }
    public IReadOnlyList<HistoryLimitChoice<TimeSpan>> SpanChoices { get; }
    public HistoryLimitChoice<int> SelectedSize { get; set; }
    public HistoryLimitChoice<TimeSpan> SelectedSpan { get; set; }

    /// <summary>沿原候选顺序构造表单，额外保留旧JSON中的任意自定义值。</summary>
    /// <param name="config">已提交的原历史配置；表单编辑不会修改此对象。</param>
    public HistorySettingsViewModel(HistoryConfig config)
    {
        var sizes = new[] { 0, 1, 10, 20, 50, 100, 200, 500, 1000, -1 }.ToList();
        if (!sizes.Contains(config.LimitSize)) sizes.Add(config.LimitSize);
        SizeChoices = sizes.Select(value => new HistoryLimitChoice<int>(value, value == -1 ? "无限制" : value.ToString())).ToArray();
        var spans = new[] { 1, 2, 3, 7, 15, 30, 100, 365 }.Select(days => TimeSpan.FromDays(days)).Append(TimeSpan.Zero).ToList();
        if (!spans.Contains(config.LimitSpan)) spans.Add(config.LimitSpan);
        SpanChoices = spans.Select(value => new HistoryLimitChoice<TimeSpan>(value, value == TimeSpan.Zero ? "无限制"
            : value.Ticks % TimeSpan.TicksPerDay == 0 ? $"{value.TotalDays:0} 天" : value.ToString())).ToArray();
        SelectedSize = SizeChoices.Single(choice => choice.Value == config.LimitSize);
        SelectedSpan = SpanChoices.Single(choice => choice.Value == config.LimitSpan);
    }

    /// <summary>将选择转成保存候选，实际归一、限制和事务由Engine执行。</summary>
    /// <returns>原数量及精确TimeSpan，不用索引或整数天数重解释旧值。</returns>
    public (int Size, TimeSpan Span) GetLimits() => (SelectedSize.Value, SelectedSpan.Value);
}

/// <summary>设置下拉框的值与文案；仅用于表现，不进入Engine或JSON。</summary>
/// <param name="Value">原配置实际值。</param><param name="Label">可独立修改的显示文案。</param>
public sealed record HistoryLimitChoice<T>(T Value, string Label);
