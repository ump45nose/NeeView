// Copyright (c) NeeLaboratory. 原 FolderParameter/FolderParameterMemento；仅将全局集合改为窗口装配的原集合。
using CommunityToolkit.Mvvm.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;
namespace NeeView;

/// <summary>原路径绑定的排序、递归及随机种子；不包含面板或文件系统操作。</summary>
public sealed class FolderParameter : ObservableObject
{
    private readonly FolderConfigCollection _folders;
    private FolderOrder _folderOrder;
    private bool _isFolderRecursive;
    private int _seed;
    /// <summary>恢复该路径原参数；旧随机记录缺种子时补齐并登记到同一集合。</summary>
    public FolderParameter(string path, FolderConfigCollection folders)
    {
        Path = path; _folders = folders;
        var memento = folders.GetFolderParameter(path); Restore(memento);
        if (_seed != memento.Seed) Save();
    }
    public string Path { get; }
    public FolderOrder FolderOrder
    {
        get => _folderOrder;
        set
        {
            if (_folderOrder == value && value != FolderOrder.Random) return;
            _folderOrder = value; _seed = GenerateRandomSeed(value); Save(); OnPropertyChanged();
        }
    }
    public int Seed => _seed;
    public bool IsFolderRecursive
    {
        get => _isFolderRecursive;
        set { if (_isFolderRecursive == value) return; _isFolderRecursive = value; Save(); OnPropertyChanged(); }
    }
    /// <summary>按原默认归一规则登记，实际文件提交由 SaveData 的统一事务完成。</summary>
    public void Save() => _folders.SetFolderParameter(Path, CreateMemento());
    /// <summary>原随机种子规则：非随机为0，缺省随机生成非零值，否则保持原值。</summary>
    private static int GenerateRandomSeed(FolderOrder order, int seed = 0) => order != FolderOrder.Random ? 0 : seed == 0 ? Random.Shared.Next(1, int.MaxValue) : seed;
    /// <summary>沿原来源类别选择默认排序；不改写普通书架的全局默认。</summary>
    public static FolderOrder GetDefaultFolderOrder(string path) => path.StartsWith("bookmark:", StringComparison.OrdinalIgnoreCase)
        ? Config.Current.Bookmark.BookmarkFolderOrder : System.IO.Path.GetExtension(path).Equals(".nvpls", StringComparison.OrdinalIgnoreCase)
            ? Config.Current.Bookshelf.PlaylistFolderOrder : Config.Current.Bookshelf.DefaultFolderOrder;
    /// <summary>递归设置仅在与父级继承值不同时持久化。</summary>
    public FolderParameterMemento CreateMemento() => new()
    {
        FolderOrder = FolderOrder, Seed = Seed,
        IsFolderRecursive = IsFolderRecursive == _folders.GetDefaultFolderRecursive(Path) ? null : IsFolderRecursive
    };
    /// <summary>直接恢复字段，避免属性 setter 重新生成随机种子或登记。</summary>
    public void Restore(FolderParameterMemento memento)
    {
        _folderOrder = memento.FolderOrder ?? GetDefaultFolderOrder(Path);
        _isFolderRecursive = memento.IsFolderRecursive ?? _folders.GetDefaultFolderRecursive(Path);
        _seed = GenerateRandomSeed(_folderOrder, memento.Seed); OnPropertyChanged("");
    }
}

/// <summary>原参数 JSON：保留 nullable 默认省略、枚举数值和未来字段。</summary>
public sealed record FolderParameterMemento
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public FolderOrder? FolderOrder { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public bool? IsFolderRecursive { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public int Seed { get; init; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? ExtensionData { get; init; }
    /// <summary>原 Normalize：与当前类别默认相同的排序不重复保存。</summary>
    public FolderParameterMemento Normalize(string path) => FolderOrder == FolderParameter.GetDefaultFolderOrder(path) ? this with { FolderOrder = null } : this;
    [JsonIgnore] public bool IsDefault => FolderOrder is null && IsFolderRecursive is null && Seed == 0 && ExtensionData is not { Count: > 0 };
}
