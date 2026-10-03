namespace NeeLaboratory.IO.Search
{
    public class WordStringCache : StringCache
    {
        protected override string Convert(string s)
        {
            return SearchStringTools.ToNormalizedWord(s, false);
        }
    }

    public class WordStringNoCache : IStringCache
    {
        public void Cleanup(int milliseconds)
        {
        }

        public string GetString(string s)
        {
            return SearchStringTools.ToNormalizedWord(s, false);
        }
    }
}
