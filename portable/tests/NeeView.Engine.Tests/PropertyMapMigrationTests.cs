using NeeView.Text;
namespace NeeView.Engine.Tests;

/// <summary>原属性映射的脚本契约，不能由直接暴露CLR配置替代。</summary>
public sealed class PropertyMapMigrationTests
{
    [Fact]
    public void OriginalConvertersAndNestedMapPreserveTypesAndErrors()
    {
        var source = new Settings(); var map = new PropertyMap("nv.Config", source, null);
        Assert.Equal("LeftToRight", map["Direction"]);
        map["Direction"] = "RightToLeft"; Assert.Equal(PageReadOrder.RightToLeft, source.Direction);
        Assert.Throws<InvalidCastException>(() => map["Direction"] = 1);
        Assert.Throws<InvalidCastException>(() => map["Direction"] = "invalid");
        map["Size"] = "20,30"; Assert.Equal(new Size(20, 30), source.Size);
        map["Color"] = "#123456"; Assert.Equal("#FF123456", map["Color"]);
        Assert.Throws<NotSupportedException>(() => map["Color"] = null);
        map["Types"] = ".cbz;.pdf"; Assert.Equal(FileTypeCollection.Parse(".cbz;.pdf").ToString(), source.Types.ToString());
        map["Words"] = "a;b"; Assert.Equal("a;b", source.Words.ToString());
        Assert.Throws<InvalidCastException>(() => map["Words"] = new[] { "a" });
        var child = Assert.IsType<PropertyMap>(map["Child"]); child["Value"] = 4d; Assert.Equal(4, source.Child.Value);
        Assert.Throws<InvalidOperationException>(() => map["Child"] = new Child());
        Assert.Throws<KeyNotFoundException>(() => map["Missing"]);
    }

    [Fact]
    public void OriginalIgnoreNameReadOnlyObsoleteAndDispatcherRulesRemain()
    {
        var source = new Settings(); var dispatcher = new CountingDispatcher();
        var map = new PropertyMap("nv.Config", null, null, source, null, "", PropertyMapOptions.Create(dispatcher));
        Assert.Throws<KeyNotFoundException>(() => map["Ignored"]);
        Assert.Throws<KeyNotFoundException>(() => map["Renamed"]);
        map["Alias"] = "value"; Assert.Equal("value", source.Renamed);
        map["ReadOnly"] = 99; Assert.Equal(7, source.ReadOnly);
        Assert.Throws<NotSupportedException>(() => map["Old"]);
        Assert.Equal(7, map["ReadOnly"]); Assert.Equal(3, dispatcher.Calls);
    }

    [Fact]
    public void NullClassStillFailsInsteadOfInventingASecondSettingsObject()
    { Assert.Throws<InvalidOperationException>(() => new PropertyMap("nv", new NullSettings(), null)); }

    public sealed class Settings
    {
        public PageReadOrder Direction { get; set; } = PageReadOrder.LeftToRight;
        public Size Size { get; set; } = new(2, 3);
        public ThemeRgba Color { get; set; } = new(255, 0, 0, 0);
        public FileTypeCollection Types { get; set; } = FileTypeCollection.Parse(".zip");
        public StringCollection Words { get; set; } = StringCollection.Parse("x;y");
        public Child Child { get; } = new();
        [PropertyMapIgnore] public string Ignored { get; } = "internal";
        [PropertyMapName("Alias")] public string Renamed { get; set; } = "";
        [PropertyMapReadOnly] public int ReadOnly { get; set; } = 7;
        [Obsolete("old"), Alternative("Alias", 46)] public int Old { get; set; }
    }
    public sealed class Child { public int Value { get; set; } }
    public sealed class NullSettings { public Child? Child { get; set; } }
    private sealed class CountingDispatcher : IPropertyMapDispatcher
    {
        public int Calls;
        public bool CheckAccess() => true;
        public void Invoke(Action action) { Calls++; action(); }
        public T Invoke<T>(Func<T> action) { Calls++; return action(); }
    }
}
