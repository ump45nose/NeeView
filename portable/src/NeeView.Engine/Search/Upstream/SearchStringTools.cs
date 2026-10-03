using CSharp.Japanese.Kanaxs;
using System;
using System.Text;
using System.Text.RegularExpressions;

namespace NeeLaboratory.IO.Search
{
    public static class SearchStringTools
    {
        private static readonly Regex _regexSpace = new(@"\s+", RegexOptions.Compiled);
        private static readonly Regex _regexNumber = new(@"0*(\d+)", RegexOptions.Compiled);

        /// <summary>
        /// 正規化された文字列に変換する
        /// </summary>
        /// <param name="src"></param>
        /// <param name="isFuzzy"></param>
        /// <returns></returns>
        public static string ToNormalizedWord(string src, bool isFuzzy)
        {
            string? s = src;

            s = KanaEx.ToPadding(s); // 濁点を１文字にまとめる
            if (s is null) return "";

            try
            {
                s = s.Normalize(NormalizationForm.FormKC); // 正規化
            }
            catch (ArgumentException)
            {
                // 無効なコードポイントがある場合は正規化はスキップする
            }

            s = s.ToUpperInvariant(); // アルファベットを大文字にする

            if (isFuzzy)
            {
                s = KanaEx.ToKatakanaWithNormalize(s); // ひらがなをカタカナにする ＋ 特定文字の正規化
                if (s is null) return "";

                s = _regexSpace.Replace(s, ""); // 空白の削除
            }

            return s;
        }

        /// <summary>
        /// 単語区切り用の正規表現生成
        /// </summary>
        public static string? GetCodeBlockRegexString(char c, bool exception)
        {
            var s = c.ToString();

            //lang=regex
            (string positive, string negative)[] patterns = [
                (@"\d", @"\D"),
                (@"\p{IsHiragana}", @"\P{IsHiragana}"),
                (@"[\p{IsKatakana}\p{IsKatakanaPhoneticExtensions}]", @"[^\p{IsKatakana}\p{IsKatakanaPhoneticExtensions}]"),
                (@"[\p{IsCJKUnifiedIdeographs}\p{IsCJKUnifiedIdeographsExtensionA}\p{IsCJKCompatibilityIdeographs}]", @"[^\p{IsCJKUnifiedIdeographs}\p{IsCJKUnifiedIdeographsExtensionA}\p{IsCJKCompatibilityIdeographs}]"),
                (@"[\u0020-\u024F-[\P{L}]]", @"[\u0250-\uFFFF\P{L}]"),
                (@"\p{L}", @"\P{L}"),
            ];

            foreach (var pattern in patterns)
            {
                if (new Regex(pattern.positive).IsMatch(s))
                {
                    return exception ? pattern.negative : pattern.positive;
                }
            }

            return null;
        }

        /// <summary>
        /// (数値)部分を0*(数値)という正規表現に変換
        /// </summary>
        public static string ToFuzzyNumberRegex(string source)
        {
            return _regexNumber.Replace(source, match => "0*" + match.Groups[1]);
        }
    }
}
