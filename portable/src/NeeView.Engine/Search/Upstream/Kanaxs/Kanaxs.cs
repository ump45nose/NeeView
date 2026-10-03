/*
 * kanaxs C# 拡張版 1.0.2
 * Copyright (c) 2011, DOBON! <http://dobon.net>
 * All rights reserved.
 * 
 * New BSD License（修正BSDライセンス）
 * http://wiki.dobon.net/index.php?free%2FkanaxsCSharp%2Flicense
 * 
 * このクラスは、以下のソースを参考にして作成しました。
 * kanaxs Kana.JS
 * shogo4405 <shogo4405 at gmail.com>
 * http://code.google.com/p/kanaxs/
*/

using System;

namespace CSharp.Japanese.Kanaxs
{
    /// <summary>
    /// ひらがなとカタカナ、半角と全角の文字変換を行うメソッドを提供します。
    /// Kanaクラスの拡張版です。
    /// </summary>
    internal sealed class KanaEx
    {
        private KanaEx()
        {
        }

        /// <summary>
        /// 全角カタカナを全角ひらがなに変換します。
        /// </summary>
        /// <param name="str">変換する String。</param>
        /// <returns>変換された String。</returns>
        /// <remarks>
        /// Kana.ToHiraganaメソッドと違い、「ヽヾヷヸヹヺ」も変換します。
        /// </remarks>
        public static string? ToHiragana(string? str)
        {
            if (str == null || str.Length == 0)
            {
                return str;
            }

            char[] cs = new char[str.Length * 2];
            int len = 0;
            int f = str.Length;

            for (int i = 0; i < f; i++)
            {
                char c = str[i];
                // ァ(0x30A1) ～ ン(0x30F3)
                // ヴ(0xu30F4)
                // ヵ(0x30F5) ～ ヶ(0x30F6)
                // ヽ(0x30FD) ヾ(0x30FE)
                if (('ァ' <= c && c <= 'ヶ') ||
                    ('ヽ' <= c && c <= 'ヾ'))
                {
                    cs[len++] = (char)(c - 0x0060);
                }
                // ヷ(0x30F7) ～ ヺ(0x30FA)
                else if ('ヷ' <= c && c <= 'ヺ')
                {
                    cs[len++] = (char)(c - 0x0068);
                    cs[len++] = '゛';
                }
                else
                {
                    cs[len++] = c;
                }
            }

            return new string(cs, 0, len);
        }

        /// <summary>
        /// 全角ひらがなを全角カタカナに変換します。
        /// </summary>
        /// <param name="str">変換する String。</param>
        /// <returns>変換された String。</returns>
        /// <remarks>
        /// Kana.ToKatakanaメソッドと違い、「ゝゞ」も変換します。
        /// </remarks>
        public static string? ToKatakana(string? str)
        {
            if (str == null || str.Length == 0)
            {
                return str;
            }

            char[] cs = str.ToCharArray();
            int f = cs.Length;

            for (int i = 0; i < f; i++)
            {
                char c = cs[i];
                // ぁ(0x3041) ～ ゖ(0x3096)
                // ゝ(0x309D) ゞ(0x309E)
                if (('ぁ' <= c && c <= 'ゖ') ||
                    ('ゝ' <= c && c <= 'ゞ'))
                {
                    cs[i] = (char)(c + 0x0060);
                }
            }

            return new string(cs);
        }

        /// <summary>
        /// 全角英数字および記号を半角英数字および記号に変換します。
        /// </summary>
        /// <param name="str">変換する String。</param>
        /// <returns>変換された String。</returns>
        /// <remarks>
        /// Kana.ToHankakuメソッドと違い、「￥”’」も変換します。
        /// </remarks>
        public static string? ToHankaku(string? str)
        {
            if (str == null || str.Length == 0)
            {
                return str;
            }

            char[] cs = str.ToCharArray();
            int f = cs.Length;

            for (int i = 0; i < f; i++)
            {
                char c = cs[i];
                // ！(0xFF01) ～ ～(0xFF5E)
                if ('！' <= c && c <= '～')
                {
                    cs[i] = (char)(c - 0xFEE0);
                }
                // 全角スペース(0x3000) -> 半角スペース(0x0020)
                else if (c == '　')
                {
                    cs[i] = ' ';
                }
                else if (c == '￥')
                {
                    cs[i] = '\\';
                }
                else if (c == '”' || c == '“')
                {
                    cs[i] = '"';
                }
                else if (c == '’' || c == '‘')
                {
                    cs[i] = '\'';
                }
            }

            return new string(cs);
        }

