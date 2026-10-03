// 固定原搜索库测试；仅移除未使用的诊断引用和 xUnit v2 命名空间，测试逻辑保持。
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Xunit.Sdk;
using System.Collections;
using System.Globalization;

namespace NeeLaboratory.IO.Search.Test
{
    public class SearchEngineTest
    {
        private readonly ITestOutputHelper _output;


        public SearchEngineTest(ITestOutputHelper output)
        {
            _output = output;
        }

        /// <summary>
        /// あいまい変換テスト
        /// </summary>
        [Fact]
        public void SearchEngineNormalizeTest()
        {
            string normalized;

            normalized = SearchStringTools.ToNormalizedWord("ひらがなゔう゛か゛", true);
            Assert.Equal("ヒラガナヴヴガ", normalized);

            normalized = SearchStringTools.ToNormalizedWord("ﾊﾝｶｸｶﾞﾅｳﾞ", true);
            Assert.Equal("ハンカクガナヴ", normalized);

            normalized = SearchStringTools.ToNormalizedWord("混合された日本語ﾃﾞス。", true);
            Assert.Equal("混合サレタ日本語デス。", normalized);

            normalized = SearchStringTools.ToNormalizedWord("㌫", true);
            Assert.Equal("パ-セント", normalized);

            normalized = SearchStringTools.ToNormalizedWord("♡♥❤?", true);
            Assert.Equal("♡♡♡?", normalized);
        }




