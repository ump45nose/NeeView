// Copyright (c) NeeLaboratory. 原 ExternalAppCollection。
namespace NeeView;
/// <summary>原有序配置集合，索引命令使用一开始编号。</summary>
public sealed class ExternalAppCollection : List<ExternalApp>, IEquatable<ExternalAppCollection>
{
    public ExternalAppCollection() { }
    public ExternalAppCollection(IEnumerable<ExternalApp> collection) : base(collection) { }
    /// <summary>按原默认值追加配置并返回同一实例。</summary>
    public ExternalApp CreateNew() { var value = new ExternalApp(); Add(value); return value; }
    /// <summary>验证内部零开始索引；原命令参数由调用方减一。</summary>
    public bool IsValidIndex(int index) => index >= 0 && index < Count;
    public bool Equals(ExternalAppCollection? other) => other is not null && this.SequenceEqual(other);
    public override bool Equals(object? other) => Equals(other as ExternalAppCollection);
    public override int GetHashCode() { var hash = new HashCode(); foreach (var item in this) hash.Add(item); return hash.ToHashCode(); }
}
