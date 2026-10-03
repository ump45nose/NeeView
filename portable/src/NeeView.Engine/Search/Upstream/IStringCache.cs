namespace NeeLaboratory.IO.Search
{
    public interface IStringCache
    {
        void Cleanup(int milliseconds);
        string GetString(string s);
    }
}