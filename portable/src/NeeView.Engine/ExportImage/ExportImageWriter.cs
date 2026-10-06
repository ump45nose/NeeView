// Copyright (c) NeeLaboratory. 原ExportImageWriter/OverwriteResolver的异步文件适配。
namespace NeeView;

/// <summary>输出命名、覆盖裁决和原子单文件/ZIP提交；不拥有书籍或图像。</summary>
internal static class ExportImageWriter
{
    /// <summary>模板可生成相对子目录，不能穿越目标根或沿既有子目录链接写入其他位置。</summary>
    internal static string RelativeName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || Path.IsPathRooted(name) || name.IndexOf('\0') >= 0
            || name.Split('/').Any(p => p is "." or ".." or ""))
            throw new IOException("导出文件名必须是目标目录内的有效相对路径：" + name);
        return name; // macOS反斜杠是文件名，不转为目录分隔符。
    }
    internal static void CheckChildLinks(string root, string path)
    {
        for (var current = path; !string.Equals(current, root, StringComparison.Ordinal); current = Path.GetDirectoryName(current)!)
        {
            if (string.IsNullOrEmpty(current)) throw new IOException("导出路径不在目标目录内。");
            if (new FileInfo(current).LinkTarget is not null || new DirectoryInfo(current).LinkTarget is not null)
                throw new IOException("导出目标子路径为链接，拒绝覆盖：" + current);
        }
    }
    internal static string UniqueName(string name, Func<string, bool> exists)
    {
        if (!exists(name)) return name;
        var extension = Path.GetExtension(name); var prefix = name[..^extension.Length];
        for (int n = 1; n <= 100000; n++) { var candidate = prefix + " (" + n + ")" + extension; if (!exists(candidate)) return candidate; }
        throw new IOException("无法生成未占用的导出文件名。");
    }
    /// <summary>返回真实覆盖授权，AddNumber仍在最终CreateNew/Move时拒绝竞争写入。</summary>
    internal static async Task<(string Name, bool Replace)> ResolveAsync(string name, ExportImageOverwriteMode mode,
        Func<string, bool> exists, Func<string, CancellationToken, Task<ExportOverwriteAnswer>>? confirm, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!await Task.Run(() => exists(name), token).ConfigureAwait(false)) return (name, false);
        if (mode == ExportImageOverwriteMode.Disallow) throw new IOException("导出文件已存在：" + name);
        if (mode == ExportImageOverwriteMode.AddNumber) return (await Task.Run(() => UniqueName(name, exists), token).ConfigureAwait(false), false);
        var answer = confirm is null ? ExportOverwriteAnswer.Cancel : await confirm(name, token);
        token.ThrowIfCancellationRequested();
        return answer switch
        {
            ExportOverwriteAnswer.Replace => (name, true),
            ExportOverwriteAnswer.AddNumber => (UniqueName(name, exists), false),
            _ => throw new OperationCanceledException(token)
        };
    }
    /// <summary>先写同目录随机临时文件，完整关闭后提交；取消与失败保留现有目标。</summary>
    internal static async Task<string> WriteFileAsync(string path, ExportImageOverwriteMode mode,
        Func<string, CancellationToken, Task<ExportOverwriteAnswer>>? confirm,
        Func<Stream, CancellationToken, Task> write, CancellationToken token)
    {
        path = Path.GetFullPath(path);
        var resolved = await ResolveAsync(path, mode, p => File.Exists(p) || Directory.Exists(p), confirm, token).ConfigureAwait(false);
        if (Directory.Exists(resolved.Name)) throw new IOException("导出文件目标是目录。");
        if (new FileInfo(resolved.Name).LinkTarget is not null) throw new IOException("导出文件目标是链接。");
        var parent = Path.GetDirectoryName(resolved.Name)!; Directory.CreateDirectory(parent);
        var temporary = Path.Combine(parent, ".neeview-export-" + Guid.NewGuid().ToString("N"));
        try
        {
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, FileOptions.Asynchronous))
            { await write(output, token); await output.FlushAsync(token); }
            token.ThrowIfCancellationRequested();
            // 单文件提交点：之后的晚取消不能把已完成替换报告为失败。
            File.Move(temporary, resolved.Name, resolved.Replace);
            return resolved.Name;
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
