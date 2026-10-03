// Copyright (c) NeeLaboratory. 从固定 Windows 基线迁入，平台值类型适配。
using System.Collections.Generic;
using System.ComponentModel;

namespace NeeView
{
    // Mouse gesture direction
    [TypeConverter(typeof(MouseDirectionConverter))]
    public enum MouseDirection
    {
        None,
        Up,
        Right,
        Down,
        Left,
        Click,
    }

    public static class MouseGestureDirectionExtensions
    {
        private static readonly Dictionary<MouseDirection, string> _map = new()
        {
            [MouseDirection.None] = "",
            [MouseDirection.Up] = "↑",
            [MouseDirection.Right] = "→",
            [MouseDirection.Down] = "↓",
            [MouseDirection.Left] = "←",
            [MouseDirection.Click] = "Click"
        };

        public static void SetDisplayString(this MouseDirection key, string value)
        {
            _map[key] = value;
        }

        public static string GetDisplayString(this MouseDirection key)
        {
            return _map.TryGetValue(key, out var s) ? s : key.ToString();
        }
    }
}
