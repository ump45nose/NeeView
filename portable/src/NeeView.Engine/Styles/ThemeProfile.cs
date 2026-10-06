// Copyright (c) NeeLaboratory. 原主题值与颜色解析规则，Mac 值类型适配。

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;


namespace NeeView
{
    public class ThemeProfile : ICloneable
    {
        public static ThemeProfile Default { get; }

        public static readonly List<string> Keys = new()
        {
            "Window.Background",
            "Window.Foreground",
            "Window.Border",
            "Window.ActiveTitle",
            "Window.InactiveTitle",

            "Window.Dialog.Border",

            "Control.Background",
            "Control.Foreground",
            "Control.Border",
            "Control.GrayText",
            "Control.Accent",
            "Control.AccentText",
            "Control.Danger",
            "Control.DangerText",
            "Control.Focus",
            "Control.MouseOver.Background",

            "Item.Separator",
            "Item.MouseOver.Background",
            "Item.MouseOver.Border",
            "Item.Selected.Background",
            "Item.Selected.Border",
            "Item.Inactive.Background",
            "Item.Inactive.Border",

            "Button.Background",
            "Button.Foreground",
            "Button.Border",
            "Button.MouseOver.Background",
            "Button.MouseOver.Border",
            "Button.Checked.Background",
            "Button.Checked.Border",
            "Button.Pressed.Background",
            "Button.Pressed.Border",

            "IconButton.Background",
            "IconButton.Foreground",
            "IconButton.Border",
            "IconButton.MouseOver.Background",
            "IconButton.MouseOver.Border",
            "IconButton.Checked.Background",
            "IconButton.Checked.Border",
            "IconButton.Pressed.Background",
            "IconButton.Pressed.Border",

            "Slider.Background",
            "Slider.Foreground",
            "Slider.Border",
            "Slider.Thumb",
            "Slider.Thumb.MouseOver",
            "Slider.Track",

            "ScrollBar.Background",
            "ScrollBar.Foreground",
            "ScrollBar.Border",
            "ScrollBar.MouseOver",
            "ScrollBar.Pressed",

            "TextBox.Background",
            "TextBox.Foreground",
            "TextBox.Border",
            "TextBox.MouseOver.Background",

            "Menu.Background",
            "Menu.Foreground",
            "Menu.Border",
            "Menu.Separator",

            "SideBar.Background",
            "SideBar.Foreground",
            "SideBar.Border",

            "Panel.Background",
            "Panel.Foreground",
            "Panel.Border",
            "Panel.Header",
            "Panel.Note",
            "Panel.Separator",
            "Panel.Splitter",

            "MenuBar.Background",
            "MenuBar.Foreground",
            "MenuBar.Border",
            "MenuBar.Address.Background",
            "MenuBar.Address.Border",

            "BottomBar.Background",
            "BottomBar.Foreground",
            "BottomBar.Border",
            "BottomBar.Slider.Background",
            "BottomBar.Slider.Foreground",
            "BottomBar.Slider.Border",
            "BottomBar.Slider.Thumb",
            "BottomBar.Slider.Thumb.MouseOver",
            "BottomBar.Slider.Track",

            "Toast.Background",
            "Toast.Foreground",
            "Toast.Border",

            "Notification.Background",
            "Notification.Foreground",

            "Thumbnail.Background",
            "Thumbnail.Foreground",

            "SelectedMark.Foreground",
            "CheckIcon.Foreground",
            "BookmarkIcon.Foreground",
            "PlaylistItemIcon.Foreground",

            "Tag.Background",
        };

        public static Dictionary<string, ThemeRgba> _defaultColors = new()
        {
            ["Control.Accent"] = ThemeRgba.FromArgb(0xFF, 0x11, 0x88, 0xDD),
            ["Control.AccentText"] = ThemeRgba.FromArgb(0xFF, 0xFF, 0xFF, 0xFF),
            ["Control.Danger"] = ThemeRgba.FromArgb(0xFF, 0xE8, 0x11, 0x23),
            ["Control.DangerText"] = ThemeRgba.FromArgb(0xFF, 0xFF, 0xFF, 0xFF),
            ["Tag.Background"] = ThemeRgba.FromArgb(0xFF, 0xA0, 0xA0, 0xA0),
        };

        static ThemeProfile()
        {
            Default = CreateDefaultThemeProfile();
        }