        /// <summary>
        /// 半角英数字および記号を全角に変換します。
        /// </summary>
        /// <param name="str">変換する String。</param>
        /// <returns>変換された String。</returns>
        /// <remarks>
        /// Kana.ToZenkakuメソッドと違い、「\"'」を「￥”’」に変換します。
        /// </remarks>
        public static string? ToZenkaku(string? str)
        {
            if (str == null || str.Length == 0)
            {
                return str;
            }

            char[] cs = str.ToCharArray();
            int f = cs.Length;

            for (int i = 0; i < f; i++)
            {
                char c = cs[i];
                if (c == '\\')
                {
                    cs[i] = '￥';
                }
                else if (c == '"')
                {
                    cs[i] = '”';
                }
                else if (c == '\'')
                {
                    cs[i] = '’';
                }
                // !(0x0021) ～ ~(0x007E)
                else if ('!' <= c && c <= '~')
                {
                    cs[i] = (char)(c + 0xFEE0);
                }
                // 半角スペース(0x0020) -> 全角スペース(0x3000)
                else if (c == ' ')
                {
                    cs[i] = '　';
                }
            }

            return new string(cs);
        }

        /// <summary>
        /// 全角カタカナを半角カタカナに変換します。
        /// </summary>
        /// <param name="str">変換する String。</param>
        /// <returns>変換された String。</returns>
        /// <remarks>
        /// Kana.ToHankakuKanaメソッドと違い、
        /// 『、。「」・゛゜U+3099（濁点）U+309A（半濁点）ヴヷヺ』も変換します。
        /// </remarks>
        public static string? ToHankakuKana(string? str)
        {
            if (str == null || str.Length == 0)
            {
                return str;
            }

            char[] cs = new char[str.Length * 2];
            int len = 0;

            int f = str.Length;

            for (int i = 0; i < f; i++)
            {
                char c = str[i];
                // 、(0x3001) ～ ー(0x30FC)
                if ('、' <= c && c <= 'ー')
                {
                    char m = ConvertToHankakuKanaChar(c);
                    if (m != '\0')
                    {
                        cs[len++] = m;
                    }
                    // カ(0x30AB) ～ ド(0x30C9)
                    else if ('カ' <= c && c <= 'ド')
                    {
                        cs[len++] = ConvertToHankakuKanaChar((char)(c - 1));
                        cs[len++] = 'ﾞ';
                    }
                    // ハ(0x30CF) ～ ポ(0x30DD)
                    else if ('ハ' <= c && c <= 'ポ')
                    {
                        int mod3 = c % 3;
                        cs[len++] = ConvertToHankakuKanaChar((char)(c - mod3));
                        cs[len++] = (mod3 == 1 ? 'ﾞ' : 'ﾟ');
                    }
                    // ヴ(0x30F4)
                    else if (c == 'ヴ')
                    {
                        cs[len++] = 'ｳ';
                        cs[len++] = 'ﾞ';
                    }
                    // ヷ(0x30F7)
                    else if (c == 'ヷ')
                    {
                        cs[len++] = 'ﾜ';
                        cs[len++] = 'ﾞ';
                    }
                    // ヺ(0x30FA)
                    else if (c == 'ヺ')
                    {
                        cs[len++] = 'ｦ';
                        cs[len++] = 'ﾞ';
                    }
                    else
                    {
                        cs[len++] = c;
                    }
                }
                else
                {
                    cs[len++] = c;
                }
            }

            return new string(cs, 0, len);
        }