        /// <summary>
        /// 検索キーワード解析テスト
        /// </summary>
        [Fact]
        public void SearchEngineKeywordAnalyzeTest()
        {
            var context = new SearchContext();
            context.AddProfile(new DateSearchProfile());
            var analyzer = new SearchKeyAnalyzer(context.KeyOptions, context.KeyAlias);
            List<SearchKey> keys;

            keys = analyzer.Analyze("");
            Assert.Empty(keys);

            keys = analyzer.Analyze("    ");
            Assert.Empty(keys);

            keys = analyzer.Analyze("word1");
            Assert.Single(keys);
            Assert.Equal(new SearchKey(SearchConjunction.And, SearchPropertyProfiles.Text, null, SearchFilterProfiles.Fuzzy, "word1"), keys[0]);


            keys = analyzer.Analyze("    word1");
            Assert.Single(keys);
            Assert.Equal(new SearchKey(SearchConjunction.And, SearchPropertyProfiles.Text, null, SearchFilterProfiles.Fuzzy, "word1"), keys[0]);

            keys = analyzer.Analyze("\"word1\"");
            Assert.Single(keys);
            Assert.Equal(new SearchKey(SearchConjunction.And, SearchPropertyProfiles.Text, null, SearchFilterProfiles.Exact, "word1"), keys[0]);

            keys = analyzer.Analyze("\"word1 word2 ");
            Assert.Single(keys);
            Assert.Equal(new SearchKey(SearchConjunction.And, SearchPropertyProfiles.Text, null, SearchFilterProfiles.Exact, "word1 word2 "), keys[0]);

            keys = analyzer.Analyze("/and word1");
            Assert.Single(keys);
            Assert.Equal(new SearchKey(SearchConjunction.And, SearchPropertyProfiles.Text, null, SearchFilterProfiles.Fuzzy, "word1"), keys[0]);

            keys = analyzer.Analyze("/or word1");
            Assert.Single(keys);
            Assert.Equal(new SearchKey(SearchConjunction.Or, SearchPropertyProfiles.Text, null, SearchFilterProfiles.Fuzzy, "word1"), keys[0]);

            keys = analyzer.Analyze("/not word1");
            Assert.Single(keys);
            Assert.Equal(new SearchKey(SearchConjunction.Not, SearchPropertyProfiles.Text, null, SearchFilterProfiles.Fuzzy, "word1"), keys[0]);


            keys = analyzer.Analyze("/m0 word1");
            Assert.Single(keys);
            Assert.Equal(new SearchKey(SearchConjunction.And, SearchPropertyProfiles.Text, null, SearchFilterProfiles.Exact, "word1"), keys[0]);

            keys = analyzer.Analyze("/m1 word1");
            Assert.Single(keys);
            Assert.Equal(new SearchKey(SearchConjunction.And, SearchPropertyProfiles.Text, null, SearchFilterProfiles.Word, "word1"), keys[0]);

            keys = analyzer.Analyze("/m2 word1");
            Assert.Single(keys);
            Assert.Equal(new SearchKey(SearchConjunction.And, SearchPropertyProfiles.Text, null, SearchFilterProfiles.Fuzzy, "word1"), keys[0]);

            keys = analyzer.Analyze("/re word1");
            Assert.Single(keys);
            Assert.Equal(new SearchKey(SearchConjunction.And, SearchPropertyProfiles.Text, null, SearchFilterProfiles.RegularExpression, "word1"), keys[0]);

            keys = analyzer.Analyze("/ire word1");
            Assert.Single(keys);
            Assert.Equal(new SearchKey(SearchConjunction.And, SearchPropertyProfiles.Text, null, SearchFilterProfiles.RegularExpressionIgnoreCase, "word1"), keys[0]);

            // multi
            keys = analyzer.Analyze("\"word1 word2\" word3");
            Assert.Equal(2, keys.Count);
            Assert.Equal(new SearchKey(SearchConjunction.And, SearchPropertyProfiles.Text, null, SearchFilterProfiles.Exact, "word1 word2"), keys[0]);
            Assert.Equal(new SearchKey(SearchConjunction.And, SearchPropertyProfiles.Text, null, SearchFilterProfiles.Fuzzy, "word3"), keys[1]);

            keys = analyzer.Analyze("word1 /or word2");
            Assert.Equal(2, keys.Count);
            Assert.Equal(new SearchKey(SearchConjunction.And, SearchPropertyProfiles.Text, null, SearchFilterProfiles.Fuzzy, "word1"), keys[0]);
            Assert.Equal(new SearchKey(SearchConjunction.Or, SearchPropertyProfiles.Text, null, SearchFilterProfiles.Fuzzy, "word2"), keys[1]);

            keys = analyzer.Analyze("word1 /or /m1 word2");
            Assert.Equal(2, keys.Count);
            Assert.Equal(new SearchKey(SearchConjunction.And, SearchPropertyProfiles.Text, null, SearchFilterProfiles.Fuzzy, "word1"), keys[0]);
            Assert.Equal(new SearchKey(SearchConjunction.Or, SearchPropertyProfiles.Text, null, SearchFilterProfiles.Word, "word2"), keys[1]);

            keys = analyzer.Analyze("word1 /not /re \"word2 word3\"");
            Assert.Equal(2, keys.Count);
            Assert.Equal(new SearchKey(SearchConjunction.And, SearchPropertyProfiles.Text, null, SearchFilterProfiles.Fuzzy, "word1"), keys[0]);
            Assert.Equal(new SearchKey(SearchConjunction.Not, SearchPropertyProfiles.Text, null, SearchFilterProfiles.RegularExpression, "word2 word3"), keys[1]);

            keys = analyzer.Analyze("word1 /not /or /re /m1 word2  ");
            Assert.Equal(2, keys.Count);
            Assert.Equal(new SearchKey(SearchConjunction.And, SearchPropertyProfiles.Text, null, SearchFilterProfiles.Fuzzy, "word1"), keys[0]);
            Assert.Equal(new SearchKey(SearchConjunction.Or, SearchPropertyProfiles.Text, null, SearchFilterProfiles.Word, "word2"), keys[1]);

            keys = analyzer.Analyze("word1 /not /or /re /m1 \"word2 word3\" ");
            Assert.Equal(2, keys.Count);
            Assert.Equal(new SearchKey(SearchConjunction.And, SearchPropertyProfiles.Text, null, SearchFilterProfiles.Fuzzy, "word1"), keys[0]);
            Assert.Equal(new SearchKey(SearchConjunction.Or, SearchPropertyProfiles.Text, null, SearchFilterProfiles.Word, "word2 word3"), keys[1]);


            keys = analyzer.Analyze("/until -5day");
            Assert.Single(keys);

            keys = analyzer.Analyze("/until +10month");
            Assert.Single(keys);

            keys = analyzer.Analyze("/until 123year");
            Assert.Single(keys);

            Assert.Throws<SearchKeywordDateTimeException>(() => analyzer.Analyze("/since -day"));
            Assert.Throws<SearchKeywordDateTimeException>(() => analyzer.Analyze("/since 1"));
            Assert.Throws<SearchKeywordDateTimeException>(() => analyzer.Analyze("/since day"));
            Assert.Throws<SearchKeywordDateTimeException>(() => analyzer.Analyze("/since +-1day"));

            keys = analyzer.Analyze("/since 2018-01-01");
            Assert.Single(keys);
            Assert.Equal(new SearchKey(SearchConjunction.And, DateSearchPropertyProfiles.Date, null, SearchFilterProfiles.GreaterThanEqual, "2018-01-01"), keys[0]);

            keys = analyzer.Analyze("/until 2018-01-01");
            Assert.Single(keys);
            Assert.Equal(new SearchKey(SearchConjunction.And, DateSearchPropertyProfiles.Date, null, SearchFilterProfiles.LessThanEqual, "2018-01-01"), keys[0]);

            keys = analyzer.Analyze("/p.date /m.lt 2018-01-01");
            Assert.Single(keys);
            Assert.Equal(new SearchKey(SearchConjunction.And, DateSearchPropertyProfiles.Date, null, SearchFilterProfiles.LessThan, "2018-01-01"), keys[0]);

            keys = analyzer.Analyze("/p.date /m.le 2018-01-01");
            Assert.Single(keys);
            Assert.Equal(new SearchKey(SearchConjunction.And, DateSearchPropertyProfiles.Date, null, SearchFilterProfiles.LessThanEqual, "2018-01-01"), keys[0]);

            keys = analyzer.Analyze("/p.date /m.eq 2018-01-01");
            Assert.Single(keys);
            Assert.Equal(new SearchKey(SearchConjunction.And, DateSearchPropertyProfiles.Date, null, SearchFilterProfiles.Equal, "2018-01-01"), keys[0]);

            keys = analyzer.Analyze("/p.date /m.ne 2018-01-01");
            Assert.Single(keys);
            Assert.Equal(new SearchKey(SearchConjunction.And, DateSearchPropertyProfiles.Date, null, SearchFilterProfiles.NotEqual, "2018-01-01"), keys[0]);

            keys = analyzer.Analyze("/p.date /m.ge 2018-01-01");
            Assert.Single(keys);
            Assert.Equal(new SearchKey(SearchConjunction.And, DateSearchPropertyProfiles.Date, null, SearchFilterProfiles.GreaterThanEqual, "2018-01-01"), keys[0]);

            keys = analyzer.Analyze("/p.date /m.gt 2018-01-01");
            Assert.Single(keys);
            Assert.Equal(new SearchKey(SearchConjunction.And, DateSearchPropertyProfiles.Date, null, SearchFilterProfiles.GreaterThan, "2018-01-01"), keys[0]);
        }

