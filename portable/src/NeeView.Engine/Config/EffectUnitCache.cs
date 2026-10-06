// Copyright (c) NeeLaboratory. MIT; original non-default effect cache. Corrected ICollection.CopyTo.
using System.Collections;
using NeeView.Effects;
namespace NeeView;
/// <summary>原按类型缓存非默认值；未知类型按其原 discriminator 区分，不能互相覆盖。</summary>
public sealed class EffectUnitCache : ICollection<EffectUnit>
{
    private readonly Dictionary<string, EffectUnit> _values = new(StringComparer.Ordinal);
    private static string Key(EffectUnit value) => value is UnknownEffectUnit u ? "$" + u.TypeName : value.GetType().FullName!;
    public int Count => _values.Count;
    public bool IsReadOnly => false;
    public void Add(EffectUnit? value) { if (value is null) return; if (value.IsDefault) _values.Remove(Key(value)); else _values[Key(value)] = value; }
    public EffectUnit? Get(Type? type) => type is null ? null : _values.GetValueOrDefault(type.FullName!) ?? EffectUnit.CreateInstance(type);
    public void Clear() => _values.Clear();
    public bool Contains(EffectUnit item) => _values.TryGetValue(Key(item), out var value) && value.ValueEquals(item);
    public bool Remove(EffectUnit item) => Contains(item) && _values.Remove(Key(item));
    /// <summary>实际复制缓存到目标数组；原实现错误地把目标数组添加到缓存。</summary>
    public void CopyTo(EffectUnit[] array, int arrayIndex) => _values.Values.CopyTo(array, arrayIndex);
    public IEnumerator<EffectUnit> GetEnumerator() => _values.Values.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
