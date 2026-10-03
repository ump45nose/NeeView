using System;
using System.Runtime.Serialization;

// TODO: 定義が細分化されすぎでは？
namespace NeeLaboratory.IO.Search
{
    public class SearchKeywordException : Exception
    {
        public SearchKeywordException() : base() { }
        public SearchKeywordException(string message) : base(message) { }
        public SearchKeywordException(string message, Exception inner) : base(message, inner) { }
    }

    public class SearchKeywordOptionException : SearchKeywordException
    {
        public SearchKeywordOptionException() : base() { }
        public SearchKeywordOptionException(string message) : base(message) { }
        public SearchKeywordOptionException(string message, Exception inner) : base(message, inner) { }

        public string? Option { get; set; }
    }

    public class SearchKeywordRegularExpressionException : SearchKeywordException
    {
        public SearchKeywordRegularExpressionException() : base() { }
        public SearchKeywordRegularExpressionException(string message) : base(message) { }
        public SearchKeywordRegularExpressionException(string message, Exception inner) : base(message, inner) { }
    }

    public class SearchKeywordDateTimeException : SearchKeywordException
    {
        public SearchKeywordDateTimeException() : base() { }
        public SearchKeywordDateTimeException(string message) : base(message) { }
        public SearchKeywordDateTimeException(string message, Exception inner) : base(message, inner) { }
    }

    public class SearchKeywordBooleanException : SearchKeywordException
    {
        public SearchKeywordBooleanException() : base() { }
        public SearchKeywordBooleanException(string message) : base(message) { }
        public SearchKeywordBooleanException(string message, Exception inner) : base(message, inner) { }
    }

    public class SearchKeywordIntegerException : SearchKeywordException
    {
        public SearchKeywordIntegerException() : base() { }
        public SearchKeywordIntegerException(string message) : base(message) { }
        public SearchKeywordIntegerException(string message, Exception inner) : base(message, inner) { }
    }
}
