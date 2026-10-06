using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
namespace NeeView.MacOS.ViewModels;

/// <summary>原外部应用有序集合的独立编辑草稿；取消不修改配置、不执行应用。</summary>
public sealed class ExternalApplicationsViewModel : ObservableObject
{
    private ExternalApp? _selected;
    public ObservableCollection<ExternalApp> Items { get; }
    public ExternalApp? SelectedItem { get => _selected; set { if (SetProperty(ref _selected, value)) { OnPropertyChanged(nameof(HasSelection)); OnPropertyChanged(nameof(SelectedArchiveIndex)); } } }
    public bool HasSelection => SelectedItem is not null;
    public IReadOnlyList<string> ArchivePolicies { get; } = ["不发送归档条目", "发送归档文件", "发送归档内部路径", "提取后发送文件"];
    public int SelectedArchiveIndex { get => SelectedItem is { } app && Enum.IsDefined(app.ArchivePolicy) ? (int)app.ArchivePolicy : -1;
        set { if (SelectedItem is { } app && value is >= 0 and <= 3) { app.ArchivePolicy = (ArchivePolicy)value; OnPropertyChanged(); } } }
    /// <summary>复制所有原字段，列表排序与表现选择不触及运行集合。</summary>
    public ExternalApplicationsViewModel(IEnumerable<ExternalApp> source) { Items = new(source.Select(app => (ExternalApp)app.Clone())); SelectedItem = Items.FirstOrDefault(); }
    /// <summary>追加原默认关联应用并选中。</summary>
    public void Add() { var item = new ExternalApp(); Items.Add(item); SelectedItem = item; }
    /// <summary>只删除草稿条目；命令索引始终由最后集合顺序解释。</summary>
    public void Remove() { if (SelectedItem is not { } item) return; var index = Items.IndexOf(item); Items.Remove(item); SelectedItem = Items.ElementAtOrDefault(Math.Min(index, Items.Count - 1)); }
    /// <summary>保留原显式有序集合，移动不复制配置对象。</summary>
    public void Move(int direction) { if (SelectedItem is not { } item) return; var index = Items.IndexOf(item); var target = index + Math.Sign(direction); if (target >= 0 && target < Items.Count) { Items.Move(index, target); SelectedItem = item; } }
    /// <summary>返回父设置草稿副本；直到五JSON事务成功才成为运行配置。</summary>
    public ExternalAppCollection ToCollection() => new(Items.Select(app => (ExternalApp)app.Clone()));
}
