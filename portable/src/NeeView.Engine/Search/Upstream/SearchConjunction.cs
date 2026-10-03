namespace NeeLaboratory.IO.Search
{
    public enum SearchConjunction
    {
        /// <summary>
        /// AND接続
        /// </summary>
        And,

        /// <summary>
        /// OR接続
        /// </summary>
        Or,

        /// <summary>
        /// NOT接続
        /// </summary>
        Not,

        /// <summary>
        /// OR接続 (先頭に接続するOR。非公開)
        /// </summary>
        PreOr,
    }


}
