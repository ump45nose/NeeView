// Copyright (c) NeeLaboratory. MIT; original selection/store/restore/name/ID rules.
using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using System.Text.Json;
using System.Text.Json.Serialization;
namespace NeeView;
public sealed class EffectProfileCollectionConfig
{
    [PropertyMapIgnore] public int IdCounter { get; set; }
    [PropertyMapIgnore] public ObservableCollection<EffectProfile> Profiles { get; set; } = new() { new() };
    [JsonExtensionData] [PropertyMapIgnore] public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}
/// <summary>保留原预设控制关系；由 BookOperation 串行调用，没有第二会话或全局事件总线。</summary>
public sealed class EffectProfileCollection(Config config)
{
    public ObservableCollection<EffectProfile> Profiles => config.EffectProfiles.Profiles;
    public EffectProfile SelectedProfile => Resolve(config.BookSetting.EffectProfileId);
    public void Store() => SelectedProfile.Store(config);
    public void Restore() { var profile = SelectedProfile; profile.Restore(config); config.BookSetting.EffectProfileId = profile.Id; }
    private EffectProfile Resolve(int id)
    {
        if (Profiles.Count == 0) Profiles.Add(new());
        return Profiles.FirstOrDefault(x => x.Id == id) ?? Profiles[0];
    }
    /// <summary>原切换先 Store 旧预设，再 Restore 新预设；缺失指定 ID 不改变当前设置。</summary>
    public bool SetSelectedId(int id)
    {
        var next = Profiles.FirstOrDefault(x => x.Id == id); if (next is null) return false;
        Store(); next.Restore(config); config.BookSetting.EffectProfileId = next.Id; return true;
    }
    public EffectProfile GetNext(int offset) => Profiles[(Profiles.IndexOf(SelectedProfile) + offset + Profiles.Count) % Profiles.Count];
    public EffectProfile CreateNew(bool clone = false)
    {
        Store(); int id = config.EffectProfiles.IdCounter;
        do { id = checked(id + 1); } while (Profiles.Any(x => x.Id == id));
        var profile = new EffectProfile { Id = id, Name = UniqueName(clone ? SelectedProfile.DisplayName : "预设 1") };
        if (clone) profile.Store(config);
        Profiles.Add(profile); profile.Restore(config); config.BookSetting.EffectProfileId = id; config.EffectProfiles.IdCounter = id % 0xFFFF; return profile;
    }
    public bool CanDelete(EffectProfile? profile) => profile is { Id: not 0 } && Profiles.Count > 1 && Profiles.Contains(profile);
    public void Delete(EffectProfile profile)
    {
        if (!CanDelete(profile)) return;
        if (ReferenceEquals(profile, SelectedProfile))
        { int index = Profiles.IndexOf(profile); SetSelectedId(Profiles[index < Profiles.Count - 1 ? index + 1 : index - 1].Id); }
        Profiles.Remove(profile);
    }
    public void Rename(EffectProfile profile, string name) { if (profile.Id != 0 && Profiles.Contains(profile)) profile.Name = UniqueName(name); }
    private string UniqueName(string name)
    {
        if (Profiles.All(x => x.DisplayName != name)) return name;
        var match = Regex.Match(name, @"^(.+) (\d+)$"); var body = match.Success ? match.Groups[1].Value : name;
        int count = match.Success && int.TryParse(match.Groups[2].Value, out var n) ? n : 1;
        do { name = $"{body} {++count}"; } while (Profiles.Any(x => x.DisplayName == name)); return name;
    }
}
