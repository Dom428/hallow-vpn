// Hallow VPN - light and dark mode (the family light/dark spec).
//   The family switch: "mode" in %APPDATA%\Hallow\theme.json ("dark" / "light" / "system"; missing = "dark"), written by Hallow
//   Toolkit - Hallow VPN only reads it. "system" = Windows' app mode (HKCU\...\Themes\Personalize AppsUseLightTheme, 1 = light),
//   followed live (the Controller polls it every few seconds; theme.json changes come through the accent watcher).
//   The app's own choice (Settings > Look): Follow Hallow (default) / Light / Dark - it wins over the family switch.
// The light palette swaps the VALUES of Theme.xaml (the resource keys stay the same), like Hallow Guard's console look.
using System;
using System.Text.RegularExpressions;
using System.Windows.Media;
using Microsoft.Win32;

namespace HallowVPN
{
    // Windows' app mode: true = light, false = dark, null = unknown (treated as dark)
    public interface ISystemTheme { bool? AppsUseLight(); }
    public class RegistrySystemTheme : ISystemTheme
    {
        public bool? AppsUseLight()
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    var v = k == null ? null : k.GetValue("AppsUseLightTheme");
                    if (v is int) return (int)v == 1;
                    return null;
                }
            }
            catch { return null; }
        }
    }
    public class FakeSystemTheme : ISystemTheme
    {
        public bool? Light; public int Reads;
        public bool? AppsUseLight() { Reads++; return Light; }
    }

    // the tray menu's colours (WinForms draws it)
    public class TrayColors : System.Windows.Forms.ProfessionalColorTable
    {
        public System.Drawing.Color Back, Hover, Line, Fore;
        public static TrayColors For(bool light)
        {
            Func<string, System.Drawing.Color> c = System.Drawing.ColorTranslator.FromHtml;
            return light ? new TrayColors { Back = c(Mode.LightPanel), Hover = c(Mode.LightPanel2), Line = c(Mode.LightLine), Fore = c(Mode.LightText) }
                         : new TrayColors { Back = c("#1C1726"), Hover = c("#3A3248"), Line = c("#3A3248"), Fore = c("#ECE8F4") };
        }
        public override System.Drawing.Color ToolStripDropDownBackground { get { return Back; } }
        public override System.Drawing.Color ImageMarginGradientBegin { get { return Back; } }
        public override System.Drawing.Color ImageMarginGradientMiddle { get { return Back; } }
        public override System.Drawing.Color ImageMarginGradientEnd { get { return Back; } }
        public override System.Drawing.Color MenuBorder { get { return Line; } }
        public override System.Drawing.Color MenuItemBorder { get { return Hover; } }
        public override System.Drawing.Color MenuItemSelected { get { return Hover; } }
        public override System.Drawing.Color MenuItemSelectedGradientBegin { get { return Hover; } }
        public override System.Drawing.Color MenuItemSelectedGradientEnd { get { return Hover; } }
        public override System.Drawing.Color SeparatorDark { get { return Line; } }
        public override System.Drawing.Color SeparatorLight { get { return Back; } }
        public override System.Drawing.Color CheckBackground { get { return Hover; } }
        public override System.Drawing.Color CheckSelectedBackground { get { return Hover; } }
        public override System.Drawing.Color CheckPressedBackground { get { return Hover; } }
    }

    public static class Mode
    {
        public const string Follow = "follow", Light = "light", Dark = "dark", System = "system";
        public static ISystemTheme Sys = new RegistrySystemTheme();
        public static bool IsLight;   // the mode in use right now (set by Program.LoadTheme)

        public static string Own(string s) { var v = (s ?? "").Trim().ToLowerInvariant(); return v == Light ? Light : v == Dark ? Dark : Follow; }
        public static string Hallow(string s) { var v = (s ?? "").Trim().ToLowerInvariant(); return v == Light ? Light : v == System ? System : Dark; }
        public static string OwnLabel(string s) { s = Own(s); return s == Light ? "Light" : s == Dark ? "Dark" : "Follow Hallow"; }

        // the app's own Light / Dark wins; else the family switch ("system" = Windows' app mode); missing = dark
        public static bool Resolve(string own, string hallow, ISystemTheme sys)
        {
            own = Own(own);
            if (own == Light) return true;
            if (own == Dark) return false;
            hallow = Hallow(hallow);
            if (hallow == Light) return true;
            if (hallow == System) { bool? v = null; try { v = sys == null ? null : sys.AppsUseLight(); } catch { } return v == true; }
            return false;
        }
        public static string Why(string own, string hallow)
        {
            if (Own(own) != Follow) return "your choice here";
            hallow = Hallow(hallow);
            return hallow == System ? "Hallow follows Windows" : hallow == Light ? "Hallow is set to light" : "Hallow is set to dark";
        }

        // ---- colour maths
        static Color Hex(string s) { return (Color)ColorConverter.ConvertFromString(s); }
        static string ToHex(Color c) { return "#" + c.R.ToString("X2") + c.G.ToString("X2") + c.B.ToString("X2"); }
        static double Lum(Color c)
        {
            Func<byte, double> f = v => { var d = v / 255.0; return d <= 0.03928 ? d / 12.92 : Math.Pow((d + 0.055) / 1.055, 2.4); };
            return 0.2126 * f(c.R) + 0.7152 * f(c.G) + 0.0722 * f(c.B);
        }
        static double Ratio(Color a, Color b) { double x = Lum(a), y = Lum(b); return (Math.Max(x, y) + 0.05) / (Math.Min(x, y) + 0.05); }
        public static double Ratio(string a, string b) { return Ratio(Hex(a), Hex(b)); }
        static Color FromHsl(double h, double s, double l)
        {
            Func<double, double, double, double> f = (p, q, t) => { if (t < 0) t += 1; if (t > 1) t -= 1; return t < 1 / 6.0 ? p + (q - p) * 6 * t : t < 0.5 ? q : t < 2 / 3.0 ? p + (q - p) * (2 / 3.0 - t) * 6 : p; };
            double r, g, b;
            if (s == 0) r = g = b = l;
            else { var q = l < 0.5 ? l * (1 + s) : l + s - l * s; var p = 2 * l - q; r = f(p, q, h + 1 / 3.0); g = f(p, q, h); b = f(p, q, h - 1 / 3.0); }
            return Color.FromRgb((byte)Math.Round(r * 255), (byte)Math.Round(g * 255), (byte)Math.Round(b * 255));
        }
        // the family accent rule (Hallow Toolkit's Get-LightTextAccent): System.Drawing's HSL, L lowered 0.01 a step until the contrast is reached
        public static string Darken(string accent, double min, string on)
        {
            Color a; try { a = Hex(accent); } catch { a = Hex(Accent.Default); }
            var bg = Hex(on);
            if (Ratio(a, bg) >= min) return ToHex(a);
            var d = global::System.Drawing.Color.FromArgb(a.R, a.G, a.B);
            double h = d.GetHue() / 360.0, s = d.GetSaturation(), l = d.GetBrightness();
            var c = a;
            while (l > 0) { l = Math.Max(0.0, l - 0.01); c = FromHsl(h, s, l); if (Ratio(c, bg) >= min) break; }
            return ToHex(c);
        }
        public static string FillText(string fill)
        {
            Color f; try { f = Hex(fill); } catch { f = Hex(Accent.Default); }
            return Ratio(f, Hex(LightText)) >= Ratio(f, Colors.White) ? LightText : "#FFFFFF";
        }
        public static string LightFill(string accent)
        {
            string a; try { a = ToHex(Hex(accent)); } catch { a = Accent.Default; }
            return Ratio(a, FillText(a)) >= 4.5 ? a : Darken(a, 4.5, "#FFFFFF");
        }
        // the accent for TEXT and thin lines on the light surfaces (4.5:1 on the darkest one, #EEEAF5): the default orange becomes about #B3540E
        public static string TextAccent(string accent) { return Darken(accent, 4.5, LightPanel2); }
        public static readonly string LilacText = Darken(LightLilac, 4.5, LightPanel2);

        // ---- the light palette (the family spec)
        public const string LightBg = "#F6F4FA", LightPanel = "#FFFFFF", LightPanel2 = "#EEEAF5", LightLine = "#DAD3E6", LightText = "#1E1A26",
            LightMuted = "#6B6478", LightLilac = "#7A5CC0", LightGreen = "#1E8E4F", LightAmber = "#A66A00", LightRed = "#C2333F",
            LightNav = "#3A3448", LightHoverLine = "#B9AED0", LightGreenText = "#1B7A44", LightAmberText = "#8F5B00";

        // Theme.xaml (dark) value -> light value. Status colours are used as small text here, so the TEXT values are used.
        public static string[,] Map { get { return new string[,] {
            { "#33E05A64", "#1FC2333F" }, { "#2EF0BE5A", "#1FA66A00" }, { "#266ED28C", "#1F1E8E4F" }, { "#2EA98BE0", "#1F7A5CC0" }, { "#1FA98BE0", "#147A5CC0" },
            { "#14101B", LightBg }, { "#110E17", LightPanel }, { "#1C1726", LightPanel }, { "#221C2E", LightPanel2 }, { "#120E18", LightPanel },
            { "#3A3248", LightLine }, { "#2A2336", "#E6E0EF" }, { "#1E1829", LightPanel }, { "#463C58", LightHoverLine }, { "#ECE8F4", LightText },
            { "#D6D0E2", LightNav }, { "#9790A8", LightMuted }, { "#6E6684", "#8A8298" }, { "#A98BE0", LilacText }, { "#6E5A96", LightHoverLine },
            { "#6ED28C", LightGreenText }, { "#F0BE5A", LightAmberText }, { "#E05A64", LightRed }, { "#2E2640", "#E4DCF3" }, { "#5AB4F0", "#2F65A8" } }; } }

        // the light dictionary: accent text / lines -> the darkened accent, the Accent button filled, no glows, then the value swap
        public static string LightTheme(string xaml, string accent)
        {
            var ta = TextAccent(accent ?? Accent.Default); var fill = LightFill(accent ?? Accent.Default);
            xaml = xaml.Replace("<GradientStop Color=\"#47F07820\" Offset=\"0\"/><GradientStop Color=\"#24F07820\" Offset=\"1\"/>", "<GradientStop Color=\"{FILL}\" Offset=\"0\"/><GradientStop Color=\"{FILL}\" Offset=\"1\"/>")
                       .Replace("<Setter Property=\"Foreground\" Value=\"#F59A50\"/>", "<Setter Property=\"Foreground\" Value=\"{FILLTEXT}\"/>")
                       .Replace("<GradientStop Color=\"#33F07820\" Offset=\"0\"/><GradientStop Color=\"#00F07820\" Offset=\"1\"/>", "<GradientStop Color=\"{PANEL2}\" Offset=\"0\"/><GradientStop Color=\"#00{PANEL2HEX}\" Offset=\"1\"/>");
            xaml = Regex.Replace(xaml, "(Property=\"(?:CaretBrush|BorderBrush|Foreground)\" Value=\")#(?:F07820|F59A50)\"", "${1}" + ta + "\"", RegexOptions.IgnoreCase);
            xaml = Regex.Replace(xaml, "((?:Stroke|BorderBrush|Fill)=\")#F07820\"", "${1}" + ta + "\"", RegexOptions.IgnoreCase);
            xaml = Regex.Replace(xaml, "(<SolidColorBrush x:Key=\"(?:AccentBrush|AccentText|CheckedLine)\" Color=\")#F07820\"", "${1}" + ta + "\"");
            xaml = Regex.Replace(xaml, "(<SolidColorBrush x:Key=\"AccentSoft\" Color=\")#F59A50\"", "${1}" + ta + "\"");
            xaml = Regex.Replace(xaml, "(<DropShadowEffect [^>]*?Opacity=\")[0-9.]+\"", "${1}0\"");
            var map = Map;
            for (int i = 0; i < map.GetLength(0); i++) xaml = Regex.Replace(xaml, Regex.Escape(map[i, 0]) + "(?![0-9A-Fa-f])", map[i, 1], RegexOptions.IgnoreCase);
            return xaml.Replace("{FILL}", fill).Replace("{FILLTEXT}", FillText(fill)).Replace("{PANEL2}", LightPanel2).Replace("{PANEL2HEX}", LightPanel2.Substring(1));
        }
    }
}
