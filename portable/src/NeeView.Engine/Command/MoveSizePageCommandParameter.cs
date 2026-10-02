// Copyright (c) NeeLaboratory. 原指定步长参数，移除 WPF 属性注解与生成器。
namespace NeeView;
/// <summary>原默认 10 页，限定 0 至 1000；0 不产生导航。</summary>
public sealed class MoveSizePageCommandParameter
{
    private int _size = 10;
    public int Size { get => _size; set => _size = Math.Clamp(value, 0, 1000); }
}