        /// <summary>
        /// 半角カタカナを全角カタカナに変換します。
        /// </summary>
        /// <param name="str">変換する String。</param>
        /// <returns>変換された String。</returns>
        /// <remarks>
        /// Kana.ToZenkakuKanaと違い、「｡｢｣､･」も変換します。
        /// また、濁点、半濁点がその前の文字と合体できる時は合体させて1文字にします。
        /// </remarks>
        public static string? ToZenkakuKana(string? str)
        {
            if (str == null || str.Length == 0)
            {
                return str;
            }

            char[] cs = new char[str.Length];
            int pos = str.Length - 1;

            for (int i = str.Length - 1; 0 <= i; i--)
            {
                char c = str[i];

                // 濁点(0xFF9E)
                if (c == 'ﾞ' && 0 < i)
                {
                    char c2 = str[i - 1];
                    // ｶ(0xFF76) ～ ﾁ(0xFF81)
                    if ('ｶ' <= c2 && c2 <= 'ﾁ')
                    {
                        cs[pos--] = (char)((c2 - 0xFF76) * 2 + 0x30AC);
                        i--;
                    }
                    // ﾂ(0xFF82) ～ ﾄ(0xFF84)
                    else if ('ﾂ' <= c2 && c2 <= 'ﾄ')
                    {
                        cs[pos--] = (char)((c2 - 0xFF82) * 2 + 0x30C5);
                        i--;
                    }
                    // ﾊ(0xFF8A) ～ ﾎ(0xFF8E)
                    else if ('ﾊ' <= c2 && c2 <= 'ﾎ')
                    {
                        cs[pos--] = (char)((c2 - 0xFF8A) * 3 + 0x30D0);
                        i--;
                    }
                    // ｳ(0xFF73)
                    else if (c2 == 'ｳ')
                    {
                        cs[pos--] = 'ヴ';
                        i--;
                    }
                    // ﾜ(0xFF9C)
                    else if (c2 == 'ﾜ')
                    {
                        cs[pos--] = 'ヷ';
                        i--;
                    }
                    // ｦ(0xFF66)
                    else if (c2 == 'ｦ')
                    {
                        cs[pos--] = 'ヺ';
                        i--;
                    }
                    // 全角濁点
                    else
                    {
                        cs[pos--] = '゛';
                    }
                }
                // 半濁点(0xFF9F)
                else if (c == 'ﾟ' && 0 < i)
                {
                    char c2 = str[i - 1];
                    // ﾊ(0xFF8A) ～ ﾎ(0xFF8E)
                    if ('ﾊ' <= c2 && c2 <= 'ﾎ')
                    {
                        cs[pos--] = (char)((c2 - 0xFF8A) * 3 + 0x30D1);
                        i--;
                    }
                    // 全角半濁点
                    else
                    {
                        cs[pos--] = '゜';
                    }
                }
                // ｡(0xFF61) ～ ﾟ(0xFF9F)
                else if ('｡' <= c && c <= 'ﾟ')
                {
                    char m = ConvertToZenkakuKanaChar(c);
                    if (m != '\0')
                    {
                        cs[pos--] = m;
                    }
                    else
                    {
                        cs[pos--] = c;
                    }
                }
                else
                {
                    cs[pos--] = c;
                }
            }

            return new string(cs, pos + 1, cs.Length - pos - 1);
        }

        /// <summary>
        /// 「は゛」を「ば」のように、濁点や半濁点を前の文字と合わせて1つの文字に変換します。
        /// </summary>
        /// <param name="str">変換する String。</param>
        /// <returns>変換された String。</returns>
        /// <remarks>
        /// Kana.ToPaddingと違い、「ゔゞヴヷヸヹヺヾ」への変換も行います。
        /// また、U+3099（濁点）とU+309A（半濁点）も前の文字と合体させて1文字にします。
        /// </remarks>
        public static string? ToPadding(string? str)
        {
            if (str == null || str.Length == 0)
            {
                return str;
            }

            char[] cs = new char[str.Length];
            int pos = str.Length - 1;

            int f = str.Length - 1;

            for (int i = f; 0 <= i; i--)
            {
                char c = str[i];

                // 濁点
                if ((c == '゛' || c == '\u3099') && 0 < i)
                {
                    char c2 = str[i - 1];
                    int mod2 = c2 % 2;
                    int mod3 = c2 % 3;

                    // か(0x304B) ～ ち(0x3061)
                    // カ(0x30AB) ～ チ(0x30C1)
                    // つ(0x3064) ～ と(0x3068)
                    // ツ(0x30C4) ～ ト(0x30C8)
                    // は(0x306F) ～ ほ(0x307B)
                    // ハ(0x30CF) ～ ホ(0x30DB)
                    // ゝ(0x309D) ヽ(0x30FD)
                    if (('か' <= c2 && c2 <= 'ち' && mod2 == 1) ||
                        ('カ' <= c2 && c2 <= 'チ' && mod2 == 1) ||
                        ('つ' <= c2 && c2 <= 'と' && mod2 == 0) ||
                        ('ツ' <= c2 && c2 <= 'ト' && mod2 == 0) ||
                        ('は' <= c2 && c2 <= 'ほ' && mod3 == 0) ||
                        ('ハ' <= c2 && c2 <= 'ホ' && mod3 == 0) ||
                        c2 == 'ゝ' || c2 == 'ヽ')
                    {
                        cs[pos--] = (char)(c2 + 1);
                        i--;
                    }
                    // う(0x3046) ウ(0x30A6) -> ゔヴ
                    else if (c2 == 'う' || c2 == 'ウ')
                    {
                        cs[pos--] = (char)(c2 + 0x004E);
                        i--;
                    }
                    // ワ(0x30EF)ヰヱヲ(0x30F2) -> ヷヸヹヺ
                    else if ('ワ' <= c2 && c2 <= 'ヲ')
                    {
                        cs[pos--] = (char)(c2 + 8);
                        i--;
                    }
                    else
                    {
                        cs[pos--] = c;
                    }
                }
                // ゜(0x309C)
                else if ((c == '゜' || c == '\u309A') && 0 < i)
                {
                    char c2 = str[i - 1];
                    int mod3 = c2 % 3;

                    // は(0x306F) ～ ほ(0x307B)
                    // ハ(0x30CF) ～ ホ(0x30DB)
                    if (('は' <= c2 && c2 <= 'ほ' && mod3 == 0) ||
                        ('ハ' <= c2 && c2 <= 'ホ' && mod3 == 0))
                    {
                        cs[pos--] = (char)(c2 + 2);
                        i--;
                    }
                    else
                    {
                        cs[pos--] = c;
                    }
                }
                else
                {
                    cs[pos--] = c;
                }
            }

            return new string(cs, pos + 1, cs.Length - pos - 1);
        }

