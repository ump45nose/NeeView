// Copyright (c) NeeLaboratory.


using System;
using System.Linq;
using System.Reflection;

namespace NeeView
{
    public class PropertyMapSource : PropertyMapNode
    {
        private readonly string? _prefix;

        public PropertyMapSource(string name, ObsoleteAttribute? obsolete, AlternativeAttribute? alternative, object source, PropertyInfo property, PropertyMapConverter converter, string? prefix)
            : base(name, obsolete, alternative)
        {
            Source = source;
            PropertyInfo = property;
            IsReadOnly = property.GetCustomAttribute(typeof(PropertyMapReadOnlyAttribute)) != null;
            Converter = converter;
            _prefix = prefix;
        }

        public object Source { get; private set; }
        public PropertyInfo PropertyInfo { get; private set; }
        public bool IsReadOnly { get; private set; }

        public PropertyMapConverter Converter { get; private set; }


        public object? Read(PropertyMapOptions options)
        {
            return Converter.Read(this, PropertyInfo.PropertyType, options);
        }

        public void Write(object? value, PropertyMapOptions options)
        {
            if (IsReadOnly) return;
            Converter.Write(this, value, options);
        }

        public object? GetValue()
        {
            return PropertyInfo.GetValue(Source);
        }

        public void SetValue(object? value)
        {
            PropertyInfo.SetValue(Source, value);
        }

    }
}
