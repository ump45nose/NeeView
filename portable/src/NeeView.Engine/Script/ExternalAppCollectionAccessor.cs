// Copyright (c) NeeLaboratory. 原集合/ExternalAppAccessor，MIT。
namespace NeeView;
/// <summary>原集合直接访问唯一配置，不保存第二份应用清单。</summary>
public sealed class ExternalAppCollectionAccessor(ScriptAccessContext context)
{
    public ExternalAppAccessor[] Items => context.Read(() => Config.Current.System.ExternalAppCollection.Select(x => new ExternalAppAccessor(context, x)).ToArray());
    public ExternalAppAccessor CreateNew() => context.Write(() => new ExternalAppAccessor(context, Config.Current.System.ExternalAppCollection.CreateNew()));
    public void Remove(ExternalAppAccessor item) => context.Write(() => { Config.Current.System.ExternalAppCollection.Remove(item.Source); });
}
/// <summary>字段读写在UI调度中，Execute等待现有系统/实体化链的真实完成。</summary>
public sealed class ExternalAppAccessor(ScriptAccessContext context, ExternalApp source)
{
    internal ExternalApp Source { get; } = source;
    public string Name { get => context.Read(() => Source.DisplayName); set => context.Write(() => Source.Name = value); }
    public string Command { get => context.Read(() => Source.Command ?? ""); set => context.Write(() => Source.Command = value); }
    public string Parameter { get => context.Read(() => Source.Parameter); set => context.Write(() => Source.Parameter = value ?? ""); }
    public string ArchivePolicy { get => context.Read(() => Source.ArchivePolicy.ToString()); set => context.Write(() => Source.ArchivePolicy = ScriptEnum.Parse<ArchivePolicy>(value)); }
    public string WorkingDirectory { get => context.Read(() => Source.WorkingDirectory ?? ""); set => context.Write(() => Source.WorkingDirectory = value); }
    public void Execute(PageAccessor page) => Execute([page]);
    public void Execute(PageAccessor[] pages)
    {
        var captured = pages.Select(p => p.Source).ToArray();
        context.Run(() => context.Operation.OpenExternalApplicationAsync(Source, token: context.Token, explicitPages: captured, throwOnError: true));
    }
    public void Execute(string path) => Execute([path]);
    public void Execute(string[] paths) => context.Run(() => context.Operation.OpenScriptExternalApplicationAsync(Source, paths.ToArray(), context.Token));
}
/// <summary>保持原字符串枚举API，同时拒绝未定义的数值以免污染原JSON。</summary>
public static class ScriptEnum
{
    public static T Parse<T>(string value) where T : struct, Enum => Enum.TryParse<T>(value, out var result) && Enum.IsDefined(result)
        ? result : throw new ArgumentException($"无效的{typeof(T).Name}：{value}", nameof(value));
}
