using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;

namespace NeeLaboratory.IO.Search
{
    /// <summary>
    /// 検索値
    /// </summary>
    public abstract class SearchValue
    {
        public abstract Type GetValueType();
        public abstract int CompareTo(SearchValue other);
        public abstract SearchValue Parse(string value);
        public override string ToString() => base.ToString() ?? "";
    }

    /// <summary>
    /// 検索値：文字列
    /// </summary>
    public class StringSearchValue : SearchValue
    {
        public static StringSearchValue Default { get; } = new("");

        private readonly string _value;

        public StringSearchValue(string value)
        {
            _value = value;
        }

        public override Type GetValueType()
        {
            return typeof(string);
        }

        public override int CompareTo(SearchValue other)
        {
            return string.Compare(_value, ((StringSearchValue)other)._value, StringComparison.CurrentCulture);
        }

        public override SearchValue Parse(string value)
        {
            return new StringSearchValue(value);
        }

        public override string ToString()
        {
            return _value;
        }
    }

    /// <summary>
    /// 検索値：Boolean
    /// </summary>
    public class BooleanSearchValue : SearchValue
    {
        public static BooleanSearchValue Default { get; } = new(default);

        private readonly bool _value;

        public BooleanSearchValue(bool value)
        {
            _value = value;
        }

        public override Type GetValueType()
        {
            return typeof(bool);
        }

        public override int CompareTo(SearchValue other)
        {
            return _value.CompareTo(((BooleanSearchValue)other)._value);
        }

        public override SearchValue Parse(string value)
        {
            try
            {
                return new BooleanSearchValue(bool.Parse(value));
            }
            catch (Exception ex)
            {
                throw new SearchKeywordIntegerException($"Integer parse error: Cannot parse {value}", ex);
            }
        }

        public override string ToString()
        {
            return _value.ToString();
        }
    }

    /// <summary>
    /// 検索値：整数
    /// </summary>
    public class IntegerSearchValue : SearchValue
    {
        public static IntegerSearchValue Default { get; } = new(default);

        private readonly long _value;

        public IntegerSearchValue(long value)
        {
            _value = value;
        }

        public override Type GetValueType()
        {
            return typeof(long);
        }

        public override int CompareTo(SearchValue other)
        {
            return _value.CompareTo(((IntegerSearchValue)other)._value);
        }

        public override SearchValue Parse(string value)
        {
            try
            {
                return new IntegerSearchValue(ParseWithUnit(value));
            }
            catch (Exception ex)
            {
                throw new SearchKeywordBooleanException($"Integer parse error: Cannot parse {value}", ex);
            }
        }

        public override string ToString()
        {
            return _value.ToString(CultureInfo.InvariantCulture);
        }


        private static long ParseWithUnit(string s)
        {
            var regex = new Regex(@"^([+-]?\d+)([kKmMgG])?$");
            var match = regex.Match(s);
            if (!match.Success) throw new FormatException();

            var value = long.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
            var scale = match.Groups[2].Value switch
            {
                "k" => 1000,
                "K" => 1024,
                "m" => 1000 * 1000,
                "M" => 1024 * 1024,
                "g" => 1000 * 1000 * 1000,
                "G" => 1024 * 1024 * 1024,
                _ => 1,
            };

            Debug.WriteLine($"int.Parse: {s} -> {value * scale:#,0}");
            return value * scale;
        }
    }

    /// <summary>
    /// 検索値：DateTime
    /// </summary>
    public class DateTimeSearchValue : SearchValue
    {
        public static DateTimeSearchValue Default { get; } = new(default);

        private static readonly Regex _regexDateTimeCustom = new(@"^([+-]?\d+)(days?|months?|years?)$");

        private readonly DateTime _value;

        public DateTimeSearchValue(DateTime date)
        {
            _value = date;
        }

        public override Type GetValueType()
        {
            return typeof(DateTime);
        }

        public override int CompareTo(SearchValue other)
        {
            return _value.CompareTo(((DateTimeSearchValue)other)._value);
        }

        public override SearchValue Parse(string value)
        {
            try
            {
                var match = _regexDateTimeCustom.Match(value);
                if (match.Success)
                {
                    var num = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
                    var dateTime = match.Groups[2].Value switch
                    {
                        "day" or "days" => DateTime.Now.AddDays(num),
                        "month" or "months" => DateTime.Now.AddMonths(num),
                        "year" or "years" => DateTime.Now.AddYears(num),
                        _ => throw new NotSupportedException(),
                    };
                    return new DateTimeSearchValue(dateTime);
                }
                else
                {
                    var dateTime = DateTime.Parse(value, CultureInfo.CurrentCulture);
                    return new DateTimeSearchValue(dateTime);
                }
            }
            catch (Exception ex)
            {
                throw new SearchKeywordDateTimeException($"DateTime parse error: Cannot parse {value}", ex);
            }
        }

        public override string ToString()
        {
            return _value.ToString(SearchDateTimeTools.DateTimeFormat, CultureInfo.InvariantCulture);
        }
    }

}
