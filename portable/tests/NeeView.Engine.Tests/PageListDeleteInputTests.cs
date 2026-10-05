using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using NeeView.Backends;
using NeeView.MacOS.ViewModels;
using NeeView.MacOS.Views;
namespace NeeView.Engine.Tests;

/// <summary>正式窗口的Tunnel/Bubble路由回归；测试废纸篓不代表AppKit真机验收。</summary>
public sealed class PageListDeleteInputTests
{
    /// <summary>列表Delete使用显式多选，正文Delete仍只处理主图；同一快捷键不能执行两次。</summary>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DeleteScopeFollowsActualWindowFocus(bool viewerFocus)
    {
        using var fixture = new Fixture(); var token = TestContext.Current.CancellationToken;
        var state = new SaveData(fixture.State); await state.LoadAsync(token);
        var operation = fixture.Operation(state); var platform = new TestTrash(Path.Combine(fixture.Root, "trash"));
        var model = new ReaderWorkspaceViewModel(operation, new CommandTable(operation), state);
        var window = new MainWindow(); window.Bind(model, new BitmapFactory(new MagickImageDecoder()), platform);
        window.Show();
        try
        {
            Config.Current.System.IsFileWriteAccessEnabled = true;
            await window.OpenAsync(fixture.Images); model.ShowPanel("PageListPanel");
            for (var i = 0; i < 3; i++)
            { Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); await Task.Delay(10, token); }
            var list = window.FindControl<ListBox>("PageList")!;
            var selected = operation.Book!.Pages.Take(2).ToArray();
            list.SelectedItems!.Clear(); foreach (var page in selected) list.SelectedItems.Add(page);
            Assert.Equal(2, list.SelectedItems.Count);
            string? confirmation = null;
            operation.ConfirmDeleteAsync = message => { confirmation = message; return Task.FromResult(true); };
            Assert.True(viewerFocus ? window.Viewer.Focus() : list.ContainerFromItem(selected[0])!.Focus());
            window.KeyPress(Key.Delete, RawInputModifiers.None, PhysicalKey.Delete, null);
            window.KeyRelease(Key.Delete, RawInputModifiers.None, PhysicalKey.Delete, null);
            var expected = viewerFocus ? 1 : 2;
            for (var i = 0; i < 200 && (platform.Calls.Count < expected || operation.IsDeletingFile); i++)
            { Dispatcher.UIThread.RunJobs(); await Task.Delay(10, token); }
            Assert.Equal(expected, platform.Calls.Count); Assert.Equal(5 - expected, operation.Book.Pages.Count);
            Assert.NotNull(confirmation); Assert.Contains("001.png", confirmation);
            if (viewerFocus) { Assert.DoesNotContain("002.png", confirmation); Assert.True(File.Exists(Path.Combine(fixture.Images, "002.png"))); }
            else { Assert.Contains("002.png", confirmation); Assert.False(File.Exists(Path.Combine(fixture.Images, "002.png"))); }
            Assert.True(File.Exists(Path.Combine(fixture.Images, "003.png")));
        }
        finally { await window.PrepareShutdownAsync(); window.Close(); }
    }
    private sealed class TestTrash(string root) : IPlatformService
    {
        public List<string> Calls { get; } = [];
        public Task RevealAsync(string path, CancellationToken token = default) => Task.CompletedTask;
        public Task TrashAsync(string path, CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested(); Directory.CreateDirectory(root);
            File.Move(path, Path.Combine(root, Path.GetFileName(path))); Calls.Add(path);
            return Task.CompletedTask;
        }
    }
}
