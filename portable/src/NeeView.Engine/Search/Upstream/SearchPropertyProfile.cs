using System;

namespace NeeLaboratory.IO.Search
{
    public class SearchPropertyProfile
    {
        public SearchPropertyProfile(string name, SearchValue defaultValue)
        {
            Name = name;
            DefaultValue = defaultValue;
        }

        public string Name { get; }
        public SearchValue DefaultValue { get; }

        public Type GetValueType()
        {
            return DefaultValue.GetValueType();
        }

        public SearchValue Parse(string format)
        {
            return DefaultValue.Parse(format);
        }
    }


    public static class SearchPropertyProfiles
    {
        public static SearchPropertyProfile Text { get; } = new SearchPropertyProfile("text", StringSearchValue.Default);
    }

    public static class DateSearchPropertyProfiles
    {
        public static SearchPropertyProfile Date { get; } = new SearchPropertyProfile("date", DateTimeSearchValue.Default);
    }

    public static class SizeSearchPropertyProfiles
    {
        public static SearchPropertyProfile Size { get; } = new SearchPropertyProfile("size", IntegerSearchValue.Default);
    }

    public static class ExtraSearchPropertyProfiles
    {
        public static SearchPropertyProfile IsDirectory { get; } = new SearchPropertyProfile("directory", BooleanSearchValue.Default);
        public static SearchPropertyProfile IsPinned { get; } = new SearchPropertyProfile("pinned", BooleanSearchValue.Default);
    }

}
