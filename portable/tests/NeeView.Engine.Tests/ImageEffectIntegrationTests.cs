using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using ImageMagick;
using NeeView.Backends;
using NeeView.Effects;
using NeeView.MacOS.Views;
using NeeView.MacOS.ViewModels;
using NeeView.PageFrames;
namespace NeeView.Engine.Tests;

/// <summary>效果接入唯一查看器后的实际像素、草稿隔离、租约释放及几何重排回归。</summary>
public sealed class ImageEffectIntegrationTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    public static bool HasMountedSample => File.Exists(Environment.GetEnvironmentVariable("NEEVIEW_IMAGE_EFFECT_SAMPLE"));
    [AvaloniaFact(SkipUnless=nameof(HasMountedSample),Skip="需显式提供只读实图样本")]
    public async Task MountedSampleUsesSameEffectAndGeometryExportWithoutChangingSource()
    {
        using var f=new Fixture();var sample=Environment.GetEnvironmentVariable("NEEVIEW_IMAGE_EFFECT_SAMPLE")!;
        var source=await File.ReadAllBytesAsync(sample,Token);var stamp=File.GetLastWriteTimeUtc(sample);
        var state=new SaveData(f.State);await state.LoadAsync(Token);var decoder=new MagickImageDecoder();var factory=new BitmapFactory(decoder);
        var op=new BookOperation(new ArchiveFactory(),decoder,state);var window=Window(op,state,factory);
        try
        {
            await window.OpenAsync(sample);await op.ApplySettingAsync(s=>{s.PageMode=PageMode.SinglePage;s.IsSupportedDividePage=false;});
            await op.EditImageOptionsAsync(c=>{c.ImageCustomSize.IsEnabled=true;c.ImageCustomSize.Size=new(600,900);c.ImageTrim.IsEnabled=true;c.ImageTrim.Left=.1;c.ImageTrim.Right=.1;c.ImageTrim.Top=.2;c.ImageTrim.Bottom=.1;});
            using var before=new MemoryStream();await window.Viewer.ExportViewAsync(op.Frame!,new ExportImageParameter{Mode=ExportImageMode.View,IsOriginalSize=true},before,Token);
            await op.EditImageOptionsAsync(c=>{c.ImageEffect.IsEnabled=true;c.ImageEffect.Layers[0].Effect=new HsvEffectUnit{Hue=120,Saturation=.2};});
            using var after=new MemoryStream();await window.Viewer.ExportViewAsync(op.Frame!,new ExportImageParameter{Mode=ExportImageMode.View,IsOriginalSize=true},after,Token);
            using var plain=new MagickImage(before.ToArray());using var effected=new MagickImage(after.ToArray());
            Assert.Equal((uint)480,effected.Width);Assert.Equal((uint)630,effected.Height);
            Assert.False(plain.ToByteArray(MagickFormat.Rgba).SequenceEqual(effected.ToByteArray(MagickFormat.Rgba)));
            Assert.Equal(source,await File.ReadAllBytesAsync(sample,Token));Assert.Equal(stamp,File.GetLastWriteTimeUtc(sample));
            if(Environment.GetEnvironmentVariable("NEEVIEW_ACCEPTANCE_PHASE")=="p5-image-effects")
                await File.WriteAllTextAsync(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../acceptance/p5-image-effects-resource.json")),
                    JsonSerializer.Serialize(new{scope="挂载图片单样本；独立临时Profile/输出，不激活正式应用，不代表Windows动态",width=effected.Width,height=effected.Height,
                        source_sha256=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(source)),source_unchanged=true,pixels_changed=true},new JsonSerializerOptions{WriteIndented=true})+"\n",Token);
        }
        finally{await window.PrepareShutdownAsync();window.Close();}
        await Wait(()=>factory.GetDiagnostics().Leases==0);Assert.Equal(0,factory.ByteCount);
    }
    private sealed class Platform : IPlatformService
    {
        public Task RevealAsync(string path, CancellationToken token = default) => Task.CompletedTask;
        public Task TrashAsync(string path, CancellationToken token = default) => throw new NotSupportedException();
    }
    private static async Task Wait(Func<bool> ready)
    {
        for (int i=0; i<1000 && !ready(); i++) { Dispatcher.UIThread.RunJobs(); await Task.Delay(10,Token); }
        Assert.True(ready());
    }
    private static MainWindow Window(BookOperation op, SaveData state, BitmapFactory factory)
    {
        var window=new MainWindow(); window.Bind(new(op,new(op),state),factory,new Platform()); window.Show(); return window;
    }
    [Fact] public void DraftLayerChangesCannotReadOrWriteGlobalCache()
    {
        var global=new Config(); global.ImageEffectCache.Add(new HsvEffectUnit{Hue=80});Config.SetCurrent(global);
        var draft=new ImageEffectConfig();var cache=new EffectUnitCache();cache.Add(new HsvEffectUnit{Hue=120});
        draft.Layers[0].ChangeType(EffectType.Hsv,draft,cache);
        Assert.Equal(120,Assert.IsType<HsvEffectUnit>(draft.Layers[0].Effect).Hue);
        draft.Layers[0].ChangeType(EffectType.Level,draft,cache);
        Assert.Equal(80,Assert.IsType<HsvEffectUnit>(global.ImageEffectCache.Get(typeof(HsvEffectUnit))).Hue);
        Assert.Null(global.ImageEffect.Layers[0].Effect);
    }
    [Fact] public void DefaultDifferencePreservesUnknownFieldsAndClonesFullValues()
    {
        var unit=JsonSerializer.Deserialize<EffectUnit>("{\"$type\":\"Hsv\",\"Hue\":120,\"Future\":7}")!;
        var raw=JsonSerializer.Serialize<EffectUnit>(unit);Assert.DoesNotContain("Saturation",raw);Assert.Contains("Future",raw);
        Assert.True(unit.ValueEquals(unit.Clone()));Assert.Equal(120,Assert.IsType<HsvEffectUnit>(unit.Clone()).Hue);
    }
    [Theory] [InlineData(4096,8192,false)] [InlineData(4097,8192,true)] [InlineData(double.NaN,10,true)]
    public void SurfaceBudgetIsExplicitAndUsesRealPixelArea(double width,double height,bool error)
    {Assert.Equal(error,ImageEffectRenderer.SurfaceError(width,height) is not null);}
    [Fact] public void AnisotropicCustomSizeDemandUsesLargestAxisAndStaysBounded()
    {
        var request=ReaderImageRenderer.CreateRequest(new(4000,1000),new(400,800),128,32768);
        Assert.True(request.TargetWidth>=3200);Assert.True(request.TargetHeight>=800);
        var thumb=ReaderImageRenderer.CreateRequest(new(4000,1000),new(400,800),64,2048,true);
        Assert.True(thumb.IsThumbnail);Assert.Equal(2048,thumb.TargetWidth);Assert.InRange(thumb.TargetHeight,1,2048);
    }
    [Fact] public async Task OriginalDpiAspectSizePrecedesCustomTrimAndSurvivesJson()
    {
        using var image=new MagickImage(MagickColors.Red,400,200);image.Density=new Density(192,96,DensityUnit.PixelsPerInch);
        using var stream=new MemoryStream();image.Write(stream,MagickFormat.Tiff);stream.Position=0;
        var info=await new MagickImageDecoder().ProbeAsync(stream,Token);Assert.Equal(new NeeView.Size(400,200),info.Size);Assert.Equal(new NeeView.Size(200,200),info.AspectSize);
        var config=new Config();config.Image.Standard.IsAspectRatioEnabled=true;config.ImageTrim.IsEnabled=true;config.ImageTrim.Left=.1;
        var context=new PageFrameContext(new(),config);Assert.Equal(new NeeView.Size(180,200),new PageSizeCalculator(context,new(info.Size){AspectSize=info.AspectSize}).GetPageSize());
        var json=JsonSerializer.Deserialize<Config>(JsonSerializer.Serialize(config))!;Assert.True(json.Image.Standard.IsAspectRatioEnabled);
        var draft=new AnimationSettingsViewModel(config.Image,config.Archive.Media);draft.AspectRatio=false;Assert.True(config.Image.Standard.IsAspectRatioEnabled);draft.Apply(config.Image,config.Archive.Media);Assert.False(config.Image.Standard.IsAspectRatioEnabled);
    }
    [AvaloniaFact] public void InvalidParametersProduceExplicitFailureAndReleaseImmediateLease()
    {
        Config.SetCurrent(new(){ImageEffect=new(){IsEnabled=true,Layers=new(){new(){Effect=new HsvEffectUnit{Hue=double.NaN}}}}});
        Assert.Throws<InvalidDataException>(ImageEffectRenderer.EnsureExportSupported);
        using var image=new MagickImage(MagickColors.Red,4,4);using var stream=new MemoryStream();image.Write(stream,MagickFormat.Png);stream.Position=0;using var source=new Bitmap(stream);
        int released=0;var result=new ImageEffectRenderResult();using var target=new RenderTargetBitmap(new PixelSize(4,4));
        using(var context=target.CreateDrawingContext())ImageEffectRenderer.Draw(context,source,new(0,0,4,4),new(0,0,4,4),()=>new Release(()=>released++),true,result);
        Assert.Equal(1,released);Assert.NotNull(result.Error);Assert.Throws<InvalidOperationException>(result.ThrowIfFailed);
    }
    private sealed class Release(Action action):IDisposable{private int _disposed;public void Dispose(){if(Interlocked.Exchange(ref _disposed,1)==0)action();}}
    [AvaloniaTheory] [InlineData(false)] [InlineData(true)]
    public void RejectedCustomDrawingReturnsLeaseExactlyOnce(bool immediate)
    {
        Config.SetCurrent(new(){ImageEffect=new(){IsEnabled=true,Layers=new(){new(){Effect=new HsvEffectUnit{Hue=120}}}}});
        using var image=new MagickImage(MagickColors.Red,4,4);using var stream=new MemoryStream();image.Write(stream,MagickFormat.Png);stream.Position=0;using var source=new Bitmap(stream);
        // 固定 Avalonia 的 DrawingGroup 不接收 Custom，模拟提交前失败而非绘制成功后的回收。
        using var context=new DrawingGroup().Open();int released=0;
        Assert.Throws<NotSupportedException>(()=>ImageEffectRenderer.Draw(context,source,new(0,0,4,4),new(0,0,4,4),()=>new Release(()=>released++),immediate));
        Assert.Equal(1,released);
    }
    [AvaloniaFact]
    public void PendingEffectNeverRendersOriginalAsSuccessfulEffect()
    {
        Config.SetCurrent(new(){ImageEffect=new(){IsEnabled=true,Layers=new(){new(){Effect=new RippleEffectUnit()}}}});
        using var image=new MagickImage(MagickColors.Red,4,4);using var stream=new MemoryStream();image.Write(stream,MagickFormat.Png);stream.Position=0;using var source=new Bitmap(stream);
        var result=new ImageEffectRenderResult();int released=0;using var target=new RenderTargetBitmap(new PixelSize(4,4));
        using(var context=target.CreateDrawingContext())ImageEffectRenderer.Draw(context,source,new(0,0,4,4),new(0,0,4,4),()=>new Release(()=>released++),true,result);
        Assert.Contains("Ripple",result.Error);Assert.Equal(1,released);Assert.Throws<InvalidOperationException>(result.ThrowIfFailed);
    }
    private sealed class Probe(Bitmap source,Action acquired,Action released):Control
    {
        public override void Render(DrawingContext context)=>ImageEffectRenderer.Draw(context,source,new(0,0,4,4),new(Bounds.Size),()=>{acquired();return new Release(released);});
    }
    [AvaloniaFact] public async Task SceneGraphOwnsLeaseUntilRedrawAndWindowClose()
    {
        Config.SetCurrent(new(){ImageEffect=new(){IsEnabled=true,Layers=new(){new(){Effect=new HsvEffectUnit{Hue=120}}}}});
        using var image=new MagickImage(MagickColors.Red,4,4);using var stream=new MemoryStream();image.Write(stream,MagickFormat.Png);stream.Position=0;using var source=new Bitmap(stream);
        int acquired=0,released=0;var probe=new Probe(source,()=>acquired++,()=>released++);var window=new Window{Width=40,Height=40,Content=probe};window.Show();
        try
        {
            using(var frame=window.CaptureRenderedFrame()){Assert.NotNull(frame);} Assert.True(acquired>released);
            probe.InvalidateVisual();using(var frame=window.CaptureRenderedFrame()){Assert.NotNull(frame);}Assert.True(released>0);
        }
        finally{window.Close();}
        await Wait(()=>acquired==released);Assert.True(acquired>0);
    }
    [AvaloniaTheory] [InlineData(false)] [InlineData(true)]
    public void OriginalGridUsesTargetAndLargerSquareCell(bool square)
    {
        Config.SetCurrent(new(){ImageGrid=new(){IsEnabled=true,DivX=4,DivY=2,IsSquare=square,Color=ThemeRgba.Parse("Red")}});
        using var target=new RenderTargetBitmap(new PixelSize(100,80));
        using(var context=target.CreateDrawingContext()){context.FillRectangle(Brushes.White,new(0,0,100,80));ImageGridRenderer.Draw(context,new(0,0,100,80),ImageGridTarget.Image);}
        using var stream=new MemoryStream();target.Save(stream,PngBitmapEncoderOptions.Default);stream.Position=0;using var image=new MagickImage(stream);using var pixels=image.GetPixels();
        var line=pixels.GetPixel(square?40:25,10).ToColor()!;Assert.True(line.G<200);
        var blank=pixels.GetPixel(square?25:40,10).ToColor()!;Assert.True(blank.G>250);
        Config.Current.ImageGrid.Target=ImageGridTarget.Screen;
        using var other=new RenderTargetBitmap(new PixelSize(100,80));using(var context=other.CreateDrawingContext()){context.FillRectangle(Brushes.White,new(0,0,100,80));ImageGridRenderer.Draw(context,new(0,0,100,80),ImageGridTarget.Image);}
        using var stream2=new MemoryStream();other.Save(stream2,PngBitmapEncoderOptions.Default);stream2.Position=0;using var image2=new MagickImage(stream2);Assert.Equal((byte)255,image2.GetPixels().GetPixel(0,10).GetChannel(1));
    }
    [AvaloniaFact] public async Task ViewExportAppliesEffectCustomTrimButCopyKeepsOriginalPixels()
    {
        using var f=new Fixture();using(var image=new MagickImage(MagickColors.Red,100,200))image.Write(Path.Combine(f.Images,"001.png"));
        var state=new SaveData(f.State);await state.LoadAsync(Token);var decoder=new MagickImageDecoder();var factory=new BitmapFactory(decoder);var op=new BookOperation(new ArchiveFactory(),decoder,state);var window=Window(op,state,factory);
        try
        {
            await window.OpenAsync(Path.Combine(f.Images,"001.png"));await op.ApplySettingAsync(s=>{s.PageMode=PageMode.SinglePage;s.IsSupportedDividePage=false;});
            await op.EditImageOptionsAsync(c=>{c.ImageCustomSize.IsEnabled=true;c.ImageCustomSize.Size=new(200,400);c.ImageTrim.IsEnabled=true;c.ImageTrim.Left=.1;c.ImageTrim.Right=.1;c.ImageTrim.Top=.2;c.ImageTrim.Bottom=.1;c.ImageEffect.IsEnabled=true;c.ImageEffect.Layers[0].Effect=new HsvEffectUnit{Hue=120};});
            await window.Viewer.RefreshAsync();await Wait(()=>window.Viewer.CanCopyImage);
            var copy=await window.Viewer.CaptureCopyImageAsync(Token);using(var decoded=new MagickImage(copy)){var color=decoded.GetPixels().GetPixel(0,0).ToColor()!;Assert.Equal((byte)255,color.R);Assert.Equal((byte)0,color.G);}
            using var output=new MemoryStream();await window.Viewer.ExportViewAsync(op.Frame!,new ExportImageParameter{Mode=ExportImageMode.View,IsOriginalSize=true},output,Token);output.Position=0;
            using var view=new MagickImage(output);Assert.Equal((uint)160,view.Width);Assert.Equal((uint)280,view.Height);var pixel=view.GetPixels().GetPixel(80,140).ToColor()!;Assert.InRange(pixel.G,253,255);Assert.InRange(pixel.R,0,2);
            Assert.True(window.IsCommandAvailable("SetEffectProfile"));Assert.True(window.IsCommandAvailable("ToggleVisibleEffectInfo"));
            await window.ExecuteAsync("ToggleVisibleEffectInfo"); window.UpdateLayout();Dispatcher.UIThread.RunJobs();
            if(Environment.GetEnvironmentVariable("NEEVIEW_ACCEPTANCE_PHASE")=="p5-image-effects")
            {using var screenshot=window.CaptureRenderedFrame()!;screenshot.Save(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../acceptance/p5-image-effects-panel.png")),PngBitmapEncoderOptions.Default);}
            state.SetCommandParameter("ToggleEffect",new ToggleCommandParameter{ToggleMode=ToggleMode.On});await window.ExecuteAsync("ToggleEffect",true);Assert.False(Config.Current.ImageEffect.IsEnabled);await window.ExecuteAsync("ToggleEffect");Assert.True(Config.Current.ImageEffect.IsEnabled);
            Assert.IsType<SetEffectProfileCommandParameter>(CommandParameterEdit.Create(state,"SetEffectProfile")!.Value);
        }
        finally{await window.PrepareShutdownAsync();window.Close();}
        await Wait(()=>factory.GetDiagnostics().Leases==0);Assert.Equal(0,factory.ByteCount);
    }
    [AvaloniaFact] public async Task BrowseGeometryKeepsAnchorWithoutRepeatingLayoutAndSceneResourcesClose()
    {
        using var f=new Fixture();for(int i=6;i<=100;i++)File.Copy(Path.Combine(f.Images,"001.png"),Path.Combine(f.Images,$"{i:000}.png"));
        var state=new SaveData(f.State);await state.LoadAsync(Token);var decoder=new MagickImageDecoder();var factory=new BitmapFactory(decoder);var op=new BookOperation(new ArchiveFactory(),decoder,state);var window=Window(op,state,factory);
        try
        {
            await window.OpenAsync(f.Images);await op.SetBrowseModeAsync(BrowseLayoutMode.Masonry);await window.Viewer.RefreshAsync();await Wait(()=>window.Viewer.DisplayCount>0&&window.Viewer.BrowsePendingCount==0);
            window.Viewer.Pan(new(0,-1500));await Wait(()=>op.Position.Index>0&&window.Viewer.BrowsePendingCount==0);var page=op.Book!.CurrentPage;
            await op.EditImageOptionsAsync(c=>{c.ImageCustomSize.IsEnabled=true;c.ImageCustomSize.Size=new(200,300);c.ImageTrim.IsEnabled=true;c.ImageTrim.Top=.2;c.ImageEffect.IsEnabled=true;c.ImageEffect.Layers[0].Effect=new HsvEffectUnit{Hue=120};});
            await window.Viewer.RefreshAsync();await Wait(()=>window.Viewer.BrowsePendingCount==0);Assert.Same(page,op.Book.CurrentPage);Assert.Contains(page!.Index,window.Viewer.BrowseLayout!.Query(window.Viewer.BrowseOffset,window.Viewer.BrowseOffset+window.Viewer.Bounds.Height));
            var layout=window.Viewer.BrowseLayout;await Task.Delay(180,Token);await window.Viewer.RefreshAsync();Assert.Same(layout,window.Viewer.BrowseLayout);
            using(var frame=window.CaptureRenderedFrame()){Assert.NotNull(frame);}
            await window.OpenAsync(f.Zip);await window.Viewer.RefreshAsync();await Wait(()=>window.Viewer.BrowseLayout?.Items.Count==5&&window.Viewer.BrowsePendingCount==0);using(var frame=window.CaptureRenderedFrame()){Assert.NotNull(frame);}
        }
        finally{await window.PrepareShutdownAsync();window.Close();}
        await Wait(()=>factory.GetDiagnostics().Leases==0);Assert.Equal(0,factory.ByteCount);
    }
}
