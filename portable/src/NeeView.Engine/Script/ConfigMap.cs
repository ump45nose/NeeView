namespace NeeView;

/// <summary>Script-facing configuration map. The map is backed by the live Config instance.</summary>
public sealed class ConfigMap
{
    public ConfigMap(Config config, PropertyMapOptions? options = null, IAccessDiagnostics? accessDiagnostics = null)
    {
        ArgumentNullException.ThrowIfNull(config);
        Map = new PropertyMap("nv.Config", null, null, config, accessDiagnostics, "", options);
    }

    public PropertyMap Map { get; }
}
