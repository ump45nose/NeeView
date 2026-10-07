using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using NeeView.Backends;
using NeeView.MacOS.ViewModels;
using NeeView.MacOS.Views;
using NeeView.Windows;
namespace NeeView.Engine.Tests;

/// <summary>真实临时实体与正式窗口的 P5 收尾回归，不操作用户桌面。</summary>
public sealed class P5CompletionTests
{
    [Fact]
    public async Task ScriptRenamePreservesCurrentBookLocationAndChecksWritePermission()
    {
        using var f = new Fixture(); var state = new SaveData(f.State); await state.LoadAsync(TestContext.Current.CancellationToken);
        await using var op = f.Operation(state); using var images = new BitmapFactory(new MagickImageDecoder());
        var backend = new FileOperationBackend(Path.Combine(f.State,"Recovery")); op.AttachFileOperations(new(backend),backend,images);
        await op.OpenAsync(f.Images,TestContext.Current.CancellationToken); await op.JumpAsync(2);
        var item = (await op.GetFileMetadataAsync(f.Images,TestContext.Current.CancellationToken))!;
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => op.RenameFolderItemAsync(item,"改名",TestContext.Current.CancellationToken));
        Config.Current.System.IsFileWriteAccessEnabled = true;
        var result = await op.RenameFolderItemAsync(item,"改名",TestContext.Current.CancellationToken);
        Assert.Equal("改名",result.Name); Assert.Equal(result.Path,op.Book!.Path);
        Assert.Equal("003.png",op.Book.CurrentPage!.EntryName); Assert.False(Directory.Exists(f.Images));
        Assert.Equal(result.Path,state.LastBookPath); Assert.False(op.IsRenamingBook);
    }
    [Fact]
    public async Task ScriptRecursiveOverrideDoesNotChangeGlobalDefaults()
    {
        using var f = new Fixture(); Directory.CreateDirectory(Path.Combine(f.Images,"sub"));
        File.Copy(Path.Combine(f.Images,"001.png"),Path.Combine(f.Images,"sub","child.png"));
        var state = new SaveData(f.State); await state.LoadAsync(TestContext.Current.CancellationToken); await using var op = f.Operation(state);
        Config.Current.BookSettingDefault.IsRecursiveFolder = false;
        await op.OpenAsync(f.Images,true,TestContext.Current.CancellationToken);
        Assert.True(op.Book!.Setting.IsRecursiveFolder); Assert.Contains(op.Book.Pages,p=>p.EntryName.EndsWith("child.png"));
        Assert.False(Config.Current.BookSettingDefault.IsRecursiveFolder);
        await op.OpenAsync(f.Images,false,TestContext.Current.CancellationToken);
        Assert.False(op.Book!.Setting.IsRecursiveFolder); Assert.DoesNotContain(op.Book.Pages,p=>p.EntryName.EndsWith("child.png"));
    }
    [AvaloniaFact]
    public void DesktopStateRestoresHiddenDecorationsAndDipSizeAfterMinimize()
    {
        var w = new Window { Width=640,Height=480,WindowDecorations=WindowDecorations.None,Position=new(-40,20) }; w.Show();
        try
        {
            var size=w.ClientSize; var pos=w.Position;
            WindowDisplayState.Set(w,WindowStateEx.FullDesktop);
            Assert.Equal(WindowStateEx.FullDesktop,WindowDisplayState.Get(w));
            WindowDisplayState.Set(w,WindowStateEx.Minimized); Assert.Equal(WindowStateEx.Minimized,WindowDisplayState.Get(w));
            WindowDisplayState.Set(w,WindowStateEx.FullDesktop); Assert.Equal(WindowStateEx.FullDesktop,WindowDisplayState.Get(w));
            WindowDisplayState.Set(w,WindowStateEx.Normal);
            Assert.Equal(size,w.ClientSize); Assert.Equal(pos,w.Position); Assert.Equal(WindowDecorations.None,w.WindowDecorations);
            WindowDisplayState.Set(w,WindowStateEx.Maximized); WindowDisplayState.Set(w,WindowStateEx.Normal);
            Assert.Equal(WindowState.Normal,w.WindowState);
        }
        finally { w.Close(); }
    }
    [Fact]
    public void DesktopUnionIncludesNegativeOrigin()
    { Assert.Equal(new PixelRect(-1920,-100,4480,1540),WindowDisplayState.Union(new(-1920,0,1920,1080),new(0,-100,2560,1540))); }
    [AvaloniaFact]
    public void AutoScrollLongReleaseEscapeAndDisposeStopClock()
    {
        var view=new ReaderView(); view.BeginAutoScroll(new(10,20),true); Assert.True(view.IsAutoScrollMode);
        view.AutoScrollButtonReleased(true); Assert.True(view.IsAutoScrollMode);
        view.AutoScrollButtonReleased(false); Assert.False(view.IsAutoScrollMode);
        view.SetAutoScrollMode(true); Assert.True(view.HandleAutoScrollEscape()); Assert.False(view.HandleAutoScrollEscape());
        view.SetAutoScrollMode(true); view.Dispose(); Assert.False(view.IsAutoScrollMode);
    }
    [Fact]
    public async Task PageRecordSwitchesFilesFlushesInOrderAndReportsFailureWithoutBlockingReading()
    {
        using var f=new Fixture(); var state=new SaveData(f.State); await state.LoadAsync(TestContext.Current.CancellationToken); await using var op=f.Operation(state); await op.OpenAsync(f.Images,TestContext.Current.CancellationToken);
        var now=new DateTime(2026,10,7,12,0,0,DateTimeKind.Local); var errors=new List<Exception>();
        await using var recorder=new PageViewRecorder(()=>now,errors.Add); var one=Path.Combine(f.Root,"one.tsv"); var two=Path.Combine(f.Root,"two.tsv");
        recorder.Configure(new(){ IsSavePageViewRecord=true, PageViewRecordFilePath=one }); recorder.RecordBook(op.Book); recorder.RecordPages([op.Book!.Pages[0]]);
        now=now.AddSeconds(2.5); recorder.Configure(new(){ IsSavePageViewRecord=true,PageViewRecordFilePath=two }); await recorder.FlushAsync();
        var rows=File.ReadAllLines(one); Assert.Equal(2,rows.Length); Assert.Contains("\tFile\t2.5000000\t",rows[0]); Assert.Contains("\tBook\t2.5000000\t",rows[1]);
        recorder.RecordBook(op.Book); recorder.RecordPages([op.Book.Pages[1]]); now=now.AddSeconds(1);
        recorder.Configure(new(){IsSavePageViewRecord=false}); await recorder.FlushAsync(); Assert.Equal(2,File.ReadAllLines(two).Length); Assert.Empty(errors);
        recorder.Configure(new(){ IsSavePageViewRecord=true,PageViewRecordFilePath=Path.Combine(f.Root,"missing","bad.tsv") }); recorder.RecordBook(op.Book);
        recorder.RecordBook(null); await recorder.FlushAsync(); Assert.NotEmpty(errors); Assert.NotNull(op.Book);
    }
}
