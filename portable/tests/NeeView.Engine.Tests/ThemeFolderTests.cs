using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using NeeView.MacOS.ViewModels;
using NeeView.MacOS.Views;
namespace NeeView.Engine.Tests;

/// <summary>原显式主题目录动作；只操作隔离夹具，系统打开通过真实契约的替身核验。</summary>
public sealed class ThemeFolderTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private sealed class FolderPlatform : IPlatformService
    {
        public readonly List<string> Opened = [];
        public bool Fail;
        public Func<CancellationToken, Task>? Pending;
        public Task RevealAsync(string path, CancellationToken token = default) => throw new InvalidOperationException("不能用定位父目录代替打开目录内容。");
        public Task TrashAsync(string path, CancellationToken token = default) => throw new NotSupportedException();
        public async Task OpenFolderAsync(string path, CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested(); Opened.Add(path);
            if (Pending is not null) await Pending(token);
            if (Fail) throw new IOException("测试 Finder 打开失败");
        }
    }
    /// <summary>首次创建中文嵌套目录；样例保持原模板字节并可被原主题加载链读取。</summary>
    [Fact]
    public async Task NewDirectoryGetsOriginalTemplateAndLoadsAsCustomTheme()
    {
        using var f = new Fixture(); var folder = Path.Combine(f.Root, "新的主题", "Themes"); var manager = new ThemeManager();
        Assert.Equal(Path.GetFullPath(folder), await manager.PrepareCustomThemeFolderAsync(folder, Token));
        using var source = typeof(ThemeProfileTools).Assembly.GetManifestResourceStream("NeeView.Styles.Themes.CustomThemeTemplate.json")!;
        using var bytes = new MemoryStream(); await source.CopyToAsync(bytes, Token);
        Assert.Equal(bytes.ToArray(), await File.ReadAllBytesAsync(Path.Combine(folder, "Sample.json"), Token));
        var listed = await manager.CollectThemesAsync(folder, Token);
        Assert.Null(listed.Error); Assert.Contains(new ThemeSource(ThemeType.Custom, "Sample.json"), listed.Items);
        var loaded = await manager.LoadAsync(new(ThemeType.Custom, "Sample.json"), folder, new(true, false, new(255, 0, 0, 0)), Token);
        Assert.Null(loaded.Error); Assert.Equal("#FFFFA500", loaded.Profile.GetColor("Control.Accent", 1).ToString());
    }
    /// <summary>原版仅在创建目录时生成；已有空目录不补样例，有用户样例时不改字节。</summary>
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task ExistingDirectoryDoesNotGenerateOrReplaceSample(bool hasSample)
    {
        using var f = new Fixture(); var folder = Path.Combine(f.Root, "Existing"); Directory.CreateDirectory(folder);
        var sample = Path.Combine(folder, "Sample.json"); var original = new byte[] { 0xef, 0xbb, 0xbf, 0x31 };
        if (hasSample) await File.WriteAllBytesAsync(sample, original, Token);
        await new ThemeManager().PrepareCustomThemeFolderAsync(folder, Token);
        if (hasSample) Assert.Equal(original, await File.ReadAllBytesAsync(sample, Token));
        else Assert.False(File.Exists(sample));
    }
    /// <summary>空路径、非法路径及文件占位明确失败，不能当作已准备目录。</summary>
    [Theory]
    [InlineData("empty")] [InlineData("whitespace")] [InlineData("invalid")] [InlineData("file")]
    public async Task InvalidTargetIsReportedWithoutChangingExistingMaterial(string issue)
    {
        using var f = new Fixture(); var file = Path.Combine(f.Root, "occupied"); await File.WriteAllTextAsync(file, "user theme", Token);
        var folder = issue switch { "empty" => "", "whitespace" => " ", "invalid" => "bad\0path", _ => file };
        var manager = new ThemeManager();
        if (issue is "empty" or "whitespace") await Assert.ThrowsAsync<InvalidOperationException>(() => manager.PrepareCustomThemeFolderAsync(folder, Token));
        else if (issue == "invalid") await Assert.ThrowsAsync<ArgumentException>(() => manager.PrepareCustomThemeFolderAsync(folder, Token));
        else await Assert.ThrowsAsync<IOException>(() => manager.PrepareCustomThemeFolderAsync(folder, Token));
        Assert.Equal("user theme", await File.ReadAllTextAsync(file, Token));
    }
    /// <summary>取消的请求不创建目录；独占样例写出保护同时出现的用户材料。</summary>
    [Fact]
    public async Task CancelledPreparationAndExclusiveWriteLeaveUserFilesUntouched()
    {
        using var f = new Fixture(); var folder = Path.Combine(f.Root, "Cancelled");
        using var canceled = new CancellationTokenSource(); canceled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new ThemeManager().PrepareCustomThemeFolderAsync(folder, canceled.Token));
        Assert.False(Directory.Exists(folder));
        var existing = Path.Combine(f.Root, "Sample.json"); await File.WriteAllTextAsync(existing, "user", Token);
        Assert.Throws<IOException>(() => ThemeProfileTools.SaveFromContent("CustomThemeTemplate.json", existing));
        Assert.Equal("user", await File.ReadAllTextAsync(existing, Token));
    }
    /// <summary>正式设置命令及按钮：失败可重试，打开草稿不提交主题或改变当前书页。</summary>
    [AvaloniaFact]
    public async Task FormalFolderActionFailureRetryAndCancelKeepOriginalConfigAndBook()
    {
        using var f = new Fixture(); var state = new SaveData(f.State); await state.LoadAsync(Token);
        var platform = new FolderPlatform { Fail = true }; var operation = f.Operation(state);
        var model = new ReaderWorkspaceViewModel(operation, new CommandTable(operation), state);
        var window = new MainWindow(); window.Bind(model, new BitmapFactory(new NeeView.Backends.MagickImageDecoder()), platform); window.Show();
        try
        {
            await window.OpenAsync(f.Images); var book = operation.Book; var page = book!.CurrentPage;
            var theme = Config.Current.Theme; var originalFolder = theme.CustomThemeFolder; var originalSource = theme.ThemeType;
            var open = window.ExecuteAsync("OpenOptionsWindow"); await Wait(() => window.OwnedWindows.OfType<SettingsWindow>().Any());
            var settings = window.OwnedWindows.OfType<SettingsWindow>().Single(); settings.FindControl<ListBox>("SettingsNavigation")!.SelectedIndex = 7;
            var draft = (ThemeSettingsViewModel)settings.FindControl<ScrollViewer>("ThemeSettings")!.DataContext!;
            var target = Path.Combine(f.Root, "目录草稿"); draft.Folder = target;
            var button = settings.FindControl<Button>("OpenThemeFolder")!; Assert.True(button.IsEnabled);
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Wait(() => !draft.IsOpeningFolder && draft.Message.Contains("测试 Finder 打开失败"));
            Assert.Single(platform.Opened); Assert.True(button.IsEnabled); Assert.True(File.Exists(Path.Combine(target, "Sample.json")));
            var sample = await File.ReadAllBytesAsync(Path.Combine(target, "Sample.json"), Token);
            platform.Fail = false; button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Wait(() => !draft.IsOpeningFolder && platform.Opened.Count == 2);
            Assert.Equal(sample, await File.ReadAllBytesAsync(Path.Combine(target, "Sample.json"), Token));
            Assert.Contains(draft.Items, i => i.Source.Equals(new ThemeSource(ThemeType.Custom, "Sample.json")));
            Assert.Equal(originalFolder, theme.CustomThemeFolder); Assert.Equal(originalSource, theme.ThemeType); Assert.False(settings.WasSaved);
            Assert.Same(book, operation.Book); Assert.Same(page, operation.Book!.CurrentPage);
            Save(settings);
            settings.FindControl<Button>("CancelSettings")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await open;
            Assert.Same(theme, Config.Current.Theme); Assert.Equal(originalFolder, theme.CustomThemeFolder); Assert.True(Directory.Exists(target));
        }
        finally { foreach (var owned in window.OwnedWindows.ToArray()) owned.Close(); await window.PrepareShutdownAsync(); window.Close(); }
    }
    /// <summary>重复动作合并；关闭取消未完成系统打开，晚到结果不改变已关闭表单。</summary>
    [AvaloniaFact]
    public async Task ClosingDraftCancelsPendingOpenAndSuppressesLatePublication()
    {
        using var f = new Fixture(); var arrived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var blocked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var platform = new FolderPlatform { Pending = token => { arrived.SetResult(); return blocked.Task.WaitAsync(token); } };
        using var draft = new ThemeSettingsViewModel(new ThemeConfig { CustomThemeFolder = Path.Combine(f.Root, "Close") });
        var opening = draft.OpenFolderAsync(platform); await arrived.Task.WaitAsync(Token);
        await draft.OpenFolderAsync(platform); Assert.Single(platform.Opened);
        var before = draft.Message; draft.Dispose(); await opening;
        Assert.Equal(before, draft.Message); Assert.Single(platform.Opened); Assert.True(File.Exists(Path.Combine(draft.Folder, "Sample.json")));
    }
    private static async Task Wait(Func<bool> done)
    { for (var i = 0; i < 150 && !done(); i++) { Dispatcher.UIThread.RunJobs(); await Task.Delay(20, Token); } Assert.True(done()); }
    private static void Save(Window window)
    {
        if (Environment.GetEnvironmentVariable("NEEVIEW_P5_THEME_FOLDER_SCREENSHOT") is not { } path) return;
        if (!Path.IsPathFullyQualified(path)) throw new InvalidOperationException("验收截图必须为绝对路径。");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!); window.UpdateLayout();
        using var frame = window.CaptureRenderedFrame(); Assert.NotNull(frame); frame!.Save(path, PngBitmapEncoderOptions.Default);
    }
}
