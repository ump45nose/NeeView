using System.Diagnostics;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using ImageMagick;
using NeeView;
using NeeView.MacOS.Views;
using NeeView.MacOS.ViewModels;
namespace NeeView.Engine.Tests;

/// <summary>固定夹具的后台资源/渲染测量；Headless耗时不充当真机显示帧率。</summary>
public sealed class P2ResourceValidationTests
{
    /// <summary>原两个命令共享循环参数；非循环时端点停止，恢复后保留差分绑定。</summary>
    [Fact]
    public async Task PageModeCommandsKeepDirectionAndSharedLoopParameter()
    {
        using var fixture = new Fixture(); var state = new SaveData(fixture.State);
        await state.LoadAsync(TestContext.Current.CancellationToken);
        await using var operation = fixture.Operation(state); var commands = new CommandTable(operation);
        await operation.OpenAsync(fixture.Images, TestContext.Current.CancellationToken);
        Assert.True(commands.IsAvailable("TogglePageModeReverse"));
        var draft = CommandParameterEdit.Create(state, "TogglePageModeReverse")!;
        Assert.Equal("TogglePageMode", draft.Owner);
        Assert.True(((TogglePageModeCommandParameter)draft.Value).IsLoop);
        ((TogglePageModeCommandParameter)draft.Value).IsLoop = false; draft.Apply(state);
        state.SetShortcut("TogglePageModeReverse", "Ctrl+Shift+F11");
        await operation.ApplySettingAsync(setting => setting.PageMode = PageMode.SinglePage);
        await commands.ExecuteAsync("TogglePageModeReverse"); Assert.Equal(PageMode.SinglePage, operation.Book!.Setting.PageMode);
        await commands.ExecuteAsync("TogglePageMode"); Assert.Equal(PageMode.WidePage, operation.Book.Setting.PageMode);
        await commands.ExecuteAsync("TogglePageMode"); Assert.Equal(PageMode.WidePage, operation.Book.Setting.PageMode);
        await commands.ExecuteAsync("TogglePageModeReverse"); Assert.Equal(PageMode.SinglePage, operation.Book.Setting.PageMode);
        await operation.SaveAsync(); var fresh = new SaveData(fixture.State); await fresh.LoadAsync(TestContext.Current.CancellationToken);
        Assert.False(fresh.GetCommandParameter<TogglePageModeCommandParameter>("TogglePageModeReverse").IsLoop);
        Assert.Equal("Ctrl+Shift+F11", fresh.GetShortcut("TogglePageModeReverse", ""));
        state.SetCommandParameter("TogglePageMode", new TogglePageModeCommandParameter());
        await commands.ExecuteAsync("TogglePageModeReverse"); Assert.Equal(PageMode.WidePage, operation.Book.Setting.PageMode);
        await commands.ExecuteAsync("TogglePageMode"); Assert.Equal(PageMode.SinglePage, operation.Book.Setting.PageMode);
    }
    [Fact]
    public async Task CacheRentMicroprobe()
    {
        await using var archive=new PixelArchive(); var decoder=new PixelDecoder(); using var factory=new BitmapFactory(decoder);
        var results=new List<object>();
        foreach(int count in new[]{256,1024})
        {
            var pages=Enumerable.Range(0,count).Select(i=>new Page(new(archive){Id=i,RawEntryName=$"{i}.png"})).ToArray();
            foreach(var page in pages) { using var lease=await factory.GetAsync(page,new(8,8),TestContext.Current.CancellationToken); }
            for(int i=0;i<100;i++){using var lease=await factory.GetAsync(pages[i%count],new(8,8),TestContext.Current.CancellationToken);}
            var calls=decoder.Calls; var allocated=GC.GetAllocatedBytesForCurrentThread(); var watch=Stopwatch.StartNew();
            for(int i=0;i<1000;i++){using var lease=await factory.GetAsync(pages[i%count],new(8,8),TestContext.Current.CancellationToken);}
            watch.Stop(); allocated=GC.GetAllocatedBytesForCurrentThread()-allocated;
            Assert.Equal(calls,decoder.Calls);Assert.True(factory.ByteCount<=factory.Budget);
            results.Add(new{entries=count,iterations=1000,elapsed_ms=watch.Elapsed.TotalMilliseconds,allocated_bytes=allocated,decode_calls_after_warmup=decoder.Calls-calls});
        }
        WriteReport("cache",new{scope="缓存租约微测量，非端到端性能",results});
    }
    /// <summary>真实4K目录/ZIP、预取分页、重复刷新和切书，经完整软件渲染采样。</summary>
    [AvaloniaFact]
    public async Task FixedDatasetRenderedResourceProbe()
    {
        using var fixture=new Fixture(); var path=Path.Combine(fixture.Root,"fixed-4k"); Directory.CreateDirectory(path);
        for(int i=0;i<8;i++) { using var image=new MagickImage(new MagickColor((byte)(i*25),(byte)(200-i*20),(byte)(40+i*20)),3840,2160); image.Write(Path.Combine(path,$"{i:000}.jpg")); }
        var zip=Path.Combine(fixture.Root,"fixed-4k.cbz");System.IO.Compression.ZipFile.CreateFromDirectory(path,zip);
        var state=new SaveData(fixture.State);await state.LoadAsync(TestContext.Current.CancellationToken);var operation=fixture.Operation(state);
        var decoder=new CountingMagick();using var factory=new BitmapFactory(decoder){Budget=8*1024*1024,ThumbnailBudget=2*1024*1024};
        var reader=new ReaderView();reader.Attach(operation,factory);var window=new Window{Width=640,Height=480,Content=reader};window.Show();Pump(window);
        var directory=new List<double>();var archive=new List<double>();var prefetched=new List<double>();var cachedFrames=new List<double>();var memory=new List<object>();
        try
        {
            // warm-up不计入样本，测量包含来源打开、资源提交与软件完整帧输出。
            await operation.OpenAsync(path,TestContext.Current.CancellationToken);await reader.RefreshAsync();Capture(window);
            for(int i=0;i<20;i++)
            {
                var source=i%2==0?path:zip;var duration=await MeasureFrameAsync(()=>operation.OpenAsync(source,TestContext.Current.CancellationToken),reader,window);
                (i%2==0?directory:archive).Add(duration);Assert.Equal(1,reader.DisplayCount);
            }
            for(int run=0;run<4;run++)
            {
                await operation.JumpAsync(0);await reader.RefreshAsync();
                for(int i=0;i<7;i++)prefetched.Add(await MeasureFrameAsync(()=>operation.MoveAsync(1),reader,window));
            }
            var creations=reader.BitmapCreationCount;var calls=decoder.Calls;
            for(int i=0;i<30;i++)cachedFrames.Add(await MeasureFrameAsync(()=>Task.CompletedTask,reader,window));
            var repeatedCreations=reader.BitmapCreationCount-creations;var repeatedDecodes=decoder.Calls-calls;Assert.Equal(calls,decoder.Calls);
            if(!(Environment.GetEnvironmentVariable("NEEVIEW_ACCEPTANCE_PHASE")??"").EndsWith("before"))Assert.Equal(0,repeatedCreations);
            for(int i=0;i<30;i++)
            {
                await operation.OpenAsync(i%2==0?path:zip,TestContext.Current.CancellationToken);await reader.RefreshAsync();Capture(window);
                Assert.True(factory.ByteCount<=factory.Budget);
                if(i%10==9){GC.Collect();GC.WaitForPendingFinalizers();using var process=Process.GetCurrentProcess();process.Refresh();memory.Add(new{cycle=i+1,cache_bytes=factory.ByteCount,working_set_bytes=process.WorkingSet64,managed_bytes=GC.GetTotalMemory(false)});}
            }
            using(var frame=window.CaptureRenderedFrame())
            {var phase=Environment.GetEnvironmentVariable("NEEVIEW_ACCEPTANCE_PHASE")??"p2-resources";frame!.Save(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,$"../../../../../acceptance/{phase}-fixed-dataset-layout.png")),Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);}
            reader.Dispose();var cacheAfterReaderClose=factory.ByteCount;Assert.True(cacheAfterReaderClose<=factory.Budget);Assert.Equal(0,reader.DisplayCount);Assert.Equal(0,reader.TransitionDisplayCount);factory.Dispose();Assert.Equal(0,factory.ByteCount);
            WriteReport("render",new{scope="640x480 Headless软件完整帧；真实Magick原生解码，非macOS屏幕呈现/长期native验收",dataset=new{images=8,width=3840,height=2160,format="JPEG",sources=new[]{"directory","ZIP"}},
                directory_open_ms=Samples(directory),zip_open_ms=Samples(archive),prefetched_page_ms=Samples(prefetched),cached_frame_ms=Samples(cachedFrames),
                repeated_refreshes=30,repeated_display_creations=repeatedCreations,repeated_decode_calls=repeatedDecodes,
                budget_bytes=factory.Budget,cache_after_reader_close_bytes=cacheAfterReaderClose,memory_samples=memory,final_cache_bytes=factory.ByteCount});
        }
        finally{reader.Dispose();await operation.DisposeAsync();window.Close();}
    }
    /// <summary>从操作到软件图像输出完成，下一页后台预取不计入当前帧完成点。</summary>
    private static async Task<double> MeasureFrameAsync(Func<Task> change,ReaderView reader,Window window)
    {
        var watch=Stopwatch.StartNew();double? result=null;
        void Ready(object? sender,EventArgs e){if(result is not null)return;Pump(window);Capture(window);watch.Stop();result=watch.Elapsed.TotalMilliseconds;}
        reader.DisplayCompleted+=Ready;
        try{await change();await reader.RefreshAsync();Assert.NotNull(result);return result.Value;}
        finally{reader.DisplayCompleted-=Ready;}
    }
    private static void Capture(Window window){using var frame=window.CaptureRenderedFrame();Assert.NotNull(frame);Assert.True(frame.PixelSize.Width>0);}
    private static void Pump(Window window){Dispatcher.UIThread.RunJobs();window.UpdateLayout();}
    private static object Samples(List<double> values){var sorted=values.Order().ToArray();return new{count=values.Count,p50=sorted[(int)Math.Ceiling(sorted.Length*.5)-1],p95=sorted[(int)Math.Ceiling(sorted.Length*.95)-1],maximum=sorted[^1],samples=values};}
    private sealed class CountingMagick:IImageDecoder
    {
        private readonly NeeView.Backends.MagickImageDecoder _inner=new();public int Calls;
        public Task<ImageInfo> ProbeAsync(Stream stream,CancellationToken token)=>_inner.ProbeAsync(stream,token);
        public Task<DecodedImageLease> DecodeAsync(Stream stream,DecodeRequest request,CancellationToken token){Interlocked.Increment(ref Calls);return _inner.DecodeAsync(stream,request,token);}
    }
    /// <summary>实际装配导出入口与占位状态，避免静态清单把已接入命令仍标待迁。</summary>
    [AvaloniaFact]
    public async Task OriginalCommandCoverageAndCleanupEntry()
    {
        using var fixture=new Fixture();var state=new SaveData(fixture.State);await state.LoadAsync(TestContext.Current.CancellationToken);var operation=fixture.Operation(state);
        var commands=new CommandTable(operation);var window=new MainWindow();window.Bind(new(operation,commands,state),new BitmapFactory(new NeeView.Backends.MagickImageDecoder()),new NoPlatform());
        try
        {
            Assert.Equal(235,commands.Definitions.Count);Assert.True(window.IsCommandAvailable("FocusBookmarkList"));Assert.True(window.IsCommandAvailable("RemoveUnlinkedHistory"));Assert.False(window.IsCommandAvailable("MoveToDestinationFolder1"));
            await window.ExecuteAsync("RemoveUnlinkedHistory");Assert.Empty(state.HistoryEntries);
            var method=typeof(MainWindow).GetMethod("IsCommandImplemented",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!;
            var items=commands.Definitions.Select(d=>new{d.Name,d.Text,d.Shortcut,d.MouseGesture,d.Source,d.Stage,implemented=(bool)method.Invoke(window,[d.Name])!}).ToArray();
            WriteReport("commands",new{scope="当前执行入口登记，不等于完整原功能覆盖；未迁能力保留原命令/菜单占位",total=items.Length,implemented=items.Count(i=>i.implemented),items});
        }
        finally{await window.PrepareShutdownAsync();window.Close();}
    }
    private sealed class NoPlatform:IPlatformService
    {public Task RevealAsync(string path,CancellationToken token=default)=>throw new NotSupportedException();public Task TrashAsync(string path,CancellationToken token=default)=>throw new NotSupportedException();}
    private static void WriteReport(string kind,object data)
    {
        var phase=Environment.GetEnvironmentVariable("NEEVIEW_ACCEPTANCE_PHASE")??"p2-resources";
        File.WriteAllText(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,$"../../../../../acceptance/{phase}-{kind}.json")),JsonSerializer.Serialize(data,new JsonSerializerOptions{WriteIndented=true})+"\n");
    }
    private sealed class PixelArchive():Archive("cache-measurement")
    {
        public override Task<IReadOnlyList<ArchiveEntry>> GetEntriesAsync(CancellationToken token)=>throw new NotSupportedException();
        public override Task<Stream> OpenEntryAsync(ArchiveEntry entry,CancellationToken token)=>Task.FromResult<Stream>(new MemoryStream([0]));
        public override ValueTask DisposeAsync()=>ValueTask.CompletedTask;
    }
    private sealed class PixelDecoder:IImageDecoder
    {
        public int Calls;
        public Task<ImageInfo> ProbeAsync(Stream stream,CancellationToken token)=>throw new NotSupportedException();
        public Task<DecodedImageLease> DecodeAsync(Stream stream,DecodeRequest request,CancellationToken token){Interlocked.Increment(ref Calls);return Task.FromResult(new DecodedImageLease(new(8,8),new byte[256]));}
    }
}
