namespace NeeLaboratory.IO.Search
{
    /// <summary>
    /// 検索キー
    /// </summary>
    public class SearchKey
    {
        public SearchKey(SearchConjunction conjunction, SearchPropertyProfile property, string? propertyParameter, SearchFilterProfile filter, string format)
        {
            Conjunction = conjunction;
            Property = property;
            PropertyParameter = propertyParameter;
            Filter = filter;
            Format = format;
        }


        /// <summary>
        /// 接続詞
        /// </summary>
        public SearchConjunction Conjunction { get; set; }

        /// <summary>
        /// 検索対象プロパティ
        /// </summary>
        public SearchPropertyProfile Property { get; set; }

        /// <summary>
        /// 検索対象プロパティのパラメータ
        /// </summary>
        public string? PropertyParameter { get; set; }

        /// <summary>
        /// 検索フィルター
        /// </summary>
        public SearchFilterProfile Filter { get; set; }

        /// <summary>
        /// 検索フォーマット
        /// </summary>
        public string Format { get; set; }


        public SearchKey Clone()
        {
            return (SearchKey)this.MemberwiseClone();
        }

        public override bool Equals(object? other)
        {
            if (other is SearchKey target)
            {
                return this.Format == target.Format
                    && this.Conjunction == target.Conjunction
                    && this.Property == target.Property
                    && this.PropertyParameter == target.PropertyParameter
                    && this.Filter == target.Filter;
            }

            return false;
        }

        public override int GetHashCode()
        {
            return base.GetHashCode();
        }

        public override string ToString()
        {
            var propertyParameter = PropertyParameter is null ? "" : "." + PropertyParameter;
            return $"{Conjunction}, {Property}{propertyParameter}, {Filter}, \"{Format}\"";
        }
    }


}
