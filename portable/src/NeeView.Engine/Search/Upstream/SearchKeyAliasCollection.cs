using System.Collections;
using System.Collections.Generic;

namespace NeeLaboratory.IO.Search
{
    public class SearchKeyAliasCollection : IEnumerable<KeyValuePair<string, List<string>>>
    {
        private readonly Dictionary<string, List<string>> _items = new();

        public SearchKeyAliasCollection()
        {
        }


        public List<string> this[string key]
        {
            get { return _items[key]; }
            set { _items[key] = value; }
        }

        public IEnumerator<KeyValuePair<string, List<string>>> GetEnumerator()
        {
            return _items.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }


        public void Add(string key, List<string> value)
        {
            _items[key] = value;
        }

        public List<string> Decode(string s)
        {
            if (_items.TryGetValue(s.ToLowerInvariant(), out var options))
            {
                return options;
            }
            else
            {
                return new List<string> { s };
            }
        }


        public void AddRange(SearchKeyAliasCollection options)
        {
            foreach (var option in options)
            {
                _items[option.Key] = option.Value;
            }
        }
    }
}
