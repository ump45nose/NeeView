using System.ComponentModel;
using System.Runtime.InteropServices;
namespace NeeView.Backends;

public sealed partial class ArchiveFactory
{
    /// <summary>实体动作才调用系统realpath；保留文件系统大小写语义，避免Profile保护被路径别名绕过。</summary>
    /// <param name="path">完整目标或受保护目录；不存在的末段保留原名。</param>
    /// <param name="token">后台排队、系统查询和返回前的取消。</param>
    /// <returns>实际大小写及系统链接解析后的绝对路径，不参与书籍身份或JSON改写。</returns>
    public Task<string> GetPhysicalPathAsync(string path, CancellationToken token) => SourceIo.RunAsync(() =>
    {
        if (OperatingSystem.IsWindows()) throw new NotSupportedException("此实体路径后端用于 macOS/Unix。");
        var current = System.IO.Path.GetFullPath(path); var remaining = new Stack<string>();
        while (true)
        {
            token.ThrowIfCancellationRequested(); var pointer = RealPath(current, IntPtr.Zero);
            if (pointer != IntPtr.Zero)
            {
                try
                {
                    var actual = Marshal.PtrToStringUTF8(pointer) ?? throw new IOException("系统未返回实体路径。");
                    while (remaining.TryPop(out var part)) actual = System.IO.Path.Combine(actual, part);
                    return System.IO.Path.TrimEndingDirectorySeparator(actual);
                }
                finally { FreePath(pointer); }
            }
            var error = Marshal.GetLastPInvokeError();
            // 只有明确缺失允许解析父级：首次Profile尚未保存时，其父级仍可可靠保护。
            if (error != 2 || System.IO.Path.GetDirectoryName(current) is not { } parent)
                throw new IOException("实体路径暂不可确认：" + path, new Win32Exception(error));
            remaining.Push(System.IO.Path.GetFileName(current)); current = parent;
        }
    }, token);

    /// <summary>系统分配实际路径，成功内存必须交还同一个libc。</summary>
    /// <param name="path">UTF8绝对路径。</param><param name="buffer">零表示由系统分配。</param>
    /// <returns>需要释放的指针；零时读取errno。</returns>
    [DllImport("libc", EntryPoint = "realpath", SetLastError = true)]
    private static extern IntPtr RealPath([MarshalAs(UnmanagedType.LPUTF8Str)] string path, IntPtr buffer);
    /// <summary>释放realpath的系统分配；不交由托管分配器释放。</summary>
    /// <param name="path">realpath成功返回的系统分配。</param>
    [DllImport("libc", EntryPoint = "free")]
    private static extern void FreePath(IntPtr path);
}
