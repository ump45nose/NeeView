using System.Diagnostics;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using NeeView.Backends;
using NeeView.MacOS.Views;
namespace NeeView.Engine.Tests;

/// <summary>显式启用的只读实图验收；用户来源和默认Profile不写入，不启动或激活正式应用。</summary>
public sealed class BrowseResourceTests
{
    public static bool HasResourceDirectory => Directory.Exists(Environment.GetEnvironmentVariable("NEEVIEW_P3_RESOURCE_ROOT"));

    [AvaloniaFact(SkipUnless = nameof(HasResourceDirectory), Skip = "需显式提供只读图片资源目录")]
    public async Task MountedResourceSubfoldersRenderAndScrollWithBoundedDisplay()
    {
        var root = Environment.GetEnvironmentVariable("NEEVIEW_P3_RESOURCE_ROOT")!;
        var artifact = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../artifacts/p3-resource-browse")); Directory.CreateDirectory(artifact);
        var folders = await Task.Run(() => Directory.EnumerateDirectories(root).Order(StringComparer.Ordinal)
            .Where(p => Directory.EnumerateFiles(p).Any(IsImage)).Take(3).ToArray(), TestContext.Current.CancellationToken);
        Assert.Equal(3, folders.Length);
        using var fixture = new Fixture(); var state = new SaveData(fixture.State); await state.LoadAsync(TestContext.Current.CancellationToken);
        var operation = fixture.Operation(state); var factory = new BitmapFactory(new MagickImageDecoder()); var window = new MainWindow();
        window.Bind(new(operation, new CommandTable(operation), state), factory, new NoPlatform()); window.Show();
        var measurements = new List<object>();
        try
        {
            await operation.SetBrowseModeAsync(BrowseLayoutMode.Masonry);
            for (int sample = 0; sample < folders.Length; sample++)
            {
                var clock = Stopwatch.StartNew(); await window.OpenAsync(folders[sample]); await window.Viewer.RefreshAsync();
                await WaitAsync(() => window.Viewer.DisplayCount > 0); var firstDisplay = clock.Elapsed.TotalMilliseconds;
                foreach (double position in new[] { 0d, .3, .65, 1d })
                {
                    window.Viewer.Navigate(new(0, position)); await WaitAsync(() => window.Viewer.BrowsePendingCount == 0 && window.Viewer.DisplayCount > 0);
                    await Task.Delay(180, TestContext.Current.CancellationToken); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
                    var layout = window.Viewer.BrowseLayout!; Assert.True(layout.ColumnCount >= 2); Assert.InRange(window.Viewer.DisplayCount, 1, 32);
                    var diagnostics = factory.GetDiagnostics(); Assert.InRange(diagnostics.ThumbnailBytes, 0, factory.ThumbnailBudget + 4 * 1024 * 1024);
                    using var frame = window.CaptureRenderedFrame(); Assert.NotNull(frame);
                    if (position == .3) frame.Save(Path.Combine(artifact, $"sample-{sample + 1}.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
                    measurements.Add(new { Sample = sample + 1, Position = position, Pages = operation.Book!.Pages.Count, layout.ColumnCount,
                        CurrentIndex = operation.Position.Index, window.Viewer.DisplayCount, Pending = window.Viewer.BrowsePendingCount,
                        diagnostics.ThumbnailBytes, diagnostics.DisplayBytes, diagnostics.Leases, FirstDisplayMs = firstDisplay });
                }
                // 同一来源立即切换连续/分页再返回，避免只验证首屏截图。
                await operation.SetBrowseModeAsync(BrowseLayoutMode.Continuous); await window.Viewer.RefreshAsync(); await WaitAsync(() => window.Viewer.DisplayCount > 0);
                await operation.SetBrowseModeAsync(BrowseLayoutMode.Paged); await window.Viewer.RefreshAsync(); await WaitAsync(() => window.Viewer.DisplayCount > 0);
                await operation.SetBrowseModeAsync(BrowseLayoutMode.Masonry); await window.Viewer.RefreshAsync();
            }
        }
        finally { await window.PrepareShutdownAsync(); window.Close(); }
        Assert.Equal(0, factory.GetDiagnostics().Leases); Assert.Equal(0, factory.ByteCount);
        File.WriteAllText(Path.Combine(artifact, "report.json"), JsonSerializer.Serialize(new { Utc = DateTimeOffset.UtcNow,
            Scope = "正式XAML/ReaderView、真实目录只读Headless速览；不代表正式macOS应用/Retina/触控板或P95性能验收", Measurements = measurements,
            ClosedLeases = factory.GetDiagnostics().Leases, ClosedBytes = factory.ByteCount }, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static bool IsImage(string path) => Path.GetExtension(path).ToLowerInvariant() is ".jpg" or ".jpeg" or ".png" or ".webp" or ".bmp" or ".tif" or ".tiff" or ".gif";
    private static async Task WaitAsync(Func<bool> ready)
    {
        for (int i = 0; i < 1500 && !ready(); i++) { Dispatcher.UIThread.RunJobs(); await Task.Delay(20, TestContext.Current.CancellationToken); }
        Assert.True(ready());
    }
    private sealed class NoPlatform : IPlatformService
    { public Task RevealAsync(string path, CancellationToken token = default) => throw new NotSupportedException(); public Task TrashAsync(string path, CancellationToken token = default) => throw new NotSupportedException(); }
}
