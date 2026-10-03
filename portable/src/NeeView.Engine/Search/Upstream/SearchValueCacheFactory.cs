namespace NeeLaboratory.IO.Search
{
    public static class SearchValueCacheFactory
    {
        public static SearchValueCache Create()
        {
            return new SearchValueCache(new FuzzyStringCache(), new WordStringCache());
        }

        public static SearchValueCache CreateWithoutCache()
        {
            return new SearchValueCache(new FuzzyStringNoCache(), new WordStringNoCache());
        }
    }


}
