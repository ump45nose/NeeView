namespace NeeLaboratory.IO.Search
{
    public class FuzzyStringCache : StringCache
    {
        protected override string Convert(string s)
        {
            return SearchStringTools.ToNormalizedWord(s, true);
        }
    }

    public class FuzzyStringNoCache : IStringCache
    {
        public void Cleanup(int milliseconds)
        {
        }

        public string GetString(string s)
        {
            return SearchStringTools.ToNormalizedWord(s, true);
        }
    }

}