        /// <summary>
        /// 検索キーワード解析詳細テスト
        /// </summary>
        [Fact]
        public void SearchEngineKeywordAnalyzeParameterTest()
        {
            var context = new SearchContext();
            context.AddProfile(new DateSearchProfile());
            var analyzer = new SearchKeyAnalyzer(context.KeyOptions, context.KeyAlias);
            List<SearchKey> keys;

            keys = analyzer.Analyze("/p.date /m.gt 2018-01-01");
            Assert.Single(keys);
            Assert.Equal(new SearchKey(SearchConjunction.And, DateSearchPropertyProfiles.Date, null, SearchFilterProfiles.GreaterThan, "2018-01-01"), keys[0]);

            keys = analyzer.Analyze("/p.date.Any /m.gt 2018-01-01");
            Assert.Single(keys);
            Assert.Equal(new SearchKey(SearchConjunction.And, DateSearchPropertyProfiles.Date, "any", SearchFilterProfiles.GreaterThan, "2018-01-01"), keys[0]);

            keys = analyzer.Analyze("/p.date.Any.Test /m.gt 2018-01-01");
            Assert.Single(keys);
            Assert.Equal(new SearchKey(SearchConjunction.And, DateSearchPropertyProfiles.Date, "any.test", SearchFilterProfiles.GreaterThan, "2018-01-01"), keys[0]);
        }

        [Fact]
        public void SearchEngineKeywordAnalyzeOptionExceptionTest()
        {
            var context = new SearchContext();
            var analyzer = new SearchKeyAnalyzer(context.KeyOptions, context.KeyAlias);
            List<SearchKey> keys;

            Assert.Throws<SearchKeywordOptionException>(() =>
            {
                keys = analyzer.Analyze("word1 /unknown word2");
                Assert.Equal(2, keys.Count);
            });
        }

        [Fact]
        public void SearchEngineKeywordAnalyzeRegularExpressionExceptionTest()
        {
            var context = new SearchContext();
            var analyzer = new SearchKeyAnalyzer(context.KeyOptions, context.KeyAlias);
            List<SearchKey> keys;

            Assert.Throws<SearchKeywordRegularExpressionException>(() =>
            {
                keys = analyzer.Analyze("word1 /re ^(hoge");
                Assert.Equal(2, keys.Count);
            });

            Assert.Throws<SearchKeywordRegularExpressionException>(() =>
            {
                keys = analyzer.Analyze("word1 /ire ^(hoge");
                Assert.Equal(2, keys.Count);
            });
        }


