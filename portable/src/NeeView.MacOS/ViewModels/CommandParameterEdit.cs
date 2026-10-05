using System.Reflection;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;
using NeeView;
namespace NeeView.MacOS.ViewModels;

/// <summary>原命令参数的独立编辑草稿；不写配置，不包含控件。</summary>
public sealed class CommandParameterEdit(string owner, object value)
{
    public string Owner => owner;
    public object Value => value;
    /// <summary>只编辑已迁入的原参数；未迁入参数仍在唯一JSON中保留。</summary>
    [return: DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)]
    public static Type? GetParameterType(string command) => CommandParameterTypes.Get(command);
    /// <summary>用原读取契约创建候选，克隆用于弹窗取消和父表单取消。</summary>
    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "正式Mac项目LinkMode=None保留原JSON反射；参数类型来自固定typeof表，未支持类型不能进入编辑器。")]
    public static CommandParameterEdit? Create(SaveData state, string command, CommandParameterEdit? existing = null)
    {
        var type = GetParameterType(command); if (type is null) return null;
        var value = existing?.Value ?? (type == typeof(MoveToFolderAsCommandParameter) ? state.GetDestinationParameter(command) : typeof(SaveData).GetMethod(nameof(SaveData.GetCommandParameter))!.MakeGenericMethod(type).Invoke(state, [command])!);
        return new(DefaultInputScheme.GetParameterOwner(command), JsonSerializer.Deserialize(JsonSerializer.Serialize(value, type), type)!);
    }
    /// <summary>保存到原共享参数拥有者；原未知字段由SaveData合并保留。</summary>
    public void Apply(SaveData state) => state.SetCommandParameter(Owner, Value);
    public IEnumerable<PropertyInfo> Fields => GetParameterType(Owner)!.GetProperties().Where(p => p.CanRead && p.CanWrite && !Attribute.IsDefined(p, typeof(JsonIgnoreAttribute)));
    /// <summary>原字段在Mac编辑页的文案，业务计算继续读取原属性名。</summary>
    public static string Label(string property) => property switch
    {
        "MultiPagePolicy" => "当前页组范围", "Scale" => "缩放步幅（0–1）", "IsSnapDefaultScale" => "跨越默认比例时吸附到 100%", "Angle" => "旋转角度（度）", "IsStretch" => "旋转后适配窗口",
        "Scroll" => "滚动步幅（视口比例）", "AllowCrossScroll" => "到边界后滚动另一轴", "Horizontal" => "水平对齐", "Vertical" => "垂直对齐", "IsSnap" => "强制对齐小于视口的图像",
        "ScrollType" => "滚动路径", "LineBreakStopTime" => "换行停顿（秒）", "EndMargin" => "终端容差（DIP）", "LineBreakStopMode" => "停顿位置", "PagesAsOne" => "全景页面作为整体（P3）",
        "IsReverse" => "允许随滑条方向反转", "IsLoop" => "循环切换", "IsToggle" => "再次选择此模式时切回原始大小", "Size" => "步进页数", "ToggleMode" => "快捷键开关动作", "IsIncludeTerminal" => "包含书籍首尾",
        "IsEnableNone" => "原始大小", "IsEnableUniform" => "适应窗口", "IsEnableUniformToFill" => "填满窗口", "IsEnableUniformToSize" => "适应面积", "IsEnableUniformToVertical" => "适应高度", "IsEnableUniformToHorizontal" => "适应宽度", _ => property
    };
    /// <summary>枚举沿用原数值，界面只转换名称。</summary>
    public static string EnumLabel(object value) => value switch
    {
        MultiPagePolicy.Once => "当前主页面", MultiPagePolicy.All => "当前页组（阅读顺序）", MultiPagePolicy.AllLeftToRight => "当前页组（从左到右）",
        LimitedHorizontalAlignment.Left => "左", LimitedHorizontalAlignment.Center => "居中", LimitedHorizontalAlignment.Right => "右",
        LimitedVerticalAlignment.Top => "上", LimitedVerticalAlignment.Center => "居中", LimitedVerticalAlignment.Bottom => "下",
        NScrollType.NType => "N 型", NScrollType.ZType => "Z 型", NScrollType.Diagonal => "斜向", NScrollType.Horizontal => "水平", NScrollType.Vertical => "垂直",
        LineBreakStopMode.Line => "换行", LineBreakStopMode.Page => "翻页", ToggleMode.Toggle => "切换", ToggleMode.On => "开启", ToggleMode.Off => "关闭", _ => value.ToString()!
    };
}
