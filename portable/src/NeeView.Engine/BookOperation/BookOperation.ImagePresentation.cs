namespace NeeView;

public sealed partial class BookOperation
{
    /// <summary>只通知图像表现；不改变阅读页框、排序或来源。</summary>
    public event EventHandler? ImagePresentationChanged;
    /// <summary>原背景命令沿唯一配置锁/JSON保存，失败恢复原值，成功后才更新画面。</summary>
    /// <param name="type">明确背景值；null 按原枚举循环。</param>
    public async Task SetBackgroundAsync(BackgroundType? type = null)
    {
        await _gate.WaitAsync();
        try
        {
            if (_disposed || _closing) return;
            _saving?.Cancel(); await saveData.SynchronizeWritesAsync();
            var before = Config.Current.Background.BackgroundType;
            try { Config.Current.Background.BackgroundType = type ?? before.GetToggle(); await SaveConfigurationAsync(); }
            catch { Config.Current.Background.BackgroundType = before; throw; }
        }
        finally { _gate.Release(); }
        ImagePresentationChanged?.Invoke(this, EventArgs.Empty);
    }
    /// <summary>原Toggle参数；菜单切换而快捷键服从On/Off。解码规格可能变化，但不重建阅读帧。</summary>
    public async Task ToggleNearestNeighborAsync(bool fromMenu = false)
    {
        await _gate.WaitAsync();
        try
        {
            if (_disposed || _closing || IsLoading) return;
            _saving?.Cancel(); await saveData.SynchronizeWritesAsync();
            var before = Config.Current.ImageDotKeep.IsEnabled;
            try
            {
                Config.Current.ImageDotKeep.IsEnabled = saveData.GetCommandParameter<ToggleCommandParameter>("ToggleNearestNeighbor").GetState(before, fromMenu);
                await SaveConfigurationAsync();
            }
            catch { Config.Current.ImageDotKeep.IsEnabled = before; throw; }
        }
        finally { _gate.Release(); }
        ImagePresentationChanged?.Invoke(this, EventArgs.Empty);
    }
    /// <summary>自定义背景通过既有来源/像素工厂读取，不能由界面直接打开文件。</summary>
    public Task<BitmapLease> LoadBackgroundAsync(BitmapFactory factory, string path, CancellationToken token) => factory.GetBackgroundAsync(path, archives, token);
}
