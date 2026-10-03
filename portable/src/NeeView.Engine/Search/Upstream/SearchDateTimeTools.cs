using System.Globalization;

namespace NeeLaboratory.IO.Search
{
    public static class SearchDateTimeTools
    {
        private static IDateTimeFormatInfo _formatInfo = DefaultDateTimeFormatInfo.Default;

        public static string DateTimeFormat => _formatInfo.GetPattern();

        public static void SetDateTimeFormatInfo(IDateTimeFormatInfo? formatInfo)
        {
            _formatInfo = formatInfo ?? DefaultDateTimeFormatInfo.Default;
        }
    }

    public interface IDateTimeFormatInfo
    {
        string GetPattern();
    }

    internal class DefaultDateTimeFormatInfo : IDateTimeFormatInfo
    {
        public static DefaultDateTimeFormatInfo Default { get; } = new();

        public string GetPattern()
        {
            var info = DateTimeFormatInfo.CurrentInfo;
            return info.ShortDatePattern + " " + info.LongTimePattern;
        }
    }
}
