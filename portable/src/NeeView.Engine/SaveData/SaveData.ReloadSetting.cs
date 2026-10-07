// Copyright (c) NeeLaboratory. 原 ReloadSetting/UserSettingTools.Restore 的单进程 Mac 适配。
using System.Text.Json.Nodes;
namespace NeeView;

public sealed partial class SaveData
{
    /// <summary>只读取并验证 UserSetting；原地应用已迁配置，命令差分以来源为权威，不读写其他集合。</summary>
    /// <param name="apply">在调用线程合并已验证配置的回调；null Config 与原缺失文件一样不改运行配置。</param>
    /// <param name="token">排队和候选读取可取消；应用开始后完成或恢复。</param>
    internal async Task ReloadSettingAsync(Func<Config?, Task> apply, CancellationToken token)
    {
        await _gate.WaitAsync(token);
        var previous = _setting;
        try
        {
            var candidate = await Task.Run(async () =>
            {
                var path = ProfileImportAssets.ResolveTarget(DirectoryPath, "UserSetting.json");
                JsonObject raw;
                try
                {
                    await using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                    if (input.Length > ProfileImportFiles.MaxFileBytes) throw new InvalidDataException("UserSetting.json 超过读取预算。");
                    using var bytes = new MemoryStream(); var buffer = new byte[64 * 1024]; int count;
                    while ((count = await input.ReadAsync(buffer, token)) != 0)
                    {
                        if (bytes.Length + count > ProfileImportFiles.MaxFileBytes) throw new InvalidDataException("UserSetting.json 读取期间超过预算。");
                        bytes.Write(buffer, 0, count);
                    }
                    token.ThrowIfCancellationRequested(); bytes.Position = 0;
                    raw = (await System.Text.Json.Nodes.JsonNode.ParseAsync(bytes, documentOptions: new() { AllowTrailingCommas = true,
                        CommentHandling = System.Text.Json.JsonCommentHandling.Skip }, cancellationToken: token))?.AsObject() ?? new();
                }
                // 只有可靠缺失沿原空UserSetting语义；权限、目录和网络错误保持原状态并传播。
                catch (FileNotFoundException) { raw = new(); }
                catch (DirectoryNotFoundException) { raw = new(); }
                if (raw["Format"] is not null)
                {
                    if (ProfileImportCompatibility.BlockReason("UserSetting.json", raw) is { } reason) throw new InvalidDataException(reason);
                    raw = ProfileImportCompatibility.Upgrade("UserSetting.json", raw);
                }
                var documents = ProfileImportFiles.Names.ToDictionary(name => name, name => name == "UserSetting.json" ? raw : (JsonObject?)null);
                ValidateImportDocuments(documents);
                return (Raw: raw, Config: raw["Config"] is null ? null : ReadProfileConfig(raw));
            }, token);
            token.ThrowIfCancellationRequested();
            // 已知字段由独立候选验证；未知节点保留来源，不递归合并复活已经从磁盘删除的旧键位。
            _setting = candidate.Raw;
            await apply(candidate.Config);
        }
        catch { _setting = previous; throw; }
        finally { _gate.Release(); }
    }
}
