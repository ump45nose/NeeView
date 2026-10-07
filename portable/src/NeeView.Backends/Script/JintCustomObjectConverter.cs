// Copyright (c) NeeLaboratory.
using Jint;
using JsEngine = Jint.Engine;
using Jint.Native;
using Jint.Runtime.Interop;


namespace NeeView.Backends
{
    public class JintCustomObjectConverter : IObjectConverter
    {
        public bool TryConvert(JsEngine engine, object value, out JsValue result)
        {
            if (value is ThemeRgba color)
            {
                result = color.ToString();
                return true;
            }

            result = JsValue.Undefined;
            return false;
        }
    }
}
