using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace NeeLaboratory.IO.Search
{
    /// <summary>
    /// 検索本体
    /// </summary>
    public class Searcher : IDisposable
    {
        private readonly SearchKeyAnalyzer _searchKeyAnalyzer;
        private bool _disposedValue = false;
        private readonly SearchContext _context;


        public Searcher(SearchContext context)
        {
            _context = context;
            _searchKeyAnalyzer = new SearchKeyAnalyzer(_context.KeyOptions, _context.KeyAlias);
        }


        public List<SearchKey> PreKeys { get; set; } = new();

        public List<SearchKey> PostKeys { get; set; } = new();


        protected void ThrowIfDisposed()
        {
            if (_disposedValue) throw new ObjectDisposedException(GetType().FullName);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposedValue)
            {
                if (disposing)
                {
                }
                _disposedValue = true;
            }
        }

        public void Dispose()
        {
            Dispose(true);
        }

        /// <summary>
        /// 生成キー解析
        /// </summary>
        /// <param name="keyword"></param>
        /// <returns></returns>
        /// <exception cref="SearchKeywordException">フォーマットエラー</exception>
        public List<SearchKey> Analyze(string keyword)
        {
            return _searchKeyAnalyzer.Analyze(keyword);
        }

        /// <summary>
        /// 検索
        /// </summary>
        /// <param name="keyword">検索キーワード</param>
        /// <param name="entries">検索対象</param>
        /// <returns></returns>
        public IEnumerable<ISearchItem> Search(string keyword, IEnumerable<ISearchItem> entries, CancellationToken token)
        {
            IEnumerable<SearchKey> keys;
            try
            {
                keys = Analyze(keyword);
            }
            catch (Exception)
            {
                return entries;
            }

            if (!keys.Any())
            {
                return entries;
            }

            return Search(keys, entries, token);
        }

        /// <summary>
        /// 検索
        /// </summary>
        /// <param name="keys">検索キー</param>
        /// <param name="entries">検索対象</param>
        /// <returns></returns>
        public IEnumerable<ISearchItem> Search(IEnumerable<SearchKey> keys, IEnumerable<ISearchItem> entries, CancellationToken token)
        {
            ThrowIfDisposed();
            token.ThrowIfCancellationRequested();

            var all = entries;

            // キーワードやエントリーが空ならそのまま返す
            if (!keys.Any() || !entries.Any())
            {
                return all;
            }
            
            // フィルター生成
            var units = CreateFilterCollection(keys);

            // キーワードによる絞込
            foreach (var unit in units)
            {
                token.ThrowIfCancellationRequested();

                switch (unit.Key.Conjunction)
                {
                    case SearchConjunction.And:
                        entries = entries.Where(e => unit.Filter.IsMatch(_context, e, token));
                        break;
                    case SearchConjunction.Or:
                        entries = entries.Union(all.Where(e => unit.Filter.IsMatch(_context, e, token)));
                        break;
                    case SearchConjunction.Not:
                        entries = entries.Where(e => !unit.Filter.IsMatch(_context, e, token));
                        break;
                    case SearchConjunction.PreOr:
                        entries = all.Where(e => unit.Filter.IsMatch(_context, e, token)).Union(entries);
                        break;
                }
            }

            return entries;
        }

        public List<SearcherFilterUnit> CreateFilterCollection(IEnumerable<SearchKey> keys)
        {
            var fixedKeys = PreKeys.Concat(keys).Concat(PostKeys);

            return  fixedKeys
                .Select(key => new SearcherFilterUnit(key, key.Filter.CreateFunc(key.Property, key.PropertyParameter, key.Format)))
                .ToList();
        }

        public SearcherFilter CreateFilter(string keyword)
        {
            var units = string.IsNullOrEmpty(keyword) ? new() : CreateFilterCollection(Analyze(keyword));
            return new SearcherFilter(_context, units);
        }
    }


    /// <summary>
    /// 検索のフィルター機能を切り出したもの
    /// </summary>
    public class SearcherFilter
    {

        private readonly SearchContext _context;
        private readonly List<SearcherFilterUnit> _units;

        public SearcherFilter(SearchContext context) : this(context, new())
        {
        }

        public SearcherFilter(SearchContext context, List<SearcherFilterUnit> units)
        {
            _context = context;
            _units = units;
        }

        public bool Filter(ISearchItem entry)
        {
            var result = true;

            foreach (var unit in _units)
            {
                switch (unit.Key.Conjunction)
                {
                    case SearchConjunction.And:
                        result = result && unit.Filter.IsMatch(_context, entry, CancellationToken.None);
                        break;
                    case SearchConjunction.Or:
                        result = result || unit.Filter.IsMatch(_context, entry, CancellationToken.None);
                        break;
                    case SearchConjunction.Not:
                        result = result && !unit.Filter.IsMatch(_context, entry, CancellationToken.None);
                        break;
                    case SearchConjunction.PreOr:
                        result = unit.Filter.IsMatch(_context, entry, CancellationToken.None) || result;
                        break;
                }
            }

            return result;
        }
    }

    public record SearcherFilterUnit(SearchKey Key, SearchFilter Filter);
}
