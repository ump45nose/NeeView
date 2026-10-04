using NeeView;
using NeeView.PageFrames;
using ImageMagick;
using BackendFactory = NeeView.Backends.ArchiveFactory;
/// <summary>原 Config 单例和 Headless 共用进程，测试按顺序执行。</summary>
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace NeeView.Engine.Tests;

public sealed class ReadingTests
{
    /// <summary>固定页框样本检查原生成器的双页、封面、宽图及奇数页规则。</summary>
    [Theory]
    [InlineData(false, false, 0, 2)]
    [InlineData(true, false, 0, 1)]
    [InlineData(false, true, 4, 1)]
    public void OriginalWideFrameRules(bool first, bool last, int index, int count)
    {
        Config.SetCurrent(new());
        var pages = Pages((800, 1200), (800, 1200), (800, 1200), (800, 1200), (800, 1200));
        var setting = new BookSettingConfig { PageMode = PageMode.WidePage, IsSupportedSingleFirstPage = first, IsSupportedSingleLastPage = last };
        var context = new PageFrameContext(setting, Config.Current);
        var frame = new PageFrameFactory(context, new BookContext(pages), new ContentSizeCalculator(context)).CreatePageFrame(new(index, 0), 1)!;
        Assert.Equal(count, frame.Elements.Count); Assert.Equal(index, frame.FrameRange.Min.Index);
    }
    /// <summary>宽页不能与相邻竖页配对，原页框生成器保留宽图优先规则。</summary>
    [Fact]
    public void LandscapePreventsPairing()
    {
        Config.SetCurrent(new()); var pages = Pages((800, 1200), (1800, 1000), (800, 1200));
        var context = new PageFrameContext(new() { PageMode = PageMode.WidePage }, Config.Current);
        var factory = new PageFrameFactory(context, new BookContext(pages), new ContentSizeCalculator(context));
        Assert.Single(factory.CreatePageFrame(new(0, 0), 1)!.Elements);
        Assert.Single(factory.CreatePageFrame(new(1, 0), 1)!.Elements);
    }
    /// <summary>分割页的两个 part 与原阅读方向一致。</summary>
    [Theory]
    [InlineData(PageReadOrder.RightToLeft, 0, PagePart.Right)]
    [InlineData(PageReadOrder.RightToLeft, 1, PagePart.Left)]
    [InlineData(PageReadOrder.LeftToRight, 0, PagePart.Left)]
    [InlineData(PageReadOrder.LeftToRight, 1, PagePart.Right)]
    public void DivideUsesOriginalParts(PageReadOrder order, int part, PagePart expected)
    {
        Config.SetCurrent(new()); var pages = Pages((1800, 1000));
        var context = new PageFrameContext(new() { IsSupportedDividePage = true, BookReadOrder = order }, Config.Current);
        var frame = new PageFrameFactory(context, new BookContext(pages), new ContentSizeCalculator(context)).CreatePageFrame(new(0, part), 1)!;
        Assert.Equal(1, frame.FrameRange.PartSize); Assert.Equal(expected, frame.Elements[0].PagePart);
        Assert.Equal(expected == PagePart.Left ? 0 : .5, frame.Elements[0].ViewSizeCalculator.GetViewBox().X);
    }
    /// <summary>原 Mix 按字段选择历史、默认和当前，不将恢复策略整体简化。</summary>
    [Fact]
    public void OriginalSettingPolicyMixesEachField()
    {
        var policy = new BookSettingPolicyConfig { PageMode = BookSettingSelectMode.Default, BookReadOrder = BookSettingSelectMode.Continue };
        var defaults = new BookSettingConfig { PageMode = PageMode.WidePage };
        var current = new BookSettingConfig { BookReadOrder = PageReadOrder.LeftToRight };
        var restored = new BookSettingConfig { IsSupportedSingleFirstPage = true };
        var actual = policy.Mix(defaults, current, restored, false);
        Assert.Equal(PageMode.WidePage, actual.PageMode); Assert.Equal(PageReadOrder.LeftToRight, actual.BookReadOrder); Assert.True(actual.IsSupportedSingleFirstPage);
    }
    /// <summary>原 Props 保存并解析页面名称、方向、排序种子及基准缩放。</summary>
    [Fact]
    public void OriginalMementoRoundTrip()
    {
        var memento = new BookMemento { PageMode = PageMode.WidePage, BookReadOrder = PageReadOrder.LeftToRight, IsSupportedDividePage = true, SortMode = PageSortMode.Random, SortSeed = 567, BaseScale = 1.12345, Page = "内部/001.png" };
        var actual = BookMemento.ParseWithProperties("book.cbz", memento.Page, memento.ToPropertiesString())!;
        Assert.Equal(memento.Page, actual.Page); Assert.Equal(memento.SortSeed, actual.SortSeed); Assert.Equal(memento.BaseScale, actual.BaseScale); Assert.Equal(memento.PageMode, actual.PageMode);
    }
    /// <summary>目录和 ZIP 使用真实后端、真实解码；帧步进与单页步进分别验证。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RealSourcesNavigateAndRestore(bool zip)
    {
        using var fixture = new Fixture(); var state = new SaveData(fixture.State); await state.LoadAsync(TestContext.Current.CancellationToken);
        Config.Current.BookSettingDefault.PageMode = PageMode.WidePage;
        var path = zip ? fixture.Zip : fixture.Images;
        await using (var operation = fixture.Operation(state))
        {
            await operation.OpenAsync(path, TestContext.Current.CancellationToken); Assert.Null(operation.Error); Assert.Equal(5, operation.Book!.Pages.Count);
            Assert.Equal(new[] { 0, 1 }, operation.Frame!.Elements.Select(e => e.Page.Index));
            await operation.MoveAsync(1, true); Assert.Equal(new[] { 1, 2 }, operation.Frame!.Elements.Select(e => e.Page.Index));
            await operation.MoveAsync(-1, true); Assert.Equal(0, operation.Book.CurrentPage!.Index);
            await operation.MoveAsync(1); Assert.Equal(new[] { 2, 3 }, operation.Frame!.Elements.Select(e => e.Page.Index));
            await operation.ApplySettingAsync(e => e.BookReadOrder = PageReadOrder.LeftToRight);
            await operation.SaveAsync();
        }
        var restored = new SaveData(fixture.State); await restored.LoadAsync(TestContext.Current.CancellationToken);
        await using var reopened = fixture.Operation(restored); await reopened.OpenAsync(path, TestContext.Current.CancellationToken);
        Assert.Null(reopened.Error); Assert.Equal("003.png", reopened.Book!.CurrentPage!.EntryName);
        Assert.Equal(PageReadOrder.LeftToRight, reopened.Book.Setting.BookReadOrder);
        await using var stream = await reopened.Book.Source.OpenEntryAsync(reopened.Book.CurrentPage.ArchiveEntry, TestContext.Current.CancellationToken);
        using var image = await new NeeView.Backends.MagickImageDecoder().DecodeAsync(stream, new(200, 300), TestContext.Current.CancellationToken);
        Assert.Equal(200 * 300 * 4, image.ByteCount);
    }
    /// <summary>图片打开定位自身，排序后保持 Page 身份；失败来源保留旧书。</summary>
    [Fact]
    public async Task ImageOpenSortAndFailedOpenKeepCurrentPage()
    {
        using var fixture = new Fixture(); var state = new SaveData(fixture.State); await state.LoadAsync(TestContext.Current.CancellationToken);
        await using var operation = fixture.Operation(state); await operation.OpenAsync(Path.Combine(fixture.Images, "003.png"), TestContext.Current.CancellationToken);
        var page = operation.Book!.CurrentPage;
        await operation.ApplySettingAsync(e => e.SortMode = PageSortMode.FileNameDescending);
        Assert.Same(page, operation.Book.CurrentPage);
        await operation.OpenAsync(Path.Combine(fixture.Root, "不存在.cbz"), TestContext.Current.CancellationToken);
        Assert.NotNull(operation.Error); Assert.Same(page, operation.Book.CurrentPage);
    }
    /// <summary>保留未知设置、未知 Props 和原 History 名称，补齐原 IsWide=false 的编码歧义。</summary>
    [Fact]
    public async Task JsonUnknownFieldsAndFalseWideSurvive()
    {
        using var fixture = new Fixture(); Directory.CreateDirectory(fixture.State);
        await File.WriteAllTextAsync(Path.Combine(fixture.State, "UserSetting.json"), """{"Format":"NeeView.UserSetting/46.3.0","Future":42,"Config":{"ImageEffect":{"Shader":"keep"},"BookSettingDefault":{"FutureFlag":true}}}""", TestContext.Current.CancellationToken);
        var history = new System.Text.Json.Nodes.JsonObject { ["Format"] = "NeeView.History/46.3.0", ["FutureRoot"] = true,
            ["Items"] = new System.Text.Json.Nodes.JsonArray(new System.Text.Json.Nodes.JsonObject { ["Path"] = fixture.Images, ["Page"] = "002.png", ["Props"] = "IsWide FutureRule=abc", ["FutureItem"] = 99 }) };
        await File.WriteAllTextAsync(Path.Combine(fixture.State, "History.json"), history.ToJsonString(), TestContext.Current.CancellationToken);
        var state = new SaveData(fixture.State); await state.LoadAsync(TestContext.Current.CancellationToken);
        await using (var operation = fixture.Operation(state))
        {
            await operation.OpenAsync(fixture.Images, TestContext.Current.CancellationToken); await operation.ApplySettingAsync(e => e.IsSupportedWidePage = false); await operation.SaveAsync();
        }
        var settingJson = System.Text.Json.Nodes.JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(fixture.State, "UserSetting.json"), TestContext.Current.CancellationToken))!;
        Assert.Equal(42, settingJson["Future"]!.GetValue<int>()); Assert.Equal("keep", settingJson["Config"]!["ImageEffect"]!["Shader"]!.GetValue<string>());
        Assert.True(settingJson["Config"]!["BookSettingDefault"]!["FutureFlag"]!.GetValue<bool>());
        var saved = System.Text.Json.Nodes.JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(fixture.State, "History.json"), TestContext.Current.CancellationToken))!;
        Assert.Contains("FutureRule=abc", saved["Items"]![0]!["Props"]!.GetValue<string>()); Assert.Equal(99, saved["Items"]![0]!["FutureItem"]!.GetValue<int>());
        var fresh = new SaveData(fixture.State); await fresh.LoadAsync(TestContext.Current.CancellationToken); Assert.False(fresh.Find(fixture.Images)!.IsSupportedWidePage);
    }
    /// <summary>损坏 JSON 不被保存流程覆盖。</summary>
    [Fact]
    public async Task CorruptJsonRemainsUntouched()
    {
        using var fixture = new Fixture(); Directory.CreateDirectory(fixture.State);
        var path = Path.Combine(fixture.State, "History.json"); await File.WriteAllTextAsync(path, "{invalid", TestContext.Current.CancellationToken);
        await Assert.ThrowsAnyAsync<System.Text.Json.JsonException>(() => new SaveData(fixture.State).LoadAsync(TestContext.Current.CancellationToken));
        Assert.Equal("{invalid", await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
    }
    /// <summary>完整清单保留数字分类名称；P1 未实现的命令不允许执行。</summary>
    [Fact]
    public async Task FullCommandManifestPreservesDeferredCommands()
    {
        using var fixture = new Fixture(); var state = new SaveData(fixture.State); await state.LoadAsync(TestContext.Current.CancellationToken);
        await using var operation = fixture.Operation(state); var table = new CommandTable(operation);
        Assert.Equal(235, table.Definitions.Count);
        Assert.Equal("Left,LeftClick", table.Definitions.Single(e => e.Name == "NextPage").Shortcut);
        for (int i = 1; i <= 9; i++) Assert.Contains(table.Definitions, e => e.Name == "MoveToDestinationFolder" + i);
        Assert.True(table.IsAvailable("UndoDestinationMove"));
        Assert.True(table.IsAvailable("DeleteFile"));
        Assert.True(table.IsAvailable("RenameBook"));
        Assert.False(table.IsAvailable("CutFile"));
        await Assert.ThrowsAsync<NotSupportedException>(() => table.ExecuteAsync("CutFile"));
    }
    /// <summary>直接构造尺寸样本，测试只依赖引擎和原页框生成器。</summary>
    private static List<Page> Pages(params (int Width, int Height)[] sizes)
    {
        var archive = new DummyArchive(); var pages = new List<Page>();
        foreach (var size in sizes)
        {
            var page = new Page(new(archive) { Id = pages.Count, RawEntryName = $"{pages.Count}.png" });
            // 尺寸设置通过真实探测入口的同一数据契约；测试程序集使用内部访问。
            page.Index = pages.Count; page.Content.PageDataSource = new(new(size.Width, size.Height)); page.Content.HasSize = true; pages.Add(page);
        }
        return pages;
    }
}

