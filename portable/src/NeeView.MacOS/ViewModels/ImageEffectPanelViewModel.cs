// Copyright (c) NeeLaboratory. 原 ImageEffectViewModel 的独立表现适配，事务交给 BookOperation。
using System.Text.Json;
using System.Diagnostics.CodeAnalysis;
namespace NeeView.MacOS.ViewModels;
/// <summary>面板草稿与运行参数隔离；每次明确编辑经同一设置事务，失败重载原值。</summary>
public sealed class ImageEffectPanelViewModel : IDisposable
{
    private readonly BookOperation _operation;
    private bool _busy, _disposed;
    public EffectProfile Draft { get; private set; } = new();
    public EffectUnitCache Cache { get; private set; } = new();
    public IReadOnlyList<EffectProfile> Profiles => Config.Current.EffectProfiles.Profiles.Order().ToArray();
    public int SelectedId => Config.Current.BookSetting.EffectProfileId;
    public event EventHandler? Changed;
    public event EventHandler<string>? Failed;
    public ImageEffectPanelViewModel(BookOperation operation) { _operation = operation; operation.Changed += OperationChanged; Refresh(); }
    private void OperationChanged(object? sender, EventArgs args) { if (!_busy) Refresh(); }
    private string? _stamp;
    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "正式Mac项目LinkMode=None保留原效果参数/JSON反射；快照只含固定六分支、缓存与预设身份，不启用AOT裁剪。")]
    private void Refresh(bool force = false)
    {
        if (_disposed) return;
        var next = JsonSerializer.Serialize(new { Config.Current.ImageCustomSize, Config.Current.ImageTrim, Config.Current.ImageDotKeep, Config.Current.ImageResizeFilter,
            Config.Current.ImageGrid, Config.Current.ImageEffect, Id = Config.Current.BookSetting.EffectProfileId,
            Profiles = Config.Current.EffectProfiles.Profiles.Select(x => new { x.Id, x.Name }) });
        if (!force && _stamp == next) return; _stamp = next;
        Draft = new(); Draft.Store(Config.Current);
        Cache = JsonSerializer.Deserialize<EffectUnitCache>(JsonSerializer.Serialize(Config.Current.ImageEffectCache))!;
        Changed?.Invoke(this, EventArgs.Empty);
    }
    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "正式Mac项目LinkMode=None保留原EffectUnit固定多态类型及未知JSON材料；克隆只在编辑事务执行。")]
    public Task EditAsync(Action<EffectProfile, EffectUnitCache> edit) => RunAsync(async () =>
    {
        edit(Draft, Cache);
        await _operation.EditImageOptionsAsync(config => { Draft.Restore(config); config.ImageEffectCache = JsonSerializer.Deserialize<EffectUnitCache>(JsonSerializer.Serialize(Cache))!; });
    });
    public Task SelectAsync(int id) => RunAsync(() => _operation.SetEffectProfileAsync(id));
    public Task CreateAsync(bool clone) => RunAsync(() => _operation.EditImageOptionsAsync(config => new EffectProfileCollection(config).CreateNew(clone)));
    public Task RenameAsync(string name) => RunAsync(() => _operation.EditImageOptionsAsync(config => { var profiles = new EffectProfileCollection(config); profiles.Rename(profiles.SelectedProfile, name); }));
    public Task DeleteAsync() => RunAsync(() => _operation.EditImageOptionsAsync(config => { var profiles = new EffectProfileCollection(config); profiles.Delete(profiles.SelectedProfile); }));
    private async Task RunAsync(Func<Task> action)
    {
        if (_disposed || _busy) return; _busy = true;
        try { await action(); } catch (Exception ex) { Failed?.Invoke(this, ex.Message); }
        finally { _busy = false; Refresh(true); }
    }
    public void Dispose() { if (_disposed) return; _disposed = true; _operation.Changed -= OperationChanged; }
}
