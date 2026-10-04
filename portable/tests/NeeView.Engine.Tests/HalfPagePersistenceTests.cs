using ImageMagick;
using NeeView;
using System.Text.Json.Nodes;

namespace NeeView.Engine.Tests;

public sealed class HalfPagePersistenceTests
{
    /// <summary>退役半页字段时，不能连带丢失同书启动快照的未知设置；扩展不跨书传播。</summary>
    [Fact]
    public async Task StartupUnknownFieldsSurviveSameBookSaveWithoutLeakingToOtherBooks()
    {
        using var fixture = new Fixture(); var token = TestContext.Current.CancellationToken;
        Directory.CreateDirectory(fixture.State);
        var last = new JsonObject { ["Path"] = fixture.Images, ["Page"] = "002.png", ["Props"] = "FutureSetting=keep",
            ["MacPagePart"] = 1, ["FutureItem"] = new JsonObject { ["Value"] = "keep" } };
        await File.WriteAllTextAsync(Path.Combine(fixture.State, "UserSetting.json"),
            new JsonObject { ["Config"] = new JsonObject { ["StartUp"] = new JsonObject { ["LastBookV2"] = last } } }.ToJsonString(), token);
        var state = new SaveData(fixture.State); await state.LoadAsync(token);
        await using var operation = fixture.Operation(state); await operation.RestoreLastAsync(token); await operation.SaveAsync();
        var path = Path.Combine(fixture.State, "UserSetting.json");
        var saved = JsonNode.Parse(await File.ReadAllTextAsync(path, token))!["Config"]!["StartUp"]!["LastBookV2"]!;
        Assert.Null(saved["MacPagePart"]); Assert.Equal("keep", saved["FutureItem"]!["Value"]!.GetValue<string>());
        Assert.Contains("FutureSetting=keep", saved["Props"]!.GetValue<string>());
        await operation.OpenAsync(fixture.Zip, token); await operation.SaveAsync();
        var switched = JsonNode.Parse(await File.ReadAllTextAsync(path, token))!["Config"]!["StartUp"]!["LastBookV2"]!;
        Assert.Null(switched["FutureItem"]); Assert.DoesNotContain("FutureSetting=keep", switched["Props"]!.GetValue<string>());
    }

    /// <summary>首半页恢复只针对普通打开；原反向页尾跨书仍进入末条目的末半页。</summary>
    [Theory]
    [InlineData(PageReadOrder.RightToLeft)]
    [InlineData(PageReadOrder.LeftToRight)]
    public async Task BackwardNextBookBoundaryStillEntersLastHalf(PageReadOrder order)
    {
        using var fixture = new Fixture(); var token = TestContext.Current.CancellationToken;
        var library = Path.Combine(fixture.Root, "library"); var first = Path.Combine(library, "A"); var second = Path.Combine(library, "B");
        Directory.CreateDirectory(first); Directory.CreateDirectory(second);
        using (var image = new MagickImage(MagickColors.Red, 1600, 900)) image.Write(Path.Combine(first, "wide.png"));
        File.Copy(Path.Combine(fixture.Images, "001.png"), Path.Combine(second, "001.png"));
        var state = new SaveData(fixture.State); await state.LoadAsync(token); await using var operation = fixture.Operation(state);
        await operation.OpenAsync(first, token);
        await operation.ApplySettingAsync(s => { s.PageMode = PageMode.SinglePage; s.BookReadOrder = order; s.IsSupportedDividePage = true; });
        await operation.SaveAsync(); await operation.OpenAsync(second, token); await operation.Bookshelf.SetPlaceAsync(library, second, token);
        Config.Current.Book.ResetNextBookPageMode = ResetNextBookPageMode.Continue;
        await operation.SetPageEndActionAsync(PageEndAction.NextBook); await operation.MoveAsync(-1);
        Assert.Equal(first, operation.Book!.Path); Assert.Equal(1, operation.Position.Part);
        Assert.Equal(order == PageReadOrder.RightToLeft ? PagePart.Left : PagePart.Right, Assert.Single(operation.Frame!.Elements).PagePart);
    }