        private static char ConvertToHankakuKanaChar(char zenkakuChar)
        {
            return zenkakuChar switch
            {
                'ァ' => 'ｧ',
                'ィ' => 'ｨ',
                'ゥ' => 'ｩ',
                'ェ' => 'ｪ',
                'ォ' => 'ｫ',
                'ー' => 'ｰ',
                'ア' => 'ｱ',
                'イ' => 'ｲ',
                'ウ' => 'ｳ',
                'エ' => 'ｴ',
                'オ' => 'ｵ',
                'カ' => 'ｶ',
                'キ' => 'ｷ',
                'ク' => 'ｸ',
                'ケ' => 'ｹ',
                'コ' => 'ｺ',
                'サ' => 'ｻ',
                'シ' => 'ｼ',
                'ス' => 'ｽ',
                'セ' => 'ｾ',
                'ソ' => 'ｿ',
                'タ' => 'ﾀ',
                'チ' => 'ﾁ',
                'ツ' => 'ﾂ',
                'テ' => 'ﾃ',
                'ト' => 'ﾄ',
                'ナ' => 'ﾅ',
                'ニ' => 'ﾆ',
                'ヌ' => 'ﾇ',
                'ネ' => 'ﾈ',
                'ノ' => 'ﾉ',
                'ハ' => 'ﾊ',
                'ヒ' => 'ﾋ',
                'フ' => 'ﾌ',
                'ヘ' => 'ﾍ',
                'ホ' => 'ﾎ',
                'マ' => 'ﾏ',
                'ミ' => 'ﾐ',
                'ム' => 'ﾑ',
                'メ' => 'ﾒ',
                'モ' => 'ﾓ',
                'ヤ' => 'ﾔ',
                'ユ' => 'ﾕ',
                'ヨ' => 'ﾖ',
                'ラ' => 'ﾗ',
                'リ' => 'ﾘ',
                'ル' => 'ﾙ',
                'レ' => 'ﾚ',
                'ロ' => 'ﾛ',
                'ワ' => 'ﾜ',
                'ヲ' => 'ｦ',
                'ン' => 'ﾝ',
                'ッ' => 'ｯ',
                //ャュョ を追加
                'ャ' => 'ｬ',
                'ュ' => 'ｭ',
                'ョ' => 'ｮ',
                // 、。「」・ を追加
                '、' => '､',
                '。' => '｡',
                '「' => '｢',
                '」' => '｣',
                '・' => '･',
                //゛゜ を追加
                '゛' => 'ﾞ',
                '゜' => 'ﾟ',
                //U+3099とU+309Aの濁点と半濁点を追加
                '\u3099' => 'ﾞ',
                '\u309A' => 'ﾟ',
                _ => '\0',
            };
        }

