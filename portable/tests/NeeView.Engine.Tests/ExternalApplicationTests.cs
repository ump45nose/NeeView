using System.Text.Json;
using System.Text.Json.Nodes;
using NeeView.Backends;
namespace NeeView.Engine.Tests;

/// <summary>原外部应用命令、页组、配置与真实临时材料；系统提交使用可观察Fake。</summary>
public sealed class ExternalApplicationTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    internal sealed class Platform : IPlatformService
    {
        public List<ExternalAppLaunchRequest> Requests { get; } = [];
        public int FailAt;
        public Func<CancellationToken, Task>? Pending;
        public async Task OpenExternalApplicationAsync(ExternalAppLaunchRequest request, CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested(); Requests.Add(request);
            if (Pending is { } work) await work(token);
            if (FailAt == Requests.Count) throw new IOException("isolated launch rejection");
        }
        public Task RevealAsync(string path, CancellationToken token = default) => throw new NotSupportedException();
        public Task TrashAsync(string path, CancellationToken token = default) => throw new NotSupportedException();
    }
    private static async Task<SaveData> State(Fixture f)
    { var state = new SaveData(f.State); await state.LoadAsync(Token); return state; }
    private static void Wide()
    {
        var setting = Config.Current.BookSettingDefault; setting.PageMode = PageMode.WidePage; setting.BookReadOrder = PageReadOrder.RightToLeft;
        setting.IsSupportedSingleFirstPage = setting.IsSupportedSingleLastPage = setting.IsSupportedWidePage = false;
    }
    [Fact]
    public void OriginalDefaultsAndZeroIndexAreKept()
    {
        var app = Assert.Single(new SystemConfig().ExternalAppCollection); var parameter = new OpenExternalAppCommandParameter();
        Assert.Null(app.Command); Assert.Null(app.WorkingDirectory); Assert.Equal("\"{File}\"", app.Parameter); Assert.Equal(app.Parameter, parameter.Parameter);
        Assert.Equal(ArchivePolicy.SendExtractFile, app.ArchivePolicy); Assert.Equal(MultiPagePolicy.Once, parameter.MultiPagePolicy);
        parameter.Parameter = " "; Assert.Equal(app.Parameter, parameter.Parameter); Assert.Equal(0, new OpenExternalAppAsCommandParameter { Index = -2 }.Index);
    }
    [Theory]
    [InlineData("\"{File}\"", false)]
    [InlineData("\"$File\"", false)]
    [InlineData("{Uri}", true)]
    [InlineData("$Uri", true)]
    public void TemplateReplacementDoesNotReinterpretFilename(string template, bool escaped)
    {
        var path = "/tmp/含 空格'\"$() `touch x` {Uri} $File.png";
        var request = ExternalAppUtility.CreateLaunchRequest(new ExternalApp { Command = "/usr/bin/true", Parameter = template }, path, "/tmp/NeeView.app");
        Assert.Equal(escaped ? Uri.EscapeDataString(path) : path, Assert.Single(request.Arguments));
    }
    [Fact]
    public void ParserKeepsEmptyAndQuotedArgumentsAndRejectsBrokenTemplate()
    {
        var values = ExternalAppUtility.ParseArguments("--flag \"\" 'two words' plain\\ word \\\"quote\\\"", "/tmp/image");
        Assert.Equal(["--flag", "", "two words", "plain word", "\"quote\""], values);
        Assert.Throws<FormatException>(() => ExternalAppUtility.ParseArguments("'broken", "/tmp/image"));
        Assert.Equal(["--flag", "/tmp/image"], ExternalAppUtility.CreateLaunchRequest(new ExternalApp { Command = "true", Parameter = "--flag" }, "/tmp/image", "/tmp/NeeView").Arguments);
        Assert.Throws<InvalidOperationException>(() => ExternalAppUtility.CreateLaunchRequest(new ExternalApp { Parameter = "--flag" }, "/tmp/image", "/tmp/NeeView"));
    }
    [Theory]
    [InlineData(MultiPagePolicy.Once, "001.png")]
    [InlineData(MultiPagePolicy.All, "001.png,002.png")]
    [InlineData(MultiPagePolicy.AllLeftToRight, "002.png,001.png")]
    public async Task DirectCommandPreservesOriginalPageOrderAndReading(MultiPagePolicy policy, string expected)
    {
        using var f = new Fixture(); var state = await State(f); Wide(); await using var op = f.Operation(state); var platform = new Platform(); op.AttachExternalApplications(platform, "/tmp/NeeView");
        state.SetCommandParameter("OpenExternalApp", new OpenExternalAppCommandParameter { MultiPagePolicy = policy, Command = "probe" });
        await op.OpenAsync(f.Images, Token); var book = op.Book; var page = book!.CurrentPage; var position = op.Position;
        await op.OpenExternalApplicationCommandAsync("OpenExternalApp", token: Token);
        Assert.Equal(expected, string.Join(',', platform.Requests.Select(request => Path.GetFileName(Assert.Single(request.Arguments)))));
        Assert.Same(book, op.Book); Assert.Same(page, book.CurrentPage); Assert.Equal(position, op.Position); Assert.Null(op.Error);
    }
    [Fact]
    public async Task ConfiguredCommandsUseOneBasedIndexAndDoNotAutoChooseZero()
    {
        using var f = new Fixture(); var state = await State(f); Wide(); await using var op = f.Operation(state); var platform = new Platform(); op.AttachExternalApplications(platform, "/tmp/NeeView");
        Config.Current.System.ExternalAppCollection = new([new ExternalApp { Command = "first" }, new ExternalApp { Command = "second" }]);
        await op.OpenAsync(f.Images, Token); await op.OpenExternalApplicationCommandAsync("OpenExternalAppAs", token: Token); Assert.Empty(platform.Requests);
        state.SetCommandParameter("OpenExternalAppAs", new OpenExternalAppAsCommandParameter { Index = 2, MultiPagePolicy = MultiPagePolicy.AllLeftToRight });
        await op.OpenExternalApplicationCommandAsync("OpenExternalAppAs", token: Token); Assert.All(platform.Requests, request => Assert.Equal("second", request.Command));
        Assert.Equal(["002.png", "001.png"], platform.Requests.Select(request => Path.GetFileName(request.Arguments.Single())));
        platform.Requests.Clear(); await op.OpenExternalApplicationCommandAsync("OpenBookExternalAppAs", 1, Token);
        Assert.Equal(f.Images, Assert.Single(Assert.Single(platform.Requests).Arguments));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => op.OpenExternalApplicationCommandAsync("OpenExternalAppAs", 99, Token));
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SuccessfullySubmittedArchiveBatchSurvivesCloseAndPartialFailure(bool partialFailure)
    {
        using var f = new Fixture(); var state = await State(f); Wide(); var op = f.Operation(state); var platform = new Platform { FailAt = partialFailure ? 2 : 0 };
        var root = Path.Combine(f.Root, "Realized"); await using var realizer = new ArchiveEntryRealizer(root);
        op.AttachArchiveEntryRealizer(realizer); op.AttachExternalApplications(platform, "/tmp/NeeView"); await op.OpenAsync(f.Zip, Token);
        await op.OpenExternalApplicationAsync(new ExternalApp { Command = "probe" }, MultiPagePolicy.All, token: Token);
        var paths = platform.Requests.Select(request => request.Arguments.Single()).ToArray(); Assert.Equal(2, paths.Length); Assert.All(paths, path => Assert.True(File.Exists(path)));
        if (partialFailure) Assert.Contains("rejection", op.Error);
        await op.DisposeAsync(); Assert.All(paths, path => Assert.True(File.Exists(path)));
        // 新剪贴板替换不能回收外部程序仍在读取的材料。
        await realizer.RetainClipboardAsync(new()); Assert.All(paths, path => Assert.True(File.Exists(path)));
        await realizer.DisposeAsync(); Assert.All(paths, path => Assert.False(File.Exists(path)));
    }
    [Fact]
    public async Task SharedProcessBudgetIsReservedBeforeLaunchAndReclaimedAfterRelease()
    {
        using var f = new Fixture(); var state = await State(f); await using var op = f.Operation(state); await op.OpenAsync(f.Zip, Token);
        var entry = op.Book!.Pages[0].ArchiveEntry; var root = Path.Combine(f.Root, "Realized");
        await using var realizer = new ArchiveEntryRealizer(root, entry.Length);
        var first = await ArchiveEntryUtility.RealizeArchiveEntry([entry], ArchivePolicy.SendExtractFile, realizer, Token);
        var path = first.Paths.Single(); await realizer.RetainExternalAsync(first);
        await Assert.ThrowsAsync<NotSupportedException>(() => ArchiveEntryUtility.RealizeArchiveEntry([entry], ArchivePolicy.SendExtractFile, realizer, Token));
        Assert.True(File.Exists(path)); await first.DisposeAsync(); Assert.False(File.Exists(path));
        await using var again = await ArchiveEntryUtility.RealizeArchiveEntry([entry], ArchivePolicy.SendExtractFile, realizer, Token); Assert.True(File.Exists(again.Paths.Single()));
    }
    [Fact]
    public async Task EmptyArchiveFileStillNeedsExternalOwnershipUntilProcessExit()
    {
        using var f = new Fixture(); var state = await State(f);
        var zip = Path.Combine(f.Root, "empty.cbz");
        using (var archive = System.IO.Compression.ZipFile.Open(zip, System.IO.Compression.ZipArchiveMode.Create)) archive.CreateEntry("001.png");
        await using var op = f.Operation(state); var platform = new Platform(); var realizer = new ArchiveEntryRealizer(Path.Combine(f.Root, "Realized"));
        op.AttachArchiveEntryRealizer(realizer); op.AttachExternalApplications(platform, "/tmp/NeeView"); await op.OpenAsync(zip, Token);
        await op.OpenExternalApplicationAsync(new ExternalApp { Command = "probe" }, token: Token);
        var path = Assert.Single(Assert.Single(platform.Requests).Arguments); Assert.True(File.Exists(path));
        await op.DisposeAsync(); Assert.True(File.Exists(path)); await realizer.DisposeAsync(); Assert.False(File.Exists(path));
    }
    [Fact]
    public async Task FirstSubmissionFailureReleasesPreparedArchiveFiles()
    {
        using var f = new Fixture(); var state = await State(f); await using var op = f.Operation(state); var platform = new Platform { FailAt = 1 };
        var root = Path.Combine(f.Root, "Realized"); await using var realizer = new ArchiveEntryRealizer(root); op.AttachArchiveEntryRealizer(realizer); op.AttachExternalApplications(platform, "/tmp/NeeView");
        await op.OpenAsync(f.Zip, Token); await op.OpenExternalApplicationAsync(new ExternalApp { Command = "probe" }, token: Token);
        Assert.Contains("rejection", op.Error); Assert.Empty(Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)); Assert.False(op.IsOpeningExternalApplication);
        platform.FailAt = 0; await op.OpenExternalApplicationAsync(new ExternalApp { Command = "probe" }, token: Token); Assert.Null(op.Error);
    }
    [Fact]
    public async Task WholeBookUsesOriginalSystemArchivePolicyRatherThanApplicationPolicy()
    {
        using var f = new Fixture(); var state = await State(f); await using var op = f.Operation(state); var platform = new Platform(); op.AttachExternalApplications(platform, "/tmp/NeeView");
        await op.OpenAsync(f.Zip, Token); await op.OpenExternalApplicationAsync(new ExternalApp { Command = "probe", ArchivePolicy = ArchivePolicy.None }, book: true, token: Token);
        Assert.Equal(f.Zip, Assert.Single(Assert.Single(platform.Requests).Arguments));
    }
    [Fact]
    public async Task CloseWaitsForLatePlatformResultAndPreventsNewSubmission()
    {
        using var f = new Fixture(); var state = await State(f); var op = f.Operation(state);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var platform = new Platform { Pending = async _ => { entered.TrySetResult(); await release.Task; } }; op.AttachExternalApplications(platform, "/tmp/NeeView"); await op.OpenAsync(f.Images, Token);
        var run = op.OpenExternalApplicationAsync(new ExternalApp { Command = "probe" }, token: Token); await entered.Task;
        var close = op.DisposeAsync().AsTask(); await Task.Delay(20, Token); Assert.False(close.IsCompleted); Assert.NotNull(op.Book);
        await op.OpenExternalApplicationAsync(new ExternalApp(), token: Token); Assert.Single(platform.Requests);
        release.TrySetResult(); await run; await close; Assert.Null(op.Book); Assert.False(op.IsOpeningExternalApplication);
    }
    [Fact]
    public async Task NeeViewPlaceholderSavesBeforeLaunchAndFailedSaveCanRetry()
    {
        using var f = new Fixture(); var state = await State(f); await using var op = f.Operation(state); var platform = new Platform(); op.AttachExternalApplications(platform, "/tmp/NeeViewMac"); await op.OpenAsync(f.Images, Token);
        var block = Path.Combine(f.State, "UserSetting.json.tmp"); Directory.CreateDirectory(block);
        try { await op.OpenExternalApplicationAsync(new ExternalApp { Command = "{NeeView}" }, token: Token); Assert.Empty(platform.Requests); Assert.Contains("失败", op.Error); }
        finally { Directory.Delete(block); }
        await op.OpenExternalApplicationAsync(new ExternalApp { Command = "$NeeView" }, token: Token);
        Assert.Equal("/tmp/NeeViewMac", Assert.Single(platform.Requests).Command); Assert.True(File.Exists(Path.Combine(f.State, "UserSetting.json")));
    }
    [Fact]
    public async Task ExternalCollectionPersistsUnknownFieldsWithoutExpandingDefaults()
    {
        using var f = new Fixture(); var state = await State(f); await using var op = f.Operation(state);
        await op.ApplyOptionsAsync(() => { }, (100, TimeSpan.FromDays(30)));
        var saved = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(f.State, "UserSetting.json"), Token))!;
        Assert.Null(saved["Config"]?["System"]?["ExternalAppCollection"]);
        Config.Current.System.ExternalAppCollection = JsonSerializer.Deserialize<ExternalAppCollection>("[{\"Name\":\"未来\",\"Future\":{\"Mode\":8}}]")!;
        await op.ApplyOptionsAsync(() => { }, (100, TimeSpan.FromDays(30)));
        var reloaded = new SaveData(f.State); await reloaded.LoadAsync(Token);
        Assert.Equal("未来", Config.Current.System.ExternalAppCollection.Single().Name);
        Assert.Equal(8, Config.Current.System.ExternalAppCollection.Single().ExtensionData!["Future"].GetProperty("Mode").GetInt32());
    }
}
