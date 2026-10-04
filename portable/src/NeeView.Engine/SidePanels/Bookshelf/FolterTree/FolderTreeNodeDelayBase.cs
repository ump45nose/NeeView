// Copyright (c) NeeLaboratory. 原 FolderTreeNodeDelayBase 展开时生成/占位规则；同步 I/O 替换为可取消异步。
using System.Collections.ObjectModel;
namespace NeeView;

/// <summary>展开才生成子项；折叠、重试和关闭拒绝晚到枚举，失败保留已生成子项。</summary>
public abstract class FolderTreeNodeDelayBase : FolderTreeNodeBase
{
    private readonly ObservableCollection<FolderTreeNodeBase> _dummy = [new DummyNode()];
    private CancellationTokenSource? _request;
    private long _revision;
    private bool _loading;
    private string? _error;
    public bool IsDelayCreation => _children is null;
    public bool IsLoading { get => _loading; private set { if (SetProperty(ref _loading, value)) OnPropertyChanged(nameof(DisplayText)); } }
    public string? Error { get => _error; private set { if (SetProperty(ref _error, value)) OnPropertyChanged(nameof(DisplayText)); } }
    public override string DisplayText => DisplayName + (IsLoading ? " …" : Error is null ? "" : " ⚠");
    public Task<bool> Loading { get; private set; } = Task.FromResult(false);
    public override ObservableCollection<FolderTreeNodeBase> Children => _children ?? _dummy;
    public override bool IsExpanded
    {
        get => base.IsExpanded;
        set
        {
            if (IsDisposed || value == base.IsExpanded) return;
            base.IsExpanded = value;
            if (value && IsDelayCreation) Loading = CreateChildrenAsync();
            else if (!value) CancelLoading();
        }
    }
    /// <summary>只生成此节点直接子目录，成功才提交；缓存展开不重复扫描。</summary>
    /// <param name="force">显式刷新忽略已有缓存，失败仍保留旧子项。</param>
    /// <param name="token">同步路径或窗口请求取消。</param>
    /// <returns>本次请求是否成功生成有效子项。</returns>
    public Task<bool> CreateChildrenAsync(bool force = false, CancellationToken token = default)
    {
        if (IsDisposed) return Task.FromResult(false);
        if (!force && _children is not null) return Task.FromResult(true);
        if (!force && _request is { IsCancellationRequested: false }) return Loading;
        CancelLoading();
        var revision = ++_revision; var request = CancellationTokenSource.CreateLinkedTokenSource(token); _request = request;
        return Loading = LoadAsync();
        async Task<bool> LoadAsync()
        {
            IsLoading = true; Error = null;
            try
            {
                var next = await LoadChildrenAsync(request.Token); request.Token.ThrowIfCancellationRequested();
                if (IsDisposed || revision != _revision) return false;
                var old = _children; var children = new ObservableCollection<FolderTreeNodeBase>();
                foreach (var node in next)
                {
                    var retained = old?.FirstOrDefault(child => child.Name == node.Name && !child.IsDisposed);
                    if (retained is null) children.Add(node); else { node.Dispose(); children.Add(retained); }
                }
                foreach (var node in old ?? []) if (!children.Contains(node)) node.Dispose();
                _children = children; OnPropertyChanged(nameof(Children)); return true;
            }
            catch (OperationCanceledException) when (request.IsCancellationRequested) { return false; }
            catch (Exception ex) { if (!IsDisposed && revision == _revision) Error = "目录暂不可访问：" + ex.Message; return false; }
            finally { if (revision == _revision) IsLoading = false; if (ReferenceEquals(_request, request)) _request = null; request.Dispose(); }
        }
    }
    /// <summary>具体目录通过来源契约枚举，无界面或同步文件系统访问。</summary>
    protected abstract Task<IReadOnlyList<FolderTreeNodeBase>> LoadChildrenAsync(CancellationToken token);
    /// <summary>折叠不丢已加载子树；只取消当前等待并禁止结果提交。</summary>
    public void CancelLoading() { ++_revision; _request?.Cancel(); _request = null; IsLoading = false; }
    /// <summary>关闭后原生/网络枚举晚到不能更新树。</summary>
    public override void Dispose() { CancelLoading(); base.Dispose(); }
}
