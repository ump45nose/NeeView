namespace NeeView;

/// <summary>文件剪贴板快照：系统文件地址（可为原虚拟策略）、原QueryPath及可选文本独立。</summary>
public sealed record FileClipboardContent(IReadOnlyList<string> Files, IReadOnlyList<string> QueryPaths, string? Text = null, ContentDropData? Content = null);

/// <summary>原ContentDropReceiver的跨平台数据快照，没有控件/原生对象；普通文本不是文件对象。</summary>
public sealed record ContentDropData(IReadOnlyList<ContentDropImage> Images, string? Html = null, IReadOnlyList<string>? WebUrls = null, IReadOnlyList<string>? BrowserFiles = null);
/// <summary>位图采用原Bgr32语义丢弃alpha，内联/下载文件保留原编码。</summary>
public sealed record ContentDropImage(byte[] Bytes, string? Name = null, bool IsBitmap = false);
/// <summary>原接收器的临时落盘替换点，生成资源由进程持有供历史导航使用。</summary>
public interface IContentDropReceiver
{
    /// <summary>准备有限完整批次，失败/取消清理已写项，不返回部分结果。</summary>
    Task<IReadOnlyList<string>> ReceiveAsync(ContentDropData content, CancellationToken token);
}

/// <summary>替代原ClipboardUtility的系统边界，不包含AppKit/控件或文件写入。</summary>
public interface IFileClipboard
{
    /// <summary>只探测文件/QueryPath/图片/HTML/URL类型，不读取图片或任意文本。</summary>
    bool HasFileContent { get; }
    /// <summary>写入捕获快照；提交后不以晚取消掩盖真实结果。</summary>
    /// <param name="content">已由业务核对的实体与逻辑地址。</param><param name="token">原生提交前的取消令牌。</param>
    /// <returns>系统写入完成的任务，拒绝/失败通过异常回报。</returns>
    Task WriteAsync(FileClipboardContent content, CancellationToken token);
    /// <summary>读取同一时刻的有限文件快照，不执行内容加载。</summary>
    /// <param name="token">读取开始前的取消令牌。</param><returns>无支持类型时返回空快照。</returns>
    Task<FileClipboardContent> ReadAsync(CancellationToken token);
}

/// <summary>有限文件协议校验；Mac合法反斜杠保持，不接受网页URL或远程file主机。</summary>
public static class FileClipboardCodec
{
    public const string QueryPathsType = "org.neeview.query-paths";
    public const int MaximumItems = 1024;
    /// <summary>原逻辑地址以有限JSON数组传递，不依赖反射序列化或平台类型。</summary>
    /// <param name="paths">业务捕获的完整逻辑地址集合。</param><returns>有限JSON数组文本。</returns>
    public static string EncodeQueryPaths(IEnumerable<string> paths)
    {
        using var stream = new MemoryStream();
        using (var writer = new System.Text.Json.Utf8JsonWriter(stream))
        { writer.WriteStartArray(); foreach (var path in ValidatePaths(paths)) writer.WriteStringValue(path); writer.WriteEndArray(); }
        var raw = System.Text.Encoding.UTF8.GetString(stream.ToArray());
        if (raw.Length > 1024 * 1024) throw new NotSupportedException("剪贴板原QueryPath数据超过支持上限。");
        return raw;
    }
    /// <summary>校验完整私有地址数组；损坏数据不能降级成另一组文件。</summary>
    /// <param name="raw">系统私有类型的JSON快照。</param><returns>保序且全部有效的地址数组。</returns>
    public static string[] DecodeQueryPaths(string raw)
    {
        if (raw.Length > 1024 * 1024) throw new NotSupportedException("剪贴板原QueryPath数据超过支持上限。");
        using var document = System.Text.Json.JsonDocument.Parse(raw);
        if (document.RootElement.ValueKind != System.Text.Json.JsonValueKind.Array) throw new System.Text.Json.JsonException("原QueryPath数据不是地址数组。");
        return ValidatePaths(document.RootElement.EnumerateArray().Select(item => item.GetString()!));
    }
    /// <summary>仅解码本机file URL；不把URL交给shell，不自动下载网络内容。</summary>
    /// <param name="url">系统file URL字符串。</param><returns>有效的绝对本机路径，其他类型返回空。</returns>
    public static string? DecodeFileUrl(string? url)
    {
        if (url is null || url.Length > 65536 || !url.StartsWith("file:", StringComparison.OrdinalIgnoreCase) || !Uri.TryCreate(url, UriKind.Absolute, out var uri) || !uri.IsFile
            || uri.Host.Length != 0 && !uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            || uri.Query.Length != 0 || uri.Fragment.Length != 0) return null;
        var path = Uri.UnescapeDataString(uri.AbsolutePath);
        return IsPath(path) ? path : null;
    }
    /// <summary>整组解码file URL；一个无效项也不能过滤后误开余下的单文件。</summary>
    /// <param name="urls">声明为file URL的全部剪贴板项。</param><returns>保序路径；非法输入抛出协议错误。</returns>
    public static string[] DecodeFileUrls(IEnumerable<string?> urls)
    {
        var values = urls.Take(MaximumItems + 1).ToArray();
        if (values.Length > MaximumItems) throw new NotSupportedException("剪贴板文件数量超过支持上限。");
        return values.Select(url => DecodeFileUrl(url) ?? throw new NotSupportedException("剪贴板包含无效的文件URL，本次未打开任何项目。")).ToArray();
    }
    /// <summary>校验系统/私有地址的数量和长度；不判断文件存在或改写来源。</summary>
    /// <param name="paths">文件或原逻辑地址。</param><returns>保序快照，失败抛出协议错误。</returns>
    public static string[] ValidatePaths(IEnumerable<string> paths)
    {
        var result = paths.Take(MaximumItems + 1).ToArray();
        if (result.Length > MaximumItems || result.Any(path => !IsPath(path))) throw new NotSupportedException("剪贴板文件地址无效或数量超过支持上限。");
        return result;
    }
    /// <summary>只判断平台绝对定位及协议限额，不进行I/O或大小写归一。</summary>
    /// <param name="path">完整文件或归档内部逻辑地址。</param><returns>有效时为true。</returns>
    private static bool IsPath(string? path) => path is { Length: > 0 and <= 16384 } && !path.Contains('\0') && System.IO.Path.IsPathFullyQualified(path);
}
