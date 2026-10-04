using System.Buffers;
using System.Diagnostics;
using System.Text.Json;
using Avalonia.Threading;
using NeeView.MacOS.Views;

namespace NeeView.MacOS;

/// <summary>正式窗口的有限运行日志；只观测现有查看器/缓存，不增加阅读或输入入口。</summary>
internal static class RuntimeDiagnostics
{
    private const long FileLimit = 5 * 1024 * 1024;
    /// <summary>显式启用 NEEVIEW_DIAGNOSTICS=1 时留证实际渲染/资源；默认不产生 UI 线程日志 I/O。</summary>
    /// <param name="window">唯一正式阅读窗口。</param>
    /// <param name="images">该窗口已使用的同一图像工厂。</param>
    internal static void Attach(MainWindow window, BitmapFactory images)
    {
        if (Environment.GetEnvironmentVariable("NEEVIEW_DIAGNOSTICS") != "1") return;
        try
        {
            var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Logs", "NeeView.Mac");
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, $"session-{DateTime.UtcNow:yyyyMMddTHHmmssfffffff}-{Environment.ProcessId}.jsonl");
            // 先清理再持有文件；保留三份旧日志，加上新日志最多四份。
            // 只清理本组件文件，枚举失败不会遗留已打开的流。
            foreach (var old in Directory.EnumerateFiles(directory, "session-*.jsonl").OrderDescending().Skip(3))
                try { File.Delete(old); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            bool stopped = false;
            void Write(string reason)
            {
                if (stopped) return;
                long originalLength = stream.Length;
                try
                {
                    // 先形成完整记录，序列化失败不能污染后续 JSONL；换行也计入硬上限。
                    var buffer = new ArrayBufferWriter<byte>();
                    using (var json = new Utf8JsonWriter(buffer))
                    {
                        json.WriteStartObject(); json.WriteString("Utc", DateTime.UtcNow); json.WriteNumber("Pid", Environment.ProcessId);
                        json.WriteString("Reason", reason); json.WritePropertyName("Reader"); window.Viewer.WriteDiagnostics(json);
                        json.WritePropertyName("Cache"); WriteCache(json, images.GetDiagnostics()); json.WriteEndObject(); json.Flush();
                    }
                    if (originalLength + buffer.WrittenCount + 1 > FileLimit) return;
                    stream.Write(buffer.WrittenSpan); stream.WriteByte(10); stream.Flush();
                }
                catch (Exception ex)
                {
                    // 写入失败时尽力回退半条记录，停止该日志，避免反复追加损坏内容。
                    stopped = true; timer.Stop();
                    try { stream.SetLength(originalLength); stream.Flush(); } catch (IOException) { } catch (ObjectDisposedException) { }
                    try { stream.Dispose(); } catch (IOException) { }
                    Trace.WriteLine("NeeView diagnostics: " + ex.GetType().Name);
                }
            }

            EventHandler displayed = (_, _) => Write("display_completed");
            timer.Tick += (_, _) => Write("sample");
            window.Viewer.DisplayCompleted += displayed;
            window.Closed += (_, _) =>
            {
                timer.Stop(); Write("window_closed"); stopped = true;
                window.Viewer.DisplayCompleted -= displayed;
                try { stream.Dispose(); } catch (IOException) { }
            };
            timer.Start(); Write("window_attached");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { Trace.WriteLine("NeeView diagnostics unavailable: " + ex.GetType().Name); }
    }
    /// <summary>显式序列化资源数值，不引入反射或保留内容路径。</summary>
    private static void WriteCache(Utf8JsonWriter json, BitmapCacheDiagnostics cache)
    {
        json.WriteStartObject();
        json.WriteNumber("CachedEntries", cache.CachedEntries); json.WriteNumber("RetiredEntries", cache.RetiredEntries);
        json.WriteNumber("PendingRequests", cache.PendingRequests); json.WriteNumber("PixelBytes", cache.PixelBytes);
        json.WriteNumber("DisplayBytes", cache.DisplayBytes); json.WriteNumber("MainBytes", cache.MainBytes);
        json.WriteNumber("ThumbnailBytes", cache.ThumbnailBytes); json.WriteNumber("Leases", cache.Leases);
        json.WriteNumber("WaitingConsumers", cache.WaitingConsumers); json.WriteNumber("DecodeSlotsInUse", cache.DecodeSlotsInUse);
        json.WriteNumber("BackgroundSlotsInUse", cache.BackgroundSlotsInUse); json.WriteNumber("Budget", cache.Budget);
        json.WriteNumber("ThumbnailBudget", cache.ThumbnailBudget); json.WriteBoolean("IsDisposed", cache.IsDisposed);
        json.WriteEndObject();
    }

}
