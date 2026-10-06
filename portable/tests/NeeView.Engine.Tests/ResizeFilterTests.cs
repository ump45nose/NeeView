using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Headless;
using Avalonia.VisualTree;
using ImageMagick;
using NeeView.Backends;
using NeeView.MacOS.Views;
using NeeView.MacOS.ViewModels;
namespace NeeView.Engine.Tests;

/// <summary>原滤镜规则、实际像素、租约规格和唯一配置事务的隔离回归。</summary>
public sealed class ResizeFilterTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    public static bool HasSample => File.Exists(Environment.GetEnvironmentVariable("NEEVIEW_IMAGE_EFFECT_SAMPLE"));
    public static TheoryData<ResizeInterpolation> Kernels => new(Enum.GetValues<ResizeInterpolation>());
    [Theory, MemberData(nameof(Kernels))]
    public void UniformImageAndBgraOrderSurviveEveryOriginalKernel(ResizeInterpolation kernel)
    {
        var source=Enumerable.Range(0,80).SelectMany(_=>new byte[]{17,83,211,137}).ToArray();
        var result=ResizeKernel.Resize(source,8,10,3,4,kernel,Token,1024*1024);
        Assert.Equal(3*4*4,result.Length);
        for(int i=0;i<result.Length;i+=4) Assert.Equal(new byte[]{17,83,211,137},result.AsSpan(i,4).ToArray());
    }
    [Theory, MemberData(nameof(Kernels))]
    public void TransparentHiddenBlueDoesNotCreateHalo(ResizeInterpolation kernel)
    {
        var source=Enumerable.Range(0,64).SelectMany(i=>i%8<4?new byte[]{0,0,255,255}:new byte[]{255,0,0,0}).ToArray();
        var result=ResizeKernel.Resize(source,8,8,3,3,kernel,Token,1024*1024);
        for(int i=0;i<result.Length;i+=4)
        {
            Assert.Equal(0,result[i]); Assert.Equal(0,result[i+1]);
            if(result[i+3]>0) Assert.Equal(255,result[i+2]);
        }
    }
    [Fact]
    public void FixedOriginalKernelSamplesDistinguishFormerApproximation()
    {
        Assert.Equal(.875,ResizeKernel.Value(ResizeInterpolation.Quadratic,.25),12);
        Assert.Equal(.5625,ResizeKernel.Value(ResizeInterpolation.CatmullRom,.5),12);
        Assert.Equal(.625,ResizeKernel.Value(ResizeInterpolation.Cubic,.5),12);
        Assert.NotEqual(ResizeKernel.Value(ResizeInterpolation.Lanczos,.5),ResizeKernel.Value(ResizeInterpolation.Spline36,.5));
        Assert.NotEqual(ResizeKernel.Value(ResizeInterpolation.Cubic,.5),ResizeKernel.Value(ResizeInterpolation.CubicSmoother,.5));
    }
    [Fact]
    public void CancellationAndSourceBudgetFailBeforeAllocatingWork()
    {
        using var cancel=new CancellationTokenSource();cancel.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(()=>ResizeKernel.Resize(new byte[64],4,4,2,2,ResizeInterpolation.Lanczos,cancel.Token,10000));
        Assert.Throws<NotSupportedException>(()=>ResizeKernel.Resize(new byte[65536],128,128,1,1,ResizeInterpolation.Lanczos,Token,10000));
        Assert.Throws<NotSupportedException>(()=>ResizeKernel.Resize(new byte[64],4,4,2,2,(ResizeInterpolation)999,Token,10000));
    }
    [Fact(SkipUnless=nameof(HasSample),Skip="需显式提供只读实图样本")]
    public async Task MountedSampleFilteredDecodePreservesSourceAndRecordsBackendTiming()
    {
        var path=Environment.GetEnvironmentVariable("NEEVIEW_IMAGE_EFFECT_SAMPLE")!;
        var source=await File.ReadAllBytesAsync(path,Token);var stamp=File.GetLastWriteTimeUtc(path);
        var times=new List<double>();var decoder=new MagickImageDecoder();
        for(int i=0;i<3;i++)
        {
            using var stream=new MemoryStream(source);var watch=System.Diagnostics.Stopwatch.StartNew();
            using var lease=await decoder.DecodeAsync(stream,new(640,960,false,new(ResizeInterpolation.Lanczos,true,40,1.5,0)),Token);
            watch.Stop();times.Add(watch.Elapsed.TotalMilliseconds);Assert.Equal(new NeeView.Size(640,960),lease.Size);
        }
        Assert.Equal(source,await File.ReadAllBytesAsync(path,Token));Assert.Equal(stamp,File.GetLastWriteTimeUtc(path));
        if(Environment.GetEnvironmentVariable("NEEVIEW_IMAGE_EFFECT_EVIDENCE_DIR") is { Length:>0 } directory)
        {
            Directory.CreateDirectory(directory);
            await File.WriteAllTextAsync(Path.Combine(directory,"resize-filter-sample.json"),JsonSerializer.Serialize(new
            { source_sha256=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(source)), source_unchanged=true,
                decode_ms=times,scope="同一只读实图三次完整后端解码/滤镜耗时，不代表首图或屏幕P95" }),Token);
        }
    }
    [Fact]
    public void OriginalDefaultsUnboundedImportAndSnapshotNotificationsRemainSeparate()
    {
        var config=new ImageResizeFilterConfig();Assert.False(config.IsEnabled);Assert.Null(config.CreateParameters());
        Assert.Equal(ResizeInterpolation.Lanczos,config.ResizeInterpolation);Assert.True(config.IsUnsharpMaskEnabled);
        Assert.Equal(40,config.UnsharpMask.Amount);Assert.Equal(1.5,config.UnsharpMask.Radius);Assert.Equal(0,config.UnsharpMask.Threshold);
        var imported=JsonSerializer.Deserialize<ImageResizeFilterConfig>("""{"IsEnabled":true,"ResizeInterpolation":10,"UnsharpMask":{"Radius":12.345678,"Threshold":257,"Future":3},"Future":8}""")!;
        Assert.Equal(12.34568,imported.UnsharpMask.Radius);var snapshot=imported.CreateParameters()!;Assert.Equal((byte)1,snapshot.Threshold);
        bool notified=false;imported.PropertyChanged+=(_,e)=>notified|=e.PropertyName=="UnsharpMask";
        imported.UnsharpMask.Radius=2;Assert.True(notified);Assert.Equal(12.34568,snapshot.Radius);
        var clone=JsonSerializer.Deserialize<ImageResizeFilterConfig>(JsonSerializer.Serialize(imported))!;
        Assert.Equal(8,clone.ExtensionData!["Future"].GetInt32());Assert.Equal(3,clone.UnsharpMask.ExtensionData!["Future"].GetInt32());
    }
    [Fact]
    public async Task SamePageDifferentFilterValuesCannotReuseStalePixels()
    {
        using var f=new Fixture();var state=new SaveData(f.State);await state.LoadAsync(Token);
        await using var op=f.Operation(state);await op.OpenAsync(f.Images,Token);using var factory=new BitmapFactory(new MagickImageDecoder());
        var page=op.Book!.Pages.First(p=>p.IsImage);var ordinary=new DecodeRequest(32,64);
        using var original=await factory.GetAsync(page,ordinary,Token);
        var filter=new ImageResizeFilterParameters(ResizeInterpolation.Lanczos,false,40,1.5,0);
        using var one=await factory.GetAsync(page,ordinary with{ResizeFilter=filter},Token);
        using var two=await factory.GetAsync(page,ordinary with{ResizeFilter=filter},Token);
        using var three=await factory.GetAsync(page,ordinary with{ResizeFilter=filter with{Interpolation=ResizeInterpolation.Linear}},Token);
        Assert.NotSame(original.Image,one.Image);Assert.Same(one.Image,two.Image);Assert.NotSame(one.Image,three.Image);
    }
    [Fact]
    public async Task NativeSharpKeepsAlphaAndThumbnailUsesUnfilteredRoute()
    {
        var raw=Enumerable.Range(0,256).SelectMany(i=>new byte[]{(byte)(i%3*70),100,200,(byte)(i%16*16)}).ToArray();
        using var image=new MagickImage(raw,new MagickReadSettings{Width=16,Height=16,Format=MagickFormat.Bgra});image.ColorSpace=ColorSpace.sRGB;
        using var encoded=new MemoryStream();image.Write(encoded,MagickFormat.Png);var bytes=encoded.ToArray();var decoder=new MagickImageDecoder();
        async Task<DecodedImageLease> Read(DecodeRequest r){using var stream=new MemoryStream(bytes);return await decoder.DecodeAsync(stream,r,Token);}
        var filter=new ImageResizeFilterParameters(ResizeInterpolation.Lanczos,false,40,1.5,0);
        using var plain=await Read(new(8,8,false,filter));using var sharp=await Read(new(8,8,false,filter with{Sharpen=true}));
        Assert.Equal(plain.Pixels.Where((_,i)=>i%4==3),sharp.Pixels.Where((_,i)=>i%4==3));
        using var thumb=await Read(new(8,8,true));using var filteredThumb=await Read(new(8,8,true,filter));Assert.Equal(thumb.Pixels,filteredThumb.Pixels);
        await Assert.ThrowsAsync<NotSupportedException>(async()=>{using var rejected=await Read(new(8,8,false,filter with{Sharpen=true,Radius=-1}));});
    }
    [AvaloniaFact]
    public async Task TogglePanelPresetAndFailedSaveUseOriginalTransaction()
    {
        using var f=new Fixture();var state=new SaveData(f.State);await state.LoadAsync(Token);var op=f.Operation(state);
        var w=new MainWindow();w.Bind(new ReaderWorkspaceViewModel(op,new(op),state),new(new MagickImageDecoder()),new Platform());w.Show();
        try
        {
            await w.OpenAsync(f.Images);Avalonia.Threading.Dispatcher.UIThread.RunJobs();w.UpdateLayout();await w.Viewer.RefreshAsync();
            Assert.True(w.Viewer.BitmapCreationCount>0);
            state.SetCommandParameter("ToggleResizeFilter",new ToggleCommandParameter{ToggleMode=ToggleMode.On});
            await w.ExecuteAsync("ToggleResizeFilter");Assert.True(Config.Current.ImageResizeFilter.IsEnabled);
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();await w.Viewer.RefreshAsync();var first=w.Viewer.BitmapCreationCount;
            await w.ExecuteAsync("ToggleResizeFilter",true);await w.Viewer.RefreshAsync();Assert.False(Config.Current.ImageResizeFilter.IsEnabled);
            Assert.True(w.Viewer.BitmapCreationCount>first);
            using var model=new ImageEffectPanelViewModel(op);
            await model.EditAsync((draft,_)=>{draft.ImageResizeFilter.IsEnabled=true;draft.ImageResizeFilter.ResizeInterpolation=ResizeInterpolation.Spline36;});
            Assert.Equal(ResizeInterpolation.Spline36,Config.Current.ImageResizeFilter.ResizeInterpolation);
            var previous=JsonSerializer.Serialize(Config.Current.ImageResizeFilter);var blocked=Path.Combine(f.State,"UserSetting.json.tmp");Directory.CreateDirectory(blocked);
            await model.EditAsync((draft,_)=>draft.ImageResizeFilter.UnsharpMask.Radius=2.5);
            Assert.Equal(previous,JsonSerializer.Serialize(Config.Current.ImageResizeFilter));Directory.Delete(blocked);
            await state.SaveAsync(op.Book,Token);var raw=JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(f.State,"UserSetting.json"),Token))!;
            Assert.Equal(10,raw["Config"]!["ImageResizeFilter"]!["ResizeInterpolation"]!.GetValue<int>());
            await new SaveData(f.State).LoadAsync(Token);Assert.Equal(ResizeInterpolation.Spline36,Config.Current.ImageResizeFilter.ResizeInterpolation);
        }
        finally {await w.PrepareShutdownAsync();w.Close();}
    }
    [AvaloniaFact]
    public async Task OriginalEffectPanelExposesRealFilterEditingControls()
    {
        using var f=new Fixture();var state=new SaveData(f.State);await state.LoadAsync(Token);await using var op=f.Operation(state);
        await op.OpenAsync(f.Images,Token);using var panel=new ImageEffectView();panel.Attach(op);
        var window=new Window{Content=panel,Width=420,Height=800};window.Show();
        try
        {
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();window.UpdateLayout();
            var checkbox=panel.GetVisualDescendants().OfType<CheckBox>().Single(c=>c.Content?.ToString()=="缩放滤镜");
            Assert.True(checkbox.IsEnabled);checkbox.IsChecked=true;
            for(int i=0;i<100&&!Config.Current.ImageResizeFilter.IsEnabled;i++){Avalonia.Threading.Dispatcher.UIThread.RunJobs();await Task.Delay(10,Token);}
            Assert.True(Config.Current.ImageResizeFilter.IsEnabled);
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();window.UpdateLayout();
            if(Environment.GetEnvironmentVariable("NEEVIEW_ACCEPTANCE_PHASE")=="p5-resize-filter")
            {
                using var frame=window.CaptureRenderedFrame();Assert.NotNull(frame);
                var path=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../acceptance/p5-resize-filter-panel.png"));frame.Save(path,Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
            }
        }
        finally{window.Close();}
    }
    private sealed class Platform:IPlatformService
    {public Task RevealAsync(string path,CancellationToken token)=>Task.CompletedTask;public Task TrashAsync(string path,CancellationToken token)=>Task.CompletedTask;}
}
