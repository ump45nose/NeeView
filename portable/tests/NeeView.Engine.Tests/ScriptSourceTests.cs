// Copyright (c) NeeLaboratory.
using System.Text.Json;
using NeeView;
namespace NeeView.Engine.Tests;

/// <summary>脚本头部解析、窄转义及配置 JSON 兼容性核验。</summary>
public sealed class ScriptSourceTests
{
    [Fact]
    public void ParsesHeaderUntilFirstCodeLineAndKeepsFirstName()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".nvjs");
        File.WriteAllText(path, "// @name First\n// @name Second\n// @args one\n// @args two\n// @description hello\\nworld\n// @argsdescription x\\t y\n// @shortcutkey Ctrl+A\n// @shortcutkey Ctrl+B\n// @mousegesture L,R\n// @touchgesture up\n// ordinary continued\nconst x = 1;\n// @name Ignored");
        try { var source = ScriptCommandSource.Create(path); Assert.Equal("First", source.Text); Assert.Equal("one two", source.Args); Assert.Equal("hello\nworld", source.Remarks); Assert.Equal("x\t y", source.ArgsDescription); Assert.Equal("Ctrl+A,Ctrl+B", source.ShortCutKey); Assert.Equal("L,R", source.MouseGesture); Assert.Equal("up", source.TouchGesture); }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData("a\\tb\\nc\\rd\\\\e\\\"f", "a\tb\nc\rd\\e\"f")]
    [InlineData("x\\q\\", "x\\q\\")]
    public void UsesOnlyScriptEscapes(string value, string expected) => Assert.Equal(expected, ScriptStringEscape.Unescape(value));

    [Fact]
    public void ConfigDefaultsAndRawFolderRoundTrip()
    {
        var config = new ScriptConfig { DefaultFolder = "/tmp/scripts" };
        Assert.False(config.IsScriptFolderEnabled); Assert.Equal(ScriptErrorLevel.Error, config.ErrorLevel); Assert.True(config.OnBookLoadedWhenRenamed); Assert.False(config.IsSQLiteEnabled); Assert.Equal("/tmp/scripts", config.ScriptFolder);
        config.ScriptFolder = "/tmp/custom"; var json = JsonSerializer.Serialize(config); var restored = JsonSerializer.Deserialize<ScriptConfig>(json)!; Assert.Equal("/tmp/custom", restored.ScriptFolderRaw); Assert.Equal("/tmp/custom", restored.ScriptFolder);
    }
}
