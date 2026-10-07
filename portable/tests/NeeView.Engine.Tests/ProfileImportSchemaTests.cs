using System.Text.Json;
using System.Text.Json.Nodes;
using NeeView.MacOS.ViewModels;

namespace NeeView.Engine.Tests;

public sealed class ProfileImportSchemaTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private static IReadOnlyList<CommandDefinition> Definitions()
    {
        using var stream = typeof(CommandTable).Assembly.GetManifestResourceStream("NeeView.Command.command-manifest.json")!;
        return JsonSerializer.Deserialize<List<CommandDefinition>>(stream)!;
    }

    private static ProfileImportService Service(IReadOnlyDictionary<string, string> files) =>
        new(new Reader(files), Definitions(), new HashSet<string>());

    private sealed class Reader(IReadOnlyDictionary<string, string> files) : IProfileImportReader
    {
        public Task<ProfileImportBundle> ReadAsync(ProfileImportSource source, CancellationToken token) =>
            Task.FromResult(new ProfileImportBundle(files, []));
    }

    private static Dictionary<string, string> ForkFiles() => new()
    {
        ["UserSetting.json"] = """{"Format":"NeeView.UserSetting/1.0.0","Config":{"FutureSetting":{"Keep":true}},"Commands":{"Future":{"Shortcut":"Ctrl+K"}},"UnknownRoot":{"X":7}}""",
        ["History.json"] = """{"Format":"NeeView.History/1.0.0","Items":[{"Path":"/fork/book.cbz","Unknown":42}]}""",
        ["Bookmark.json"] = """{"Format":"NeeView.Bookmark/1.0.0","Nodes":{"Children":[{"Name":"Future","Unknown":true}]}}""",
        ["Foldres.json"] = """{"Format":"NeeView.Folders/1.0.0","Folders":[{"Place":"/fork"}],"Unknown":1}""",
        ["QuicAccess.json"] = """{"Format":"NeeView.QuickAccess/1.0.0","Items":[{"Name":"Fork","Unknown":true}]}"""
    };

    [Theory]
    [InlineData(ProfileImportSourceKind.Directory)]
    [InlineData(ProfileImportSourceKind.Backup)]
    public async Task ExplicitForkSchemaPreviewsDirectoryAndBackupWithModernShape(ProfileImportSourceKind kind)
    {
        var files = ForkFiles();
        var preview = await Service(files).PreviewAsync(new("synthetic", kind, ProfileImportSchema.ConfirmedNeeView46_3Fork), [], TestContext.Current.CancellationToken);
        Assert.All(ProfileImportFiles.Names, name => Assert.NotNull(preview.GetDocument(name)));
        Assert.Equal("NeeView/46.3.0", preview.GetDocument("UserSetting.json")!["Format"]!.GetValue<string>());
        Assert.Equal("NeeView/46.3.4340", preview.GetDocument("UserSetting.json")!["MacImportedSourceSchema"]!.GetValue<string>());
        Assert.Equal("NeeView.UserSetting/1.0.0", preview.GetDocument("UserSetting.json")!["MacImportedSourceFormat"]!.GetValue<string>());
        Assert.Equal("true", preview.GetDocument("UserSetting.json")!["Config"]!["FutureSetting"]!["Keep"]!.ToString().ToLowerInvariant());
        Assert.Contains(preview.Notices, n => n.Contains("明确确认的46.3 fork"));
        Assert.Equal(5, preview.Files.Count(f => f.Present));
    }

    [Fact]
    public async Task ForkSchemaIsRejectedByDefaultAndWrongNamespaceAndUnknownVersionStayRejected()
    {
        var files = ForkFiles();
        var defaultPreview = await Service(files).PreviewAsync(new("synthetic", ProfileImportSourceKind.Directory), [], TestContext.Current.CancellationToken);
        Assert.Throws<InvalidDataException>(() => defaultPreview.CreateRequest(new(true, true, true, true, true)));

        files["History.json"] = files["History.json"].Replace("NeeView.History/1.0.0", "Other/1.0.0");
        var wrong = await Service(files).PreviewAsync(new("synthetic", ProfileImportSourceKind.Directory, ProfileImportSchema.ConfirmedNeeView46_3Fork), [], TestContext.Current.CancellationToken);
        Assert.Throws<InvalidDataException>(() => wrong.CreateRequest(new(true, true, true, true, true)));

        files["History.json"] = files["History.json"].Replace("Other/1.0.0", "NeeView.History/47.0.0");
        var future = await Service(files).PreviewAsync(new("synthetic", ProfileImportSourceKind.Directory, ProfileImportSchema.ConfirmedNeeView46_3Fork), [], TestContext.Current.CancellationToken);
        Assert.Throws<InvalidDataException>(() => future.CreateRequest(new(true, true, true, true, true)));
    }

    [Theory]
    [InlineData("UserSetting.json", "{\"Commands\":{}}")]
    [InlineData("UserSetting.json", "{\"Config\":{},\"Commands\":[]}")]
    [InlineData("History.json", "{\"Items\":{}}")]
    [InlineData("Bookmark.json", "{\"Nodes\":[]}")]
    [InlineData("Foldres.json", "{\"Folders\":{}}")]
    [InlineData("QuicAccess.json", "{\"Items\":{}}")]
    public async Task ExplicitSourceConfirmationCannotBypassModernRootShape(string name, string invalid)
    {
        var files = ForkFiles(); var raw = JsonNode.Parse(invalid)!.AsObject();
        raw["Format"] = JsonNode.Parse(files[name])!["Format"]!.DeepClone(); files[name] = raw.ToJsonString();
        // 错误类型可能在只读解析时立即拒绝；缺字段候选也不能进入应用事务。
        var error = await Record.ExceptionAsync(async () =>
        {
            var preview = await Service(files).PreviewAsync(new("synthetic", ProfileImportSourceKind.Directory, ProfileImportSchema.ConfirmedNeeView46_3Fork), [], Token);
            preview.CreateRequest(new(true, true, true, true, true));
        });
        Assert.IsType<InvalidDataException>(error);
    }

    [Fact]
    public async Task ConfirmationChangeInvalidatesCandidateAndNewSourceResetsConfirmation()
    {
        using var model = new ProfileImportViewModel(Service(ForkFiles()));
        await model.SelectSourceAsync(new("fork", ProfileImportSourceKind.Directory, ProfileImportSchema.ConfirmedNeeView46_3Fork));
        Assert.NotNull(await model.CreateRequestAsync());
        model.ConfirmedForkSchema = false;
        Assert.Null(model.Preview); Assert.Null(await model.CreateRequestAsync());
        await model.RefreshAsync(); Assert.Null(await model.CreateRequestAsync());
        model.ConfirmedForkSchema = true; await model.RefreshAsync(); Assert.NotNull(await model.CreateRequestAsync());
        await model.SelectSourceAsync(new("other", ProfileImportSourceKind.Directory));
        Assert.False(model.ConfirmedForkSchema); Assert.Null(await model.CreateRequestAsync());
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("NeeView.QuickAccess/1.0.0", true)]
    [InlineData("NeeView.QuickAccess/47.0.0", false)]
    [InlineData("Other/1.0.0", false)]
    public async Task EmbeddedQuickAccessIsValidatedSeparatelyFromConfirmedBookmark(string? format, bool valid)
    {
        var files = ForkFiles(); files.Remove("QuicAccess.json");
        var bookmark = JsonNode.Parse(files["Bookmark.json"])!.AsObject();
        var quick = new JsonObject { ["Items"] = new JsonArray(new JsonObject { ["Path"] = "/fork" }) };
        if (format is not null) quick["Format"] = format;
        bookmark["QuickAccess"] = quick; files["Bookmark.json"] = bookmark.ToJsonString();
        var preview = await Service(files).PreviewAsync(new("synthetic", ProfileImportSourceKind.Directory, ProfileImportSchema.ConfirmedNeeView46_3Fork), [], Token);
        var selection = new ProfileImportSelection(false, false, true);
        if (valid) Assert.NotNull(preview.CreateRequest(selection));
        else Assert.Throws<InvalidDataException>(() => preview.CreateRequest(selection));
        Assert.Equal(format, JsonNode.Parse(files["Bookmark.json"])!["QuickAccess"]?["Format"]?.GetValue<string>());
    }

    [Fact]
    public async Task ConfirmedForkAppliesSavesAndReopensWithoutAncientMigrationOrSourceMutation()
    {
        var files = ForkFiles(); var originals = files.ToDictionary(p => p.Key, p => p.Value);
        var preview = await Service(files).PreviewAsync(new("synthetic", ProfileImportSourceKind.Directory, ProfileImportSchema.ConfirmedNeeView46_3Fork), [], Token);
        var target = Path.Combine(Path.GetTempPath(), "neeview-fork-import-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(target);
        try
        {
            var state = new SaveData(target); await state.LoadAsync(Token);
            var result = await state.ApplyProfileImportAsync(preview.CreateRequest(new(true, true, true, true, true)), Token);
            var next = new SaveData(target); await next.LoadAsync(Token); await next.SaveAsync(null, Token);
            var written = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(target, "UserSetting.json"), Token))!;
            Assert.Equal("NeeView.UserSetting/1.0.0", written["MacImportedSourceFormat"]!.GetValue<string>());
            Assert.Equal(7, written["UnknownRoot"]!["X"]!.GetValue<int>());
            Assert.True(written["Config"]!["FutureSetting"]!["Keep"]!.GetValue<bool>());
            Assert.Equal("Ctrl+K", written["Commands"]!["Future"]!["Shortcut"]!.GetValue<string>());
            Assert.Single(next.HistoryEntries);
            Assert.Equal(42, JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(target, "History.json"), Token))!["Items"]![0]!["Unknown"]!.GetValue<int>());
            Assert.All(originals, p => Assert.Equal(p.Value, files[p.Key]));
            await next.RestoreProfileImportAsync(result.BackupDirectory, Token);
            Assert.All(ProfileImportFiles.Names, n => Assert.False(File.Exists(Path.Combine(target, n))));
        }
        finally { Directory.Delete(target, true); }
    }
}
