using System.Text.Json;
using System.Text.Json.Nodes;
namespace NeeView;
public sealed partial class SaveData
{
    /// <summary>原46之前的两个读取别名；现代值明确存在时优先，来源材料不修改。</summary>
    private static SlideShowConfig ReadSlideShowBranch(JsonObject raw)
    {
        var node = raw["SlideShow"]?.DeepClone().AsObject() ?? new JsonObject();
        if (node["PageEndAction"] is null && node["IsSlideShowByLoop"] is { } loop)
            node["PageEndAction"] = (int)(loop.GetValue<bool>() ? PageEndAction.Loop : PageEndAction.None);
        // 原旧setter忽略传入bool，始终改为MouseMove；不能按bool重解释。
        if (node["TimerResetGesture"] is null && node.ContainsKey("IsCancelSlideByMouseMove"))
            node["TimerResetGesture"] = (int)SlideShowTimerResetGesture.MouseMove;
        node.Remove("IsSlideShowByLoop"); node.Remove("IsCancelSlideByMouseMove");
        return node.Deserialize<SlideShowConfig>(ReadOptions) ?? new();
    }
}
