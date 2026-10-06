// Copyright (c) NeeLaboratory. 原效果命令与预设控制，保存复用唯一配置事务。
namespace NeeView;
public sealed partial class BookOperation
{
    public Task EditImageOptionsAsync(Action<Config> edit) => ApplyOptionsAsync(() => edit(Config.Current), (Config.Current.History.LimitSize, Config.Current.History.LimitSpan));
    public Task SetEffectProfileCommandAsync() => SetEffectProfileAsync(saveData.GetCommandParameter<SetEffectProfileCommandParameter>("SetEffectProfile").Id);
    public Task SetEffectProfileAsync(int id) => EditImageOptionsAsync(config => new EffectProfileCollection(config).SetSelectedId(id));
    public Task MoveEffectProfileAsync(int offset) => EditImageOptionsAsync(config => { var profiles = new EffectProfileCollection(config); profiles.SetSelectedId(profiles.GetNext(offset).Id); });
    /// <summary>原 ToggleCommandParameter 仍按各命令 owner 读取，不因平台更换键位重解释参数。</summary>
    public Task ToggleImageOptionAsync(string command, bool fromMenu = false) => EditImageOptionsAsync(config =>
    {
        var parameter = saveData.GetCommandParameter<ToggleCommandParameter>(command);
        switch (command)
        {
            case "ToggleCustomSize": config.ImageCustomSize.IsEnabled = parameter.GetState(config.ImageCustomSize.IsEnabled, fromMenu); break;
            case "ToggleTrim": config.ImageTrim.IsEnabled = parameter.GetState(config.ImageTrim.IsEnabled, fromMenu); break;
            case "ToggleGrid": config.ImageGrid.IsEnabled = parameter.GetState(config.ImageGrid.IsEnabled, fromMenu); break;
            case "ToggleEffect": config.ImageEffect.IsEnabled = parameter.GetState(config.ImageEffect.IsEnabled, fromMenu); break;
            case "ToggleResizeFilter": config.ImageResizeFilter.IsEnabled = parameter.GetState(config.ImageResizeFilter.IsEnabled, fromMenu); break;
        }
    });
}
