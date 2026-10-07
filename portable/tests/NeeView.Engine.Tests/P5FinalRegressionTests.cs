using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Headless;
using Avalonia;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using NeeView.Backends;
using NeeView.MacOS.Views;
using NeeView.MacOS.ViewModels;
using NeeView.Windows;
namespace NeeView.Engine.Tests;

/// <summary>收尾中的真实实体、原JSON、Jint和唯一窗口回归；用户图片与桌面保持只读。</summary>
public sealed class P5FinalRegressionTests
{
    private sealed class Platform : IPlatformService
    {
        public Task RevealAsync(string path, CancellationToken token=default)=>Task.CompletedTask;
        public Task TrashAsync(string path, CancellationToken token=default)=>Task.CompletedTask;
    }
    private static async Task<(SaveData State, ReaderWorkspaceViewModel Model, MainWindow Window)> OpenAsync(Fixture f, Action<Config>? configure = null)
    {
        var state=new SaveData(f.State);await state.LoadAsync(TestContext.Current.CancellationToken);
        configure?.Invoke(Config.Current);
        var op=f.Operation(state);var images=new BitmapFactory(new MagickImageDecoder());var files=new FileOperationBackend(Path.Combine(f.State,"recovery"));
        op.AttachFileOperations(new(files),files,images);
        var model=new ReaderWorkspaceViewModel(op,new(op),state);var window=new MainWindow();window.Bind(model,images,new Platform());window.Show();
        await window.OpenAsync(f.Images);await PageListThumbnailTests.SettleAsync(window);return(state,model,window);
    }
    [AvaloniaFact]
    public async Task SettingsActionDiscardClosesBeforeHostActionAndKeepsConfig()
    {
        using var f=new Fixture();var(_,model,window)=await OpenAsync(f);
        try
        {
            var options=new SettingsWindow(model);options.Show(window);
            options.FindControl<CheckBox>("Wide")!.IsChecked=false;
            var navigation=options.FindControl<ListBox>("SettingsNavigation")!;
            Assert.All(navigation.Items.Cast<ListBoxItem>().Skip(15),x=>Assert.True(x.IsEnabled));
            navigation.SelectedIndex=15;Assert.True(options.FindControl<ScrollViewer>("EffectIntegrationSettings")!.IsVisible);
            options.FindControl<Button>("DiscardAndOpenEffects")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.False(options.IsVisible);Assert.Equal(SettingsAction.ImageEffects,options.RequestedAction);Assert.False(options.WasSaved);
            Assert.True(Config.Current.BookSetting.IsSupportedWidePage);
            var import=new SettingsWindow(model);import.Show(window);import.FindControl<Button>("DiscardAndOpenImport")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(SettingsAction.ProfileImport,import.RequestedAction);
        }
        finally{await window.PrepareShutdownAsync();window.Close();}
    }
    [AvaloniaFact]
    public async Task SettingsSaveFailureDoesNotTransferAction()
    {
        using var f=new Fixture();var(state,model,window)=await OpenAsync(f);var blocked=Path.Combine(f.State,"UserSetting.json.tmp");
        var settings=new SettingsWindow(model);settings.Show(window);
        try
        {
            await state.SynchronizeWritesAsync();Directory.CreateDirectory(blocked);
            settings.FindControl<Button>("SaveAndOpenImport")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            for(var i=0;i<300&&settings.FindControl<TextBlock>("Message")!.Text?.StartsWith("保存失败：")!=true;i++){Dispatcher.UIThread.RunJobs();await Task.Delay(10);}
            Assert.True(settings.IsVisible);Assert.False(settings.WasSaved);Assert.Equal(SettingsAction.None,settings.RequestedAction);
            Directory.Delete(blocked);settings.Close();
        }
        finally{if(Directory.Exists(blocked))Directory.Delete(blocked);settings.Close();await window.PrepareShutdownAsync();window.Close();}
    }
    [AvaloniaFact]
    public async Task ScriptItemRenameAndMetadataUseActualJintAndOriginalJson()
    {
        using var f=new Fixture();var(state,model,window)=await OpenAsync(f);
        try
        {
            await window.AttachScriptsAsync(new JintScriptRuntimeFactory(),(_,_)=>{},new System.Collections.Concurrent.ConcurrentDictionary<string,object?>());
            Config.Current.System.IsFileWriteAccessEnabled=true;await model.Operation.Bookshelf.SetPlaceAsync(f.Root,force:true);
            Assert.Contains(model.Folders,x=>x.Path==f.Images);
            await window.Scripts!.EvaluateAsync("var item=nv.Bookshelf.Items.find(x=>x.Name==='图片'); item.Name='renamed'; nv.Values.actualPath=item.Path; nv.Values.modified=item.LastWriteTime;",TestContext.Current.CancellationToken);
            var renamed=Path.Combine(f.Root,"renamed");
            Assert.True(Directory.Exists(renamed));Assert.False(Directory.Exists(f.Images));
            Assert.Equal(renamed,model.Operation.Book!.Path);
            await state.SaveAsync(model.Operation.Book);
            Assert.Equal(renamed,state.LastBookPath);Assert.Equal("001.png",state.GetLastBook()!.Page);
            Assert.Equal(renamed,await window.Scripts.EvaluateAsync("nv.Values.actualPath",TestContext.Current.CancellationToken));
            AssertScriptDate(Directory.GetLastWriteTime(renamed),await window.Scripts.EvaluateAsync("nv.Values.modified",TestContext.Current.CancellationToken));
        }
        finally{await window.PrepareShutdownAsync();window.Close();}
    }
    [Fact]
    public async Task CanceledScriptBookmarkRenameKeepsNodeAndPersistedName()
    {
        using var f = new Fixture(); var state = new SaveData(f.State); await state.LoadAsync(TestContext.Current.CancellationToken);
        await using var op = f.Operation(state);
        var node = await state.AddBookmarkFolderAsync(null, "before", TestContext.Current.CancellationToken);
        var item = new FolderItem(node.DisplayName, "bookmark:") { Bookmark = node };
        var before = await File.ReadAllTextAsync(Path.Combine(f.State, "Bookmark.json"), TestContext.Current.CancellationToken);
        using var canceled = new CancellationTokenSource(); canceled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => op.RenameFolderItemAsync(item, "after", canceled.Token));
        Assert.Equal("before", node.DisplayName);
        Assert.Equal(before, await File.ReadAllTextAsync(Path.Combine(f.State, "Bookmark.json"), TestContext.Current.CancellationToken));
    }
    [Fact]
    public async Task FactoryAsyncCloseWaitsForOwnedLeaseAndDoesNotFreeDisplayedPixels()
    {
        using var f = new Fixture(); var state = new SaveData(f.State); await state.LoadAsync(TestContext.Current.CancellationToken);
        await using var op = f.Operation(state); await op.OpenAsync(f.Images, TestContext.Current.CancellationToken);
        using var factory = new BitmapFactory(new MagickImageDecoder());
        using var lease = await factory.GetAsync(op.Book!.Pages[0], new(64, 64, true), TestContext.Current.CancellationToken, true);
        lease.RegisterDisplayBytes(lease.Image.ByteCount);
        var bytes = factory.ByteCount; var closing = factory.DisposeAsync().AsTask();
        Assert.False(closing.IsCompleted); Assert.Equal(bytes, factory.ByteCount);
        Assert.Equal(1, factory.GetDiagnostics().Leases);
        lease.Dispose(); await closing.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Equal(0, factory.ByteCount); await factory.DisposeAsync();
    }
    [Fact]
    public async Task ScriptImageRenameUpdatesCurrentAndSavedPageWithoutChangingBookPath()
    {
        using var f=new Fixture();var state=new SaveData(f.State);await state.LoadAsync(TestContext.Current.CancellationToken);
        await using var op=f.Operation(state);using var images=new BitmapFactory(new MagickImageDecoder());var backend=new FileOperationBackend(Path.Combine(f.State,"recovery"));op.AttachFileOperations(new(backend),backend,images);
        await op.OpenAsync(f.Images,TestContext.Current.CancellationToken);await op.JumpAsync(2);Config.Current.System.IsFileWriteAccessEnabled=true;
        var item=(await op.GetFileMetadataAsync(Path.Combine(f.Images,"003.png"),TestContext.Current.CancellationToken))!;
        await op.RenameFolderItemAsync(item,"changed.png",TestContext.Current.CancellationToken);
        Assert.Equal(f.Images,op.Book!.Path);Assert.Equal("changed.png",op.Book.CurrentPage!.EntryName);
        Assert.Equal("changed.png",state.GetLastBook()!.Page);
        Assert.Equal("changed.png",state.HistoryEntries.Single(e=>e.Path==f.Images).Page);
        Assert.False(File.Exists(Path.Combine(f.State,".book-rename-pending.json")));
    }
    [AvaloniaFact]
    public async Task AutoScrollSurvivesResizeAndStopsOnSwitchInBrowseMode()
    {
        using var f=new Fixture();var(_,model,window)=await OpenAsync(f);
        try
        {
            await model.Operation.SetBrowseModeAsync(BrowseLayoutMode.Masonry);await PageListThumbnailTests.SettleAsync(window);
            window.Viewer.SetAutoScrollMode(true);await window.Viewer.RefreshAsync();Assert.True(window.Viewer.IsAutoScrollMode);
            await window.OpenAsync(f.Zip);await window.Viewer.RefreshAsync();Assert.False(window.Viewer.IsAutoScrollMode);
        }
        finally{await window.PrepareShutdownAsync();window.Close();}
    }
    [AvaloniaFact]
    public async Task RestoredFullDesktopFloatingWindowKeepsStateAndOwnWindowList()
    {
        using var f=new Fixture();var(_,_,window)=await OpenAsync(f);
        try
        {
            Config.Current.MainView.WindowPlacement=new(WindowStateEx.FullDesktop,40,50,640,480);
            await window.ExecuteAsync("ToggleMainViewFloating");
            Assert.Equal(WindowStateEx.FullDesktop,WindowDisplayState.Get(window.FloatingMainView!));
            Assert.Equal(2,window.GetReaderWindows().Count);Assert.True(window.IsCommandAvailable("FocusNextApp"));
            Assert.True(window.IsCommandAvailable("SelectArchiver"));
            await window.ExecuteAsync("SelectArchiver");Assert.Contains(window.Viewer.ContextMenu!.Items.Cast<MenuItem>(),x=>x.IsChecked&&x.Header!.ToString()!.Contains(".NET"));
            window.Viewer.ContextMenu.Close();
        }
        finally{await window.PrepareShutdownAsync();window.Close();}
    }
    [Fact]
    public async Task RecorderInvalidPathAndThrowingReporterCannotStopSubsequentWritesOrConcurrentClose()
    {
        using var f=new Fixture();var state=new SaveData(f.State);await state.LoadAsync(TestContext.Current.CancellationToken);await using var op=f.Operation(state);await op.OpenAsync(f.Images,TestContext.Current.CancellationToken);
        var file=Path.Combine(f.Root,"safe.tsv");var errors=0;
        var recorder=new PageViewRecorder(error:_=>{Interlocked.Increment(ref errors);throw new Exception("reporter");});
        recorder.Configure(new(){IsSavePageViewRecord=true,PageViewRecordFilePath="bad\0path"});
        recorder.Configure(new(){IsSavePageViewRecord=true,PageViewRecordFilePath=file});recorder.RecordBook(op.Book);recorder.RecordPages([op.Book!.Pages[0]]);
        await Task.WhenAll(Enumerable.Range(0,8).Select(_=>Task.Run(async()=>{await recorder.FlushAsync();await recorder.DisposeAsync();})));
        Assert.True(errors>0);Assert.Equal(2,File.ReadAllLines(file).Length);
        await recorder.DisposeAsync();Assert.Equal(2,File.ReadAllLines(file).Length);
    }
    [AvaloniaTheory]
    [InlineData(WindowStateEx.FullDesktop)]
    [InlineData(WindowStateEx.FullScreen)]
    public void RestoredFullscreenStateReturnsToPersistedMaximized(WindowStateEx state)
    {
        var window=new Window{Width=640,Height=480};window.Show();
        try { WindowDisplayState.Set(window,state,WindowStateEx.Maximized);WindowDisplayState.Set(window,WindowStateEx.Normal);Assert.Equal(WindowState.Maximized,window.WindowState); }
        finally { window.Close(); }
    }
    [AvaloniaFact]
    public async Task MainWindowAndFloatingViewerRestoreLastStateFromOriginalConfig()
    {
        using var f=new Fixture();var(_,_,window)=await OpenAsync(f,c=>{c.Window.LastState=WindowStateEx.Maximized;c.Window.WindowPlacement=new(WindowStateEx.FullDesktop,40,40,640,480);});
        try
        {
            Assert.Equal(WindowStateEx.FullDesktop,WindowDisplayState.Get(window));window.SetFullDesktop(false);Assert.Equal(WindowState.Maximized,window.WindowState);
            Config.Current.MainView.LastState=WindowStateEx.Maximized;Config.Current.MainView.WindowPlacement=new(WindowStateEx.FullDesktop,40,40,640,480);
            await window.ExecuteAsync("ToggleMainViewFloating");window.SetFullDesktop(false);Assert.Equal(WindowState.Maximized,window.FloatingMainView!.WindowState);
        }
        finally {await window.PrepareShutdownAsync();window.Close();}
    }
    [AvaloniaFact]
    public async Task LongPressReleaseDragCaptureAndFocusStopPendingActions()
    {
        Config.SetCurrent(new());Config.Current.Mouse.LongButtonDownMode=LongButtonDownMode.AutoScroll;
        Config.Current.Mouse.LongButtonMask=LongButtonMask.Left;Config.Current.Mouse.LongButtonDownTime=.02;
        var reader=new ReaderView{Width=300,Height=300,Focusable=true};var other=new Button{Width=100,Height=300};
        var window=new Window{Width=500,Height=400,Content=new StackPanel{Orientation=Avalonia.Layout.Orientation.Horizontal,Children={reader,other}}};window.Show();
        try
        {
            window.UpdateLayout();Avalonia.Headless.HeadlessWindowExtensions.CaptureRenderedFrame(window);Dispatcher.UIThread.RunJobs();
            window.MouseDown(new(60,60),MouseButton.Left);await PumpTimer();Assert.True(reader.IsAutoScrollMode);
            window.MouseUp(new(60,60),MouseButton.Left);Assert.False(reader.IsAutoScrollMode);
            window.MouseDown(new(90,90),MouseButton.Left);window.MouseMove(new(120,125),RawInputModifiers.LeftMouseButton);
            await PumpTimer();Assert.False(reader.IsAutoScrollMode);window.MouseUp(new(120,125),MouseButton.Left);
            window.MouseDown(new(160,160),MouseButton.Left);window.MouseUp(new(160,160),MouseButton.Left);await PumpTimer();Assert.False(reader.IsAutoScrollMode);
            reader.SetAutoScrollMode(true);other.Focus();Assert.False(reader.IsAutoScrollMode);
            async Task PumpTimer(){for(var i=0;i<10;i++){Dispatcher.UIThread.RunJobs();await Task.Delay(10,TestContext.Current.CancellationToken);}}
        }
        finally { reader.Dispose();window.Close(); }
    }
    [AvaloniaFact]
    public async Task BookmarkScriptMetadataUsesEntryTimeForFoldersAndRealFiles()
    {
        using var f=new Fixture();var(state,_,window)=await OpenAsync(f);
        try
        {
            var folder=await state.AddBookmarkFolderAsync(null,"测试收藏",TestContext.Current.CancellationToken);
            await window.AttachScriptsAsync(new JintScriptRuntimeFactory(),(_,_)=>{},new System.Collections.Concurrent.ConcurrentDictionary<string,object?>());
            AssertScriptDate(folder.EntryTime,await window.Scripts!.EvaluateAsync("nv.Bookmark.Items.find(x=>x.Name==='测试收藏').LastWriteTime",TestContext.Current.CancellationToken));
            Assert.Equal(-1L,Convert.ToInt64(await window.Scripts.EvaluateAsync("nv.Bookmark.Items.find(x=>x.Name==='测试收藏').Size",TestContext.Current.CancellationToken)));
        }
        finally { await window.PrepareShutdownAsync();window.Close(); }
    }
    // JavaScript Date按UTC毫秒保留时间，不承诺.NET的100ns精度或原Kind。
    private static void AssertScriptDate(DateTime expected,object? actual)=>Assert.Equal(new DateTimeOffset(expected).ToUnixTimeMilliseconds(),new DateTimeOffset(Assert.IsType<DateTime>(actual)).ToUnixTimeMilliseconds());
    [AvaloniaFact]
    public async Task PrintDialogPreservesDraftAndCancelsPendingPreview()
    {
        using var f = new Fixture();
        var parameters = new PrintParameters { Mode = PrintMode.ViewStretch, Orientation = PrintOrientation.Landscape,
            Columns = 3, Rows = 2, IsBackground = true, IsDotScale = true, MarginMm = new(-2, 3, 4, 5) };
        var model = new PrintViewModel { Parameters = parameters }; var dialog = new PrintWindow(model);
        dialog.PreviewAsync = (_, _) => Task.FromResult(new PrintImage(File.ReadAllBytes(Path.Combine(f.Images, "001.png")), 400, 600, 400, 600));
        dialog.Show(); dialog.UpdateLayout();
        try
        {
            Assert.Equal(parameters, dialog.GetParameters()); Assert.Equal(800, dialog.Width); Assert.Equal(600, dialog.Height);
            var button = dialog.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "刷新内容预览"));
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await dialog.PendingPreview;
            dialog.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            Assert.NotNull(dialog.FindControl<Image>("Preview")!.Source);
            if (Environment.GetEnvironmentVariable("NEEVIEW_ACCEPTANCE_PHASE") == "p5-completion")
            {
                var output = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../acceptance/p5-completion-print-layout.png"));
                using var bitmap = new Avalonia.Media.Imaging.RenderTargetBitmap(new PixelSize(800, 600)); bitmap.Render(dialog);
                bitmap.Save(output, Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
            }
            dialog.FindControl<NumericUpDown>("Rows")!.Value = 4;
            Assert.Same(parameters, model.Parameters); Assert.Equal(4, dialog.GetParameters().Rows);
            var pending = new TaskCompletionSource<PrintImage>(TaskCreationOptions.RunContinuationsAsynchronously);
            dialog.PreviewAsync = (_, token) => pending.Task.WaitAsync(token);
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Assert.False(dialog.PendingPreview.IsCompleted);
            dialog.Close(null); await dialog.PendingPreview;
            Assert.Null(dialog.FindControl<Image>("Preview")!.Source); Assert.Same(parameters, model.Parameters);
        }
        finally { dialog.Close(null); await dialog.PendingPreview; }
    }
    [AvaloniaFact]
    public async Task PrintCaptureUsesSameViewerAndCanceledPreviewReleasesOutputLock()
    {
        using var f=new Fixture();var(_,model,window)=await OpenAsync(f);
        try
        {
            window.Width = 900; window.Height = 480; window.UpdateLayout(); await window.Viewer.RefreshAsync();
            var book=model.Operation.Book;var position=model.Operation.Position;
            foreach(var mode in Enum.GetValues<PrintMode>())
            {
                Assert.True(await model.Operation.PrintCurrentAsync(async token=>
                {
                    Assert.True(model.Operation.IsExporting);
                    var capturing = window.Viewer.CapturePrintAsync(new(){Mode=mode,IsBackground=true},token);
                    // 输出中的普通UI刷新不能覆盖专用捕获的规格或取消其解码。
                    await window.Viewer.RefreshAsync(); var image = await capturing;
                    Assert.True(image.Png.Length>0);Assert.True(image.Width>0);Assert.NotNull(image.BackgroundPng);
                    if (mode == PrintMode.RawImage) { Assert.Equal(400, image.Width); Assert.Equal(600, image.Height); }
                    return true;
                },TestContext.Current.CancellationToken));
                Assert.Same(book,model.Operation.Book);Assert.Equal(position,model.Operation.Position);Assert.False(model.Operation.IsExporting);
            }
            using var canceled=new CancellationTokenSource();canceled.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>model.Operation.PrintCurrentAsync(_=>Task.FromResult(true),canceled.Token));
            Assert.False(model.Operation.IsExporting);await model.Operation.MoveAsync(1,true);Assert.NotEqual(position,model.Operation.Position);
        }
        finally {await window.PrepareShutdownAsync();window.Close();}
    }
    [Fact]
    public async Task RecorderCloseWaitsForFullQueueAndPreservesFinalRows()
    {
        using var f=new Fixture();var state=new SaveData(f.State);await state.LoadAsync(TestContext.Current.CancellationToken);await using var op=f.Operation(state);await op.OpenAsync(f.Images,TestContext.Current.CancellationToken);
        using var resume=new ManualResetEventSlim();var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var errors=0;
        await using var recorder=new PageViewRecorder(error:_=>{if(Interlocked.Increment(ref errors)==1){entered.SetResult();resume.Wait(TimeSpan.FromSeconds(10));}});
        try
        {
            recorder.Configure(new(){IsSavePageViewRecord=true,PageViewRecordFilePath=Path.Combine(f.Root,"missing","bad.tsv")});recorder.RecordBook(op.Book);recorder.RecordBook(null);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5),TestContext.Current.CancellationToken);
            var file=Path.Combine(f.Root,"final.tsv");recorder.Configure(new(){IsSavePageViewRecord=true,PageViewRecordFilePath=file});recorder.RecordBook(op.Book);
            for(var i=0;i<700;i++)recorder.RecordPages([op.Book!.Pages[0]]);
            recorder.RecordPages([op.Book!.Pages[4]]);var closing=recorder.DisposeAsync().AsTask();Assert.False(closing.IsCompleted);
            resume.Set();await closing.WaitAsync(TimeSpan.FromSeconds(5),TestContext.Current.CancellationToken);
            var rows=File.ReadAllLines(file);Assert.EndsWith("\t005.png",rows[^2]);Assert.Contains("\tBook\t",rows[^1]);Assert.True(errors>1);
        }
        finally { resume.Set(); }
    }
    [AvaloniaFact]
    public async Task ConfiguredHostKeepsOriginalCommandsAndThreeExplicitPlaceholders()
    {
        using var f=new Fixture();var(_,model,window)=await OpenAsync(f);
        try
        {
            // 同正式启动层装配读取/应用入口，只核验登记，不执行导入或系统动作。
            window.AttachProfileImport(new ProfileImportReader(),_=>throw new InvalidOperationException("此用例不应执行导入"));
            var implemented=typeof(MainWindow).GetMethod("IsCommandImplemented",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)!;
            var items=model.Commands.Definitions.Select(d=>new{d.Name,d.Text,d.Shortcut,d.MouseGesture,d.Source,d.Stage,implemented=(bool)implemented.Invoke(window,[d.Name])!}).ToArray();
            Assert.Equal(235,items.Length);Assert.Equal(232,items.Count(x=>x.implemented));
            Assert.Equal(new[]{"CutBook","CutFile","TouchEmulate"},items.Where(x=>!x.implemented).Select(x=>x.Name).Order().ToArray());
            var output=System.IO.Path.GetFullPath(System.IO.Path.Combine(AppContext.BaseDirectory,"../../../../../acceptance/p5-completion-configured-commands.json"));
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(output)!);
            await File.WriteAllTextAsync(output,System.Text.Json.JsonSerializer.Serialize(new{scope="正式宿主装配后的原命令执行入口；不代表参数/设备或完整功能一致性",total=items.Length,implemented=items.Count(x=>x.implemented),items},new System.Text.Json.JsonSerializerOptions{WriteIndented=true})+"\n",TestContext.Current.CancellationToken);
        }
        finally {await window.PrepareShutdownAsync();window.Close();}
    }
}
