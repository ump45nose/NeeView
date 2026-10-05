using System.Text.Json.Nodes;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using NeeView.MacOS.ViewModels;
using NeeView.MacOS.Views;
using NeeView.PageFrames;
using NeeView.StringTemplate;
namespace NeeView.Engine.Tests;

public sealed class WindowTitleTests
{
    /// <summary>分割前进/后退、左右阅读及整页切换由原页框决定L/R，排序后的索引及时反映。</summary>
    [Theory]
    [InlineData(PageReadOrder.LeftToRight, 1, "L", "R")]
    [InlineData(PageReadOrder.RightToLeft, 1, "R", "L")]
    [InlineData(PageReadOrder.LeftToRight, -1, "L", "R")]
    [InlineData(PageReadOrder.RightToLeft, -1, "R", "L")]
    public void TitleUsesActualFramePart(PageReadOrder order, int move, string first, string second)
    {
        Config.SetCurrent(new()); var source = new DummyArchive();
        var page = new Page(new(source) { RawEntryName = "内部/003.png" });
        page.Content.PageDataSource = new(new(1600, 900)); page.Content.HasSize = true;
        var setting = new BookSettingConfig { BookReadOrder = order, IsSupportedDividePage = true };
        var book = new Book(source, [page], setting); book.CurrentPage = page;
        var context = new PageFrameContext(setting, Config.Current);
        var factory = new PageFrameFactory(context, new BookContext(book.Pages), new ContentSizeCalculator(context));
        var formatter = new TitleStringFormatter.WindowFormatter();
        Assert.Equal($"fixture (1 ({first}) / 1) - 内部 > 003.png", formatter.Format(book, factory.CreatePageFrame(new(0, 0), move)));
        Assert.Equal($"fixture (1 ({second}) / 1) - 内部 > 003.png", formatter.Format(book, factory.CreatePageFrame(new(0, 1), move)));
        Config.Current.WindowTitle.WindowTitleFormat1 = "{Page:000}{Part:(#)} / {PageMax} {Future}";
        Assert.Equal($"001({second}) / 1 {{Future}}", formatter.Format(book, factory.CreatePageFrame(new(0, 1), move)));
        setting.IsSupportedDividePage = false;
        Assert.Equal("001 / 1 {Future}", formatter.Format(book, factory.CreatePageFrame(new(0, 0), 1)));
    }
    /// <summary>原双页L/R后缀按视觉方向选真实元素，dummy不决定格式。</summary>
    [Theory]
    [InlineData(PageReadOrder.LeftToRight, "001.png | 002.png")]
    [InlineData(PageReadOrder.RightToLeft, "002.png | 001.png")]
    public void PairedTitleUsesOriginalVisualDirection(PageReadOrder order, string expected)
    {
        Config.SetCurrent(new()); Config.Current.WindowTitle.WindowTitleFormat2 = "{NameL} | {NameR}";
        var source = new DummyArchive(); var pages = Enumerable.Range(0, 2).Select(i => new Page(new(source) { RawEntryName = $"{i + 1:000}.png" }) { Index = i }).ToList();
        foreach (var page in pages) { page.Content.PageDataSource = new(new(800, 1200)); page.Content.HasSize = true; }
        var setting = new BookSettingConfig { PageMode = PageMode.WidePage, BookReadOrder = order };
        var book = new Book(source, pages, setting); book.CurrentPage = pages[0];
        var context = new PageFrameContext(setting, Config.Current);
        var frame = new PageFrameFactory(context, new BookContext(pages), new ContentSizeCalculator(context)).CreatePageFrame(new(0, 0), 1)!;
        Assert.Equal(expected, new TitleStringFormatter.WindowFormatter().Format(book, frame));
        Assert.DoesNotContain("|", new TitleStringFormatter.WindowFormatter().Format(book, frame, false));
    }
    /// <summary>原窗口格式与未知字段沿同一JSON事务往返；无效格式回退且不丢原文。</summary>
    [Fact]
    public async Task TitleSettingsRoundTripUnknownFieldsAndInvalidFormat()
    {
        using var fixture = new Fixture(); Directory.CreateDirectory(fixture.State); var file = Path.Combine(fixture.State, "UserSetting.json");
        await File.WriteAllTextAsync(file, """{"Config":{"WindowTitle":{"WindowTitleFormat1":"{Book} {Page}{Part:(#)}/{PageMax}","Future":12}}}""", TestContext.Current.CancellationToken);
        var state = new SaveData(fixture.State); await state.LoadAsync(TestContext.Current.CancellationToken); var operation = fixture.Operation(state);
        try
        {
            await operation.OpenAsync(fixture.Images, token: TestContext.Current.CancellationToken);
            var formatter = new TitleStringFormatter.WindowFormatter(); Assert.Equal("图片 1/5", formatter.Format(operation.Book, operation.Frame));
            Config.Current.WindowTitle.WindowTitleFormat1 = "invalid {";
            Assert.StartsWith("图片 (1 / 5)", formatter.Format(operation.Book, operation.Frame));
            await state.SaveAsync(operation.Book, TestContext.Current.CancellationToken);
            var saved = JsonNode.Parse(await File.ReadAllTextAsync(file, TestContext.Current.CancellationToken))!["Config"]!["WindowTitle"]!;
            Assert.Equal(12, saved["Future"]!.GetValue<int>()); Assert.Equal("invalid {", saved["WindowTitleFormat1"]!.GetValue<string>());
        }
        finally { await operation.DisposeAsync(); }
    }
    /// <summary>正式窗口绑定随原导航刷新；不改底部临时选择页号。</summary>
    [AvaloniaFact]
    public async Task ActualWindowTitleTracksCommittedNavigation()
    {
        using var fixture = new Fixture(); var state = new SaveData(fixture.State); await state.LoadAsync(TestContext.Current.CancellationToken);
        var operation = fixture.Operation(state); var model = new ReaderWorkspaceViewModel(operation, new CommandTable(operation), state);
        var main = new MainWindow(); main.Bind(model, new BitmapFactory(new NeeView.Backends.MagickImageDecoder()), new NoPlatform()); main.Show();
        try
        {
            await main.OpenAsync(fixture.Zip); Dispatcher.UIThread.RunJobs(); Assert.Contains("(1 / 5)", main.Title);
            await operation.MoveAsync(1); Dispatcher.UIThread.RunJobs(); Assert.Contains("(2 / 5)", main.Title);
            operation.PageSelector.SetSelectedIndex(this, 4, true); model.RefreshSelection(); Dispatcher.UIThread.RunJobs();
            Assert.Contains("(2 / 5)", main.Title); // 临时选择不能改变实际标题位置。
            await operation.ApplySettingAsync(e => e.SortMode = PageSortMode.FileNameDescending); Dispatcher.UIThread.RunJobs();
            Assert.Contains("(4 / 5)", main.Title); Assert.Contains("002.png", main.Title);
        }
        finally { await main.PrepareShutdownAsync(); main.Close(); }
    }
    [Theory]
    [InlineData(" (#)", "R", " (R)")]
    [InlineData(" (#)", "", "")]
    [InlineData("\\#:#", "L", "#:L")]
    [InlineData("#\\", "L", "L")]
    [InlineData("\\n#\\\\", "L", "nL\\")]
    [InlineData("/ > ", "内部/003.png", "内部 > 003.png")]
    [InlineData("/ > ", "含\\反斜杠/003.png", "含\\反斜杠 > 003.png")]
    public void OriginalStringFormatting(string format, string value, string expected) => Assert.Equal(expected, StringFormatTools.FormatValue(format, value));
    private sealed class NoPlatform : IPlatformService
    {
        public Task RevealAsync(string path, CancellationToken token = default) => throw new NotSupportedException();
        public Task TrashAsync(string path, CancellationToken token = default) => throw new NotSupportedException();
    }
}
