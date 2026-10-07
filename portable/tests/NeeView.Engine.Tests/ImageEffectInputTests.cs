using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using NeeView.Effects;
using NeeView.MacOS.ViewModels;
using NeeView.MacOS.Views;
namespace NeeView.Engine.Tests;

/// <summary>效果事务实际重建表单时，方向键不得泄漏到主窗的切书命令。</summary>
public sealed class ImageEffectInputTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DropdownSurvivesNavigationAndRestoresFocusAfterCommitOrRollback(bool failSave)
    {
        using var fixture = new Fixture();
        var state = new SaveData(fixture.State); await state.LoadAsync(TestContext.Current.CancellationToken);
        var a = Directory.CreateDirectory(Path.Combine(fixture.Root, "Books", "A")).FullName;
        var b = Directory.CreateDirectory(Path.Combine(fixture.Root, "Books", "B")).FullName;
        foreach (var directory in new[] { a, b })
            foreach (var file in Directory.GetFiles(fixture.Images)) File.Copy(file, Path.Combine(directory, Path.GetFileName(file)));
        state.SetShortcut("PrevBook", "Up"); state.SetShortcut("NextBook", "Down");
        var operation = fixture.Operation(state);
        var model = new ReaderWorkspaceViewModel(operation, new CommandTable(operation), state);
        var main = new MainWindow();
        main.Bind(model, new BitmapFactory(new NeeView.Backends.MagickImageDecoder()), new NoPlatform()); main.Show();
        try
        {
            await main.OpenAsync(b); await operation.JumpAsync(2);
            model.ShowPanel("ImageEffectPanel"); Pump(main);
            var view = main.FindControl<ImageEffectView>("ImageEffectPanelView")!;
            var combo = view.GetVisualDescendants().OfType<ComboBox>().Single(c => c.SelectedItem is EffectType);
            var previous = (EffectType)combo.SelectedItem!; var editorName = combo.Name;
            Assert.True(combo.Focus()); combo.IsDropDownOpen = true; Pump(main);
            SendDown(main); await Settle(main);
            // 用真实下拉选择事件触发候选变化；Headless的popup键盘选择与macOS原生焦点分别验收。
            combo.SelectedItem = EffectType.Monochrome; Pump(main);
            Assert.True(combo.IsDropDownOpen);
            Assert.Contains(combo, view.GetVisualDescendants());
            Assert.Equal(previous, Config.Current.ImageEffect.Layers[0].EffectType);
            Assert.Equal(b, operation.Book!.Path); Assert.Equal(2, operation.Position.Index);
            if (failSave) { Directory.Delete(fixture.State, true); await File.WriteAllTextAsync(fixture.State, "blocked", TestContext.Current.CancellationToken); }
            combo.IsDropDownOpen = false;
            ComboBox? replacement = null;
            for (int i = 0; i < 100; i++)
            {
                await Settle(main);
                replacement = view.GetVisualDescendants().OfType<ComboBox>().Single(c => c.Name == editorName);
                if (!ReferenceEquals(combo, replacement)) break;
            }
            Assert.NotSame(combo, replacement);
            Assert.Same(replacement, main.FocusManager!.GetFocusedElement());
            Assert.Equal(failSave ? previous : EffectType.Monochrome, Config.Current.ImageEffect.Layers[0].EffectType);
            Assert.Equal(failSave ? previous : EffectType.Monochrome, replacement!.SelectedItem);
            SendDown(main); await Settle(main);
            Assert.Equal(b, operation.Book!.Path); Assert.Equal(2, operation.Position.Index);
        }
        finally
        {
            if (File.Exists(fixture.State)) { File.Delete(fixture.State); Directory.CreateDirectory(fixture.State); }
            await main.PrepareShutdownAsync(); main.Close();
        }
    }
    [AvaloniaFact]
    public async Task EarlierLayerParameterChangesKeepFocusOnTheSameLaterLayerEditor()
    {
        using var fixture = new Fixture(); var state = new SaveData(fixture.State); await state.LoadAsync(TestContext.Current.CancellationToken);
        var operation = fixture.Operation(state); var model = new ReaderWorkspaceViewModel(operation, new CommandTable(operation), state);
        var main = new MainWindow(); main.Bind(model, new BitmapFactory(new NeeView.Backends.MagickImageDecoder()), new NoPlatform()); main.Show();
        try
        {
            await main.OpenAsync(fixture.Images);
            await operation.EditImageOptionsAsync(config =>
            {
                config.ImageEffect.Layers[0].ChangeType(EffectType.Hsv, config.ImageEffect, config.ImageEffectCache);
                config.ImageEffect.Layers.CreateNew();
            });
            model.ShowPanel("ImageEffectPanel"); Pump(main);
            var view = main.FindControl<ImageEffectView>("ImageEffectPanelView")!;
            var combo = view.GetVisualDescendants().OfType<ComboBox>().Single(c => c.SelectedItem is EffectType.Hsv);
            Assert.True(combo.Focus());
            await operation.EditImageOptionsAsync(config => config.ImageEffect.Layers[0].ChangeType(EffectType.Colorize, config.ImageEffect, config.ImageEffectCache));
            await Settle(main);
            var replacement = view.GetVisualDescendants().OfType<ComboBox>().Single(c => c.SelectedItem is EffectType.Hsv);
            Assert.NotSame(combo, replacement); Assert.Same(replacement, main.FocusManager!.GetFocusedElement());
        }
        finally { await main.PrepareShutdownAsync(); main.Close(); }
    }
    private static void SendDown(Window window)
    { window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null); window.KeyRelease(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null); }
    private static void Pump(Window window) { Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); }
    private static async Task Settle(Window window) { await Task.Delay(30, TestContext.Current.CancellationToken); Pump(window); }
    private sealed class NoPlatform : IPlatformService
    {
        public Task RevealAsync(string path, CancellationToken token = default) => throw new NotSupportedException();
        public Task TrashAsync(string path, CancellationToken token = default) => throw new NotSupportedException();
    }
}
