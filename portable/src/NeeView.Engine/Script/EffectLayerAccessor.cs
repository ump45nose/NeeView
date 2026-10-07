// Copyright (c) NeeLaboratory. 原脚本效果访问器，MIT。
using NeeView.Effects;
namespace NeeView;
public sealed class EffectLayerAccessor(ScriptAccessContext context, EffectLayerCollection layers, EffectLayer source)
{
    public bool IsEnabled { get => context.Read(() => source.IsEnabled); set => context.Write(() => source.IsEnabled = value); }
    public string EffectType { get => context.Read(() => source.EffectType.ToString()); set => context.Write(() => source.ChangeType(ScriptEnum.Parse<EffectType>(value), Config.Current.ImageEffect, Config.Current.ImageEffectCache)); }
    public PropertyMap? Effect => context.Read(() => source.Effect is { } effect ? new PropertyMap("nv.ImageEffect.Layers.Effect", null, null, effect, context.Diagnostics, "", PropertyMapOptions.Create(context.Dispatcher, p => p.Name is "ExtensionData" or "Extra")) : null);
    public void Remove() => context.Write(() => layers.Delete(source, Config.Current.ImageEffectCache));
    public void MoveUp() => context.Write(() => layers.MoveUp(source));
    public void MoveDown() => context.Write(() => layers.MoveDown(source));
    public void Reset() => context.Write(source.Reset);
}
