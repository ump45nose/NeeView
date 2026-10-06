using System.Text.Json;
using NeeView.Effects;
using NeeView.MacOS.Views;
namespace NeeView.Engine.Tests;
public sealed class EffectUnitMigrationTests
{
    [Theory] [InlineData("Level")] [InlineData("Hsv")] [InlineData("ColorSelect")] [InlineData("Colorize")] [InlineData("Magnify")] [InlineData("Ripple")]
    public void OriginalPolymorphicRoundTrip(string type)
    {
        var unit = JsonSerializer.Deserialize<EffectUnit>("{\"$type\":\""+type+"\",\"Future\":{\"x\":5}}")!;
        var clone = unit.Clone(); Assert.True(unit.ValueEquals(clone)); Assert.False(unit.IsDefault);
        var raw = JsonSerializer.Serialize<EffectUnit>(clone); Assert.Contains("\"$type\":\""+type+"\"", raw); Assert.Contains("\"Future\"",raw);
    }
    [Fact] public void UnknownTypesAndDefaultValuesSurvive()
    {
        var unit = JsonSerializer.Deserialize<EffectUnit>("{\"$type\":\"Future\",\"Center\":\".2,.8\"}")!;
        Assert.Equal("Future",Assert.IsType<UnknownEffectUnit>(unit.Clone()).TypeName);
        Assert.True(new LevelEffectUnit().IsDefault); Assert.True(new ColorizeEffectUnit().IsDefault);
        Assert.Contains("\"Center\":\".2,.8\"",JsonSerializer.Serialize<EffectUnit>(unit.Clone()));
    }
    [Fact] public void RawLevelDoesNotMoveCenterAndUiSetterDoes()
    {
        var unit = JsonSerializer.Deserialize<EffectUnit>("{\"$type\":\"Level\",\"Center\":0.3,\"Black\":0.1,\"White\":0.9}")!;
        var level = Assert.IsType<LevelEffectUnit>(unit); Assert.Equal(.3,level.Center); level.Black=.2; Assert.Equal(.375,level.Center);
    }
    [Fact] public void OriginalRoundingAndDefaults()
    {
        Assert.Equal(.12346,new HsvEffectUnit{Hue=.123456}.Hue); Assert.Equal(1,new BloomEffectUnit{Threshold=2}.Threshold);
        Assert.Equal(-2,new BloomEffectUnit{Threshold=-2}.Threshold); Assert.Equal(.5,new MagnifyEffectUnit().Amount);
        Assert.Equal("{\"$type\":\"Magnify\"}", JsonSerializer.Serialize<EffectUnit>(new MagnifyEffectUnit()));
        Assert.Equal("\"0.5,0.5\"", JsonSerializer.Serialize(new EffectPoint(.5,.5)));
    }
    [Theory] [InlineData("Level")] [InlineData("Hsv")] [InlineData("ColorSelect")] [InlineData("Colorize")]
    public void OriginalShaderCompilesOnActualSkia(string type)
    { using var filter = ImageEffectRenderer.CreateFilter(EffectUnit.CreateInstance(JsonEffectUnitConverter.Types[type])); Assert.NotNull(filter); }
}
