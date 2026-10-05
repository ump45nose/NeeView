using System.ComponentModel;
using System.Runtime.InteropServices;
namespace NeeView.Backends;

/// <summary>Unix实体路径替换点；系统解析父级别名和真实大小写，不逐级枚举目录。</summary>
internal static class UnixPhysicalPath
{
    /// <summary>解析系统路径；只有ENOENT允许保留尚未创建的末段。</summary>
    /// <param name="path">绝对或相对路径；本方法跟随最后一段链接。</param>
    /// <param name="token">调用间的取消；realpath本身可能不可及时中断。</param>
    /// <returns>系统实际路径，缺失末段使用原名称。</returns>
    internal static string Resolve(string path, CancellationToken token = default)
    {
        if (OperatingSystem.IsWindows()) throw new NotSupportedException("此实体路径后端用于 macOS/Unix。");
        var current = Path.GetFullPath(path); var remaining = new Stack<string>();
        while (true)
        {
            token.ThrowIfCancellationRequested(); var pointer = RealPath(current, IntPtr.Zero);
            if (pointer != IntPtr.Zero)
            {
                try
                {
                    var actual = Marshal.PtrToStringUTF8(pointer) ?? throw new IOException("系统未返回实体路径。");
                    while (remaining.TryPop(out var part)) actual = Path.Combine(actual, part);
                    return Path.TrimEndingDirectorySeparator(actual);
                }
                finally { FreePath(pointer); }
            }
            var error = Marshal.GetLastPInvokeError();
            if (error != 2 || Path.GetDirectoryName(current) is not { } parent)
                throw new IOException("实体路径暂不可确认：" + path, new Win32Exception(error));
            remaining.Push(Path.GetFileName(current)); current = parent;
        }
    }
    /// <summary>系统分配实际路径，成功内存交还同一个libc。</summary>
    [DllImport("libc", EntryPoint = "realpath", SetLastError = true)]
    private static extern IntPtr RealPath([MarshalAs(UnmanagedType.LPUTF8Str)] string path, IntPtr buffer);
    /// <summary>释放realpath分配。</summary>
    [DllImport("libc", EntryPoint = "free")]
    private static extern void FreePath(IntPtr path);
}