/// <summary>纯页框样本来源，不模拟文件操作成功。</summary>
internal sealed class DummyArchive() : Archive("fixture")
{
    public override Task<IReadOnlyList<ArchiveEntry>> GetEntriesAsync(CancellationToken token) => throw new NotSupportedException();
    public override Task<Stream> OpenEntryAsync(ArchiveEntry entry, CancellationToken token) => throw new NotSupportedException();
    public override ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

/// <summary>临时目录中的真实 PNG 和 ZIP，测试不修改用户图片。</summary>
internal sealed class Fixture : IDisposable
{
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "neeview-port-" + Guid.NewGuid().ToString("N"));
    public string Images => Path.Combine(Root, "图片");
    public string State => Path.Combine(Root, "state");
    public string Zip => Path.Combine(Root, "漫画.cbz");
    /// <summary>生成五张竖图和中文归档，覆盖真实原生解码。</summary>
    public Fixture()
    {
        Directory.CreateDirectory(Images);
        for (int i = 1; i <= 5; i++) { using var image = new MagickImage(new MagickColor((byte)(i * 35), (byte)(i * 25), (byte)(200 - i * 15)), 400, 600); image.Write(Path.Combine(Images, $"{i:000}.png")); }
        System.IO.Compression.ZipFile.CreateFromDirectory(Images, Zip);
    }
    /// <summary>装配测试使用的真实内容及图片后端。</summary>
    public BookOperation Operation(SaveData state) => new(new BackendFactory(), new NeeView.Backends.MagickImageDecoder(), state);
    /// <summary>测试完成后删除自建夹具，来源须先释放。</summary>
    public void Dispose() { Directory.Delete(Root, true); }
}
