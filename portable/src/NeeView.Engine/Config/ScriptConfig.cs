// Copyright (c) NeeLaboratory.
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;
namespace NeeView;

/// <summary>脚本开关、错误级别及脚本目录；Raw 字段保持原 JSON 目录值。</summary>
public sealed class ScriptConfig : ObservableObject
{
    private string? _scriptFolder;
    private bool _enabled, _onBookLoadedWhenRenamed = true, _sqliteEnabled;
    private ScriptErrorLevel _errorLevel = ScriptErrorLevel.Error;
    [JsonIgnore] internal string DefaultFolder { get; set; } = "";
    public bool IsScriptFolderEnabled { get => _enabled; set => SetProperty(ref _enabled, value); }
    [JsonIgnore] public string ScriptFolder
    {
        get => _scriptFolder ?? DefaultFolder;
        set { if (SetProperty(ref _scriptFolder, string.IsNullOrWhiteSpace(value) || value.Trim() == DefaultFolder ? null : value.Trim())) OnPropertyChanged(nameof(ScriptFolderRaw)); }
    }
    [PropertyMapIgnore, JsonPropertyName("ScriptFolder")] public string? ScriptFolderRaw
    {
        get => _scriptFolder;
        set { if (SetProperty(ref _scriptFolder, value)) OnPropertyChanged(nameof(ScriptFolder)); }
    }
    public ScriptErrorLevel ErrorLevel { get => _errorLevel; set => SetProperty(ref _errorLevel, value); }
    public bool OnBookLoadedWhenRenamed { get => _onBookLoadedWhenRenamed; set => SetProperty(ref _onBookLoadedWhenRenamed, value); }
    /// <summary>保留原Windows SQLite程序集开关；Mac脚本不会装载该专属后端。</summary>
    public bool IsSQLiteEnabled { get => _sqliteEnabled; set => SetProperty(ref _sqliteEnabled, value); }
}
