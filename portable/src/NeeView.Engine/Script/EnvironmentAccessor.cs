// Copyright (c) NeeLaboratory. 原环境路径的独立Mac替换。
namespace NeeView;
public sealed class EnvironmentAccessor(SaveData state, IAccessDiagnostics? diagnostics = null)
{
    public string NeeViewPath => System.Environment.ProcessPath ?? "";
    public string UserSettingFilePath => Path.Combine(state.DirectoryPath, "UserSetting.json");
    public string PackageType => "MacOS";
    public string ReleaseType => "Release";
    public string Version => typeof(EnvironmentAccessor).Assembly.GetName().Version?.ToString() ?? "";
    public string ProductVersion => Version;
    public string Revision => typeof(EnvironmentAccessor).Assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false).OfType<System.Reflection.AssemblyInformationalVersionAttribute>().FirstOrDefault()?.InformationalVersion ?? Version;
    public bool SelfContained => Directory.Exists(Path.Combine(AppContext.BaseDirectory, "..", "MonoBundle"));
    public string OSVersion => System.Environment.OSVersion.VersionString;
    public string UserAgent => $"NeeView/{Version}";
    [Obsolete("no used")] public string DateVersion { get { diagnostics?.Throw(new NotSupportedException("DateVersion已废弃。"), ScriptErrorLevel.Warning); return ""; } }
}
