using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using ImageMagick;
using NeeView.Backends;
using NeeView.MacOS.Views;
namespace NeeView.Engine.Tests;

/// <summary>原首图像源语义、完整源像素和真实宿主的异步资源/错误边界；不写系统剪贴板。</summary>
public sealed class ImageCopyTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private sealed class Clipboard : IImageClipboard
    {
        public List<byte[]> Writes { get; } = [];
        public bool Fail;
        public Func<CancellationToken, Task>? Pending;
        public async Task WritePngAsync(byte[] png, CancellationToken token)
        {
            ImageClipboardCodec.ValidatePng(png); token.ThrowIfCancellationRequested();
            if (Pending is { } pending) await pending(token);
            if (Fail) throw new IOException("isolated image clipboard rejection");
            token.ThrowIfCancellationRequested(); Writes.Add(png.ToArray());
        }
    }
    private sealed class Platform : IPlatformService
    {
        public Task RevealAsync(string path, CancellationToken token = default) => throw new NotSupportedException();
        public Task TrashAsync(string path, CancellationToken token = default) => throw new NotSupportedException();
    }
    [Fact]
    public void InvalidPngAndOutputOverflowAreRejectedBeforePublication()
    {
        Assert.Throws<ArgumentException>(() => ImageClipboardCodec.ValidatePng([]));
        Assert.Throws<ArgumentException>(() => ImageClipboardCodec.ValidatePng(new byte[8]));
        using var stream = new ImageCopyEncoder.LimitedPngStream(8);
        stream.Write(new byte[8]); Assert.Throws<NotSupportedException>(() => stream.WriteByte(0));
        Assert.Throws<NotSupportedException>(() => stream.SetLength(9)); Assert.Equal(8, stream.Length);
    }
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DirectoryAndZipCopyActualSourceWithoutAnotherDecode(bool zip)
    {
        using var f = new Fixture(); var state = new SaveData(f.State); await state.LoadAsync(Token);
        var op = f.Operation(state); var factory = new BitmapFactory(new MagickImageDecoder()); var board = new Clipboard();
        var window = Window(op, factory, state, board);
        try
        {
            Assert.False(window.IsCommandAvailable("CopyImage")); await window.OpenAsync(zip ? f.Zip : f.Images); await Ready(window);
            var page = op.Frame!.Elements.First().Page; var creationCount = window.Viewer.BitmapCreationCount;
            await window.ExecuteAsync("CopyImage"); using var copied = new MagickImage(Assert.Single(board.Writes));
            Assert.Equal((uint)400, copied.Width); Assert.Equal((uint)600, copied.Height);
            Assert.Equal((byte)((page.Index + 1) * 35), copied.GetPixels().GetPixel(0, 0).GetChannel(0));
            Assert.Equal(creationCount, window.Viewer.BitmapCreationCount); Assert.Same(page, op.Frame.Elements.First().Page);
            Assert.True(window.IsCommandAvailable("CopyImage"));
        }
        finally { await window.PrepareShutdownAsync(); window.Close(); }
        Assert.Equal(0, factory.ByteCount); Assert.Equal(0, factory.GetDiagnostics().Leases);
    }
    [AvaloniaTheory]
    [InlineData(PageReadOrder.LeftToRight)]
    [InlineData(PageReadOrder.RightToLeft)]
    public async Task DoublePageKeepsOriginalElementOrderRatherThanScreenOrder(PageReadOrder order)
    {
        using var f = new Fixture(); var state = new SaveData(f.State); await state.LoadAsync(Token); var op = f.Operation(state);
        var window = Window(op, new(new MagickImageDecoder()), state, new Clipboard());
        try
        {
            await window.OpenAsync(f.Images); await op.ApplySettingAsync(s => { s.PageMode = PageMode.WidePage; s.BookReadOrder = order; s.IsSupportedSingleFirstPage = false; });
            await op.JumpAsync(2); await Ready(window);
            Assert.Equal(2, op.Frame!.Elements.Count); Assert.Same(op.Frame.Elements[0].Page, window.Viewer.CopyImagePage);
            using var copied = new MagickImage(await window.Viewer.CaptureCopyImageAsync(Token));
            Assert.Equal((byte)((op.Frame.Elements[0].Page.Index + 1) * 35), copied.GetPixels().GetPixel(0, 0).GetChannel(0));
        }
        finally { await window.PrepareShutdownAsync(); window.Close(); }
    }
    [AvaloniaFact]
    public async Task SplitRotationFlipAndCanvasBackgroundDoNotEnterCopiedPixels()
    {
        using var f = new Fixture();
        using (var source = new MagickImage(new MagickColor(200, 40, 80, 128), 600, 100)) source.Write(Path.Combine(f.Images, "001.png"));
        var state = new SaveData(f.State); await state.LoadAsync(Token); var op = f.Operation(state);
        var window = Window(op, new(new MagickImageDecoder()), state, new Clipboard());
        try
        {
            await window.OpenAsync(f.Images); await op.ApplySettingAsync(s => { s.IsSupportedDividePage = true; s.PageMode = PageMode.SinglePage; }); await op.JumpAsync(0); await Ready(window);
            Assert.Equal(.5, op.Frame!.Elements[0].ViewSizeCalculator.GetViewBox().Width);
            window.Viewer.Flip(true, true); await window.Viewer.RotateAsync(1, new ViewRotateCommandParameter { Angle = 90 });
            Config.Current.Background.BackgroundType = BackgroundType.Black;
            using var copied = new MagickImage(await window.Viewer.CaptureCopyImageAsync(Token));
            Assert.Equal((uint)600, copied.Width); Assert.Equal((uint)100, copied.Height);
            var pixel = copied.GetPixels().GetPixel(0, 0); Assert.InRange((int)pixel.GetChannel(0), 198, 202); Assert.InRange((int)pixel.GetChannel(3), 127, 129);
        }
        finally { await window.PrepareShutdownAsync(); window.Close(); }
    }
    [AvaloniaFact]
    public async Task ClipboardFailureCanRetryAndDoesNotBlockClosing()
    {
        using var f = new Fixture(); var state = new SaveData(f.State); await state.LoadAsync(Token); var op = f.Operation(state); var board = new Clipboard { Fail = true };
        var window = Window(op, new(new MagickImageDecoder()), state, board);
        try
        {
            await window.OpenAsync(f.Images); await Ready(window); await window.ExecuteAsync("CopyImage");
            Assert.Empty(board.Writes); Assert.Contains("isolated image", window.FindControl<TextBlock>("StatusField")!.Text);
            board.Fail = false; await window.ExecuteAsync("CopyImage"); Assert.Single(board.Writes);
        }
        finally { await window.PrepareShutdownAsync(); window.Close(); }
    }
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SwitchOrCloseCancelsQueuedWriteAndWaitsForRealCompletion(bool close)
    {
        using var f = new Fixture(); var state = new SaveData(f.State); await state.LoadAsync(Token); var op = f.Operation(state);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken captured = default;
        var board = new Clipboard { Pending = async token => { captured = token; entered.TrySetResult(); await release.Task; } };
        var factory = new BitmapFactory(new MagickImageDecoder()); var window = Window(op, factory, state, board);
        try
        {
            await window.OpenAsync(f.Images); await Ready(window); var copy = window.ExecuteAsync("CopyImage"); await entered.Task;
            Assert.False(window.IsCommandAvailable("CopyImage")); await window.ExecuteAsync("CopyImage");
            if (close)
            {
                var shutdown = window.PrepareShutdownAsync(); await Wait(() => captured.IsCancellationRequested);
                Assert.False(shutdown.IsCompleted); release.TrySetResult(); await copy; await shutdown; Assert.Equal(0, factory.ByteCount);
            }
            else
            {
                await window.OpenAsync(f.Zip); await Wait(() => captured.IsCancellationRequested); release.TrySetResult(); await copy;
                await Ready(window); Assert.True(window.IsCommandAvailable("CopyImage"));
            }
            Assert.Empty(board.Writes);
        }
        finally { release.TrySetResult(); await window.PrepareShutdownAsync(); window.Close(); }
    }
    [AvaloniaFact]
    public async Task AnimatedStrategyIsDisabledWhileStaticConfigurationCanCopyFirstFrame()
    {
        using var f = new Fixture(); var path = Path.Combine(f.Images, "000.gif"); await File.WriteAllBytesAsync(path, NeeView.Tests.AnimationFixture.Create(AnimatedImageType.Gif), Token);
        var state = new SaveData(f.State); await state.LoadAsync(Token); var op = f.Operation(state);
        var window = Window(op, new(new MagickImageDecoder()), state, new Clipboard());
        try
        {
            await window.OpenAsync(path); await window.Viewer.RefreshAsync(); await Wait(() => window.Viewer.AnimationCount == 1);
            Assert.False(window.IsCommandAvailable("CopyImage"));
            Config.Current.Image.Standard.IsAnimatedGifEnabled = false; await Ready(window); Assert.True(window.IsCommandAvailable("CopyImage"));
            using var copied = new MagickImage(await window.Viewer.CaptureCopyImageAsync(Token)); Assert.Equal((uint)2, copied.Width);
        }
        finally { await window.PrepareShutdownAsync(); window.Close(); }
    }
    [AvaloniaFact]
    public async Task MasonryRequiresExplicitSelectionAndUsesSelectedSource()
    {
        using var f = new Fixture(); var state = new SaveData(f.State); await state.LoadAsync(Token); var op = f.Operation(state);
        var board = new Clipboard(); var window = Window(op, new(new MagickImageDecoder()), state, board);
        try
        {
            await window.OpenAsync(f.Images); await op.SetBrowseModeAsync(BrowseLayoutMode.Masonry); await window.Viewer.RefreshAsync();
            await Wait(() => window.Viewer.DisplayCount > 0 && window.Viewer.BrowsePendingCount == 0); Assert.False(window.IsCommandAvailable("CopyImage"));
            var rect = window.Viewer.BrowseLayout!.Items[1]; var point = window.Viewer.TranslatePoint(new(rect.X + 15, rect.Y + 40), window)!.Value;
            window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left);
            await Wait(() => window.Viewer.BrowseSelection?.Index == 1); Assert.True(window.IsCommandAvailable("CopyImage"));
            await window.ExecuteAsync("CopyImage"); using var copied = new MagickImage(Assert.Single(board.Writes));
            Assert.Equal((byte)70, copied.GetPixels().GetPixel(0, 0).GetChannel(0));
        }
        finally { await window.PrepareShutdownAsync(); window.Close(); }
    }
    [AvaloniaFact]
    public async Task OriginalMenuAndControlShiftCUseActualPixelClipboard()
    {
        using var f = new Fixture(); var state = new SaveData(f.State); await state.LoadAsync(Token); var op = f.Operation(state);
        var board = new Clipboard(); var window = Window(op, new(new MagickImageDecoder()), state, board);
        try
        {
            await window.OpenAsync(f.Images); await Ready(window);
            var item = Find(window.FindControl<Menu>("MenuBar")!.Items.OfType<MenuItem>());
            Assert.NotNull(item); Assert.True(item.IsEnabled); Assert.DoesNotContain("尚未迁移", ToolTip.GetTip(item)?.ToString());
            window.Viewer.Focus();
            window.KeyPress(Key.C, RawInputModifiers.Control | RawInputModifiers.Shift, PhysicalKey.C, null);
            window.KeyRelease(Key.C, RawInputModifiers.Control | RawInputModifiers.Shift, PhysicalKey.C, null);
            await Wait(() => board.Writes.Count == 1); ImageClipboardCodec.ValidatePng(board.Writes[0]);
        }
        finally { await window.PrepareShutdownAsync(); window.Close(); }
        static MenuItem? Find(IEnumerable<MenuItem> items)
        { foreach (var item in items) { if (Equals(item.Tag, "CopyImage")) return item; if (Find(item.Items.OfType<MenuItem>()) is { } child) return child; } return null; }
    }
    private static MainWindow Window(BookOperation op, BitmapFactory factory, SaveData state, Clipboard board)
    { var window = new MainWindow(); window.Bind(new(op, new(op), state), factory, new Platform()); window.AttachImageClipboard(board); window.Show(); return window; }
    private static async Task Ready(MainWindow window)
    { Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); await window.Viewer.RefreshAsync(); await Wait(() => window.Viewer.DisplayCount > 0); }
    private static async Task Wait(Func<bool> condition)
    { for (int i = 0; i < 300 && !condition(); i++) { Dispatcher.UIThread.RunJobs(); await Task.Delay(10, Token); } Assert.True(condition()); }
}