        [Theory]
        [InlineData(1, "/p.date /m.eq 2018-02-01")]
        [InlineData(2, "/p.date /m.ne 2018-02-01")]
        [InlineData(1, "/p.date /m.lt 2018-02-01")]
        [InlineData(2, "/p.date /m.le 2018-02-01")]
        [InlineData(1, "/p.date /m.gt 2018-02-01")]
        [InlineData(2, "/p.date /m.ge 2018-02-01")]
        public void SearchCoreCompareTest(int expected, string keyword)
        {
            var context = new SearchContext();
            context.AddProfile(new DateSearchProfile());
            var search = new Searcher(context);
            var items = new List<SampleSearchItem>
            {
                new SampleSearchItem("2018-01-01"),
                new SampleSearchItem("2018-02-01"),
                new SampleSearchItem("2018-03-01"),
            };

            var result = search.Search(keyword, items, CancellationToken.None);
            Assert.Equal(expected, result.Count());
        }

        [Fact]
        public void SearchCoreWordTest()
        {
            var context = new SearchContext();
            var search = new Searcher(context);

            var keyword = "/word AB";

            var result = search.Search(keyword, new SampleSearchItemCollection("AB"), CancellationToken.None);
            Assert.Single(result);

            result = search.Search(keyword, new SampleSearchItemCollection("ABC"), CancellationToken.None);
            Assert.Empty(result);

            result = search.Search(keyword, new SampleSearchItemCollection("ABAB"), CancellationToken.None);
            Assert.Empty(result);

            result = search.Search(keyword, new SampleSearchItemCollection("BABA"), CancellationToken.None);
            Assert.Empty(result);

            result = search.Search(keyword, new SampleSearchItemCollection("ABです"), CancellationToken.None);
            Assert.Single(result);

            result = search.Search(keyword, new SampleSearchItemCollection("これはAB"), CancellationToken.None);
            Assert.Single(result);

            result = search.Search(keyword, new SampleSearchItemCollection("これはABです"), CancellationToken.None);
            Assert.Single(result);

            result = search.Search(keyword, new SampleSearchItemCollection("これはABCです"), CancellationToken.None);
            Assert.Empty(result);

            keyword = "/word 12";

            result = search.Search(keyword, new SampleSearchItemCollection("これは12デス"), CancellationToken.None);
            Assert.Single(result);

            result = search.Search(keyword, new SampleSearchItemCollection("これは012デス"), CancellationToken.None);
            Assert.Single(result);

            result = search.Search(keyword, new SampleSearchItemCollection("これは120デス"), CancellationToken.None);
            Assert.Empty(result);

            keyword = "/word アレ";

            keyword = "/word あれ";

            result = search.Search(keyword, new SampleSearchItemCollection("これハあれデス"), CancellationToken.None);
            Assert.Single(result);

            result = search.Search(keyword, new SampleSearchItemCollection("これハぽあれデス"), CancellationToken.None);
            Assert.Empty(result);

            keyword = "/word アレ";

            result = search.Search(keyword, new SampleSearchItemCollection("これはアレです"), CancellationToken.None);
            Assert.Single(result);

            result = search.Search(keyword, new SampleSearchItemCollection("これはポアレです"), CancellationToken.None);
            Assert.Empty(result);

            keyword = "/word 漢字";

            result = search.Search(keyword, new SampleSearchItemCollection("これは漢字です"), CancellationToken.None);
            Assert.Single(result);

            result = search.Search(keyword, new SampleSearchItemCollection("これは日本の漢字です"), CancellationToken.None);
            Assert.Single(result);

            result = search.Search(keyword, new SampleSearchItemCollection("これは漢字体です"), CancellationToken.None);
            Assert.Empty(result);
        }

        [Fact]
        public void SearchCoreWordTSTest()
        {
            var context = new SearchContext();
            var search = new Searcher(context);

            var keyword = "/word TS";

            var result = search.Search(keyword, new SampleSearchItemCollection("TS物です"), CancellationToken.None);
            Assert.Single(result);
        }
    }


    public class SampleSearchItemCollection : IEnumerable<ISearchItem>
    {
        private List<ISearchItem> _items;

        public SampleSearchItemCollection(List<ISearchItem> items)
        {
            _items = items;
        }

        public SampleSearchItemCollection(params string[] items) : this((IEnumerable<string>)items)
        {
        }

        public SampleSearchItemCollection(IEnumerable<string> items)
        {
            _items = items.Select(e => new SampleSearchItem(e)).ToList<ISearchItem>();
        }

        public IEnumerator<ISearchItem> GetEnumerator()
        {
            return _items.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return this.GetEnumerator();
        }
    }


    public class SampleSearchItem : ISearchItem
    {
        private string _value;


        public SampleSearchItem(string value)
        {
            _value = value;
        }

        public SearchValue GetValue(SearchPropertyProfile profile, string? parameter, CancellationToken token)
        {
            switch (profile.Name)
            {
                case "text":
                    return new StringSearchValue(_value);
                case "date":
                    return new DateTimeSearchValue(DateTime.Parse(_value, CultureInfo.CurrentCulture));
                default:
                    throw new NotSupportedException();
            }
        }
    }

}
