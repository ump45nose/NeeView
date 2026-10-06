using System.Buffers.Binary;
using System.IO.Compression;
using ImageMagick;
using NeeView.Backends;
using NeeView.Tests;
namespace NeeView.Engine.Tests;

/// <summary>真实合成格式验证，颜色/时长由夹具定义而非解码实现推导。</summary>
public sealed class AnimatedDecoderTests
{
    [Fact]
    public async Task ActualGifPreviousDisposalRestoresCanvasAndKeepsFrameOffset()
    {
        using var stream=new MemoryStream(AnimationFixture.CreatePreviousGif());
        using var source=await new MagickAnimatedImageDecoder().OpenAnimationAsync(stream,new(2,2),1024*1024,TestContext.Current.CancellationToken);
        Assert.NotNull(source);Assert.Equal(3,source.Info.FrameCount);
        using var green=await source.ReadFrameAsync(1,TestContext.Current.CancellationToken);
        Assert.Equal(new byte[]{0,255,0,255},green.Pixels.Take(4));Assert.Equal(new byte[]{0,0,255,255},green.Pixels.Skip(4).Take(4));
        using var third=await source.ReadFrameAsync(2,TestContext.Current.CancellationToken);
        Assert.Equal(new byte[]{0,0,255,255},third.Pixels.Take(4));Assert.Equal(new byte[]{255,0,0,255},third.Pixels.Skip(12).Take(4));
    }
    [Theory]
    [InlineData(AnimatedImageType.Gif)]
    [InlineData(AnimatedImageType.Webp)]
    public async Task ActualFormatsHaveDistinctCompositeFramesAndDurations(AnimatedImageType type)
    {
        using var stream = new MemoryStream(AnimationFixture.Create(type));
        using var source = await new MagickAnimatedImageDecoder().OpenAnimationAsync(stream, new(16,16), 1024*1024, TestContext.Current.CancellationToken);
        Assert.NotNull(source); Assert.Equal(type, source.Info.Type); Assert.Equal(2, source.Info.FrameCount);
        Assert.Equal(TimeSpan.FromMilliseconds(100), source.Info.FrameDurations[0]); Assert.Equal(TimeSpan.FromMilliseconds(300), source.Info.FrameDurations[1]);
        using var first = await source.ReadFrameAsync(0, TestContext.Current.CancellationToken); using var second = await source.ReadFrameAsync(1, TestContext.Current.CancellationToken);
        Assert.Equal(new Size(2,2), first.Size); Assert.NotEqual(first.Pixels, second.Pixels);
        Assert.True(first.Pixels[2] > 240); Assert.True(first.Pixels[1] < 10);
        Assert.True(second.Pixels[1] > 240);
    }

    [Theory]
    [InlineData(AnimatedImageType.Gif)]
    [InlineData(AnimatedImageType.Webp)]
    public async Task FramesCanBeReadOutOfOrderWithoutChangingComposites(AnimatedImageType type)
    {
        using var stream = new MemoryStream(AnimationFixture.Create(type));
        using var source = await new MagickAnimatedImageDecoder().OpenAnimationAsync(stream, new(16, 16), 1024 * 1024, TestContext.Current.CancellationToken);
        Assert.NotNull(source);
        using var second = await source.ReadFrameAsync(1, TestContext.Current.CancellationToken);
        using var first = await source.ReadFrameAsync(0, TestContext.Current.CancellationToken);
        using var secondAgain = await source.ReadFrameAsync(1, TestContext.Current.CancellationToken);
        using var firstAgain = await source.ReadFrameAsync(0, TestContext.Current.CancellationToken);
        Assert.Equal(first.Pixels, firstAgain.Pixels);
        Assert.Equal(second.Pixels, secondAgain.Pixels);
    }
    [Fact]
    public async Task StaticPngReturnsNullAndBudgetCancelDisposeAreExplicit()
    {
        var decoder = new MagickAnimatedImageDecoder();
        using var image = new MagickImage(MagickColors.Red,2,2); using var png = new MemoryStream(image.ToByteArray(MagickFormat.Png));
        Assert.Null(await decoder.OpenAnimationAsync(png,new(2,2),1024,TestContext.Current.CancellationToken));
        using var gif = new MemoryStream(AnimationFixture.Create(AnimatedImageType.Gif));
        await Assert.ThrowsAsync<NotSupportedException>(()=>decoder.OpenAnimationAsync(gif,new(2,2),1,TestContext.Current.CancellationToken));
        using var canceled=new CancellationTokenSource(); canceled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>decoder.OpenAnimationAsync(gif,new(2,2),1024,canceled.Token));
        var source=await decoder.OpenAnimationAsync(gif,new(2,2),1024,TestContext.Current.CancellationToken); Assert.NotNull(source); source.Dispose();
        await Assert.ThrowsAsync<ObjectDisposedException>(()=>source.ReadFrameAsync(0,TestContext.Current.CancellationToken));
    }
}
