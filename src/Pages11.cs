// Hallow VPN 1.1 pages: First-run setup, Auto-connect, Fix my connection - and on the Connection page the health numbers and the
// leak test card.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace HallowVPN
{
    public partial class MainWindow
    {
        static FrameworkElement StatusIcon(string status, double size = 18)
        {
            string icon = "circle", key = "Faint";
            if (status == "ok") { icon = "circle-check"; key = "Green"; } else if (status == "warn") { icon = "triangle-exclamation"; key = "Amber"; } else if (status == "fail") { icon = "circle-xmark"; key = "Red"; } else if (status == "busy") { icon = "arrows-rotate"; key = "AccentBrush"; }
            var i = Icons.Make(icon, size, Ui.R(key)); i.VerticalAlignment = VerticalAlignment.Top; i.Margin = new Thickness(0, 2, 14, 0); return i;
        }
        // one numbered step / check: icon, title, text, then whatever goes under it
        static Border Step(string status, string title, string text, params UIElement[] below)
        {
            var d = new DockPanel();
            var ic = StatusIcon(status, 20); DockPanel.SetDock(ic, Dock.Left); d.Children.Add(ic);
            var sp = new StackPanel();
            sp.Children.Add(Ui.T(title, 14.5, true, null, 3));
            if (text != null) sp.Children.Add(Ui.T(text, 12.5, false, Ui.R("TextSoft"), below.Length > 0 ? 10 : 0));
            foreach (var b in below) sp.Children.Add(b);
            d.Children.Add(sp);
            var card = Ui.Card(d, 16); card.Margin = new Thickness(0, 0, 0, 10);
            if (status == "fail") card.BorderBrush = Ui.R("Red"); else if (status == "ok") card.BorderBrush = Ui.R("LineDim");
            return card;
        }

        // the certificate: download on click, check, show the fingerprint, then Windows asks (UAC) - the user's click
        void InstallCa()
        {
            Say("Getting NordVPN's root certificate from downloads.nordcdn.com...");
            c.CaFetchAsync((err, thumb) =>
            {
                if (err != null) { Say(err); Dialogs.Info(this, "Not installed", err); return; }
                if (!Dialogs.Confirm(this, "Install NordVPN's root certificate?", "Subject: NordVPN Root CA (self-signed, a certificate authority)\nSHA-1 fingerprint: " + thumb + "\n\nIt goes into this computer's Trusted Root store so Windows accepts Nord's IKEv2 servers. Windows will ask for permission (UAC). You can remove it on the Account page any time.", "Install (asks Windows)", false)) return;
                c.CaChangeAsync(true, e2 => Say(e2 ?? "NordVPN's root certificate is installed."));
            });
        }

        // ================= First-run setup
        FrameworkElement SetupPage()
        {
            var sp = PageBody("First-run setup", "Three steps to your first connection. Each gets a green tick when it's done.");
            // 1 - credentials
            bool acc = c.HasAccount;
            if (acc) sp.Children.Add(Step("ok", "1  Service credentials", "Saved, encrypted for your Windows user (Windows DPAPI). You can replace them on the Account page."));
            else
            {
                var user = Ui.Field(""); user.Width = 380; user.HorizontalAlignment = HorizontalAlignment.Left;
                var pass = new PasswordBox { Width = 380, HorizontalAlignment = HorizontalAlignment.Left, Background = Ui.R("Well"), Foreground = Ui.R("Text"), BorderBrush = Ui.R("Line"), CaretBrush = Ui.R("AccentBrush"), Padding = new Thickness(8, 6, 8, 6), FontFamily = Ui.Mono, FontSize = 12.5 };
                var save = Ui.Btn("Save", () =>
                {
                    var e = Account.Save(user.Text, pass.Password); pass.Clear();
                    if (e != null) { Say(e); Dialogs.Info(this, "Not saved", e); return; }
                    user.Text = ""; c.WriteStatus(); BuildPage(); Say("Saved, encrypted for your Windows user.");
                }, true);
                save.Margin = new Thickness(0, 12, 0, 0); save.HorizontalAlignment = HorizontalAlignment.Left;
                sp.Children.Add(Step("fail", "1  Service credentials",
                    "Not your Nord login. On nordaccount.com go to NordVPN > \"Set up NordVPN manually\" (Nord may email you a code), then copy the two Service credentials: a username and a password made of letters and numbers.",
                    Ui.Muted("Service username", 12, 4), user, new Border { Height = 8 }, Ui.Muted("Service password", 12, 4), pass, save));
            }
            // 2 - certificate
            if (!c.CaChecked) sp.Children.Add(Step("busy", "2  NordVPN's root certificate", "Checking…"));
            else if (c.CaInstalled) sp.Children.Add(Step("ok", "2  NordVPN's root certificate", "Installed - Windows trusts Nord's servers."));
            else
            {
                var b = Ui.Btn("Install Nord's certificate (asks Windows)", InstallCa, true); b.HorizontalAlignment = HorizontalAlignment.Left;
                var again = Ui.Btn("Check again", () => c.CheckAdminThings());
                sp.Children.Add(Step("fail", "2  NordVPN's root certificate",
                    "Missing. Without it Windows refuses Nord's servers with error 13801. Click Install: Hallow VPN downloads it from Nord, checks it and shows its fingerprint, then Windows asks for permission (UAC) - that click is yours. It's the only step that needs it.",
                    Wrap(b, again)));
            }
            // 3 - test
            bool ready = acc && c.CaInstalled;
            string st = c.SetupTest, status, text; var under = new List<UIElement>();
            if (st == "pass" || (c.Phase == Phase.On && st == null && c.S.SetupDone))
            {
                status = "ok"; text = "Passed: connected to " + (c.Current != null ? c.Current.Name + " (" + c.Current.Host + ")" : "Nord") + ". Your traffic now goes through Nord. You're set up - Hallow VPN won't show this screen by itself any more.";
                if (c.Phase == Phase.On) under.Add(Wrap(Go("connect", "Open the Connection page"), Ui.Danger("Disconnect", () => c.Disconnect(true))));
            }
            else if (st == "running" || c.Phase == Phase.Connecting) { status = "busy"; text = "Testing: picking the fastest server, then Windows dials it…"; }
            else if (st == "fail") { status = "fail"; text = "Failed: " + (c.LastError ?? "it didn't connect.") + " \"Fix my connection\" checks everything in order."; under.Add(Wrap(Ui.Btn("Test again", () => Act(c.StartSetupTest()), true), Go("fix", "Fix my connection"))); }
            else
            {
                status = ready ? "pending" : "pending";
                text = ready ? "Connects to the fastest Nord server and says whether it worked." : "Do steps 1 and 2 first.";
                var t = Ui.Btn("Test connection to Fastest", () => Act(c.StartSetupTest()), true); t.IsEnabled = ready && c.Home.Current == null; t.HorizontalAlignment = HorizontalAlignment.Left; under.Add(t);
            }
            sp.Children.Add(Step(status, "3  Test connection", text, under.ToArray()));
            sp.Children.Add(Ui.Muted("Everything but step 2 works without admin. Nothing is sent anywhere except to Nord.", 11.5));
            return Scroll(sp);
        }

        // ================= Auto-connect
        FrameworkElement AutoPage()
        {
            var sp = PageBody("Auto-connect", "When Hallow VPN connects by itself. It never pops anything up during a game.");
            sp.Children.Add(Section("When I sign in"));
            sp.Children.Add(Ui.Switch("Start Hallow VPN with Windows (in the tray, for your Windows user - no admin)", c.StartsWithWindows(), on => { c.SetStartWithWindows(on); BuildPage(); }));
            sp.Children.Add(Ui.Switch("Connect when Hallow VPN starts", c.S.ConnectOnStart, on => { c.S.ConnectOnStart = on; c.S.Save(); }));
            sp.Children.Add(Ui.Muted("Both on = connected at sign-in. If a game is running and your game mode pauses the VPN, it waits.", 12, 4));

            sp.Children.Add(Section("Untrusted Wi-Fi"));
            sp.Children.Add(Ui.Switch("Connect by itself on Wi-Fi that isn't trusted", c.S.AutoConnectUntrusted, on => { c.S.AutoConnectUntrusted = on; c.S.Save(); c.WriteStatus(); BuildPage(); }));
            sp.Children.Add(Ui.Switch("Wired connections are trusted", c.S.TrustWired, on => { c.S.TrustWired = on; c.S.Save(); c.TrustChanged(); }));
            sp.Children.Add(Ui.Muted("Open Wi-Fi (no password) is always untrusted. If you disconnect by hand, Hallow VPN leaves it off on that network until you change networks. With \"Disconnect while I play\", it waits for the game to close.", 12, 8));

            var n = c.NetCurrent; var card = new StackPanel();
            if (n == null) card.Children.Add(Ui.Muted("Looking at the network…", 12.5));
            else if (n.Kind == "none") card.Children.Add(Ui.T("No network right now", 14, true));
            else
            {
                var row = new StackPanel { Orientation = Orientation.Horizontal };
                var ic = Icons.Make(n.Kind == "wifi" ? "wifi" : "globe", 16, Ui.R(c.NetUntrusted ? "Amber" : "Green")); ic.Margin = new Thickness(0, 0, 10, 0); row.Children.Add(ic);
                row.Children.Add(Ui.T(n.Kind == "wifi" ? n.Ssid : "Wired connection", 15, true));
                var pill = Ui.Pill(c.NetUntrusted ? "untrusted" : "trusted", c.NetUntrusted ? "Amber" : "Green", c.NetUntrusted ? "AmberBg" : "GreenBg"); pill.Margin = new Thickness(10, 0, 0, 0); row.Children.Add(pill);
                if (n.Kind == "wifi" && n.Open) { var o = Ui.Pill("open - no password", "Red", "RedBg"); o.Margin = new Thickness(6, 0, 0, 0); row.Children.Add(o); }
                card.Children.Add(row);
                card.Children.Add(Ui.Muted(n.Kind == "wifi" ? "The network you're on now (its name only - Hallow VPN never reads Wi-Fi keys or passwords)." : "You're on a wired connection.", 12, 8));
                if (n.Kind == "wifi" && !n.Open)
                {
                    bool trusted = c.S.TrustedNetworks.Contains(n.Ssid); var ss = n.Ssid;
                    card.Children.Add(Wrap(trusted ? Ui.Btn("Don't trust this network", () => c.TrustNetwork(ss, false)) : Ui.Btn("Trust this network", () => { var e = c.TrustNetwork(ss, true); if (e != null) Say(e); }, true)));
                }
            }
            sp.Children.Add(Ui.Card(card, 16));
            sp.Children.Add(Section("Trusted Wi-Fi networks"));
            if (c.S.TrustedNetworks.Count == 0) sp.Children.Add(Ui.Muted("None yet. Join your home Wi-Fi and click \"Trust this network\".", 12.5));
            foreach (var t in c.S.TrustedNetworks)
            {
                var tt = t; var d = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
                var rm = Ui.Btn("Remove", () => c.TrustNetwork(tt, false)); rm.Margin = new Thickness(0); DockPanel.SetDock(rm, Dock.Right); d.Children.Add(rm);
                d.Children.Add(Ui.Row(Pad(Icons.Make("wifi", 13, Ui.R("Muted"))), Pad(Ui.T("   " + t, 13))));
                var b = Ui.Card(d, 10); b.Margin = new Thickness(0, 0, 0, 6); sp.Children.Add(b);
            }
            return Scroll(sp);
        }

        // ================= Fix my connection
        FrameworkElement FixPage()
        {
            var sp = PageBody("Fix my connection", "Checks, in order, everything a first connection needs. Read-only - the only changes are the repair buttons, and none of them needs admin.");
            var run = Ui.Btn(c.DiagRunning ? "Checking…" : c.Diag == null ? "Run the checks" : "Check again", () => c.RunDiagnosis(null), true); run.IsEnabled = !c.DiagRunning;
            sp.Children.Add(Wrap(run));
            sp.Children.Add(new Border { Height = 12 });
            if (c.Diag == null) { if (!c.DiagRunning && !Snap.On) c.RunDiagnosis(null); sp.Children.Add(Ui.Muted("Checking…", 12.5)); return Scroll(sp); }
            int n = 0;
            foreach (var s in c.Diag)
            {
                n++; var below = new List<UIElement>();
                if (s.Id == "account" && s.Status == "fail") below.Add(Wrap(Go("setup", "Open the setup")));
                if (s.Id == "certificate" && s.Status == "fail") { var b = Ui.Btn("Install Nord's certificate (asks Windows)", InstallCa, true); below.Add(Wrap(b)); }
                if (s.Id == "blockers" && s.Status == "fail") below.Add(Wrap(Go("home", "Open Home")));
                if (s.Repair == "profile") { var b = Ui.Btn("Recreate the VPN profile", () => { Say("Setting up the \"Hallow VPN\" profile again…"); c.RepairAsync("profile", e => Say(e ?? "The profile is set up again, with Nord's DNS.")); }); b.IsEnabled = c.Phase == Phase.Off; below.Add(Wrap(b)); }
                if (s.Repair == "servers") below.Add(Wrap(Ui.Btn("Refresh the server list", () => { Say("Asking Nord for servers…"); c.RepairAsync("servers", e => Say(e ?? "Server list refreshed.")); })));
                sp.Children.Add(Step(s.Status, n + "  " + s.Title, s.Text, below.ToArray()));
            }
            if (c.DiagAt != null) sp.Children.Add(Ui.Muted("Checked " + Fmt.Ago(c.DiagAt.Value, DateTime.Now) + ". Repairs that need admin aren't offered here: the certificate and the kill switch always ask Windows (UAC) on their own pages.", 11.5));
            return Scroll(sp);
        }

        // ================= Connection page: health and the leak test
        TextBlock hTraffic, hPing, hSlowText; Border hSlow;
        FrameworkElement HealthRows()
        {
            var sp = new StackPanel();
            hTraffic = new TextBlock { FontFamily = Ui.Mono, FontSize = 12.5 };
            sp.Children.Add(KV("Data", hTraffic));
            hPing = new TextBlock { FontFamily = Ui.Mono, FontSize = 12.5 };
            sp.Children.Add(KV("Live ping", hPing));
            hSlowText = Ui.T("", 12.5, false, Ui.R("Text"));
            var sw = Ui.Btn("Switch to the fastest", () => Act(c.Connect(new Target { Kind = "fastest" }, true))); sw.Margin = new Thickness(12, 0, 0, 0);
            var d = new DockPanel(); DockPanel.SetDock(sw, Dock.Right); d.Children.Add(sw); hSlowText.VerticalAlignment = VerticalAlignment.Center; d.Children.Add(hSlowText);
            hSlow = new Border { Background = Ui.R("AmberBg"), BorderBrush = Ui.R("Amber"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(2), Padding = new Thickness(10, 6, 8, 6), Margin = new Thickness(0, 8, 0, 0), Child = d };
            sp.Children.Add(hSlow);
            UpdateHealth();
            return sp;
        }
        void UpdateHealth()
        {
            if (hTraffic == null) return;
            hTraffic.Text = c.HSent < 0 ? "-" : "↑ " + Diagnosis.Bytes(c.HSent) + "   ↓ " + Diagnosis.Bytes(c.HRecv);
            hPing.Text = c.LivePing < 0 ? "-" : c.LivePing + " ms";
            hPing.Foreground = c.LivePing < 0 ? Ui.R("Muted") : c.LivePing < 50 ? Ui.R("Green") : c.LivePing <= 150 ? Ui.R("Amber") : Ui.R("Red");
            bool slow = c.SlowServer;
            hSlow.Visibility = slow ? Visibility.Visible : Visibility.Collapsed;
            if (slow) hSlowText.Text = "Slow server? " + (c.LivePing > 150 ? "The ping is " + c.LivePing + " ms" : "It's " + c.Current.Load + "% busy") + " - another server may be quicker.";
        }
        void LeakCard(StackPanel sp)
        {
            sp.Children.Add(Section("Leak test"));
            var card = new StackPanel();
            var st = c.LeakState;
            var head = new StackPanel { Orientation = Orientation.Horizontal };
            head.Children.Add(StatusIcon(st ?? "busy", 18));
            head.Children.Add(Ui.T(st == null ? "Checking…" : st == "ok" ? "No leaks found" : st == "warn" ? "Mostly protected - see below" : "Leaking - your traffic isn't protected", 15, true));
            card.Children.Add(head);
            var g = new StackPanel { Margin = new Thickness(32, 6, 0, 0) };
            g.Children.Add(KV("VPN address", c.PublicIp ?? "-", c.IpProtected == true ? "Nord says: protected" : c.IpProtected == false ? "Nord says: NOT protected" : "Nord's IP check"));
            if (c.LeakDns != null) g.Children.Add(KV("DNS", c.LeakDns.Vpn.Count > 0 ? string.Join(", ", c.LeakDns.Vpn) : "-", c.LeakDns.VpnFirst && c.LeakDns.AllVpnDnsThroughVpn ? "through the VPN, asked first" : "not asked first"));
            var real = c.RealIpLoading ? (FrameworkElement)Ui.Muted("asking…", 12.5) : c.RealIp != null ? (FrameworkElement)Ui.T(c.RealIp + (c.RealIpIsp != null ? "   " + c.RealIpIsp : ""), 12.5) : c.RealIpError != null ? (FrameworkElement)Ui.Muted(c.RealIpError, 12) : null;
            if (real == null)
            {
                var b = Ui.Btn("Show my real IP", () => c.FetchRealIp()); b.HorizontalAlignment = HorizontalAlignment.Left; b.Margin = new Thickness(0);
                b.ToolTip = "Asks Nord's IP check once through your normal connection (outside the VPN), so you can compare. Not saved anywhere.";
                real = b;
            }
            g.Children.Add(KV("Your real IP", real));
            if (real is Button) g.Children.Add(Ui.Muted("Only when you click: that one question goes out through your normal connection, on purpose, so you can compare. It isn't saved or shared.", 11.5));
            card.Children.Add(g);
            if (c.LeakNotes.Count > 0) { var b = Bullets(c.LeakNotes); ((FrameworkElement)b).Margin = new Thickness(32, 8, 0, 0); card.Children.Add(b); }
            var foot = Ui.Muted((c.LeakAt != null ? "Checked " + Fmt.Ago(c.LeakAt.Value, c.Now()) + ". " : "") + "Uses Nord's IP check and Windows' own DNS settings - no third-party test sites. WebRTC is a browser thing: Hallow VPN can't test it, but with \"Everything through the VPN\" the addresses a browser could reveal are the VPN's.", 11.5);
            foot.Margin = new Thickness(32, 8, 0, 0); card.Children.Add(foot);
            var again = Ui.Btn("Run the leak test again", () => { c.FetchIp(); }); again.Margin = new Thickness(32, 10, 0, 0); again.HorizontalAlignment = HorizontalAlignment.Left;
            card.Children.Add(again);
            var cb = Ui.Card(card, 16); sp.Children.Add(cb);
        }
    }
}
