// Copyright (c) NeeLaboratory. MIT；原 StringTemplate 源码迁入，见 source-migration.json。
namespace NeeView.StringTemplate
{
    public class KeyInfo<TSource>
    {
        public delegate string KeyFormatter(TSource source, string format, string suffix);

        public KeyInfo(KeyFormatter formatter) : this(formatter, StringFormatChangedAction.None)
        {
        }

        public KeyInfo(KeyFormatter formatter, StringFormatChangedAction changedAction)
        {
            Formatter = formatter;
            ChangedAction = changedAction;
        }

        public KeyFormatter Formatter { get; set; }
        public StringFormatChangedAction ChangedAction { get; set; }
    }
}

