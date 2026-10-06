using System.Text.Json.Nodes;
using NeeView;
using NeeView.MacOS.ViewModels;
using NeeView.PageFrames;

namespace NeeView.Engine.Tests;

public sealed class LoupeEngineTests
{
    [Fact]
    public void DefaultsAndFiveDecimalSettersMatchOriginal()
    {
        var c = new LoupeConfig();
        Assert.Equal(2, c.DefaultScale); Assert.Equal(1, c.MinimumScale); Assert.Equal(10, c.MaximumScale);
        Assert.Equal(1, c.ScaleStep); Assert.Equal(1, c.Speed);
        Assert.True(c.IsResetByPageChanged && c.IsVisibleLoupeInfo && c.IsWheelScalingEnabled && c.IsEscapeKeyEnabled);
        c.DefaultScale = 1.234567; c.MinimumScale = -123.456789; c.MaximumScale = double.MaxValue;
        c.ScaleStep = -2; c.Speed = 2.345678;
        Assert.Equal(1.23457, c.DefaultScale); Assert.Equal(-123.45679, c.MinimumScale);
        Assert.Equal(Math.Round(double.MaxValue, 5), c.MaximumScale); Assert.Equal(0, c.ScaleStep); Assert.Equal(2.34568, c.Speed);
    }

    [Fact]
    public void LoupeContextUsesOriginalAsymmetricBoundsAndOriginalFallback()
    {
        var c = new LoupeConfig { DefaultScale = 5, MinimumScale = 10, MaximumScale = 2, ScaleStep = 3 };
        var x = new LoupeContext(c); x.ZoomIn(); Assert.Equal(2, x.Scale); x.ZoomOut(); Assert.Equal(10, x.Scale);
        x.Scale = 4; c.IsBaseOnOriginal = true; Assert.Equal(2, x.GetFixedScale(2));
        Assert.Equal(4, x.GetFixedScale(0)); Assert.Equal(4, x.GetFixedScale(double.NaN));
        c.IsBaseOnOriginal = false; Assert.Equal(4, x.GetFixedScale(double.PositiveInfinity));
    }

    [Theory]
    [InlineData(false, -10, -20, 5, 10)]
    [InlineData(true, -10, -20, 10, 20)]
    public void TransformCapturesInitialAnchor(bool center, double x, double y, double ex, double ey)
    {
        var p = LoupeTransform.GetBasePoint(new(x, y), 2, center);
        Assert.Equal(ex, p.X); Assert.Equal(ey, p.Y);
    }

    [Fact]
    public void TransformKeepsFrozenBaseAcrossScaleAndRoundsDevicePixels()
    {
        var basePoint = LoupeTransform.GetBasePoint(new(3, 5), 2, false);
        Assert.Equal(new Vector(-1.5, -2.5), basePoint);
        Assert.Equal(new Vector(-1.5, -2.5), LoupeTransform.GetPoint(basePoint, new(0, 0), 1, 2));
        Assert.Equal(new Vector(-2.5, -3.5), LoupeTransform.GetPoint(basePoint, new(1, 1), 1, 2));
        Assert.Equal(new Vector(-2, -2), LoupeTransform.GetPoint(basePoint, new(0, 0), 1, 1));
    }

    [Fact]
    public void TransformSanitizesNonFiniteInput()
    {
        var p = LoupeTransform.GetBasePoint(new(double.NaN, double.PositiveInfinity), double.NaN, false);
        Assert.Equal(new Vector(0, 0), LoupeTransform.GetPoint(p, new(double.NaN, double.NegativeInfinity), double.NaN, 0));
        Assert.Equal(new Vector(1, 2), LoupeTransform.GetPoint(new(1, 2), new(0, 0), double.PositiveInfinity));
    }

