using System.Diagnostics;
using System.Linq;

namespace NeeLaboratory.IO.Search
{
    // NOTE: 未使用
    public class StringKey
    {
        private readonly int _key;

        public StringKey(int value)
        {
            _key = value;
        }

        public StringKey(string s)
        {
            _key = ToKey(s);
        }

        public int Key => _key;

        public override string ToString()
        {
            var s = new string(new char[] {
                (char)((_key >> 0x18) & 0xff),
                (char)((_key >> 0x10) & 0xff),
                (char)((_key >> 0x08) & 0xff),
                (char)((_key >> 0x00) & 0xff)});

            return s;
        }

        public static int ToKey(string s)
        {
            Debug.Assert(s.Length == 4);
            Debug.Assert(s.All(e => (e & 0xff) == e));
            return (s[0] << 0x18) | (s[1] << 0x10) | (s[2] << 0x08) | (s[3] << 0x00);
        }
    }
}
