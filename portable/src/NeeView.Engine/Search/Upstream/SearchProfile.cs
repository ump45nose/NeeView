namespace NeeLaboratory.IO.Search
{
    public class SearchProfile
    {
        public SearchKeyOptionCollection Options { get; } = new();

        public SearchKeyAliasCollection Alias { get; } = new();
    }


    public class DefaultSearchProfile : SearchProfile
    {
        public DefaultSearchProfile()
        {
            Options.Add(SearchConjunction.And);
            Options.Add(SearchConjunction.Or);
            Options.Add(SearchConjunction.Not);

            Options.Add(SearchPropertyProfiles.Text);

            Options.Add(SearchFilterProfiles.Exact);
            Options.Add(SearchFilterProfiles.Word);
            Options.Add(SearchFilterProfiles.Fuzzy);
            Options.Add(SearchFilterProfiles.RegularExpression);
            Options.Add(SearchFilterProfiles.RegularExpressionIgnoreCase);

            Options.Add(SearchFilterProfiles.LessThan);
            Options.Add(SearchFilterProfiles.LessThanEqual);
            Options.Add(SearchFilterProfiles.Equal);
            Options.Add(SearchFilterProfiles.NotEqual);
            Options.Add(SearchFilterProfiles.GreaterThanEqual);
            Options.Add(SearchFilterProfiles.GreaterThan);

            Alias.Add("/and", new() { "/c.and" });
            Alias.Add("/or", new() { "/c.or" });
            Alias.Add("/not", new() { "/c.not" });

            Alias.Add("/text", new() { "/p.text" });

            Alias.Add("/re", new() { "/m.re" });
            Alias.Add("/ire", new() { "/m.ire" });
            Alias.Add("/m0", new() { "/m.exact" });
            Alias.Add("/exact", new() { "/m.exact" });
            Alias.Add("/m1", new() { "/m.word" });
            Alias.Add("/word", new() { "/m.word" });
            Alias.Add("/m2", new() { "/m.fuzzy" });
            Alias.Add("/fuzzy", new() { "/m.fuzzy" });

            Alias.Add("/lt", new() { "/m.lt" });
            Alias.Add("/le", new() { "/m.le" });
            Alias.Add("/eq", new() { "/m.eq" });
            Alias.Add("/ne", new() { "/m.ne" });
            Alias.Add("/ge", new() { "/m.ge" });
            Alias.Add("/gt", new() { "/m.gt" });
        }
    }

    public class DateSearchProfile : SearchProfile
    {
        public DateSearchProfile()
        {
            Options.Add(DateSearchPropertyProfiles.Date);

            Alias.Add("/date", new() { "/p.date" });
            Alias.Add("/since", new() { "/p.date", "/m.ge" });
            Alias.Add("/until", new() { "/p.date", "/m.le" });
        }
    }

    public class SizeSearchProfile : SearchProfile
    {
        public SizeSearchProfile()
        {
            Options.Add(SizeSearchPropertyProfiles.Size);

            Alias.Add("/size", new() { "/p.size" });
        }
    }

}
