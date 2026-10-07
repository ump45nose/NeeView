// Copyright (c) NeeLaboratory.
using Jint;
using JsEngine = Jint.Engine;
using Jint.Runtime.Interop;
using System;
using System.Diagnostics.CodeAnalysis;


namespace NeeView.Backends
{
    public class JintCustomTypeConverter : DefaultTypeConverter
    {
        public JintCustomTypeConverter(JsEngine engine) : base(engine)
        {
        }

        public override object? Convert(object? value, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicFields)] Type type, IFormatProvider formatProvider)
        {
            if (type == typeof(ThemeRgba) && value is string s)
            {
                try
                {
                    return ThemeRgba.Parse(s);
                }
                catch (Exception ex)
                {
                    throw new ArgumentException($"Could not convert '{s}' to type Color.", ex);
                }
            }

            return base.Convert(value, type, formatProvider);
        }

        public override bool TryConvert(object? value, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicFields)] Type type, IFormatProvider formatProvider, [NotNullWhen(true)] out object? converted)
        {
            if (type == typeof(ThemeRgba) && value is string s)
            {
                try
                {
                    converted = ThemeRgba.Parse(s);
                    return true;
                }
                catch
                {
                    converted = null;
                    return false;
                }
            }

            return base.TryConvert(value, type, formatProvider, out converted);
        }
    }
}
