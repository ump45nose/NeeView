using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace NeeLaboratory.IO.Search
{
    public abstract class StringCache : IStringCache
    {
        private struct Item
        {
            public Item(string text, int timestamp)
            {
                Text = text;
                Timestamp = timestamp;
            }

            public string Text { get; }
            public int Timestamp { get; set; }
        }


        private readonly Dictionary<string, Item> _cache = new();
        private readonly Lock _lock = new();

        public string GetString(string s)
        {
            lock (_lock)
            {
                if (_cache.TryGetValue(s, out var result))
                {
                    result.Timestamp = System.Environment.TickCount;
                    return result.Text;
                }

                //var value = StringUtils.ToNormalizedWord(s, true);
                var value = Convert(s);
                _cache.Add(s, new Item(value, System.Environment.TickCount));
                return value;
            }
        }

        protected abstract string Convert(string s);

        public void Cleanup(int milliseconds)
        {
            lock (_lock)
            {
                var now = System.Environment.TickCount;

                var list = new List<string>();
                foreach (var pair in _cache)
                {
                    if (now - pair.Value.Timestamp > milliseconds)
                    {
                        list.Add(pair.Key);
                    }
                }

                Debug.WriteLine($"Cleanup: remove={list.Count}");

                foreach (var key in list)
                {
                    _cache.Remove(key);
                }
            }
        }
    }
}
