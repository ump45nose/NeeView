using System.Diagnostics;
using Avalonia;
using Microsoft.Extensions.DependencyInjection;
using NeeView.Application;
using NeeView.Desktop;
using NeeView.Host;

namespace NeeView.Preview;

public static class Program
{
    /// <summary>开发入口；正式 MacOS Host 单独使用 AppKit 类型绑定。</summary>
    [STAThread]
    public static int Main(string[] args)
    {
        if (args.FirstOrDefault() == "--inspect-api")
        {
            foreach (var e in typeof(Avalonia.Input.InputElement).GetEvents().Where(e => e.Name.Contains("Magnify") || e.Name.Contains("Pinch")))
            {
                Console.WriteLine(e.Name + "=" + e.EventHandlerType);
                foreach (var t in e.EventHandlerType!.GenericTypeArguments) Console.WriteLine(t.FullName + ": " + string.Join(", ", t.GetProperties().Select(p => p.Name + "=" + p.PropertyType.Name)));
            }
            foreach (var type in typeof(Avalonia.Input.PointerWheelEventArgs).Assembly.GetTypes()
                .Concat(typeof(Avalonia.Controls.ApplicationLifetimes.ActivatedEventArgs).Assembly.GetTypes())
                .Where(t => t.Name.Contains("TouchPad") || t.Name.Contains("PinchEvent") || t.Name.Contains("FileActivated") || t.Name.Contains("GestureEventArgs") || t.Name == "PointerWheelEventArgs"))
                Console.WriteLine(type.FullName + ": " + string.Join(", ", type.GetProperties().Select(p => p.Name + "=" + p.PropertyType.Name)));
            return 0;
        }
        if (args.FirstOrDefault() == "--make-fixtures") { AcceptanceBenchmarks.MakeFixtures(args[1]); return 0; }
        if (args.FirstOrDefault() == "--benchmark") return AcceptanceBenchmarks.RunAsync(args[1], args[2]).GetAwaiter().GetResult();
        if (args.FirstOrDefault() == "--smoke") return SmokeAsync(args[1]).GetAwaiter().GetResult();
        Composition.CreateAsync(new PreviewPlatform()).GetAwaiter().GetResult();
        DesktopApp.InitialPaths = args.Where(a => !a.StartsWith('-')).ToArray();
        return AppBuilder.Configure<DesktopApp>().UsePlatformDetect().With(new MacOSPlatformOptions { ShowInDock = true }).LogToTrace().StartWithClassicDesktopLifetime(args);
    }
    /// <summary>真实原生解码和保存恢复链路冒烟，使用独立临时数据，不写用户配置。</summary>
    private static async Task<int> SmokeAsync(string source)
    {
        var data = Path.Combine(Path.GetTempPath(), "neeview-smoke-" + Guid.NewGuid().ToString("N"));
        try
        {
            await using var services = await Composition.CreateAsync(new PreviewPlatform(), data);
            var session = services.GetRequiredService<IReaderSession>(); var watch = Stopwatch.StartNew();
            await session.OpenAsync(new(source));
            if (session.Snapshot.Error is { } error) { Console.Error.WriteLine(error); return 1; }
            var scheduler = services.GetRequiredService<IImageRequestScheduler>();
            var pages = session.Snapshot.Index!.Pages;
            foreach (var page in pages.Take(8))
            {
                await using (var stream = await session.Source!.OpenReadAsync(page, default))
                {
                    var info = await services.GetRequiredService<IImageDecoder>().ProbeAsync(stream, default);
                    Console.WriteLine($"probe {info.Size.Width}x{info.Size.Height} {info.Format}");
                }
                using var image = await scheduler.RequestAsync(session.Source!, new(page, 2048, 4096), ImagePriority.Current, CancellationToken.None);
                Console.WriteLine($"decode {page.Name} {image.Size.Width}x{image.Size.Height} {image.ByteCount}B");
                await session.NavigateAsync(1, true);
            }
            await session.FlushAsync(); var anchor = session.Snapshot.Anchor;
            await session.DisposeAsync();
            var restored = services.GetRequiredService<IReaderSession>(); await restored.OpenAsync(new(source));
            Console.WriteLine($"pages={pages.Count} elapsed={watch.ElapsedMilliseconds}ms cache={scheduler.CachedBytes} restored={restored.Snapshot.Anchor == anchor}");
            await restored.DisposeAsync(); return restored.Snapshot.Anchor == anchor ? 0 : 1;
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(data)) Directory.Delete(data, true);
        }
    }
}
