using System.Text.Json;
using NeeView.PageFrames;
namespace NeeView.Engine.Tests;
public sealed class ImageGeometryMigrationTests
{
    [Fact] public void TrimKeepsOppositeSidesAndNotifiesCorrectName()
    {
        var c=new ImageTrimConfig{Left=.8};var changes=new List<string?>();c.PropertyChanged+=(_,e)=>changes.Add(e.PropertyName);c.Right=.6;
        Assert.Equal(.3,c.Left);Assert.Equal(.6,c.Right);Assert.Contains("Right",changes);Assert.Contains("Left",changes);
        c.Top=.4;c.Bottom=.7;Assert.Equal(.2,c.Top);Assert.Equal(.7,c.Bottom);
    }
    [Fact] public void LegacyUniformedMapsToOriginButNeverWritesOldName()
    {
        var c=JsonSerializer.Deserialize<ImageCustomSizeConfig>("{\"IsUniformed\":true}")!;Assert.Equal(CustomSizeAspectRatio.Origin,c.AspectRatio);
        Assert.DoesNotContain("IsUniformed",JsonSerializer.Serialize(c));
    }
    [Theory]
    [InlineData(CustomSizeAspectRatio.None,256,256)] [InlineData(CustomSizeAspectRatio.Origin,128,256)]
    [InlineData(CustomSizeAspectRatio.Ratio_1_1,256,256)] [InlineData(CustomSizeAspectRatio.Ratio_2_3,170.6666667,256)]
    [InlineData(CustomSizeAspectRatio.Ratio_4_3,256,192)] [InlineData(CustomSizeAspectRatio.Ratio_8_9,227.5555556,256)]
    [InlineData(CustomSizeAspectRatio.Ratio_16_9,256,144)] [InlineData(CustomSizeAspectRatio.HalfView,256,192)] [InlineData(CustomSizeAspectRatio.View,256,96)]
    public void OriginalAspectRulesFitInsideTarget(CustomSizeAspectRatio ratio,double w,double h)
    {
        var config=new ImageCustomSizeConfig{IsEnabled=true,AspectRatio=ratio};var size=new PageCustomSize(config,()=>new(1600,600)).TransformToCustomSize(new(100,200));
        Assert.InRange(Math.Abs(size.Width-w),0,.00001);Assert.InRange(Math.Abs(size.Height-h),0,.00001);
    }
    [Theory] [InlineData(0,100,200)] [InlineData(.5,178,228)] [InlineData(1,256,256)]
    public void OriginalApplicabilityRateInterpolatesBothAxes(double rate,double w,double h)
    {var config=new ImageCustomSizeConfig{IsEnabled=true,ApplicabilityRate=rate};Assert.Equal(new Size(w,h),new PageCustomSize(config,()=>new(1000,800)).TransformToCustomSize(new(100,200)));}
    [Theory] [InlineData(1,0,.1)] [InlineData(1,1,.5)] [InlineData(-1,0,.5)] [InlineData(-1,1,.1)]
    public void CustomTrimThenDirectionDependentSplit(int direction,int part,double x)
    {
        var config=new Config{ImageCustomSize=new(){IsEnabled=true,Size=new(200,400)},ImageTrim=new(){IsEnabled=true,Left=.1,Right=.1,Top=.2,Bottom=.1}};
        var context=new PageFrameContext(new(),config);var view=new PageViewSizeCalculator(context,new(new(100,200)),new PageRange(new PagePosition(0,part),new PagePosition(0,part)),direction);
        Assert.Equal(new Size(80,280),view.GetViewSize());var crop=view.GetViewBox();Assert.InRange(Math.Abs(x-crop.X),0,.000001);Assert.InRange(Math.Abs(.4-crop.Width),0,.000001);Assert.Equal(.2,crop.Y);
        Assert.InRange(Math.Abs(200-view.GetSourceSize(new(80,280)).Width),0,.000001);
    }
    [Fact] public void GridRetainsRawDivisionsAndOriginalColorString()
    {var config=new ImageGridConfig{DivX=0,DivY=100};Assert.Equal(0,config.DivX);Assert.Contains("#80808080",JsonSerializer.Serialize(config));}
    [Fact] public void OriginalTrimArithmeticDoesNotAddPixelAtIntegerBoundary()
    {
        var config=new Config{ImageCustomSize=new(){IsEnabled=true,Size=new(600,900)},ImageTrim=new(){IsEnabled=true,Left=.1,Right=.1,Top=.2,Bottom=.1}};
        var size=new PageSizeCalculator(new(new(),config),new(new(3600,5400))).GetPageSize();
        Assert.Equal(new Size(480,630),size);Assert.Equal(630,Math.Ceiling(size.Height));
    }
}
