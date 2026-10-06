using NeeView.Tests;
namespace NeeView.Backends.MacOS.Tests;
public sealed class AnimatedNativeTests
{
    [Theory]
    [InlineData((byte)1)]
    [InlineData((byte)2)]
    public async Task ActualApngDisposalAndPremultipliedAlphaUseCompleteCanvas(byte disposal)
    {
        using var stream=new MemoryStream(AnimationFixture.CreateDisposalApng(disposal));
        using var source=await new MacAnimatedPngDecoder().OpenAnimationAsync(stream,new(2,2),1024*1024,TestContext.Current.CancellationToken);
        Assert.NotNull(source);Assert.Equal(3,source.Info.FrameCount);
        using var third=await source.ReadFrameAsync(2,TestContext.Current.CancellationToken);
        Assert.Equal(disposal==1?new byte[]{0,0,0,0}:new byte[]{0,0,255,255},third.Pixels.Take(4));
        Assert.Equal(new byte[]{0,0,255,255},third.Pixels.Skip(4).Take(4));
        Assert.Equal(new byte[]{128,0,0,128},third.Pixels.Skip(12).Take(4));
        using var second=await source.ReadFrameAsync(1,TestContext.Current.CancellationToken);
        Assert.Equal(new byte[]{0,255,0,255},second.Pixels.Take(4));
    }
    [Fact]
    public async Task ActualImageIoApngComposesPartialFrameAndUsesRealDelays()
    {
        using var stream=new MemoryStream(AnimationFixture.Create(AnimatedImageType.Png));
        using var source=await new MagickImageDecoder(new MacAnimatedPngDecoder()).OpenAnimationAsync(stream,new(2,2),1024*1024,TestContext.Current.CancellationToken);
        Assert.NotNull(source);Assert.Equal(2,source.Info.FrameCount);Assert.Equal(AnimatedImageType.Png,source.Info.Type);
        Assert.Equal(TimeSpan.FromMilliseconds(100),source.Info.FrameDurations[0]);Assert.Equal(TimeSpan.FromMilliseconds(300),source.Info.FrameDurations[1]);
        using var first=await source.ReadFrameAsync(0,TestContext.Current.CancellationToken);using var second=await source.ReadFrameAsync(1,TestContext.Current.CancellationToken);
        Assert.Equal(new byte[]{0,0,255,255},first.Pixels.Take(4));Assert.Equal(new byte[]{0,255,0,255},second.Pixels.Take(4));Assert.Equal(new byte[]{0,0,255,255},second.Pixels.Skip(4).Take(4));
    }

    [Fact]
    public async Task ActualImageIoApngSupportsRandomFrameReadsAndDispose()
    {
        using var stream=new MemoryStream(AnimationFixture.Create(AnimatedImageType.Png));
        var source=await new MagickImageDecoder(new MacAnimatedPngDecoder()).OpenAnimationAsync(stream,new(2,2),1024*1024,TestContext.Current.CancellationToken);
        Assert.NotNull(source);
        using var second=await source.ReadFrameAsync(1,TestContext.Current.CancellationToken);
        using var first=await source.ReadFrameAsync(0,TestContext.Current.CancellationToken);
        Assert.Equal(new byte[]{0,255,0,255},second.Pixels.Take(4));
        Assert.Equal(new byte[]{0,0,255,255},first.Pixels.Take(4));
        source.Dispose();
        await Assert.ThrowsAsync<ObjectDisposedException>(()=>source.ReadFrameAsync(0,TestContext.Current.CancellationToken));
    }
}
