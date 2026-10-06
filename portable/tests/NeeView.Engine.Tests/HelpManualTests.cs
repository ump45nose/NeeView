using System.Net;
using System.Text.RegularExpressions;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using NeeLaboratory.IO.Search;
using NeeView.Backends;
using NeeView.MacOS.ViewModels;
using NeeView.MacOS.Views;
namespace NeeView.Engine.Tests;

public sealed class HelpManualTests
{
    [Fact]
    public void SearchManualPreservesTemplateEightTablesAliasesAndMetadataBoundary()
    {
        var html = SearchOptionManual.CreateSearchOptionManual();
        Assert.StartsWith("<!DOCTYPE html>", html); Assert.EndsWith("</html>\n", html);
        Assert.Equal(8, Regex.Matches(html, "<table>").Count); Assert.DoesNotContain("[[", html); Assert.DoesNotContain("@_SearchManual", html);
        foreach (var option in new[] { "/and", "/or", "/not", "/date", "/size", "/bookmark", "/history", "/m0", "/exact", "/re", "/since", "/until", "/p.meta.[key]" }) Assert.Contains(option, html);
        foreach (var key in HelpText.MetadataKeys) Assert.Contains("<td>" + key + "</td>", html);
        Assert.Contains("元数据和评分搜索尚未迁移", html); Assert.Contains("/since 2019-04-01 /until 2019-05-01", html);
        // 真正的profile选项必须全部进入别名展开表，不能维护另一份过时清单。
        var context = new SearchContext().AddProfile(new DateSearchProfile()).AddProfile(new SizeSearchProfile()).AddProfile(new BookSearchProfile()).AddProfile(new PageSearchProfile());
        foreach (var alias in context.KeyAlias) { Assert.Contains(alias.Key, html); Assert.Contains(WebUtility.HtmlEncode(string.Join(" ", alias.Value)), html); }
        Assert.DoesNotContain("<script", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task MainMenuUsesOriginalGroupsRemarksAndRetainsUnmigratedCommands()
    {
        using var f = new Fixture(); var state = new SaveData(f.State); await state.LoadAsync(TestContext.Current.CancellationToken);
        await using var op = f.Operation(state); var commands = new CommandTable(op); var root = MenuTree.CreateDefault();
        var html = MainMenuManual.CreateMainMenuManual(commands.Definitions, commands.IsAvailable, true, "test-build");
        Assert.Equal(root.Children!.Count, Regex.Matches(html, "<h3>").Count);
        foreach (var group in root.Children) Assert.Contains("<h3>" + WebUtility.HtmlEncode(group.Name) + "</h3>", html);
        Assert.Contains("Version test-build", html); Assert.Contains("尚未迁移", html);
        Assert.Contains(HelpText.GetString("NextPageCommand.Remarks"), html);
        Assert.DoesNotContain("@Search", html); Assert.DoesNotContain("Version", MainMenuManual.CreateMainMenuManual(commands.Definitions, commands.IsAvailable));
    }

    [Fact]
    public void DynamicMenuAndVersionTextCannotBecomeHtmlOrExpandResourceMarkers()
    {
        var label = "<img src=x onerror=alert(1)> & @Homepage.DotNetDateTime";
        var root = new MenuNode { Children = [new("组<script>", MenuElementType.Group, null) { Children = [new(label, MenuElementType.Command, "unknown")] }] };
        var html = MainMenuManual.CreateMainMenuManual([], _ => false, true, "<script>alert(1)</script>", root);
        Assert.Contains(WebUtility.HtmlEncode(label), html); Assert.Contains("组&lt;script&gt;", html);
        Assert.DoesNotContain("<script>", html); Assert.DoesNotContain("<img", html);
        Assert.Contains("尚未迁移", html); Assert.Contains("<title>NeeView", html);
    }

    [Theory]
    [InlineData(HelpDocumentKind.MainMenu, "MainMenuList.html")]
    [InlineData(HelpDocumentKind.SearchOptions, "SearchOptions.html")]
    public async Task ServiceWritesUtf8AtomicFileAndOnlyOpensFileUri(HelpDocumentKind kind, string name)
    {
        using var f = new Fixture(); var platform = new Platform(); var temp = Path.Combine(f.Root, "help");
        var service = new HelpDocumentService(platform, temp); var unrelated = Path.Combine(f.Root, "untouched.txt"); File.WriteAllText(unrelated, "original");
        await service.OpenAsync(kind, "<!DOCTYPE html>中文<&>", TestContext.Current.CancellationToken);
        var uri = Assert.Single(platform.Opened); Assert.True(uri.IsFile); Assert.Equal(name, Path.GetFileName(uri.LocalPath));
        Assert.Equal("<!DOCTYPE html>中文<&>", await File.ReadAllTextAsync(uri.LocalPath, TestContext.Current.CancellationToken));
        Assert.DoesNotContain(Directory.EnumerateFiles(Path.GetDirectoryName(uri.LocalPath)!), p => p.EndsWith(".tmp"));
        await service.DisposeAsync(); Assert.False(File.Exists(uri.LocalPath)); Assert.Equal("original", File.ReadAllText(unrelated));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => service.OpenAsync(kind, "old", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CancelledServiceDoesNotOpenAndSystemFailureCanRetry()
    {
        using var f = new Fixture(); var platform = new Platform(); await using var service = new HelpDocumentService(platform, f.Root);
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.OpenAsync(HelpDocumentKind.MainMenu, "old", cancelled.Token)); Assert.Empty(platform.Opened);
        platform.Fail = true; await Assert.ThrowsAsync<IOException>(() => service.OpenAsync(HelpDocumentKind.MainMenu, "first", TestContext.Current.CancellationToken));
        platform.Fail = false; await service.OpenAsync(HelpDocumentKind.MainMenu, "next", TestContext.Current.CancellationToken);
        Assert.Equal("next", File.ReadAllText(Assert.Single(platform.Opened).LocalPath));
    }

    [AvaloniaFact]
    public async Task FormalTwoCommandsUseSamePlatformAndDoNotAlterReaderOrJson()
    {
        using var f = new Fixture(); var token = TestContext.Current.CancellationToken;
        var state = new SaveData(f.State); await state.LoadAsync(token); await using var op = f.Operation(state); await op.OpenAsync(f.Zip, token); await op.SaveAsync();
        var json = File.ReadAllBytes(Path.Combine(f.State, "UserSetting.json")); var book = op.Book; var position = op.Position;
        var platform = new Platform(); var window = new MainWindow(); window.Bind(new(op, new(op), state), new BitmapFactory(new MagickImageDecoder()), platform); window.Show();
        try
        {
            Assert.True(window.IsCommandAvailable("HelpMainMenu")); Assert.True(window.IsCommandAvailable("HelpSearchOption"));
            await window.ExecuteAsync("HelpMainMenu"); await window.ExecuteAsync("HelpSearchOption"); Assert.Equal(2, platform.Opened.Count);
            Assert.Equal(new[] { "MainMenuList.html", "SearchOptions.html" }, platform.Opened.Select(u => Path.GetFileName(u.LocalPath)));
            Assert.Contains("尚未迁移", File.ReadAllText(platform.Opened[0].LocalPath));
            Assert.Same(book, op.Book); Assert.Equal(position, op.Position); Assert.Equal(json, File.ReadAllBytes(Path.Combine(f.State, "UserSetting.json")));
        }
        finally { await window.PrepareShutdownAsync(); window.Close(); }
        Assert.All(platform.Opened, uri => Assert.False(File.Exists(uri.LocalPath)));
    }

    [AvaloniaFact]
    public async Task CloseCancelsAndWaitsForInFlightHelpOpenWithoutLateReaderUpdates()
    {
        using var f = new Fixture(); var state = new SaveData(f.State); await state.LoadAsync(TestContext.Current.CancellationToken);
        await using var op = f.Operation(state); var platform = new Platform { Pause = true }; var window = new MainWindow();
        window.Bind(new(op, new(op), state), new BitmapFactory(new MagickImageDecoder()), platform); window.Show();
        var opening = window.ExecuteAsync("HelpMainMenu");
        try
        {
            await platform.Started.Task.WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
            Assert.False(window.IsCommandAvailable("HelpMainMenu"));
            await window.PrepareShutdownAsync(); await opening; Assert.Empty(platform.Opened);
        }
        finally { window.Close(); }
    }

    private sealed class Platform : IPlatformService
    {
        public List<Uri> Opened { get; } = []; public bool Fail, Pause;
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task RevealAsync(string path, CancellationToken token) => Task.CompletedTask;
        public Task TrashAsync(string path, CancellationToken token) => Task.CompletedTask;
        public async Task OpenUriAsync(Uri uri, CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested(); Assert.True(uri.IsFile); Assert.True(File.Exists(uri.LocalPath));
            if (Fail) throw new IOException("test failure");
            Started.TrySetResult(); if (Pause) await Task.Delay(Timeout.Infinite, token);
            token.ThrowIfCancellationRequested(); Opened.Add(uri);
        }
    }
}