        private static ThemeProfile CreateDefaultThemeProfile()
        {
            var profile = new ThemeProfile();

            foreach (var key in Keys)
            {
                if (!key.EndsWith("Foreground", StringComparison.Ordinal) && !key.EndsWith("Background", StringComparison.Ordinal))
                {
                    profile.Colors[key] = new ThemeColor(ThemeRgba.FromArgb(255, 128, 128, 128), 1.0);
                }
            }
            profile.Colors["Window.Background"] = new ThemeColor(ThemeRgba.FromArgb(255, 0, 0, 0), 1.0);
            profile.Colors["Window.Foreground"] = new ThemeColor(ThemeRgba.FromArgb(255, 255, 255, 255), 1.0);
            profile.Colors["Control.Accent"] = new ThemeColor(ThemeRgba.FromArgb(255, 255, 255, 255), 1.0);

            // ver 43.0 で追加されたキーのデフォルト値
            profile.Colors["Control.MouseOver.Background"] = new ThemeColor("Control.Background", 1.0);
            profile.Colors["Slider.Thumb.MouseOver"] = new ThemeColor("Slider.Thumb", 0.5);
            profile.Colors["TextBox.MouseOver.Background"] = new ThemeColor("TextBox.Background", 1.0);
            profile.Colors["BottomBar.Slider.Thumb.MouseOver"] = new ThemeColor("BottomBar.Slider.Thumb", 0.5);

            return profile;
        }

        public ThemeProfile()
        {
            Format = "NeeView.Theme/1.0.0";
            Colors = new Dictionary<string, ThemeColor>();
        }

        public string Format { get; set; }

        public string? BasedOn { get; set; }

        public Dictionary<string, ThemeColor> Colors { get; set; }

        public ThemeColor this[string key] { get => Colors[key]; set => Colors[key] = value; }

        [Conditional("DEBUG")]
        public void Verify()
        {
            var lack = Keys.Except(Colors.Keys);
            var surplus = Colors.Keys.Except(Keys);

            Debug.WriteLine("ThemProfile.Verify.Lack: " + string.Join(", ", lack)); // 不足
            Debug.WriteLine("ThemProfile.Verify.Surplus: " + string.Join(", ", surplus)); // 余剰
        }

        /// <summary>按原标准键解析颜色和不透明度，显示端无需再执行引用规则。</summary>
        public ThemeProfile Validate()
        {
            var themeProfile = new ThemeProfile();
            themeProfile.Colors = ThemeProfile.Keys.ToDictionary(e => e, e => new ThemeColor(this.GetColor(e, 1.0), 1.0));
            return themeProfile;
        }

        /// <summary>原具体色/链接/默认角色规则；默认角色跳转也保留循环链，避免坏主题递归溢出。</summary>
        public ThemeRgba GetColor(string key, double opacity, IEnumerable<string>? nests = null)
        {
            if (nests != null && (nests.Contains(key) || nests.Count() >= 256)) throw new FormatException($"Circular reference: {key}");
            nests = nests is null ? new List<string>() { key } : nests.Append(key);
            if (Colors.TryGetValue(key, out var value))
            {
                switch (value.ThemeColorType)
                {
                    case ThemeColorType.Default:
                        return GetDefaultColor(key, opacity, nests);

                    case ThemeColorType.Color:
                        return AddOpacityToColor(value.Color, value.Opacity * opacity);

                    case ThemeColorType.Link:
                        return GetColor(value.Link, value.Opacity * opacity, nests);

                    default:
                        throw new NotSupportedException();
                }
            }
            else
            {
                return GetDefaultColor(key, opacity, nests);
            }
        }

        private static ThemeRgba AddOpacityToColor(ThemeRgba color, double opacity)
        {
            if (opacity == 1.0) return color;
            return ThemeRgba.FromArgb((byte)(Math.Clamp(color.A * opacity, 0.0, 255.0)), color.R, color.G, color.B);
        }

        private ThemeRgba GetDefaultColor(string key, double opacity, IEnumerable<string> nests)
        {
            if (_defaultColors.TryGetValue(key, out var color))
            {
                return AddOpacityToColor(color, opacity);
            }

            var tokens = key.Split('.');
            if (tokens.Length < 2) throw new FormatException($"Wrong format: {key}");

            var name = string.Join(".", tokens.Take(tokens.Length - 1));
            var role = tokens.Last();

            switch (role)
            {
                case "Foreground":
                case "Background":
                    if (name == "Window") return Default.GetColor(key, opacity);
                    return GetColor("Window." + role, opacity, nests);

                default:
                    if (!(Colors.ContainsKey(key) || Keys.Contains(key)))
                    {
                        throw new FormatException($"No such key: {key}");
                    }
                    return GetColor(name + ".Background", opacity, nests);
            }
        }

        public object Clone()
        {
            var clone = (ThemeProfile)MemberwiseClone();
            clone.Colors = new Dictionary<string, ThemeColor>(this.Colors);
            return clone;
        }
    }
}
