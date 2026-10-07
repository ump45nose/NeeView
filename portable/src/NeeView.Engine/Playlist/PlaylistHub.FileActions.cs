// Copyright (c) NeeLaboratory. 原PlaylistHub/PlaylistService文件动作和可恢复清理，MIT。
namespace NeeView;

/// <summary>确认使用原列表及原条目引用；换列表后不能应用旧清理计划。</summary>
public sealed record PlaylistInvalidPlan(Playlist Playlist,IReadOnlyList<PlaylistItem> Items);

public sealed partial class PlaylistHub
{
    public bool CanDeleteFile=>Current is {} list&&list.Path!=Config.DefaultPlaylist;
    /// <summary>原显式打开先Flush；尚未创建的默认空列表仅在该明确动作中实体化。</summary>
    /// <param name="expected">明确选择的列表引用。</param><param name="token">原子提交前取消。</param>
    public async Task PrepareOpenAsBookAsync(Playlist expected,CancellationToken token=default)
    {
        await _gate.WaitAsync(token);
        try
        {
            if(!ReferenceEquals(expected,Current))throw new InvalidOperationException("播放列表已切换，请重新操作。");
            if(_fingerprint is null)
            {
                var source=new PlaylistSource{Items=expected.Items.Select(item=>item.Source).ToList(),ExtensionData=expected.Source.ExtensionData};
                var bytes=System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(source,Options);
                await Task.Run(()=>WriteCore(expected.Path,bytes,token),CancellationToken.None);
            }
            else await VerifyFileAsync(expected,token);
        }
        finally { _gate.Release(); }
    }
    /// <summary>同一原列表锁内验证来源字节；外部修改不能被改名或删除静默覆盖。</summary>
    private async Task VerifyFileAsync(Playlist expected,CancellationToken token)
    {
        if(!ReferenceEquals(expected,Current))throw new InvalidOperationException("播放列表已切换，请重新操作。");
        var bytes=await Task.Run(()=>File.ReadAllBytes(expected.Path),token);
        if(Fingerprint(bytes)!=_fingerprint)throw new IOException("播放列表已被外部修改，请重新打开后再操作。");
    }
    /// <summary>实际改名复用整书同目录后端；成功才更新路径，名称/顺序/恢复批次不重建。</summary>
    /// <param name="expected">采集名称前的原列表。</param><param name="plan">原编号算法产生的目标。</param>
    /// <param name="backend">已有实体操作后端。</param><param name="token">提交前取消。</param>
    public async Task RenameFileAsync(Playlist expected,BookRenamePlan plan,IBookRenameBackend backend,CancellationToken token=default)
    {
        await _gate.WaitAsync(token);
        try
        {
            await VerifyFileAsync(expected,token);
            if(plan.Target.Path!=expected.Path)throw new IOException("改名计划不属于当前列表。");
            await backend.RenameAsync(plan,token);
            expected.Path=plan.Destination;Config.CurrentPlaylist=plan.Destination;
            // 实体已经提交；扫描失败只报告刷新错误，不能被调用方误认为改名未发生。
            Error=null;
            try { PlaylistFiles=await Task.Run(()=>GetFiles(plan.Destination)); }
            catch(Exception error) { PlaylistFiles=[Config.DefaultPlaylist,plan.Destination];Error="播放列表已更名，但文件列表刷新失败："+error.Message; }
        }
        catch(Exception error){Error=error.Message;throw;}
        finally{_gate.Release();Changed?.Invoke(this,EventArgs.Empty);}
    }
    /// <summary>原默认列表不能删除；平台真实成功才切回Default，不删除引用图片。</summary>
    /// <param name="expected">确认前捕获的原列表。</param><param name="platform">现有废纸篓能力。</param>
    /// <param name="token">平台提交前取消。</param>
    public async Task DeleteFileAsync(Playlist expected,IPlatformService platform,CancellationToken token=default)
    {
        await _gate.WaitAsync(token);
        try
        {
            if(expected.Path==Config.DefaultPlaylist)throw new InvalidOperationException("不能删除默认播放列表。");
            await VerifyFileAsync(expected,token);await platform.TrashAsync(expected.Path,token);
            // 已真实移走文件，不能因默认源损坏而继续持有已删除的列表并回写旧路径。
            Current=null;SelectedItem=null;_fingerprint=null;Config.CurrentPlaylist=Config.DefaultPlaylist;
            try{await LoadCoreAsync(Config.DefaultPlaylist,CancellationToken.None);}
            catch(Exception error){throw new IOException("播放列表文件已移至废纸篓，但默认列表加载失败。",error);}
        }
        catch(Exception error){Error=error.Message;throw;}
        finally{_gate.Release();Changed?.Invoke(this,EventArgs.Empty);}
    }
    /// <summary>先可靠读取整组，再保存Invalid标记；断线/权限/取消不能当作链接缺失。</summary>
    /// <param name="exists">原归档存在检查，包括内部条目。</param><param name="token">遍历取消。</param>
    /// <returns>需要用户确认的可恢复移除计划，源和条目身份保持。</returns>
    public async Task<PlaylistInvalidPlan> PlanInvalidAsync(Func<string,CancellationToken,Task<bool>> exists,CancellationToken token=default)
    {
        await InitializeAsync(token);await _gate.WaitAsync(token);
        try
        {
            var list=Current??throw new IOException(Error??"播放列表未加载。");var missing=new List<PlaylistItem>();
            foreach(var item in list.Items){token.ThrowIfCancellationRequested();if(!await exists(item.Path,token))missing.Add(item);}
            await EditCoreAsync(_=>{foreach(var item in list.Items)item.Source.Invalid=missing.Contains(item);},token);
            Error=null;return new(list,missing);
        }
        catch(Exception error){Error=error.Message;throw;}
        finally{_gate.Release();Changed?.Invoke(this,EventArgs.Empty);}
    }
    /// <summary>确认后再次检查缺失，换列表拒绝，恢复的文件不移除；失败保持原恢复批次。</summary>
    public async Task<int> RemoveInvalidAsync(PlaylistInvalidPlan plan,Func<string,CancellationToken,Task<bool>> exists,CancellationToken token=default)
    {
        await _gate.WaitAsync(token);
        try
        {
            var current=Current;
            if(current is null||!ReferenceEquals(plan.Playlist,current))throw new InvalidOperationException("播放列表已切换，请重新检查无效登记。");
            var missing=new List<PlaylistItem>();
            foreach(var item in plan.Items.Where(current.Items.Contains))if(!await exists(item.Path,token))missing.Add(item);
            await EditCoreAsync(list=>list.Remove(missing),token);Error=null;return missing.Count;
        }
        catch(Exception error){Error=error.Message;throw;}
        finally{_gate.Release();Changed?.Invoke(this,EventArgs.Empty);}
    }
}
