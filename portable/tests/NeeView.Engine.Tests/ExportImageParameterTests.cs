namespace NeeView.Tests;

public class ExportImageParameterTests
{
    [Fact]
    public void DefaultsPreserveOriginalSemantics()
    {
        var p = new ExportImageParameter();
        Assert.Equal(BitmapImageFormat.Png, p.FileFormat);
        Assert.Equal("{Name}", p.FileNameFormat0);
        Assert.True(p.IsOriginalSize);
        Assert.Equal(ExportImageOverwriteMode.Confirm, p.OverwriteMode);
        Assert.Equal(BitmapImageFormat.Jpeg, new ExportImageCommandParameter().FileFormat);
        Assert.Equal(ExportImageOverwriteMode.AddNumber, new ExportBookParameter().OverwriteMode);
    }

    [Fact]
    public void OriginalKeepsLowercaseSourceExtensionAndBackslash()
    {
        var p = new ExportImageParameter { Mode = ExportImageMode.Original };
        var source = new ExportPageSource("book", 1, [new(new PageNameSource(1, "folder/Slash\\Cover.JPG"))]);
        Assert.Equal("Slash\\Cover.jpg", new DefaultExportImageFileNamePolicy(p).CreateFileName(source, 1));
    }

    [Fact]
    public void ViewUsesSelectedExtension()
    {
        var p = new ExportImageParameter { Mode = ExportImageMode.View, FileFormat = BitmapImageFormat.Png };
        var source = new ExportPageSource("book", 1, [new(new PageNameSource(1, "Cover.JPG"))]);
        Assert.Equal("Cover.png", new DefaultExportImageFileNamePolicy(p).CreateFileName(source, 1));
    }
    [Theory]
    [InlineData(1, "first_second")]
    [InlineData(-1, "second_first")]
    public void FileNameFieldsKeepOriginalDirectionPartAndPageSemantics(int direction, string sides)
    {
        var source = new ExportPageSource("/books/Book.cbz", direction,
            [new(new PageNameSource(2, "dir/first.jpg"), PagePart.Left), new(new PageNameSource(3, "dir/second.jpg"), PagePart.Right)]);
        Assert.Equal("Book_009_003_dir/first_first_" + PagePart.Left.ToSuffix(), ExportFileNameFormat.Format("{Book}_{Index:000}_{Page:000}_{EntryPath}_{Name}_{Part}", source, 9));
        Assert.Equal(sides, ExportFileNameFormat.Format("{NameL}_{NameR}", source, 9));
        Assert.Equal("first_second", ExportFileNameFormat.Format("{Name1}_{Name2}", source, 9));
    }
    [Fact]
    public void InvalidViewTemplateFallsBackToOriginalSeparateDefaults()
    {
        var source = ExportFileNameFormat.CreateDummyFileNameSource(2, 1);
        var p = new ExportImageParameter { Mode = ExportImageMode.View, FileNameFormat1 = "{Page" };
        // 原策略错误回退后双页格式是空，不能偷偷改为单页默认。
        var policy = new DefaultExportImageFileNamePolicy(p);
        Assert.Equal(".png", policy.CreateFileName(source, 1));
    }
    [Fact]
    public async Task JsonRoundTripKeepsSeparateDefaultsUnknownFieldsAndOriginalCommandParameters()
    {
        using var f = new NeeView.Engine.Tests.Fixture(); Directory.CreateDirectory(f.State);
        await File.WriteAllTextAsync(Path.Combine(f.State, "UserSetting.json"), """
        {"Format":"NeeView.UserSetting/46.3.0","Config":{"Book":{"ExportImageParameter":{"QualityLevel":90,"Future":5},"ExportBookParameter":{"BookType":"Folder","Future":6}}},"Commands":{"ExportImage":{"Parameter":{"Mode":"View","Future":7}}}}
        """, TestContext.Current.CancellationToken);
        var state = new SaveData(f.State); await state.LoadAsync(TestContext.Current.CancellationToken);
        Assert.Equal(90, Config.Current.Book.ExportImageParameter.QualityLevel);
        Assert.Equal(ExportBookType.Folder, Config.Current.Book.ExportBookParameter.BookType);
        var direct = state.GetCommandParameter<ExportImageCommandParameter>("ExportImage");
        Assert.Equal(ExportImageMode.View, direct.Mode); Assert.Equal(BitmapImageFormat.Jpeg, direct.FileFormat);
        Config.Current.Book.ExportImageParameter.QualityLevel = 80;
        await state.SaveAsync(null, TestContext.Current.CancellationToken);
        var json = System.Text.Json.Nodes.JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(f.State, "UserSetting.json"), TestContext.Current.CancellationToken))!;
        Assert.Null(json["Config"]!["Book"]!["ExportImageParameter"]!["QualityLevel"]);
        Assert.Equal(5, json["Config"]!["Book"]!["ExportImageParameter"]!["Future"]!.GetValue<int>());
        Assert.Equal(6, json["Config"]!["Book"]!["ExportBookParameter"]!["Future"]!.GetValue<int>());
        Assert.Equal(7, json["Commands"]!["ExportImage"]!["Parameter"]!["Future"]!.GetValue<int>());
        var reload = new SaveData(f.State); await reload.LoadAsync(TestContext.Current.CancellationToken);
        Assert.Equal(80, Config.Current.Book.ExportImageParameter.QualityLevel);
        Assert.Equal(BitmapImageFormat.Jpeg, reload.GetCommandParameter<ExportImageCommandParameter>("ExportImage").FileFormat);
    }
    [Fact]
    public async Task ImportMapsAllActiveExportFoldersWithoutChangingLogicalNameTemplates()
    {
        using var f = new NeeView.Engine.Tests.Fixture(); Directory.CreateDirectory(f.State);
        await File.WriteAllTextAsync(Path.Combine(f.State, "UserSetting.json"), """
        {"Format":"NeeView.UserSetting/46.3.0","Config":{"Book":{"ExportImageParameter":{"ExportFolder":"P:\\Books\\One","FileNameFormat0":"{EntryPath}"},"ExportBookParameter":{"ExportFolder":"P:\\Books\\Two"}}},"Commands":{"ExportImage":{"Parameter":{"Type":"ExportImageCommandParameter","Value":{"ExportFolder":"P:\\Books\\Three"}}}}}
        """, TestContext.Current.CancellationToken);
        await using var operation = f.Operation(new SaveData(f.State));
        var definitions = new CommandTable(operation).Definitions;
        var service = new ProfileImportService(new NeeView.Backends.ProfileImportReader(), definitions, new HashSet<string>());
        var preview = await service.PreviewAsync(new(f.State, ProfileImportSourceKind.Directory), [new(@"P:\Books", "/exports")], TestContext.Current.CancellationToken);
        Assert.Equal(3, preview.Paths.Count(p => p.Result.StartsWith("/exports/", StringComparison.Ordinal)));
    }
}
