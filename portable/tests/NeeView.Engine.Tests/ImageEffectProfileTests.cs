using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using ImageMagick;
using NeeView.Backends;
using NeeView.Effects;
using NeeView.MacOS.Views;
using NeeView.MacOS.ViewModels;
namespace NeeView.Engine.Tests;
public sealed class ImageEffectProfileTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private sealed class Fixture : IDisposable
    {
        public string Root {get;}=Path.Combine(Path.GetTempPath(),"NeeView-P5-Effects-"+Guid.NewGuid().ToString("N"));
        public string State=>Path.Combine(Root,"Profile"); public string Images=>Path.Combine(Root,"Images");
        public Fixture(){Directory.CreateDirectory(State);Directory.CreateDirectory(Images);using var image=new MagickImage(MagickColors.Red,100,200);image.Write(Path.Combine(Images,"001.png"));}
        public BookOperation Operation(SaveData data)=>new(new ArchiveFactory(),new MagickImageDecoder(),data);
        public void Dispose()=>Directory.Delete(Root,true);
    }
    [Fact] public void CacheKeepsOnlyNonDefaultsAndActuallyCopiesOut()
    {
        var cache=new EffectUnitCache();var unit=new HsvEffectUnit{Hue=120};cache.Add(unit);cache.Add(new BlurEffectUnit());Assert.Single(cache);
        var result=new EffectUnit[1];cache.CopyTo(result,0);Assert.Same(unit,result[0]);Assert.Single(cache);
        Assert.True(cache.Remove(unit.Clone()));Assert.Empty(cache);
    }
    [Fact] public void OriginalLayerTypeSwitchCacheIsolationAndTenLayerLimit()
    {
        var config=new ImageEffectConfig();var cache=new EffectUnitCache();var layer=config.Layers[0];
        layer.ChangeType(EffectType.Hsv,config,cache);Assert.IsType<HsvEffectUnit>(layer.Effect).Hue=120;
        layer.IsEnabled=false;layer.ChangeType(EffectType.Level,config,cache);Assert.True(layer.IsEnabled);
        layer.ChangeType(EffectType.Hsv,config,cache);Assert.Equal(120,Assert.IsType<HsvEffectUnit>(layer.Effect).Hue);
        var next=config.Layers.CreateNew()!;next.ChangeType(EffectType.Hsv,config,cache);Assert.Equal(0,Assert.IsType<HsvEffectUnit>(next.Effect).Hue);Assert.NotSame(next.Effect,layer.Effect);
        for(int i=0;i<20;i++)config.Layers.CreateNew();Assert.Equal(10,config.Layers.Count);Assert.Null(config.Layers.CreateNew());
        config.Layers.MoveDown(next);config.Layers.MoveUp(next);while(config.Layers.Count>1)config.Layers.Delete(config.Layers[0],cache);config.Layers.Delete(config.Layers[0],cache);Assert.Single(config.Layers);
    }
    [Fact] public void OriginalProfilesStoreRestoreUniqueNamesAndDefaultProtection()
    {
        var config=new Config();var profiles=new EffectProfileCollection(config);config.ImageTrim.Left=.2;config.ImageResizeFilter.ExtensionData=new(){["FutureBackend"]=JsonSerializer.SerializeToElement(true)};
        var clone=profiles.CreateNew(true);Assert.Equal(.2,config.ImageTrim.Left);Assert.True(config.ImageResizeFilter.ExtensionData!["FutureBackend"].GetBoolean());
        config.ImageTrim.Left=.4;profiles.SetSelectedId(0);Assert.Equal(.2,config.ImageTrim.Left);profiles.SetSelectedId(clone.Id);Assert.Equal(.4,config.ImageTrim.Left);
        var fresh=profiles.CreateNew();Assert.Equal(0,config.ImageTrim.Left);Assert.NotEqual(clone.Name,fresh.Name);profiles.Rename(fresh,clone.Name);Assert.NotEqual(clone.Name,fresh.Name);
        profiles.Delete(config.EffectProfiles.Profiles[0]);Assert.Equal(3,profiles.Profiles.Count);profiles.Delete(fresh);Assert.Equal(clone.Id,config.BookSetting.EffectProfileId);
        config.BookSetting.EffectProfileId=999;profiles.Restore();Assert.Equal(0,config.BookSetting.EffectProfileId);
    }
    [Fact] public async Task UniqueJsonStateRestoresEffectsUnknownLayersAndReadOnlyLegacyAlias()
    {
        using var f=new Fixture();await File.WriteAllTextAsync(Path.Combine(f.State,"UserSetting.json"),"""
        {"Format":"NeeView.UserSetting/46.3.0","Config":{"ImageCustomSize":{"IsUniformed":true,"AspectRatio":"None","Future":5},"ImageEffect":{"IsEnabled":true,"Layers":[{"Effect":{"$type":"Hsv","Hue":120,"NewParam":3}},{"Effect":{"$type":"Future","NewParam":7}}]},"ImageEffectCache":[{"$type":"Ripple","Frequency":99}],"ImageResizeFilter":{"IsEnabled":true,"Future":6}}}
        """,Token);
        var data=new SaveData(f.State);await data.LoadAsync(Token);Assert.Equal(CustomSizeAspectRatio.None,Config.Current.ImageCustomSize.AspectRatio);
        await using(var op=f.Operation(data)){await op.OpenAsync(f.Images,Token);await op.SaveAllAsync(Token);}
        var next=new SaveData(f.State);await next.LoadAsync(Token);Assert.Equal(120,Assert.IsType<HsvEffectUnit>(Config.Current.ImageEffect.Layers[0].Effect).Hue);
        Assert.IsType<UnknownEffectUnit>(Config.Current.ImageEffect.Layers[1].Effect);Assert.Equal(99,Assert.IsType<RippleEffectUnit>(Config.Current.ImageEffectCache.Get(typeof(RippleEffectUnit))).Frequency);
        var raw=JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(f.State,"UserSetting.json"),Token))!;
        Assert.Null(raw["Config"]?["ImageCustomSize"]?["IsUniformed"]);Assert.Equal(5,raw["Config"]!["ImageCustomSize"]!["Future"]!.GetValue<int>());
        Assert.Equal(6,Config.Current.ImageResizeFilter.ExtensionData!["Future"].GetInt32());
    }
    [Fact] public async Task EditingAndSwitchingProfilesKeepCurrentPageAndRestoreAfterReopen()
    {
        using var f=new Fixture();var data=new SaveData(f.State);await data.LoadAsync(Token);int id;
        await using(var op=f.Operation(data))
        {
            await op.OpenAsync(f.Images,Token);var page=op.Book!.CurrentPage;
            await op.EditImageOptionsAsync(config=>{var p=new EffectProfileCollection(config).CreateNew();config.ImageTrim.IsEnabled=true;config.ImageTrim.Left=.2;id=p.Id;});
            id=Config.Current.BookSetting.EffectProfileId;Assert.NotEqual(0,id);Assert.Same(page,op.Book.CurrentPage);Assert.True(op.Frame!.GetRawContentSize().Width<100);
            await op.SaveAllAsync(Token);
        }
        var restored=new SaveData(f.State);await restored.LoadAsync(Token);await using var reader=f.Operation(restored);await reader.RestoreLastAsync(Token);
        Assert.Equal(id,reader.Book!.Setting.EffectProfileId);Assert.True(Config.Current.ImageTrim.IsEnabled);Assert.Equal(.2,Config.Current.ImageTrim.Left);
    }
    [Fact] public async Task FailedSaveRollsBackProfilesCacheGeometryAndCurrentFrame()
    {
        using var f=new Fixture();var data=new SaveData(f.State);await data.LoadAsync(Token);await using var op=f.Operation(data);await op.OpenAsync(f.Images,Token);
        await op.SaveAllAsync(Token);var position=op.Position;var raw=JsonSerializer.Serialize(Config.Current);Directory.Delete(f.State,true);await File.WriteAllTextAsync(f.State,"blocked",Token);
        await Assert.ThrowsAnyAsync<Exception>(()=>op.EditImageOptionsAsync(config=>{new EffectProfileCollection(config).CreateNew(true);config.ImageTrim.Left=.3;config.ImageEffectCache.Add(new HsvEffectUnit{Hue=20});config.ImageResizeFilter.ExtensionData=new(){["Changed"]=JsonSerializer.SerializeToElement(true)};}));
        Assert.Equal(raw,JsonSerializer.Serialize(Config.Current));Assert.Equal(position,op.Position);File.Delete(f.State);Directory.CreateDirectory(f.State);await op.SaveAllAsync(Token);
    }
    [AvaloniaFact] public async Task OriginalPanelHasSixSectionsAndChangingPageDoesNotRebuildDraft()
    {
        using var f=new Fixture();var data=new SaveData(f.State);await data.LoadAsync(Token);await using var op=f.Operation(data);
        using var view=new ImageEffectView();view.Attach(op);Dispatcher.UIThread.RunJobs();Assert.Equal(6,view.FindControl<StackPanel>("Sections")!.Children.Count);
        using var model=new ImageEffectPanelViewModel(op);var draft=model.Draft;await op.OpenAsync(f.Images,Token);Assert.Same(draft,model.Draft);
        await model.CreateAsync(true);Dispatcher.UIThread.RunJobs();Assert.Equal(2,model.Profiles.Count);Assert.NotSame(draft,model.Draft);
    }
}
