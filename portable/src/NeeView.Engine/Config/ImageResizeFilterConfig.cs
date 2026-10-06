// Copyright (c) NeeLaboratory. MIT; original resize configuration material, backend remains pending.
using System.Text.Json.Nodes;
namespace NeeView;
/// <summary>原缩放滤镜分支暂用原始对象保存；不猜测 MagicScaler 隐含默认，也不冒充后端已迁。</summary>
public static class ImageResizeFilterCapability
{
    public const string PendingReason = "缩放滤镜后端尚未迁入；原参数完整保留。";
    public static JsonObject Clone(JsonObject source) => source.DeepClone().AsObject();
}
