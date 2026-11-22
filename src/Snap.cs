// Hallow VPN - developer snapshots: "HallowVPN.exe --snap <folder>" renders the windows to PNGs without showing them.
// Nothing is changed in this mode: no relay, screen capture, input, settings, tray or hotkeys.
using System;
using System.IO;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace HallowVPN
{
    public static class Snap
    {
        public static bool On;

        public static void Save(Window win, string path, double width, double height)
        {
            var c = win.Content as UIElement; win.Content = null;
            var host = new System.Windows.Controls.Border { Background = win.Background, Child = c, Width = width };
            if (height > 0) host.Height = height;
            TextElement.SetForeground(host, win.Foreground); TextElement.SetFontFamily(host, win.FontFamily); TextElement.SetFontSize(host, win.FontSize);
            foreach (var k in win.Resources.Keys) host.Resources[k] = win.Resources[k];
            host.Measure(new Size(width, height > 0 ? height : double.PositiveInfinity));
            var sz = host.DesiredSize; if (height <= 0) host.Height = sz.Height;
            host.Arrange(new Rect(0, 0, width, host.Height)); host.UpdateLayout();
            var bmp = new RenderTargetBitmap((int)Math.Ceiling(width), (int)Math.Ceiling(host.Height), 96, 96, PixelFormats.Pbgra32);
            bmp.Render(host);
            var enc = new PngBitmapEncoder(); enc.Frames.Add(BitmapFrame.Create(bmp));
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            using (var f = File.Create(path)) enc.Save(f);
            host.Child = null; win.Content = c;
        }
    }
}
