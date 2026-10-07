// Copyright (c) NeeLaboratory. 原脚本效果访问器，MIT。
using System.Linq;
namespace NeeView;
public sealed class ImageEffectAccessor(ScriptAccessContext context)
{
    public bool IsEnabled { get => context.Read(() => Config.Current.ImageEffect.IsEnabled); set => context.Write(() => Config.Current.ImageEffect.IsEnabled = value); }
    public EffectLayerAccessor[] Layers => context.Read(() => Config.Current.ImageEffect.Layers.Select(x => new EffectLayerAccessor(context, Config.Current.ImageEffect.Layers, x)).ToArray());
    public EffectLayerAccessor? CreateNew() => context.Write(() => { var layers = Config.Current.ImageEffect.Layers; var x = layers.CreateNew(); return x is null ? null : new EffectLayerAccessor(context, layers, x); });
}
