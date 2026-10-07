using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using NeeView.Backends;
using NeeView.MacOS.ViewModels;
using NeeView.MacOS.Views;
namespace NeeView.Engine.Tests;

/// <summary>原播放列表文件动作/无效登记/四模板，真实文件仅限自建隔离夹具。</summary>
public sealed class PlaylistCompletionTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private static async Task<SaveData> StateAsync(Fixture f) { var state = new SaveData(f.State); await state.LoadAsync(Token); return state; }
    private sealed class Trash : IPlatformService
    {
        public int Calls; public bool Fail;
        public Task RevealAsync(string path, CancellationToken token = default) => Task.CompletedTask;
        public Task TrashAsync(string path, CancellationToken token = default)
        { Calls++; if (Fail) throw new UnauthorizedAccessException("denied"); token.ThrowIfCancellationRequested(); File.Move(path, path+".fixture-trash"); return Task.CompletedTask; }
    }
    private static void Attach(Fixture f, BookOperation operation, BitmapFactory images, Trash trash)
    {
        var backend=new FileOperationBackend(Path.Combine(f.State,"Recovery"));
        operation.AttachFileOperations(new(backend),backend,images); operation.AttachFileDeletion(trash,images);
        Config.Current.System.IsFileWriteAccessEnabled=true;
    }

    /// <summary>重命名保留条目/选择/可恢复批次/未知字段，实体联动与配置只采用真实新路径。</summary>
    [Fact]
    public async Task RenameRetainsItemAndRecoveryAndUsesUniqueNumber()
    {
        using var f=new Fixture(); var state=await StateAsync(f); await using var operation=f.Operation(state);
        using var images=new BitmapFactory(new MagickImageDecoder()); Attach(f,operation,images,new());
        await operation.Playlists.CreateNamedAsync("原列表",Token); var list=operation.Playlists.Current!;
        var first=(await operation.Playlists.AddAsync(Path.Combine(f.Images,"001.png"),Token))!;
        var removed=(await operation.Playlists.AddAsync(Path.Combine(f.Images,"002.png"),Token))!;
        await operation.Playlists.RemoveAsync([removed],Token); operation.Playlists.SelectedItem=first;
        await File.WriteAllTextAsync(Path.Combine(state.Playlists.Config.PlaylistFolder,"新列表.nvpls"),"{\"Format\":\"NeeView.Playlist/2.0.1\",\"Items\":[]}",Token);
        await operation.RenamePlaylistFileAsync(list,"新列表",Token);
        Assert.Equal("新列表 (2).nvpls",Path.GetFileName(list.Path)); Assert.Same(list,operation.Playlists.Current);
        Assert.Same(first,operation.Playlists.SelectedItem); Assert.True(list.CanRestore);
        Assert.False(File.Exists(Path.Combine(state.Playlists.Config.PlaylistFolder,"原列表.nvpls")));
        await operation.Playlists.RestoreAsync(Token); Assert.Equal(new[]{first,removed},list.Items);
        Assert.False(File.Exists(Path.Combine(f.State,".book-rename-pending.json")));
        var next=await StateAsync(f); Assert.Equal(list.Path,next.Playlists.Config.CurrentPlaylist);
    }

    /// <summary>当前阅读同一列表文件时，改名先释放来源，再恢复原Page/设置并联动历史。</summary>
    [Fact]
    public async Task RenameOpenedPlaylistBookRestoresReaderAtActualPath()
    {
        using var f=new Fixture(); var state=await StateAsync(f); await using var operation=f.Operation(state);
        using var images=new BitmapFactory(new MagickImageDecoder()); Attach(f,operation,images,new());
        await operation.Playlists.CreateNamedAsync("当前书",Token); var list=operation.Playlists.Current!;
        await operation.Playlists.AddAsync(Path.Combine(f.Images,"003.png"),Token);
        await operation.OpenPlaylistAsBookAsync(list); Assert.True(operation.Book!.Source.IsPlaylist);
        await operation.RenamePlaylistFileAsync(list,"更名书",Token);
        Assert.Equal(list.Path,operation.Book!.Path); Assert.EndsWith("003.png",operation.Book.CurrentPage!.EntryFullName);
        Assert.Equal(list.Path,state.LastBookPath); Assert.Contains(state.HistoryEntries,e=>e.Path==list.Path);
    }

    /// <summary>未授权写入或旧列表确认不得改变实体，外部修改保留原字节和恢复记录。</summary>
    [Fact]
    public async Task StaleSelectionAndExternalChangesRejectFileActions()
    {
        using var f=new Fixture(); var state=await StateAsync(f); await using var operation=f.Operation(state);
        using var images=new BitmapFactory(new MagickImageDecoder()); var trash=new Trash(); Attach(f,operation,images,trash);
        await operation.Playlists.CreateNamedAsync("旧选择",Token); var old=operation.Playlists.Current!;
        await operation.Playlists.CreateNamedAsync("新选择",Token);
        await Assert.ThrowsAsync<IOException>(()=>operation.RenamePlaylistFileAsync(old,"不能改",Token)); Assert.True(File.Exists(old.Path));
        var current=operation.Playlists.Current!; await File.AppendAllTextAsync(current.Path," ",Token);
        await Assert.ThrowsAsync<IOException>(()=>operation.DeletePlaylistFileAsync(current,Token));
        Assert.Equal(0,trash.Calls); Assert.Same(current,operation.Playlists.Current); Assert.True(File.Exists(current.Path));
        Config.Current.System.IsFileWriteAccessEnabled=false;
        Assert.False(operation.CanManagePlaylistFile); await operation.RenamePlaylistFileAsync(current,"no",Token); Assert.True(File.Exists(current.Path));
    }

    /// <summary>默认列表保护、废纸篓失败保持原状态，成功只切默认源且不触碰引用图片。</summary>
    [Fact]
    public async Task DeleteOnlyTrashesNonDefaultPlaylistAfterActualSuccess()
    {
        using var f=new Fixture(); var state=await StateAsync(f); await using var operation=f.Operation(state);
        using var images=new BitmapFactory(new MagickImageDecoder()); var trash=new Trash{Fail=true}; Attach(f,operation,images,trash);
        await operation.Playlists.InitializeAsync(Token); Assert.False(operation.CanDeletePlaylistFile);
        await operation.Playlists.CreateNamedAsync("待删除",Token); var list=operation.Playlists.Current!;
        await operation.Playlists.AddAsync(Path.Combine(f.Images,"001.png"),Token);
        await Assert.ThrowsAsync<IOException>(()=>operation.DeletePlaylistFileAsync(list,Token)); Assert.Same(list,operation.Playlists.Current);
        trash.Fail=false; await operation.OpenPlaylistAsBookAsync(list); await operation.DeletePlaylistFileAsync(list,Token);
        Assert.Null(operation.Book); Assert.NotEqual(list.Path,state.LastBookPath);
        Assert.Equal(operation.Playlists.Config.DefaultPlaylist,operation.Playlists.Current!.Path);
        Assert.True(File.Exists(list.Path+".fixture-trash")); Assert.True(File.Exists(Path.Combine(f.Images,"001.png")));
    }

    /// <summary>默认空列表惰性创建；明确作为书籍打开时实体化，没有第二默认源。</summary>
    [Fact]
    public async Task OpenAsBookMaterializesOnlyExplicitDefaultFile()
    {
        using var f=new Fixture(); var state=await StateAsync(f); await using var operation=f.Operation(state);
        await operation.Playlists.InitializeAsync(Token); var list=operation.Playlists.Current!; Assert.False(File.Exists(list.Path));
        await operation.OpenPlaylistAsBookAsync(list); Assert.True(File.Exists(list.Path)); Assert.True(operation.Book!.Source.IsPlaylist);
        Assert.Same(list,operation.Playlists.Current);
    }

    /// <summary>真实废纸篓成功后默认源损坏仍立即清除启动目标，不恢复已删除的旧书。</summary>
    [Fact]
    public async Task CorruptDefaultAfterTrashStillClearsPersistedStartup()
    {
        using var f=new Fixture(); var state=await StateAsync(f); await using var operation=f.Operation(state);
        using var images=new BitmapFactory(new MagickImageDecoder()); Attach(f,operation,images,new());
        await operation.Playlists.CreateNamedAsync("待删除书",Token); var list=operation.Playlists.Current!;
        await operation.Playlists.AddAsync(Path.Combine(f.Images,"001.png"),Token); await operation.OpenPlaylistAsBookAsync(list);
        await operation.SaveAsync(); await File.WriteAllTextAsync(operation.Playlists.Config.DefaultPlaylist,"corrupt",Token);
        await Assert.ThrowsAsync<IOException>(()=>operation.DeletePlaylistFileAsync(list,Token));
        Assert.Null(operation.Book); Assert.Null(operation.Playlists.Current); Assert.False(File.Exists(list.Path));
        Assert.NotEqual(list.Path,state.LastBookPath); var reloaded=await StateAsync(f); Assert.NotEqual(list.Path,reloaded.LastBookPath);
    }

    /// <summary>重新出现的文件不能从确认计划误删，缺失登记可恢复且别名/顺序保持。</summary>
    [Fact]
    public async Task InvalidPlanRechecksAndPreservesRecovery()
    {
        using var f=new Fixture(); var state=await StateAsync(f); var hub=state.Playlists;
        var first=(await hub.AddAsync(Path.Combine(f.Images,"001.png"),Token))!;
        var missing=(await hub.AddAsync(Path.Combine(f.Images,"missing.png"),Token))!;
        var recovered=(await hub.AddAsync(Path.Combine(f.Images,"recovered.png"),Token))!;
        await hub.RenameAsync(missing,"保留别名",Token);
        Task<bool> Exists(string path,CancellationToken token)=>Task.FromResult(File.Exists(path));
        var plan=await hub.PlanInvalidAsync(Exists,Token); Assert.Equal(new[]{missing,recovered},plan.Items);
        await File.WriteAllTextAsync(recovered.Path,"restored",Token);
        Assert.Equal(1,await hub.RemoveInvalidAsync(plan,Exists,Token)); Assert.Equal(new[]{first,recovered},hub.Current!.Items);
        await hub.RestoreAsync(Token); Assert.Equal(new[]{first,missing,recovered},hub.Current.Items); Assert.Equal("保留别名",missing.Name);
        var reloaded=new PlaylistHub(hub.Config); await reloaded.InitializeAsync(Token); Assert.Equal(hub.Current.Items.Select(i=>i.Path),reloaded.Current!.Items.Select(i=>i.Path));
    }

    /// <summary>权限/断线/取消不提交部分标记或删除批次；旧计划换列表后失效。</summary>
    [Fact]
    public async Task InvalidProbeFailuresNeverCommitPartialResults()
    {
        using var f=new Fixture(); var state=await StateAsync(f); var hub=state.Playlists;
        var first=(await hub.AddAsync("/missing-a.png",Token))!; var second=(await hub.AddAsync("/offline-b.png",Token))!;
        var bytes=await File.ReadAllBytesAsync(hub.Current!.Path,Token);
        await Assert.ThrowsAsync<IOException>(()=>hub.PlanInvalidAsync((path,_)=>path==second.Path?Task.FromException<bool>(new IOException("offline")):Task.FromResult(false),Token));
        Assert.False(first.Source.Invalid); Assert.False(second.Source.Invalid); Assert.Equal(bytes,await File.ReadAllBytesAsync(hub.Current.Path,Token));
        var plan=await hub.PlanInvalidAsync((_,_)=>Task.FromResult(false),Token);
        await hub.CreateNamedAsync("new",Token);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>hub.RemoveInvalidAsync(plan,(_,_)=>Task.FromResult(false),Token)); Assert.Empty(hub.Current!.Items);
    }

    /// <summary>四个正式模板可切换，保持原行/选择、整数JSON及正文不重新加载。</summary>
    [AvaloniaFact]
    public async Task FourTemplatesKeepSelectionAndReaderAndPersistInteger()
    {
        using var f=new Fixture(); var state=await StateAsync(f); var operation=f.Operation(state);
        var window=new MainWindow(); var images=new BitmapFactory(new MagickImageDecoder());
        var model=new ReaderWorkspaceViewModel(operation,new CommandTable(operation),state); window.Bind(model,images,new Trash()); window.Show();
        try
        {
            await window.OpenAsync(f.Images); model.ShowPanel("PlaylistPanel"); await operation.Playlists.InitializeAsync(Token);
            await operation.TogglePlaylistItemAsync(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            var view=window.FindControl<PlaylistView>("PlaylistPanelView")!; var list=view.FindControl<ListBox>("PlaylistItems")!;
            var book=operation.Book; var page=book!.CurrentPage; var selected=list.SelectedItem;
            foreach(var style in Enum.GetValues<PanelListItemStyle>())
            {
                await view.SetListStyleAsync(style); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
                Assert.Same(book,operation.Book); Assert.Same(page,book.CurrentPage); Assert.Same(selected,list.SelectedItem);
                Assert.Equal((int)style,Config.Current.Playlist.PanelListItemStyle);
                Assert.Equal(style,((ListBoxItem)list.ContainerFromIndex(0)!).GetVisualDescendants().OfType<PanelListItemView>().Single().DisplayStyle);
            }
            var json=System.Text.Json.Nodes.JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(f.State,"UserSetting.json"),Token));
            Assert.Equal(3,json!["Config"]!["Playlist"]!["PanelListItemStyle"]!.GetValue<int>());
        }
        finally { await window.PrepareShutdownAsync(); window.Close(); }
    }
}
