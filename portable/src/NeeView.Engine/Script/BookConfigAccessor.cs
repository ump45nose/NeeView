namespace NeeView;
public sealed class BookConfigAccessor(ScriptAccessContext context)
{
    private BookSettingConfig Setting => context.Read(() => context.Operation.Book?.Setting ?? Config.Current.BookSetting);
    public int ViewPageSize { get => (int)context.Read(() => Setting.PageMode) + 1; set
        { context.Run(() => context.Operation.ApplySettingAsync(s => s.PageMode = (PageMode)Math.Clamp(value - 1, 0, 1))); } }
    public string BookReadOrder { get => context.Read(() => Setting.BookReadOrder.ToString()); set => context.Run(() => context.Operation.ApplySettingAsync(s => s.BookReadOrder = ScriptEnum.Parse<PageReadOrder>(value))); }
    public bool IsSupportedDividePage { get => context.Read(() => Setting.IsSupportedDividePage); set => context.Run(() => context.Operation.ApplySettingAsync(s => s.IsSupportedDividePage = value)); }
    public bool IsSupportedSingleFirstPage { get => context.Read(() => Setting.IsSupportedSingleFirstPage); set => context.Run(() => context.Operation.ApplySettingAsync(s => s.IsSupportedSingleFirstPage = value)); }
    public bool IsSupportedSingleLastPage { get => context.Read(() => Setting.IsSupportedSingleLastPage); set => context.Run(() => context.Operation.ApplySettingAsync(s => s.IsSupportedSingleLastPage = value)); }
    public bool IsSupportedWidePage { get => context.Read(() => Setting.IsSupportedWidePage); set => context.Run(() => context.Operation.ApplySettingAsync(s => s.IsSupportedWidePage = value)); }
    public bool IsRecursiveFolder { get => context.Read(() => Setting.IsRecursiveFolder); set => context.Run(() => context.Operation.ApplySettingAsync(s => s.IsRecursiveFolder = value)); }
    public string SortMode { get => context.Read(() => Setting.SortMode.ToString()); set => context.Run(() => context.Operation.ApplySettingAsync(s => s.SortMode = ScriptEnum.Parse<PageSortMode>(value))); }
    public string AutoRotate { get => context.Read(() => Setting.AutoRotate.ToString()); set => context.Run(() => context.Operation.ApplySettingAsync(s => s.AutoRotate = ScriptEnum.Parse<AutoRotateType>(value))); }
    public double BaseScale { get => context.Read(() => Setting.BaseScale); set => context.Run(() => context.Operation.ApplySettingAsync(s => s.BaseScale = value)); }
    public int EffectProfileId { get => context.Read(() => Setting.EffectProfileId); set => context.Run(() => context.Operation.ApplySettingAsync(s => s.EffectProfileId = value)); }
}
