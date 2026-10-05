// Copyright (c) NeeLaboratory. MIT；原 StringTemplate 源码迁入，见 source-migration.json。
namespace NeeView.StringTemplate
{
    public class WordInfo<TSource>
    {
        public WordInfo(string placeholder, KeyInfo<TSource>? formatInfo, string suffix, string format)
        {
            Placeholder = placeholder;
            FormatInfo = formatInfo;
            Suffix = suffix;
            Format = format;
        }

        public string Placeholder { get; }
        public KeyInfo<TSource>? FormatInfo { get; }
        public string Suffix { get; }
        public string Format { get; }
    }
}

