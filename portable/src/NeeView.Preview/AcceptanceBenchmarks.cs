using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;
using ImageMagick;
using ImageMagick.Drawing;
using Microsoft.Extensions.DependencyInjection;
using NeeView.Application;
using NeeView.Core;
using NeeView.Host;

namespace NeeView.Preview;

/// <summary>可复现的测试材料和服务链路测量；不将后台耗时当作窗口绘制帧耗时。</summary>
public static class AcceptanceBenchmarks
{
    /// <summary>输入新夹具目录，生成12张混合横竖4K JPEG和同内容CBZ；拒绝覆盖现有目录。</summary>
    public static void MakeFixtures(string directory)
    {
        directory = Path.GetFullPath(directory);
        if (Directory.Exists(directory)) throw new IOException("夹具目录已存在，拒绝覆盖。");
        Directory.CreateDirectory(directory);
        for (var i = 1; i <= 12; i++)
        {
            var landscape = i % 3 == 0; var width = landscape ? 3840u : 2160u; var height = landscape ? 2160u : 3840u;
            using var image = new MagickImage(new MagickColor((byte)(30 + i * 53 % 200), (byte)(30 + i * 83 % 180), (byte)(30 + i * 29 % 200)), width, height);
            new Drawables().FillColor(MagickColors.White).Rectangle(100, 100, width - 100, height - 100).FillColor(MagickColors.Black)
                .Rectangle(160, 160, width - 160, height - 160).FillColor(MagickColors.White).Rectangle(230, 230, 230 + i * 70, 430).Draw(image);
            image.Format = MagickFormat.Jpeg; image.Quality = 90; image.Write(Path.Combine(directory, $"{i:000}.jpg"));
        }
        ZipFile.CreateFromDirectory(directory, directory + ".cbz");
    }
    /// <summary>重复采样索引到真实解码、已缓存需求与万条目几何查询，输出原始样本和范围说明。</summary>
    public static async Task<int> RunAsync(string source, string report)
    {
        var data = Path.Combine(Path.GetTempPath(), "neeview-benchmark-" + Guid.NewGuid().ToString("N"));
        const int samples = 96;
        var first = new List<double>(); var cached = new List<double>(); var layout = new List<double>(); var memory = new List<long>(); var managed = new List<long>();
        try
        {
            await using (var services = await Composition.CreateAsync(new PreviewPlatform(), data))
            {
                for (var iteration = 0; iteration < samples; iteration++)
                {
                    var session = services.GetRequiredService<IReaderSession>();
                    await using var scheduler = new ImageScheduler(services.GetRequiredService<IImageDecoder>());
                    var watch = Stopwatch.StartNew(); await session.OpenAsync(new(source));
                    var page = session.Snapshot.Current ?? throw new IOException(session.Snapshot.Error ?? "测试来源没有图片。");
                    using (var image = await scheduler.RequestAsync(session.Source!, new(page, 2048, 4096), ImagePriority.Current, default)) first.Add(watch.Elapsed.TotalMilliseconds);
                    watch.Restart(); using (var image = await scheduler.RequestAsync(session.Source!, new(page, 2048, 4096), ImagePriority.Current, default)) cached.Add(watch.Elapsed.TotalMilliseconds);
                    await session.DisposeAsync(); memory.Add(Process.GetCurrentProcess().WorkingSet64); managed.Add(GC.GetTotalMemory(false));
                }
                var pages = Enumerable.Range(0, 10000).Select(i => new PageDescriptor(new(i.ToString()), i + ".jpg", new(i + ".jpg"), new(100, 1), i, new(600 + i % 3 * 150, 900 + i % 5 * 100))).ToArray();
                var geometry = new MasonryLayout().Calculate(new(pages, new() { Mode = ReaderMode.Masonry }, 1280, 800, new(pages[0].Id)));
                for (var i = 0; i < 1000; i++) { var watch = Stopwatch.StartNew(); _ = geometry.Visible(i * 100, i * 100 + 800).ToArray(); layout.Add(watch.Elapsed.TotalMilliseconds); }
            }
            // 强制 GC 只用于结束后的诊断，不加入首图样本；RSS 回收与托管存活量分别记录。
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            var result = new { Source = Path.GetFullPath(source), Samples = samples, Scope = "同进程、操作系统文件缓存未清空；每次使用新会话和空像素缓存。首图测量终点为像素租约，不包含 UI 上传与绘制。已缓存需求不等同于翻页帧。万条目只测几何查询。",
                FirstImageP95Ms = P95(first), CachedRequestP95Ms = P95(cached), VisibleQueryP95Ms = P95(layout), FirstImageMs = first, CachedRequestMs = cached, WorkingSetBytes = memory,
                ManagedHeapBytes = managed, ManagedHeapAfterGcBytes = GC.GetTotalMemory(false), WorkingSetAfterGcBytes = Process.GetCurrentProcess().WorkingSet64,
                RenderingAndNativeLeakAcceptance = "未在此命令测量；结束后强制 GC 的诊断值不证明正常浏览内存稳定" };
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(report))!);
            await File.WriteAllTextAsync(report, JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine($"first-p95={result.FirstImageP95Ms:F2}ms cached-p95={result.CachedRequestP95Ms:F3}ms query-p95={result.VisibleQueryP95Ms:F3}ms"); return 0;
        }
        finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); if (Directory.Exists(data)) Directory.Delete(data, true); }
    }
    /// <summary>输入原始样本，按最近秩返回 P95。</summary>
    private static double P95(IReadOnlyList<double> values) => values.Order().ElementAt(Math.Clamp((int)Math.Ceiling(values.Count * 0.95) - 1, 0, values.Count - 1));
}
