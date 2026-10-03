using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;

namespace NeeLaboratory.IO.Search
{
    public abstract class SearchFilter
    {
        public SearchFilter(SearchPropertyProfile property, string? parameter, string format)
        {
            Property = property;
            Parameter = parameter;
            Format = format;
        }

        public SearchPropertyProfile Property { get; }
        public string? Parameter { get; }
        protected string Format { get; }

        public abstract bool IsMatch(SearchContext context, ISearchItem e, CancellationToken token);
    }


    /// <summary>
    /// 全一致 (既定のフィルター)
    /// </summary>
    public class TrueSearchFilter : SearchFilter
    {
        public TrueSearchFilter(SearchPropertyProfile property, string? parameter, string format)
            : base(property, parameter, format)
        {
        }

        public override bool IsMatch(SearchContext context, ISearchItem e, CancellationToken token)
        {
            return true;
        }
    }

    /// <summary>
    /// 文字列：曖昧検索
    /// </summary>
    public class FuzzySearchFilter : SearchFilter
    {
        private readonly Regex _regex;

        public FuzzySearchFilter(SearchPropertyProfile property, string? parameter, string format)
            : base(property, parameter, format)
        {
            var s = format;
            s = SearchStringTools.ToNormalizedWord(s, true);
            s = Regex.Escape(s);
            s = SearchStringTools.ToFuzzyNumberRegex(s);
            _regex = new Regex(s, RegexOptions.Compiled);
        }

        public override bool IsMatch(SearchContext context, ISearchItem e, CancellationToken token)
        {
            var s = e.GetValue(Property, Parameter, token).ToString();
            s = context.FuzzyStringCache.GetString(s);
            return _regex.Match(s).Success;
        }
    }

    /// <summary>
    /// 文字列：単語検索
    /// </summary>
    public class WordSearchFilter : SearchFilter
    {
        private readonly Regex _regex;

        public WordSearchFilter(SearchPropertyProfile property, string? parameter, string format)
            : base(property, parameter, format)
        {
            var s = format;
            var first = SearchStringTools.GetCodeBlockRegexString(s.First(), true);
            var last = SearchStringTools.GetCodeBlockRegexString(s.Last(), true);
            s = SearchStringTools.ToNormalizedWord(s, false);
            s = Regex.Escape(s);
            s = SearchStringTools.ToFuzzyNumberRegex(s);
            if (first != null) s = $"(^|{first}){s}";
            if (last != null) s = $"{s}({last}|$)";
            _regex = new Regex(s, RegexOptions.Compiled);
        }

        public override bool IsMatch(SearchContext context, ISearchItem e, CancellationToken token)
        {
            var s = e.GetValue(Property, Parameter, token).ToString();
            s = context.WordStringCache.GetString(s);
            return _regex.Match(s).Success;
        }
    }

    /// <summary>
    /// 文字列：完全検索
    /// </summary>
    public class ExactSearchFilter : SearchFilter
    {
        private readonly Regex _regex;

        public ExactSearchFilter(SearchPropertyProfile property, string? parameter, string format)
            : base(property, parameter, format)
        {
            var s = Regex.Escape(format);
            _regex = new Regex(s, RegexOptions.Compiled);
        }

        public override bool IsMatch(SearchContext context, ISearchItem e, CancellationToken token)
        {
            var s = e.GetValue(Property, Parameter, token).ToString();
            return _regex.Match(s).Success;
        }
    }

    /// <summary>
    /// 文字列：正規表現検索
    /// </summary>
    public class RegularExpressionSearchFilter : SearchFilter
    {
        private readonly Regex _regex;

        public RegularExpressionSearchFilter(SearchPropertyProfile property, string? parameter, string format)
            : base(property, parameter, format)
        {
            try
            {
                // Mac 迁移：保留正则语义，限制单项回溯，超时由面板保留旧结果并报告。
                _regex = new Regex(format, RegexOptions.Compiled, TimeSpan.FromMilliseconds(250));
            }
            catch (Exception ex)
            {
                throw new SearchKeywordRegularExpressionException($"RegularExpression error: {format}", ex);
            }
        }

        public override bool IsMatch(SearchContext context, ISearchItem e, CancellationToken token)
        {
          var s = e.GetValue(Property, Parameter, token).ToString();
            return _regex.Match(s).Success;
        }
    }

