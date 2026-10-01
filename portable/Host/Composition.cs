using Microsoft.Extensions.DependencyInjection;
using NeeView.Application;
using NeeView.Content;
using NeeView.Desktop;
using NeeView.Imaging;
using NeeView.Persistence;

namespace NeeView.Host;

/// <summary>两个 Host 使用相同装配，只有系统能力适配不同。</summary>
public static class Composition
{
    /// <summary>输入平台实现及可选测试数据根，返回进程级服务容器。</summary>
    public static async Task<ServiceProvider> CreateAsync(IPlatformService platform, string? dataRoot = null)
    {
        var data = dataRoot ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support", "NeeView.Portable");
        var cache = dataRoot is not null ? Path.Combine(dataRoot, "cache") : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Caches", "NeeView.Portable");
        Directory.CreateDirectory(data); Directory.CreateDirectory(cache);
        var collection = new ServiceCollection();
        collection.AddSingleton(platform);
        collection.AddSingleton(new JsonSettingsStore(Path.Combine(data, "settings.json")));
        collection.AddSingleton<ISettingsStore>(p => p.GetRequiredService<JsonSettingsStore>());
        collection.AddSingleton(new SqliteStateStore(Path.Combine(data, "state.sqlite")));
        collection.AddSingleton<IIdentityRegistry>(p => p.GetRequiredService<SqliteStateStore>());
        collection.AddSingleton<IReaderStateStore>(p => p.GetRequiredService<SqliteStateStore>());
        collection.AddSingleton<IContentSourceFactory>(p => new ContentSourceFactory(p.GetRequiredService<IIdentityRegistry>(), cache));
        collection.AddSingleton<IImageDecoder, MagickImageDecoder>();
        collection.AddSingleton<IThumbnailCache>(new DiskThumbnailCache(Path.Combine(cache, "thumbnails")));
        collection.AddSingleton<IImageRequestScheduler>(p => new ImageScheduler(p.GetRequiredService<IImageDecoder>(), thumbnails: p.GetRequiredService<IThumbnailCache>()));
        collection.AddSingleton<IFileActionService>(p => new FileActionService(p.GetRequiredService<IIdentityRegistry>(), p.GetRequiredService<IReaderStateStore>(), platform, Path.Combine(data, "file-backups")));
        collection.AddTransient<IDestinationFolderService, DestinationFolderService>();
        collection.AddSingleton<IFolderNavigator, FolderNavigator>();
        collection.AddTransient<IReaderSession, ReaderSession>();
        collection.AddSingleton<LegacyImporter>(); collection.AddSingleton<ILegacyImporter>(p => p.GetRequiredService<LegacyImporter>());
        var services = collection.BuildServiceProvider();
        await services.GetRequiredService<LegacyImporter>().RecoverAsync();
        await services.GetRequiredService<IFileActionService>().RecoverAsync();
        DesktopApp.Services = services; DesktopApp.ShutdownServices = async () =>
        {
            await services.GetRequiredService<IFileActionService>().DrainAsync();
            await services.DisposeAsync();
        };
        return services;
    }
}
