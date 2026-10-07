// Copyright (c) NeeLaboratory. 原ScriptManager.OpenScriptsFolder的目录/Sample规则，MIT。
namespace NeeView;
public static class ScriptFolderService
{
    private static readonly SemaphoreSlim Gate = new(1);
    /// <summary>仅首次创建目录时写原Sample字节；已有目录不补样例，绝不覆盖材料。</summary>
    /// <param name="folder">明确动作的目录草稿，不提交Config。</param>
    /// <param name="token">未开始准备前可取消，创建以后等待文件句柄释放。</param>
    /// <returns>可交给唯一平台打开的绝对目录。</returns>
    public static async Task<string> PrepareAsync(string folder, CancellationToken token = default)
    {
        if (string.IsNullOrWhiteSpace(folder)) throw new ArgumentException("尚未设置脚本目录。", nameof(folder));
        await Gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            return await Task.Run(() =>
            {
                token.ThrowIfCancellationRequested(); var path = Path.GetFullPath(folder);
                if (!Directory.Exists(path))
                {
                    Directory.CreateDirectory(path);
                    using var sample = typeof(ScriptFolderService).Assembly.GetManifestResourceStream("NeeView.Resources.Scripts.Sample.nvjs")
                        ?? throw new InvalidDataException("缺少原脚本Sample资源。");
                    using var output = new FileStream(Path.Combine(path, "Sample.nvjs"), FileMode.CreateNew, FileAccess.Write, FileShare.None);
                    sample.CopyTo(output); output.Flush(true);
                }
                return path;
            }, CancellationToken.None).ConfigureAwait(false);
        }
        finally { Gate.Release(); }
    }
}
