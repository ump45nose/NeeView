using System.Threading;

namespace NeeLaboratory.IO.Search
{
    public interface ISearchItem
    {
        SearchValue GetValue(SearchPropertyProfile profile, string? parameter, CancellationToken token);
    }

}
