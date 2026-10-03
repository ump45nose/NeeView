using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace NeeLaboratory.IO.Search
{
    public class SearchKeyOptionCollection : IEnumerable<KeyValuePair<string, SearchKeyOption>>
    {
        private readonly Dictionary<string, SearchKeyOption> _items = new();

        public SearchKeyOptionCollection()
        {
        }

        public SearchKeyOption this[string key]
        {
            get { return _items[key]; }
            set { _items[key] = value; }
        }

        public IEnumerator<KeyValuePair<string, SearchKeyOption>> GetEnumerator()
        {
            return _items.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        public bool TryGetValue(string key, [MaybeNullWhen(false)] out SearchKeyOption value)
        {
            return _items.TryGetValue(key, out value);
        }


        public void Add(SearchConjunction conjunction)
        {
            var option = new ConjunctionSearchKeyOption("/c." + conjunction.ToString().ToLowerInvariant(), conjunction);
            _items.Add(option.Name, option);
        }

        public void Add(SearchPropertyProfile profile)
        {
            var option = new PropertySearchKeyOption("/p." + profile.Name, profile);
            _items.Add(option.Name, option);
        }

        public void Add(SearchFilterProfile profile)
        {
            var option = new FilterSearchKeyOption("/m." + profile.Name, profile);
            _items.Add(option.Name, option);
        }

        public void AddRange(SearchKeyOptionCollection options)
        {
            foreach(var option in options)
            {
                _items[option.Key] = option.Value;
            }
        }

    }

}
