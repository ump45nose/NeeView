namespace NeeLaboratory.IO.Search
{
    public class SearchValueCache
    {
        public SearchValueCache(IStringCache fuzzyStringCache, IStringCache wordStringCache)
        {
            FuzzyStringCache = fuzzyStringCache;
            WordStringCache = wordStringCache;
        }

        public IStringCache FuzzyStringCache { get; }
        public IStringCache WordStringCache { get; }
    }
}
