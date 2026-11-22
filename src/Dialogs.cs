// Hallow VPN - the small themed dialogs: "Install the kill switch?" / "Install Nord's certificate?" (always asked before anything
// that needs admin), a message, and the crash notice. Each is a Window built in code; --snap renders them without showing them.
using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace HallowVPN
{
    public static class Dialogs
    {
        static Window Make(string title, double width)
        {
            var w = new Window(); Ui.Dress(w, title, width);
            w.ResizeMode = ResizeMode.NoResize; w.ShowInTaskbar = false; w.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            return w;
        }
        static StackPanel Body(Window w) { var sp = new StackPanel { Margin = new Thickness(22, 18, 22, 18) }; w.Content = sp; return sp; }
        static WrapPanel Buttons(StackPanel sp) { var row = new WrapPanel { Margin = new Thickness(0, 16, 0, 0), HorizontalAlignment = HorizontalAlignment.Right }; sp.Children.Add(row); return row; }

        public static Window ConfirmWindow(string title, string text, string okLabel, bool danger, Action<bool> done)
        {
            var w = Make("Hallow VPN", 480); var sp = Body(w);
            sp.Children.Add(Ui.T(title, 15, true, null, 6)); sp.Children.Add(Ui.Muted(text, 12.5));
            var row = Buttons(sp); bool answer = false;
            row.Children.Add(Ui.Btn("Cancel", () => w.Close()));
            var ok = danger ? Ui.Danger(okLabel, () => { answer = true; w.Close(); }) : Ui.Btn(okLabel, () => { answer = true; w.Close(); }, true); ok.Margin = new Thickness(0); ok.MinWidth = 90;
            row.Children.Add(ok);
            w.Closed += (s, e) => { if (done != null) done(answer); };
            w.Loaded += (s, e) => ((Button)row.Children[0]).Focus();   // Cancel has the focus: Enter never changes anything by accident
            return w;
        }
        public static bool Confirm(Window owner, string title, string text, string okLabel, bool danger)
        {
            bool r = false; var w = ConfirmWindow(title, text, okLabel, danger, a => r = a); Own(w, owner); w.ShowDialog(); return r;
        }
        static void Own(Window w, Window owner) { if (owner != null && owner.IsVisible) { w.Owner = owner; w.WindowStartupLocation = WindowStartupLocation.CenterOwner; } }

        public static Window InfoWindow(string title, string text, Action done)
        {
            var w = Make("Hallow VPN", 460); var sp = Body(w);
            sp.Children.Add(Ui.T(title, 15, true, null, 6)); sp.Children.Add(Ui.Muted(text, 12.5));
            var row = Buttons(sp); var ok = Ui.Btn("OK", () => w.Close(), true); ok.Margin = new Thickness(0); ok.MinWidth = 80; row.Children.Add(ok);
            w.Closed += (s, e) => { if (done != null) done(); };
            return w;
        }
        public static void Info(Window owner, string title, string text) { var w = InfoWindow(title, text, null); Own(w, owner); w.ShowDialog(); }
    }

    public static class CrashNotice
    {
        public static Window Make(string file)
        {
            var w = new Window(); Ui.Dress(w, "Hallow VPN", 440);
            w.ResizeMode = ResizeMode.NoResize; w.WindowStartupLocation = WindowStartupLocation.CenterScreen; w.Topmost = true;
            var sp = new StackPanel { Margin = new Thickness(22, 18, 22, 18) };
            sp.Children.Add(Ui.T("Hallow VPN closed unexpectedly last time", 15, true, null, 6));
            DateTime when = DateTime.Now; try { if (File.Exists(file)) when = File.GetLastWriteTime(file); } catch { }
            sp.Children.Add(Ui.Muted("On " + when.ToString("d MMM yyyy, HH:mm") + ". A report was saved on this PC - nothing was sent anywhere, and it never contains your Nord credentials.", 12.5, 14));
            var row = new WrapPanel();
            row.Children.Add(Ui.Btn("Show details", () => { try { System.Diagnostics.Process.Start("notepad.exe", "\"" + file + "\""); } catch { } }));
            row.Children.Add(Ui.Btn("Open folder", () => { try { System.Diagnostics.Process.Start("explorer.exe", "/select,\"" + file + "\""); } catch { } }));
            var ok = Ui.Btn("OK", () => w.Close(), true); ok.MinWidth = 70; row.Children.Add(ok);
            sp.Children.Add(row); w.Content = sp;
            return w;
        }
    }
}