    /// <summary>
    /// 文字列：正規表現検索 (IgnoreCase)
    /// </summary>
    public class RegularExpressionIgnoreCaseSearchFilter : SearchFilter
    {
        private readonly Regex _regex;

        public RegularExpressionIgnoreCaseSearchFilter(SearchPropertyProfile property, string? parameter, string format)
            : base(property, parameter, format)
        {
            try
            {
                // 同一取消/预算规则适用于忽略大小写的原正则选项。
                _regex = new Regex(format, RegexOptions.IgnoreCase | RegexOptions.Compiled, TimeSpan.FromMilliseconds(250));
            }
            catch (Exception ex)
            {
                throw new SearchKeywordRegularExpressionException($"RegularExpression error: {format}", ex);
            }
        }

        public override bool IsMatch(SearchContext context, ISearchItem e, CancellationToken token)
        {
            var s = e.GetValue(Property, Parameter, token).ToString();
            return _regex.Match(s).Success;
        }
    }


    /// <summary>
    /// 比較：等しい
    /// </summary>
    public class EqualSearchFilter : SearchFilter
    {
        private readonly SearchValue _referenceValue;

        public EqualSearchFilter(SearchPropertyProfile property, string? parameter, string format) 
            : base(property, parameter, format)
        {
            _referenceValue = property.Parse(format);
        }

        public override bool IsMatch(SearchContext context, ISearchItem e, CancellationToken token)
        {
            var value = e.GetValue(Property, Parameter, token);
            return value.CompareTo(_referenceValue) == 0;
        }
    }

    /// <summary>
    /// 比較：等しくない
    /// </summary>
    public class NotEqualSearchFilter : SearchFilter
    {
        private readonly SearchValue _referenceValue;

        public NotEqualSearchFilter(SearchPropertyProfile property, string? parameter, string format) 
            : base(property, parameter, format)
        {
            _referenceValue = property.Parse(format);
        }

        public override bool IsMatch(SearchContext context, ISearchItem e, CancellationToken token)
        {
            var value = e.GetValue(Property, Parameter, token);
            return value.CompareTo(_referenceValue) != 0;
        }
    }

    /// <summary>
    /// 比較：より大きい
    /// </summary>
    public class GreaterThanSearchFilter : SearchFilter
    {
        private readonly SearchValue _referenceValue;

        public GreaterThanSearchFilter(SearchPropertyProfile property, string? parameter, string format) 
            : base(property, parameter, format)
        {
            _referenceValue = property.Parse(format);
        }

        public override bool IsMatch(SearchContext context, ISearchItem e, CancellationToken token)
        {
            var value = e.GetValue(Property, Parameter, token);

            return value.CompareTo(_referenceValue) > 0;
        }
    }

    /// <summary>
    /// 比較：より小さい
    /// </summary>
    public class LessThanSearchFilter : SearchFilter
    {
        private readonly SearchValue _referenceValue;

        public LessThanSearchFilter(SearchPropertyProfile property, string? parameter, string format) 
            : base(property, parameter, format)
        {
            _referenceValue = property.Parse(format);
        }

        public override bool IsMatch(SearchContext context, ISearchItem e, CancellationToken token)
        {
            var value = e.GetValue(Property, Parameter, token);

            return value.CompareTo(_referenceValue) < 0;
        }
    }


    /// <summary>
    /// 比較：以上
    /// </summary>
    public class GreaterThanEqualSearchFilter : SearchFilter
    {
        private readonly SearchValue _referenceValue;

        public GreaterThanEqualSearchFilter(SearchPropertyProfile property, string? parameter, string format) 
            : base(property, parameter, format)
        {
            _referenceValue = property.Parse(format);
        }

        public override bool IsMatch(SearchContext context, ISearchItem e, CancellationToken token)
        {
            var value = e.GetValue(Property, Parameter, token);

            return value.CompareTo(_referenceValue) >= 0;
        }
    }

    /// <summary>
    /// 比較：以下
    /// </summary>
    public class LessThanEqualSearchFilter : SearchFilter
    {
        private readonly SearchValue _referenceValue;

        public LessThanEqualSearchFilter(SearchPropertyProfile property, string? parameter, string format)
            : base(property, parameter, format)
        {
            _referenceValue = property.Parse(format);
        }

        public override bool IsMatch(SearchContext context, ISearchItem e, CancellationToken token)
        {
            var value = e.GetValue(Property, Parameter, token);

            return value.CompareTo(_referenceValue) <= 0;
        }
    }

}