    /// <summary>对照原 BookMemento：半页只属于当前阅读，切书和启动恢复均从条目首半页开始。</summary>
    /// <param name="zip">是否读取包含相同宽图的真实 ZIP。</param>
    /// <param name="order">决定首半页实际为左侧还是右侧的原阅读方向。</param>
    [Theory]
    [InlineData(false, PageReadOrder.RightToLeft)]
    [InlineData(false, PageReadOrder.LeftToRight)]
    [InlineData(true, PageReadOrder.RightToLeft)]
    [InlineData(true, PageReadOrder.LeftToRight)]
    public async Task ReopenRestoresEntryInsteadOfPersistedHalf(bool zip, PageReadOrder order)
    {
        using var fixture = new Fixture(); var token = TestContext.Current.CancellationToken;
        using (var image = new MagickImage(MagickColors.Red, 1600, 900)) image.Write(Path.Combine(fixture.Images, "004.png"));
        var splitZip = Path.Combine(fixture.Root, "split.cbz");
        System.IO.Compression.ZipFile.CreateFromDirectory(fixture.Images, splitZip);
        var path = zip ? splitZip : fixture.Images;
        var memento = new BookMemento { Path = path, Page = "004.png", PageMode = PageMode.SinglePage,
            BookReadOrder = order, IsSupportedDividePage = true };
        Directory.CreateDirectory(fixture.State);
        var legacy = new JsonObject { ["Path"] = path, ["Page"] = memento.Page, ["Props"] = memento.ToPropertiesString(),
            ["MacPagePart"] = 1, ["FutureItem"] = "keep" };
        await File.WriteAllTextAsync(Path.Combine(fixture.State, "History.json"),
            new JsonObject { ["Items"] = new JsonArray(legacy) }.ToJsonString(), token);
        var state = new SaveData(fixture.State); await state.LoadAsync(token);
        await using (var operation = fixture.Operation(state))
        {
            await operation.OpenAsync(path, token); AssertFirstHalf(operation, order);
            await operation.MoveAsync(1); Assert.Equal(1, operation.Position.Part);
            await operation.OpenAsync(fixture.Zip, token);
            await operation.OpenAsync(path, token); AssertFirstHalf(operation, order);
            await operation.MoveAsync(1); Assert.Equal(1, operation.Position.Part);
            await operation.SaveAsync();
        }
        var history = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(fixture.State, "History.json"), token))!;
        var saved = history["Items"]!.AsArray().OfType<JsonObject>().Single(e => e["Path"]!.GetValue<string>() == path);
        Assert.False(saved.ContainsKey("MacPagePart")); Assert.Equal("keep", saved["FutureItem"]!.GetValue<string>());
        var settings = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(fixture.State, "UserSetting.json"), token))!;
        Assert.Null(settings["Config"]!["StartUp"]!["LastBookV2"]!["MacPagePart"]);
        var fresh = new SaveData(fixture.State); await fresh.LoadAsync(token);
        await using var restarted = fixture.Operation(fresh); await restarted.RestoreLastAsync(token);
        AssertFirstHalf(restarted, order);
    }

    /// <summary>同时核对条目、逻辑 part 和真实页框裁剪方向，避免只验证保存字段。</summary>
    private static void AssertFirstHalf(BookOperation operation, PageReadOrder order)
    {
        Assert.Null(operation.Error); Assert.Equal("004.png", operation.Book!.CurrentPage!.EntryName);
        Assert.Equal(0, operation.Position.Part); Assert.True(operation.Book.Setting.IsSupportedDividePage);
        Assert.Equal(order, operation.Book.Setting.BookReadOrder);
        Assert.Equal(order == PageReadOrder.RightToLeft ? PagePart.Right : PagePart.Left, Assert.Single(operation.Frame!.Elements).PagePart);
    }
}
