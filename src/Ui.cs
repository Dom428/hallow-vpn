// Hallow VPN - small helpers for building WPF in code (no XAML compiler): theme brushes by key, text, buttons, cards, dark title
// or light title bars, program icons. No colour is written here: everything comes from Theme.xaml (light mode swaps its values - Look.cs).
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace HallowVPN
{
    public static class Ui
    {
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int val, int size);
        public static Brush R(string key) { return (Brush)Application.Current.FindResource(key); }
        public static Style S(string key) { return (Style)Application.Current.FindResource(key); }
        public static FontFamily Mono { get { return (FontFamily)Application.Current.FindResource("MonoFont"); } }
        public static FontFamily Head { get { return (FontFamily)Application.Current.FindResource("HeadFont"); } }

        public static void Dress(Window w, string title, double width)
        {
            w.Title = title; w.Width = width; w.SizeToContent = SizeToContent.Height;
            Restyle(w); w.FontSize = 12.5;
            try { w.Icon = AppIcon(); } catch { }
            w.SourceInitialized += (s, e) => TitleBar(w);
        }
        // Windows' title bar follows the mode (DWMWA_USE_IMMERSIVE_DARK_MODE = 20)
        public static void TitleBar(Window w)
        {
            try { var h = new WindowInteropHelper(w).Handle; if (h == IntPtr.Zero) return; int dark = Mode.IsLight ? 0 : 1; DwmSetWindowAttribute(h, 20, ref dark, 4); } catch { }
        }
        public static void Restyle(Window w) { w.Background = R("Bg"); w.Foreground = R("Text"); w.FontFamily = (FontFamily)Application.Current.FindResource("BodyFont"); }
        static ImageSource icon;
        public static ImageSource AppIcon()
        {
            if (icon != null) return icon;
            var ico = System.Drawing.Icon.ExtractAssociatedIcon(Assembly.GetExecutingAssembly().Location);
            icon = Imaging.CreateBitmapSourceFromHIcon(ico.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions()); icon.Freeze();
            return icon;
        }
        public static TextBlock T(string text, double size = 12.5, bool bold = false, Brush fg = null, double bottom = 0)
        {
            var t = new TextBlock { Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, bottom) };
            if (bold) t.FontWeight = FontWeights.SemiBold; if (fg != null) t.Foreground = fg;
            return t;
        }
        public static TextBlock Muted(string text, double size = 12, double bottom = 0) { return T(text, size, false, R("Muted"), bottom); }
        public static TextBlock Num(string text, double size = 12, Brush fg = null) { var t = new TextBlock { Text = text, FontSize = size, FontFamily = Mono }; if (fg != null) t.Foreground = fg; return t; }
        // a heading in the console style: spaced capitals in the head font
        public static TextBlock Caps(string text, double size = 11, Brush fg = null)
        {
            return new TextBlock { Text = Spaced(text.ToUpperInvariant()), FontSize = size, FontFamily = Head, Foreground = fg ?? R("Lilac"), FontWeight = FontWeights.SemiBold };
        }
        public static string Spaced(string s) { var sb = new System.Text.StringBuilder(); foreach (var c in s) { if (sb.Length > 0) sb.Append(' '); sb.Append(c); } return sb.ToString(); }
        public static Button Btn(string label, Action click, bool accent = false, string tip = null)
        {
            var b = new Button { Content = label, Style = S(accent ? "Accent" : "Btn"), Margin = new Thickness(0, 0, 8, 0) };
            if (tip != null) b.ToolTip = tip;
            if (click != null) b.Click += (s, e) => click();
            return b;
        }
        public static Button Danger(string label, Action click) { var b = Btn(label, click); b.Style = S("Danger"); return b; }
        public static StackPanel Row(params UIElement[] items) { var p = new StackPanel { Orientation = Orientation.Horizontal }; foreach (var i in items) p.Children.Add(i); return p; }
        public static Border Card(UIElement child, double pad = 16)
        {
            return new Border { Child = child, Background = R("Panel"), BorderBrush = R("Line"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(3), Padding = new Thickness(pad) };
        }
        // a small coloured label ("Anti-cheat - leave alone", "Game", "Lowered")
        public static Border Pill(string text, string fgKey, string bgKey)
        {
            return new Border { Background = R(bgKey), BorderBrush = R(fgKey), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(2), Padding = new Thickness(7, 1, 7, 2), VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock { Text = text, FontSize = 11, Foreground = R(fgKey), FontWeight = FontWeights.SemiBold } };
        }
        public static FrameworkElement Header(string word, double size = 20)
        {
            var title = new StackPanel { Orientation = Orientation.Horizontal };
            try { title.Children.Add(new Image { Source = AppIcon(), Width = size + 8, Height = size + 8, Margin = new Thickness(0, 0, 10, 0) }); } catch { }
            title.Children.Add(new TextBlock { Text = "HALLOW", FontSize = size, FontWeight = FontWeights.Bold, Foreground = R("AccentBrush"), VerticalAlignment = VerticalAlignment.Center, FontFamily = Head });
            title.Children.Add(new TextBlock { Text = " " + word, FontSize = size, FontWeight = FontWeights.Light, VerticalAlignment = VerticalAlignment.Center, FontFamily = Head });
            return title;
        }
        public static CheckBox Switch(string text, bool on, Action<bool> changed)
        {
            var c = new CheckBox { Content = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap }, IsChecked = on, Style = S("Switch") };
            if (changed != null) c.Click += (s, e) => changed(c.IsChecked == true);
            return c;
        }
        public static TextBox Field(string text = "", double width = double.NaN) { var t = new TextBox { Text = text, Style = S("Field") }; if (!double.IsNaN(width)) t.Width = width; return t; }

        // the readable accent: a very dark theme.json accent is lightened until it reads on the panels (like Hallow Guard's console)
        public static string Readable(string accent)
        {
            try
            {
                var c = (Color)ColorConverter.ConvertFromString(accent); var on = (Color)ColorConverter.ConvertFromString("#221C2E");
                Func<Color, double> lum = x => { Func<byte, double> f = v => { var d = v / 255.0; return d <= 0.03928 ? d / 12.92 : Math.Pow((d + 0.055) / 1.055, 2.4); }; return 0.2126 * f(x.R) + 0.7152 * f(x.G) + 0.0722 * f(x.B); };
                Func<Color, double> ratio = x => (Math.Max(lum(x), lum(on)) + 0.05) / (Math.Min(lum(x), lum(on)) + 0.05);
                if (ratio(c) >= 4.6) return accent;
                for (int i = 1; i <= 20; i++)
                {
                    double t = i * 0.05; var m = Color.FromRgb((byte)(c.R + (255 - c.R) * t), (byte)(c.G + (255 - c.G) * t), (byte)(c.B + (255 - c.B) * t));
                    if (ratio(m) >= 4.6) return "#" + m.R.ToString("X2") + m.G.ToString("X2") + m.B.ToString("X2");
                }
            }
            catch { }
            return accent;
        }

        // the country's flag (embedded PNGs from flagcdn.com, 40 px wide, one per Nord country - built into the exe, nothing is
        // fetched at run time); the code in a small tile ("NL") if there's no flag for it. Hover shows the code.
        static readonly Dictionary<string, BitmapSource> flagCache = new Dictionary<string, BitmapSource>();
        public static BitmapSource Flag(string code)
        {
            if (string.IsNullOrEmpty(code)) return null;
            var c = code.Trim().ToLowerInvariant(); if (c == "uk") c = "gb";
            if (c.Length != 2) return null;
            BitmapSource b;
            lock (flagCache)
            {
                if (flagCache.TryGetValue(c, out b)) return b;
                try
                {
                    using (var st = Assembly.GetExecutingAssembly().GetManifestResourceStream("HallowVPN.Flags." + c + ".png"))
                    {
                        if (st != null)
                        {
                            var img = new BitmapImage(); img.BeginInit(); img.CacheOption = BitmapCacheOption.OnLoad; img.StreamSource = st; img.EndInit(); img.Freeze(); b = img;
                        }
                    }
                }
                catch { b = null; }
                flagCache[c] = b; return b;
            }
        }
        public static FrameworkElement CountryTile(string code, double size = 22)
        {
            var flag = Flag(code);
            if (flag != null)
            {
                // flags have different shapes (Switzerland square, Nepal taller than wide): fit inside the same box, centred
                var im = new Image { Source = flag, Stretch = Stretch.Uniform, Width = size + 6, Height = size - 4, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, SnapsToDevicePixels = true };
                RenderOptions.SetBitmapScalingMode(im, BitmapScalingMode.HighQuality);
                return new Border { Width = size + 6, Height = size - 4, VerticalAlignment = VerticalAlignment.Center, Child = im, ToolTip = code.ToUpperInvariant() };
            }
            return new Border { Width = size + 6, Height = size - 4, CornerRadius = new CornerRadius(2), Background = R("TileBg"), VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock { Text = string.IsNullOrEmpty(code) ? "--" : code.ToUpperInvariant(), FontSize = size * 0.46, FontWeight = FontWeights.Bold, FontFamily = Mono, Foreground = R("TileText"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
        }
        // a small load bar (0-100 %): green / amber / red
        public static FrameworkElement LoadBar(int load, double width = 60)
        {
            var g = new Grid { Width = width, Height = 6, VerticalAlignment = VerticalAlignment.Center };
            g.Children.Add(new Border { Background = R("LineDim"), CornerRadius = new CornerRadius(3) });
            if (load >= 0) g.Children.Add(new Border { Background = R(load < 40 ? "Green" : load < 70 ? "Amber" : "Red"), CornerRadius = new CornerRadius(3), Width = Math.Max(3, width * Math.Min(100, load) / 100.0), HorizontalAlignment = HorizontalAlignment.Left });
            return g;
        }
        public static Border Note(string text, string fgKey, string bgKey)
        {
            return new Border { Background = R(bgKey), BorderBrush = R(fgKey), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(2), Padding = new Thickness(12, 8, 12, 8), Margin = new Thickness(0, 0, 0, 10),
                Child = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Foreground = R("Text"), FontSize = 12.5 } };
        }
    }
}