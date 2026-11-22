// Hallow VPN - Font Awesome icons (path data in Icons.g.cs). Buttons get their icon from their label,
// so a label that changes ("Mute" -> "Muted") changes its icon too.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Shapes;

namespace HallowVPN
{
    public static partial class Icons
    {
        static readonly Dictionary<string, Geometry> cache = new Dictionary<string, Geometry>();

        // exact label -> icon
        static readonly Dictionary<string, string> Labels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
            { "Disconnect", "link-slash" }, { "Cancel", "xmark" }, { "Best for gaming", "gamepad" }, { "Fastest server", "bolt" }, { "Pick a server", "globe" },
            { "Check my IP again", "location-dot" }, { "Refresh", "arrows-rotate" }, { "Save", "check" }, { "Save and reconnect", "arrows-rotate" }, { "Replace", "check" },
            { "Remove", "xmark" }, { "Check again", "arrows-rotate" }, { "Use this", "house" }, { "Stop using it", "xmark" }, { "Check for updates", "arrows-rotate" },
            { "Open data folder", "folder-open" }, { "Show details", "eye" }, { "Open folder", "folder-open" }, { "OK", "check" }, { "Update", "download" },
            { "Open Account", "key" }, { "Connected", "circle-check" }, { "Remove the Windows VPN profile", "xmark" },
            { "Fix my connection", "stethoscope" }, { "Open the setup", "list-check" }, { "Open the first-run setup", "list-check" }, { "Run the checks", "stethoscope" }, { "Trust this network", "wifi" },
            { "Don't trust this network", "xmark" }, { "Show my real IP", "location-dot" }, { "Run the leak test again", "magnifying-glass" }, { "Recreate the VPN profile", "wrench" },
            { "Refresh the server list", "arrows-rotate" }, { "Best for torrents", "share-nodes" }, { "Switch to the fastest", "bolt" }, { "Test connection to Fastest", "power-off" }, { "Test again", "arrows-rotate" },
            { "Open Home", "house" }, { "Open the Connection page", "power-off" },
        };
        static readonly string[][] Prefixes = { new[] { "Update to", "download" }, new[] { "Connect", "power-off" }, new[] { "Install", "shield-halved" }, new[] { "Remove", "xmark" }, new[] { "Disconnect", "link-slash" } };        public static string ForLabel(string label)
        {
            if (string.IsNullOrEmpty(label)) return null;
            string n; if (Labels.TryGetValue(label.Trim(), out n)) return n;
            foreach (var p in Prefixes) if (label.StartsWith(p[0], StringComparison.OrdinalIgnoreCase)) return p[1];
            return null;
        }

        public static Geometry Get(string name)
        {
            if (name == null) return null;
            Geometry g;
            if (!cache.TryGetValue(name, out g))
            {
                string d; if (!Data.TryGetValue(name, out d)) return null;
                g = Geometry.Parse(d); g.Freeze(); cache[name] = g;
            }
            return g;
        }

        // A standalone icon (strip headers, the header bar).
        public static FrameworkElement Make(string name, double size, Brush fill)
        {
            return new Path { Data = Get(name), Fill = fill, Stretch = Stretch.Uniform, Width = size, Height = size, SnapsToDevicePixels = true };
        }
        // Icon + text for places that aren't styled buttons.
        public static FrameworkElement WithText(string name, string text, double size, Brush fill)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            var ic = Make(name, size, fill); ic.Margin = new Thickness(0, 0, 7, 0); ic.VerticalAlignment = VerticalAlignment.Center;
            sp.Children.Add(ic); sp.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center });
            return sp;
        }
    }

    // Used by the button styles in Theme.xaml.
    public class IconConv : IValueConverter
    {
        public object Convert(object v, Type t, object p, CultureInfo c) { return Icons.Get(Icons.ForLabel(v as string)); }
        public object ConvertBack(object v, Type t, object p, CultureInfo c) { throw new NotSupportedException(); }
    }
    public class IconVis : IValueConverter
    {
        public object Convert(object v, Type t, object p, CultureInfo c) { return Icons.ForLabel(v as string) != null ? Visibility.Visible : Visibility.Collapsed; }
        public object ConvertBack(object v, Type t, object p, CultureInfo c) { throw new NotSupportedException(); }
    }
    // Space between icon and text (none when the text is hidden).
    public class IconGap : IValueConverter
    {
        public object Convert(object v, Type t, object p, CultureInfo c)
        {
            var s = v as string;
            return s != null && s.Length > 1 ? new Thickness(0, 0, 7, 0) : new Thickness(0);
        }
        public object ConvertBack(object v, Type t, object p, CultureInfo c) { throw new NotSupportedException(); }
    }
    // Hides the text of one-character labels that have an icon ("+", "x").
    public class LabelVis : IValueConverter
    {
        public object Convert(object v, Type t, object p, CultureInfo c)
        {
            var s = v as string;
            return s != null && s.Length == 1 && Icons.ForLabel(s) != null ? Visibility.Collapsed : Visibility.Visible;
        }
        public object ConvertBack(object v, Type t, object p, CultureInfo c) { throw new NotSupportedException(); }
    }
}
