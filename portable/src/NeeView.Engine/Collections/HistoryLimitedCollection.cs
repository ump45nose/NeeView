// Copyright (c) NeeLaboratory. 原有界环形历史迁入，移除调试生成器，MathUtility.Clamp 替换为 Math.Clamp。
using System.Diagnostics;
namespace NeeView.Collections;

/// <summary>保留原容量、游标及后退后新分支截断规则；T 为原业务记录。</summary>
public sealed class HistoryLimitedCollection<T>
{
    private readonly T?[] _buffer;
    private readonly int _bufferCapacity;
    private int _bufferTop, _bufferSize, _current;
    public event EventHandler? Changed;
    /// <summary>创建固定容量的历史，容量必须为正。</summary>
    public HistoryLimitedCollection(int capacity)
    { ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity); _bufferCapacity = capacity; _buffer = new T?[capacity]; }
    /// <summary>原逻辑索引转换为环形缓冲下标。</summary>
    private int GetRawIndex(int index) => (_bufferTop + index) % _bufferCapacity;
    /// <summary>写入已分配范围内的一项。</summary>
    private void Set(int index, T? value) { Debug.Assert(index >= 0 && index < _bufferSize); _buffer[GetRawIndex(index)] = value; }
    /// <summary>越界返回默认值，保留原游标相对查询语义。</summary>
    private T? Get(int index) => index < 0 || index >= _bufferSize ? default : _buffer[GetRawIndex(index)];
    /// <summary>截断当前游标后的分支再追加；超容量时回收最旧项。</summary>
    public void Add(T? element)
    {
        if (_current != _bufferSize) _bufferSize = _current;
        _bufferSize++;
        if (_bufferSize > _bufferCapacity) { _bufferSize--; _bufferTop = (_bufferTop + 1) % _bufferCapacity; }
        Set(_bufferSize - 1, element); _current = _bufferSize; Changed?.Invoke(this, EventArgs.Empty);
    }
    /// <summary>移除末尾与给定空记录等价的项，避免空内容阻碍后退。</summary>
    public void TrimEnd(T? element)
    {
        while (_bufferSize > 0 && EqualityComparer<T>.Default.Equals(Get(_bufferSize - 1), element)) _bufferSize--;
        if (_current > _bufferSize) _current = _bufferSize;
    }
    /// <summary>移动原游标，范围是 0 至有效项数；1 表示第一项。</summary>
    public void Move(int delta) { _current = Math.Clamp(_current + delta, 0, _bufferSize); Changed?.Invoke(this, EventArgs.Empty); }
    /// <summary>取得当前项。</summary>
    public T? GetCurrent() => Get(_current - 1);
    /// <summary>判断是否存在上一项。</summary>
    public bool CanPrevious() => _current - 2 >= 0;
    /// <summary>取得上一项，不修改游标。</summary>
    public T? GetPrevious() => Get(_current - 2);
    /// <summary>判断是否存在下一项。</summary>
    public bool CanNext() => _current < _bufferSize;
    /// <summary>取得下一项，不修改游标。</summary>
    public T? GetNext() => Get(_current);
    /// <summary>取得原从零开始的历史索引。</summary>
    public T? GetHistory(int index) => Get(index);
    /// <summary>定位原从一开始的游标。</summary>
    public void SetCurrent(int index) { _current = Math.Clamp(index, 0, _bufferSize); Changed?.Invoke(this, EventArgs.Empty); }
    /// <summary>明确文件改名后映射已有记录，保留环形顺序、容量和当前游标。</summary>
    public void ReplaceValues(Func<T?, T?> map)
    { for (int i = 0; i < _bufferSize; i++) Set(i, map(Get(i))); Changed?.Invoke(this, EventArgs.Empty); }
    /// <summary>按方向返回靠近游标的历史，用于前进/后退列表。</summary>
    public List<KeyValuePair<int, T>> GetHistory(int direction, int size)
    {
        var list = new List<KeyValuePair<int, T>>();
        for (int i = 0; i < size; i++)
        {
            var index = direction < 0 ? _current - 2 - i : _current + i;
            if (index < 0 || index >= _bufferSize) break;
            var item = Get(index); if (item is not null) list.Add(new(index, item));
        }
        return list;
    }
}
