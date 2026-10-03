namespace NeeLaboratory.IO.Search
{
    public abstract class SearchKeyOption
    {
        protected SearchKeyOption(string option)
        {
            Name = option;
        }

        public string Name { get; }
    }

    /// <summary>
    /// 接続詞オプション
    /// </summary>
    public class ConjunctionSearchKeyOption : SearchKeyOption
    {
        public ConjunctionSearchKeyOption(string option, SearchConjunction conjunction) : base(option)
        {
            SearchConjunction = conjunction;
        }

        public SearchConjunction SearchConjunction { get; }
    }

    /// <summary>
    /// 検索オプション
    /// </summary>
    public class FilterSearchKeyOption : SearchKeyOption
    {
        public FilterSearchKeyOption(string option, SearchFilterProfile profile) : base(option)
        {
            Profile = profile;
        }

        public SearchFilterProfile Profile { get; }
    }

   /// <summary>
   /// プロパティオプション
   /// </summary>
    public class PropertySearchKeyOption : SearchKeyOption
    {
        public PropertySearchKeyOption(string option, SearchPropertyProfile profile) : base(option)
        {
            Profile = profile;
        }

        public SearchPropertyProfile Profile { get; }
    }
}
