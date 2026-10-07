using NeeView.Text;
namespace NeeView;
/// <summary>原--script的脚本路径和引号参数；路径参数继续进入唯一打开流程。</summary>
public sealed record ScriptLaunchRequest(string Path, object?[] Args)
{
    public static (string[] Paths, ScriptLaunchRequest? Script) Parse(IEnumerable<string> args)
    {
        var paths = new List<string>(); ScriptLaunchRequest? script = null; using var iterator = args.GetEnumerator();
        while (iterator.MoveNext())
        {
            if (iterator.Current != "--script") { paths.Add(iterator.Current); continue; }
            if (!iterator.MoveNext()) throw new ArgumentException("--script需要脚本路径及可选参数。");
            var values = StringTools.SplitArgument(iterator.Current); if (values.Count == 0) throw new ArgumentException("脚本路径为空。");
            script = new(values[0], values.Skip(1).Cast<object?>().ToArray());
        }
        return (paths.ToArray(), script);
    }
}