        private static char ConvertToZenkakuKanaChar(char hankakuChar)
        {
            return hankakuChar switch
            {
                'ｦ' => 'ヲ',
                'ｧ' => 'ァ',
                'ｨ' => 'ィ',
                'ｩ' => 'ゥ',
                'ｪ' => 'ェ',
                'ｫ' => 'ォ',
                'ｰ' => 'ー',
                'ｱ' => 'ア',
                'ｲ' => 'イ',
                'ｳ' => 'ウ',
                'ｴ' => 'エ',
                'ｵ' => 'オ',
                'ｶ' => 'カ',
                'ｷ' => 'キ',
                'ｸ' => 'ク',
                'ｹ' => 'ケ',
                'ｺ' => 'コ',
                'ｻ' => 'サ',
                'ｼ' => 'シ',
                'ｽ' => 'ス',
                'ｾ' => 'セ',
                'ｿ' => 'ソ',
                'ﾀ' => 'タ',
                'ﾁ' => 'チ',
                'ﾂ' => 'ツ',
                'ﾃ' => 'テ',
                'ﾄ' => 'ト',
                'ﾅ' => 'ナ',
                'ﾆ' => 'ニ',
                'ﾇ' => 'ヌ',
                'ﾈ' => 'ネ',
                'ﾉ' => 'ノ',
                'ﾊ' => 'ハ',
                'ﾋ' => 'ヒ',
                'ﾌ' => 'フ',
                'ﾍ' => 'ヘ',
                'ﾎ' => 'ホ',
                'ﾏ' => 'マ',
                'ﾐ' => 'ミ',
                'ﾑ' => 'ム',
                'ﾒ' => 'メ',
                'ﾓ' => 'モ',
                'ﾔ' => 'ヤ',
                'ﾕ' => 'ユ',
                'ﾖ' => 'ヨ',
                'ﾗ' => 'ラ',
                'ﾘ' => 'リ',
                'ﾙ' => 'ル',
                'ﾚ' => 'レ',
                'ﾛ' => 'ロ',
                'ﾜ' => 'ワ',
                'ﾝ' => 'ン',
                'ﾞ' => '゛',
                'ﾟ' => '゜',
                // ｬｭｮｯ を追加
                'ｬ' => 'ャ',
                'ｭ' => 'ュ',
                'ｮ' => 'ョ',
                'ｯ' => 'ッ',
                // ｡｢｣､･ を追加
                '｡' => '。',
                '｢' => '「',
                '｣' => '」',
                '､' => '、',
                '･' => '・',
                _ => '\0',
            };
        }

        #region NeeLaboratory拡張

        /// <summary>
        /// ひらがなをカタカナにする ＋ 特定文字の正規化
        /// </summary>
        /// <param name="str"></param>
        /// <returns></returns>
        public static string? ToKatakanaWithNormalize(string? str)
        {
            if (str == null || str.Length == 0)
            {
                return str;
            }

            char[] cs = str.ToCharArray();
            int f = cs.Length;

            for (int i = 0; i < f; i++)
            {
                char c = cs[i];
                // ぁ(0x3041) ～ ゖ(0x3096)
                // ゝ(0x309D) ゞ(0x309E)
                if (('ぁ' <= c && c <= 'ゖ') ||
                    ('ゝ' <= c && c <= 'ゞ'))
                {
                    cs[i] = (char)(c + 0x0060);
                }

                else
                {
                    // 特定文字の正規化
                    cs[i] = ToNormalizedChar(c);
                }
            }

            return new string(cs);
        }

        /// <summary>
        /// 特定文字の正規化
        /// </summary>
        /// <param name="src"></param>
        /// <returns></returns>
        private static char ToNormalizedChar(char src)
        {
            return src switch
            {
                'ー' => '-',
                '　' => ' ',
                '♠' => '♤',
                '♥' => '♡',
                '❤' => '♡',
                '❥' => '♡',
                '♢' => '◇',
                '♦' => '◇',
                '◆' => '◇',
                '♣' => '♧',
                '〇' => '○',
                '◯' => '○',
                '⭘' => '○',
                '`' => '\'',
                '‘' => '\'',
                '’' => '\'',
                '‛' => '\'',
                'ʹ' => '\'',
                'ʻ' => '\'',
                'ʼ' => '\'',
                'ʽ' => '\'',
                '“' => '"',
                '”' => '"',
                '‟' => '"',
                'ʺ' => '"',
                _ => src,
            };
        }

        #endregion
    }
}