    [Fact]
    public async Task JsonDefaultsOmitLoupeBranchAndUnknownFieldsSurvive()
    {
        using var f = new LoupeTestDirectory(); await f.Load();
        var path = Path.Combine(f.Path, "UserSetting.json");
        await File.WriteAllTextAsync(path, "{\"Config\":{\"Loupe\":{\"FutureLoupe\":42}}}", TestContext.Current.CancellationToken); await f.Load();
        await f.Save();
        var node = JsonNode.Parse(await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken))!;
        Assert.Equal(42, node["Config"]!["Loupe"]!["FutureLoupe"]!.GetValue<int>());
    }

    [Fact]
    public async Task JsonRoundTripsAllNondefaultLoupeFields()
    {
        using var f = new LoupeTestDirectory(); await f.Load();
        var c = Config.Current.Loupe; c.DefaultScale = 3.123456; c.MinimumScale = .25; c.MaximumScale = 40; c.ScaleStep = 2.5; c.Speed = .75;
        c.IsLoupeCenter = c.IsResetByRestart = c.IsBaseOnOriginal = true; c.IsResetByPageChanged = c.IsVisibleLoupeInfo = c.IsWheelScalingEnabled = c.IsEscapeKeyEnabled = false;
        await f.Save(); using var g = new LoupeTestDirectory(f.Path); await g.Load(); var d = Config.Current.Loupe;
        Assert.Equal(3.12346, d.DefaultScale); Assert.Equal(.25, d.MinimumScale); Assert.Equal(40, d.MaximumScale); Assert.Equal(2.5, d.ScaleStep); Assert.Equal(.75, d.Speed);
        Assert.True(d.IsLoupeCenter && d.IsResetByRestart && d.IsBaseOnOriginal); Assert.False(d.IsResetByPageChanged || d.IsVisibleLoupeInfo || d.IsWheelScalingEnabled || d.IsEscapeKeyEnabled);
    }

    [Fact]
    public void ViewModelDraftsAreIndependentAndPreserveExtremeUneditedValues()
    {
        var c = new LoupeConfig { DefaultScale = double.MaxValue }; var a = new LoupeSettingsViewModel(c); var b = new LoupeSettingsViewModel(c);
        a.DefaultScale = 3; a.IsLoupeCenter = true; Assert.Equal(double.MaxValue, c.DefaultScale); Assert.False(b.IsLoupeCenter);
        a.Apply(c); Assert.Equal(3, c.DefaultScale); Assert.True(c.IsLoupeCenter);
        var d = new LoupeConfig { DefaultScale = double.MaxValue }; new LoupeSettingsViewModel(d).Apply(d); Assert.Equal(Math.Round(double.MaxValue, 5), d.DefaultScale);
    }

    [Fact]
    public async Task DefaultBranchIsOmittedAndFailedSaveRestoresSameLoupeObject()
    {
        using var f = new Fixture(); var state = new SaveData(f.State); await state.LoadAsync(TestContext.Current.CancellationToken);
        await state.SaveAsync(null, TestContext.Current.CancellationToken);
        var raw = JsonNode.Parse(File.ReadAllText(Path.Combine(f.State,"UserSetting.json")))!;
        Assert.Null(raw["Config"]?["Loupe"]);
        await using var op = f.Operation(state); var original = Config.Current.Loupe;
        var blocked = Path.Combine(f.State,"UserSetting.json.tmp"); Directory.CreateDirectory(blocked);
        try
        {
            await Assert.ThrowsAnyAsync<Exception>(()=>op.ApplyOptionsAsync(()=> { original.DefaultScale=7; original.IsLoupeCenter=true; },(-1,TimeSpan.Zero)));
            Assert.Same(original, Config.Current.Loupe); Assert.Equal(2,original.DefaultScale); Assert.False(original.IsLoupeCenter);
        }
        finally { Directory.Delete(blocked); }
        await op.ApplyOptionsAsync(()=>original.DefaultScale=7,(-1,TimeSpan.Zero)); await new SaveData(f.State).LoadAsync(TestContext.Current.CancellationToken);
        Assert.Equal(7,Config.Current.Loupe.DefaultScale);
    }

    private sealed class LoupeTestDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "neeview-loupe-" + Guid.NewGuid().ToString("N"));
        public LoupeTestDirectory() { Directory.CreateDirectory(Path); }
        public LoupeTestDirectory(string path) => Path = path;
        private SaveData? _state;
        public async Task Load() { _state = new SaveData(Path); await _state.LoadAsync(TestContext.Current.CancellationToken); }
        public async Task Save() => await _state!.SaveAsync(null, TestContext.Current.CancellationToken);
        public void Dispose() { if (Directory.Exists(Path)) Directory.Delete(Path, true); }
    }
}
