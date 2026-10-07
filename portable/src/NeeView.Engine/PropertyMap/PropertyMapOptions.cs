// Copyright (c) NeeLaboratory.
using System.Reflection;
namespace NeeView;

/// <summary>原转换器顺序与实际线程替换点；不包含窗口或控件。</summary>
public sealed class PropertyMapOptions
{
    public IList<PropertyMapConverter> Converters { get; } = [];
    public IPropertyMapDispatcher Dispatcher { get; init; } = new InlinePropertyMapDispatcher();
    public Func<PropertyInfo, bool>? ExcludeProperty { get; init; }
    /// <summary>创建原六类转换器的已使用纯值子集。坐标无需通用WPF类型模拟。</summary>
    public static PropertyMapOptions Create(IPropertyMapDispatcher dispatcher, Func<PropertyInfo, bool>? exclude = null)
    {
        var options = new PropertyMapOptions { Dispatcher = dispatcher, ExcludeProperty = exclude };
        options.Converters.Add(new PropertyMapEnumConverter());
        options.Converters.Add(new PropertyMapSizeConverter());
        options.Converters.Add(new PropertyMapColorConverter());
        options.Converters.Add(new PropertyMapFileTypeCollectionConverter());
        options.Converters.Add(new PropertyMapStringCollectionConverter());
        return options;
    }
}
/// <summary>同步脚本的属性访问边界，桌面实现将操作转交真实UI调度器。</summary>
public interface IPropertyMapDispatcher
{
    bool CheckAccess();
    void Invoke(Action action);
    T Invoke<T>(Func<T> action);
}
/// <summary>纯模型默认不要求UI线程；产品装配显式注入真实调度器。</summary>
public sealed class InlinePropertyMapDispatcher : IPropertyMapDispatcher
{
    public bool CheckAccess() => true;
    public void Invoke(Action action) => action();
    public T Invoke<T>(Func<T> action) => action();
}
