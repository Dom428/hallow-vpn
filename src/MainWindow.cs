// Hallow VPN main window: the page list on the left (Connection, Servers, Split tunnel, Gaming, Kill switch, Home, Account,
// Settings), the open page, and a status line. Pages are rebuilt when the Controller says something changed.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;

namespace HallowVPN
{
    public partial class MainWindow : Window
    {
        readonly Controller c;
        string page = "connect";
        readonly Dictionary<string, Button> nav = new Dictionary<string, Button>();
        Border content; TextBlock sayText, meter; Button updateBtn; DispatcherTimer sayTimer, rebuildTimer;
        public bool Exiting;
        public static readonly string[][] Pages = {
            new[] { "connect", "Connection", "power-off" }, new[] { "setup", "First-run setup", "list-check" }, new[] { "servers", "Servers", "globe" }, new[] { "split", "Split tunnel", "code-branch" },
            new[] { "gaming", "Gaming", "gamepad" }, new[] { "auto", "Auto-connect", "wifi" }, new[] { "killswitch", "Kill switch", "shield-halved" }, new[] { "home", "Home (Tailscale)", "house" },
            new[] { "account", "Account", "key" }, new[] { "nordlynx", "NordLynx", "bolt" }, new[] { "fix", "Fix my connection", "stethoscope" }, new[] { "settings", "Settings", "gear" } };
        public static string PageId(string s)
        {
            var v = (s ?? "").Trim().ToLowerInvariant().Replace(" ", "");
            if (v == "connection" || v == "status") v = "connect"; if (v == "kill" || v == "firewall") v = "killswitch"; if (v == "tailscale") v = "home"; if (v == "wireguard" || v == "protocol" || v == "protocols") v = "nordlynx"; if (v == "game" || v == "games") v = "gaming";
            if (v == "splittunnel" || v == "splittunneling" || v == "splittunnelling") v = "split";
            if (v == "firstrun" || v == "first-runsetup" || v == "wizard" || v == "start") v = "setup";
            if (v == "fixmyconnection" || v == "diagnose" || v == "diagnosis" || v == "checks") v = "fix";
            if (v == "autoconnect" || v == "auto-connect" || v == "wifi" || v == "networks") v = "auto";
            return Pages.Any(p => p[0] == v) ? v : null;
        }

        public MainWindow(Controller controller)
        {
            c = controller;
            Ui.Dress(this, "Hallow VPN", 1180);
            SizeToContent = SizeToContent.Manual; Height = 760; MinWidth = 960; MinHeight = 600;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Rebuild();
            Action changed = () => Dispatcher.BeginInvoke(new Action(SoonRebuild));
            c.Changed += changed;
            Action health = () => Dispatcher.BeginInvoke(new Action(UpdateHealth));
            c.HealthChanged += health;
            healthTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };   // never faster; idle while minimised or on another page
            healthTimer.Tick += (s, e) => { if (page == "connect" && IsVisible && WindowState != WindowState.Minimized && c.Phase == Phase.On) c.PollHealth(); };
            if (!Snap.On) healthTimer.Start();
            Closed += (s, e) => { c.Changed -= changed; c.HealthChanged -= health; healthTimer.Stop(); c.MainClosed(); };
        }
        DispatcherTimer healthTimer;
        // a burst of changes = one rebuild (never while typing in a field of the open page)
        void SoonRebuild()
        {
            if (Snap.On) { BuildPage(); return; }
            if (rebuildTimer == null) { rebuildTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) }; rebuildTimer.Tick += (s, e) => { rebuildTimer.Stop(); if (!Typing()) BuildPage(); else UpdateMeter(); }; }
            rebuildTimer.Stop(); rebuildTimer.Start();
        }
        bool Typing() { var f = System.Windows.Input.Keyboard.FocusedElement as DependencyObject; return (f is TextBox || f is PasswordBox) && IsKeyboardFocusWithin && (page == "split" || page == "account" || page == "gaming" || page == "servers" || page == "setup" || page == "auto"); }

        public void Rebuild()
        {
            Ui.Restyle(this); Ui.TitleBar(this);
            var root = new Grid();
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(212) });
            root.ColumnDefinitions.Add(new ColumnDefinition());
            var side = new DockPanel { Background = Ui.R("Side"), LastChildFill = false };
            var head = new StackPanel { Margin = new Thickness(16, 18, 16, 18) };
            head.Children.Add(Ui.Header("VPN", 18));
            head.Children.Add(Ui.Muted("NordVPN, the Hallow way", 11.5, 0));
            DockPanel.SetDock(head, Dock.Top); side.Children.Add(head);
            nav.Clear();
            foreach (var p in Pages)
            {
                var id = p[0];
                if (id == "setup" && c.S.SetupDone && page != "setup") continue;
                var b = new Button { Style = Ui.S("Nav"), Content = Icons.WithText(p[2], p[1], 14, Ui.R("Text")), Margin = new Thickness(8, 0, 8, 0) };
                b.Click += (s, e) => Show(id);
                DockPanel.SetDock(b, Dock.Top); side.Children.Add(b); nav[id] = b;
            }
            var foot = new StackPanel { Margin = new Thickness(16, 0, 16, 14) };
            updateBtn = Ui.Btn("Update", () => c.InstallUpdate(), true); updateBtn.Visibility = c.PendingUpdate == null ? Visibility.Collapsed : Visibility.Visible; updateBtn.HorizontalAlignment = HorizontalAlignment.Left; updateBtn.Margin = new Thickness(0, 0, 0, 10);
            if (c.PendingUpdate != null) updateBtn.Content = "Update to v" + c.PendingUpdate.Version.ToString(2);
            foot.Children.Add(updateBtn);
            foot.Children.Add(Ui.Muted("v" + Program.Version + " · IKEv2 + NordLynx", 11, 0));
            DockPanel.SetDock(foot, Dock.Bottom); side.Children.Add(foot);
            root.Children.Add(side);
            root.Children.Add(new Border { Width = 1, Background = Ui.R("Line"), HorizontalAlignment = HorizontalAlignment.Right });

            var right = new DockPanel(); Grid.SetColumn(right, 1);
            var bar = new DockPanel { Background = Ui.R("Side"), Height = 28 };
            meter = new TextBlock { FontFamily = Ui.Mono, FontSize = 11, Foreground = Ui.R("Muted"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 14, 0) };
            DockPanel.SetDock(meter, Dock.Right); bar.Children.Add(meter);
            sayText = new TextBlock { FontSize = 12, Foreground = Ui.R("TextSoft"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis };
            bar.Children.Add(sayText);
            var barWrap = new Border { BorderBrush = Ui.R("Line"), BorderThickness = new Thickness(0, 1, 0, 0), Child = bar };
            DockPanel.SetDock(barWrap, Dock.Bottom); right.Children.Add(barWrap);
            content = new Border(); right.Children.Add(content);
            root.Children.Add(right);
            Content = root;
            Show(page);
        }
        public void ShowUpdate(string label) { if (updateBtn == null) return; updateBtn.Content = label; updateBtn.Visibility = Visibility.Visible; }
        public void Say(string text)
        {
            if (sayText == null) return; sayText.Text = text ?? "";
            if (sayTimer != null) sayTimer.Stop();
            sayTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(12) }; sayTimer.Tick += (s, e) => { sayTimer.Stop(); sayText.Text = ""; }; sayTimer.Start();
        }
        public void Show(string id)
        {
            page = PageId(id) ?? "connect";
            foreach (var kv in nav) kv.Value.Tag = kv.Key == page ? "on" : null;
            if (page == "home" && !Snap.On) c.CheckHome();
            if (page == "killswitch" && !Snap.On) c.CheckAdminThings();
            BuildPage();
        }
        void BuildPage()
        {
            clock = null;
            FrameworkElement el;
            switch (page)
            {
                case "servers": el = ServersPage(); break;
                case "split": el = SplitPage(); break;
                case "gaming": el = GamingPage(); break;
                case "killswitch": el = KillSwitchPage(); break;
                case "home": el = HomePage(); break;
                case "account": el = AccountPage(); break;
                case "settings": el = SettingsPage(); break;
                case "setup": el = SetupPage(); break;
                case "auto": el = AutoPage(); break;
                case "fix": el = FixPage(); break;
                case "nordlynx": el = NordLynxPage(); break;
                default: el = ConnectPage(); break;
            }
            content.Child = el;
            UpdateMeter();
        }
        void UpdateMeter()
        {
            if (meter == null) return;
            var state = c.Phase == Phase.On ? "CONNECTED" + (c.Current != null ? " · " + c.Current.Short : "") : c.Phase == Phase.Connecting ? "CONNECTING" : c.Phase == Phase.Disconnecting ? "DISCONNECTING" : c.PausedForGame ? "PAUSED FOR GAME" : "NOT CONNECTED";
            meter.Text = state + "   " + (c.Phase == Phase.On ? c.Proto : c.ProtoFor(false)) + "   kill switch " + (c.KillSwitchOn ? "on" : "off") + "   " + Split.Label(c.S.SplitMode).ToLowerInvariant();
        }
        TextBlock clock;
        public void TickClock() { if (clock != null && c.Since != null) clock.Text = Fmt.Span(c.Now() - c.Since.Value); }

        // ---- page building blocks
        static ScrollViewer Scroll(UIElement child)
        {
            UIElement inner = child;
            if (child is StackPanel && ((StackPanel)child).Tag as string == "page")
            {   // the page column: as wide as the window allows, at most 900
                var g = new Grid(); g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MaxWidth = 952 }); g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.0001, GridUnitType.Star) });
                g.Children.Add(child); inner = g;
            }
            return new ScrollViewer { Content = inner, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        }
        static StackPanel PageBody(string title, string sub)
        {
            var sp = new StackPanel { Margin = new Thickness(26, 20, 26, 24), Tag = "page" };
            sp.Children.Add(Ui.T(title, 22, true, null, 2));
            if (sub != null) sp.Children.Add(Ui.Muted(sub, 12.5, 16));
            return sp;
        }
        static TextBlock Section(string text) { var t = Ui.Caps(text, 11); t.Margin = new Thickness(0, 14, 0, 8); return t; }
        static FrameworkElement KV(string k, FrameworkElement v)
        {
            var g = new Grid { Margin = new Thickness(0, 3, 0, 3) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) }); g.ColumnDefinitions.Add(new ColumnDefinition());
            g.Children.Add(Ui.Muted(k, 12.5)); Grid.SetColumn(v, 1); g.Children.Add(v); return g;
        }
        static FrameworkElement KV(string k, string v, string note = null)
        {
            var t = new TextBlock { TextWrapping = TextWrapping.Wrap };
            t.Inlines.Add(new System.Windows.Documents.Run(v) { FontFamily = Ui.Mono, FontSize = 12.5 });
            if (note != null) t.Inlines.Add(new System.Windows.Documents.Run("   " + note) { FontSize = 11.5, Foreground = Ui.R("Muted") });
            return KV(k, t);
        }
        static UIElement Bullets(IEnumerable<string> lines)
        {
            var sp = new StackPanel();
            foreach (var l in lines) { var d = new DockPanel { Margin = new Thickness(0, 2, 0, 2) }; var dot = Ui.Muted("•", 12.5); dot.Width = 14; d.Children.Add(dot); d.Children.Add(Ui.T(l, 12.5, false, Ui.R("TextSoft"))); sp.Children.Add(d); }
            return sp;
        }
        ToggleButton Chip(string label, bool on, Action click)
        {
            var t = new ToggleButton { Content = label, Style = Ui.S("Chip"), Margin = new Thickness(0, 0, 6, 6), IsChecked = on };
            t.Click += (s, e) => click(); return t;
        }
        // a choice card: a chip-like button with a title and a muted line
        Border Choice(string title, string text, bool on, Action pick)
        {
            var sp = new StackPanel();
            var top = new StackPanel { Orientation = Orientation.Horizontal };
            top.Children.Add(Icons.Make(on ? "circle-check" : "circle", 14, on ? Ui.R("AccentBrush") : Ui.R("Faint")));
            var tt = Ui.T(title, 13.5, true); tt.Margin = new Thickness(10, 0, 0, 0); top.Children.Add(tt);
            sp.Children.Add(top);
            var m = Ui.Muted(text, 12); m.Margin = new Thickness(24, 3, 0, 0); sp.Children.Add(m);
            var b = new Border { Child = sp, Background = Ui.R("Panel"), BorderBrush = on ? Ui.R("AccentBrush") : Ui.R("Line"), BorderThickness = new Thickness(on ? 2 : 1), CornerRadius = new CornerRadius(3), Padding = on ? new Thickness(13, 9, 13, 9) : new Thickness(14, 10, 14, 10), Margin = new Thickness(0, 0, 0, 8), Cursor = System.Windows.Input.Cursors.Hand };
            if (!on) { b.MouseEnter += (s, e) => b.BorderBrush = Ui.R("LilacDim"); b.MouseLeave += (s, e) => b.BorderBrush = Ui.R("Line"); }
            b.MouseLeftButtonUp += (s, e) => pick();
            return b;
        }        Button Go(string page, string label) { return Ui.Btn(label, () => Show(page)); }
        static WrapPanel Wrap(params UIElement[] items) { var w = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) }; foreach (var i in items) w.Children.Add(i); return w; }

        // ================= Connection
        FrameworkElement ConnectPage()
        {
            var sp = PageBody("Connection", "Your NordVPN subscription through Windows' own IKEv2 client, or NordLynx through the official WireGuard for Windows - no NordVPN app.");
            if (!c.HasAccount) { sp.Children.Add(Ui.Note("Add your Nord SERVICE credentials first. They're on Nord's website (NordVPN > Set up NordVPN manually) and are not your Nord login.", "Amber", "AmberBg")); sp.Children.Add(Wrap(Go("account", "Open Account"))); }
            if (c.CaChecked && !c.CaInstalled) { sp.Children.Add(Ui.Note("Windows doesn't trust Nord's servers yet: NordVPN's root certificate isn't installed. Without it, connecting fails with error 13801. Install it on the Account page (Windows asks for permission).", "Amber", "AmberBg")); }
            if (c.Home.Current != null) sp.Children.Add(Ui.Note("Home mode is on: your traffic goes through " + c.Home.Current.Name + " (Tailscale). Stop it on the Home page to use Nord.", "Lilac", "LilacBg"));
            if (!c.S.SetupDone && c.Phase != Phase.On) { sp.Children.Add(Ui.Note("First time? The setup walks you through it: your service credentials, Nord's certificate, then a test connection.", "Lilac", "LilacBg")); sp.Children.Add(Wrap(Go("setup", "Open the setup"))); sp.Children.Add(new Border { Height = 10 }); }
            // Nord's IKEv2 login (EAP) only works with a login Windows saved itself - one sign-in through Windows' own dialog (1.2.5)
            bool winLogin = c.HasAccount && c.ProtoFor(false) != Controller.ProtoWg && c.WindowsLogin();
            if (c.HasAccount && c.Phase == Phase.Off && c.ProtoFor(false) != Controller.ProtoWg && !winLogin)
            {
                sp.Children.Add(Ui.Note("One-time step: Windows has to remember your Nord login itself (Nord's sign-in type only works that way). Click below - a Windows box opens in front of this window. Paste the same service username and password into it, keep \"Save\" ticked and click Connect. After that Hallow VPN connects, reconnects and auto-connects by itself. Windows keeps it encrypted for your Windows user.", "Amber", "AmberBg"));
                var sign = Ui.Btn("Sign in through Windows (opens a Windows box)", () => { Say("Getting the \"Hallow VPN\" connection ready…"); c.RepairAsync("profile", e => { if (e != null) { Say(e); return; } Say(Ras.WindowsSignIn(WindowsVpn.Phonebook, () => Dispatcher.BeginInvoke(new Action(() => c.Poke()))) ?? "Windows' sign-in box is open in front of this window (if you don't see it, check the taskbar): paste the service username and password, tick Save (Me only), click Connect."); }); }, true);
                sp.Children.Add(Wrap(sign)); sp.Children.Add(new Border { Height = 10 });
            }
            else if (winLogin && c.Phase == Phase.Off) sp.Children.Add(Ui.Muted("Windows has your Nord login saved - Hallow VPN uses it to connect (IKEv2).", 12, 6));   // 1.2.6
            if (c.Ipv6 && !c.KillSwitchOn && c.S.SplitMode != Split.Only) sp.Children.Add(Ui.Note("This PC has IPv6 internet. Nord's IKEv2 carries IPv4 only, so IPv6 traffic can go around the VPN. The kill switch blocks public IPv6 (Kill switch page).", "Amber", "AmberBg"));

            // the status card
            var card = new Grid(); card.ColumnDefinitions.Add(new ColumnDefinition()); card.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var left = new StackPanel();
            string word, sub, color;
            switch (c.Phase)
            {
                case Phase.On: word = "Connected"; color = "Green"; sub = c.Current != null ? (c.Current.Name + (c.Current.City != null ? " · " + c.Current.City : "")) : "Hallow VPN"; break;
                case Phase.Connecting: word = "Connecting…"; color = "Amber"; sub = c.CurrentTarget == null ? "" : TargetLabel(c.CurrentTarget) + " - picking a server, then Windows dials it"; break;
                case Phase.Disconnecting: word = "Disconnecting…"; color = "Amber"; sub = ""; break;
                default:
                    word = c.PausedForGame ? "Paused for your game" : "Not connected"; color = c.PausedForGame ? "Lilac" : "Muted";
                    sub = c.PausedForGame ? (c.Game ?? "A game") + " is running - Hallow VPN reconnects 10 seconds after it closes." : "Your traffic goes out directly, as usual."; break;
            }
            var head = new StackPanel { Orientation = Orientation.Horizontal };
            var dot = new Border { Width = 14, Height = 14, CornerRadius = new CornerRadius(7), Background = Ui.R(color), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
            head.Children.Add(dot);
            if (c.Phase == Phase.On && c.Current != null) { var ct = Ui.CountryTile(c.Current.Code, 22); ct.Margin = new Thickness(0, 0, 10, 0); head.Children.Add(ct); }
            head.Children.Add(new TextBlock { Text = word, FontSize = 26, FontWeight = FontWeights.SemiBold, FontFamily = Ui.Head, VerticalAlignment = VerticalAlignment.Center });
            left.Children.Add(head);
            var subT = Ui.Muted(sub, 13, 12); subT.Margin = new Thickness(26, 2, 0, 12); left.Children.Add(subT);
            if (c.Phase == Phase.On)
            {
                var g = new StackPanel { Margin = new Thickness(26, 0, 0, 0) };
                if (c.Current != null) g.Children.Add(KV("Server", c.Current.Host, c.Current.Country));
                g.Children.Add(c.Proto == Controller.ProtoWg ? KV("Protocol", "NordLynx", "WireGuard for Windows, tunnel " + NordLynx.Tunnel) : KV("Protocol", "IKEv2", "Windows' built-in client (EAP-MSCHAPv2)"));
                clock = new TextBlock { FontFamily = Ui.Mono, FontSize = 12.5, Text = c.Since == null ? "-" : Fmt.Span(c.Now() - c.Since.Value) };
                g.Children.Add(KV("Connected for", clock));
                g.Children.Add(KV("Public IP", c.IpLoading ? "checking…" : c.PublicIp ?? "-", (c.IpProtected == true ? "protected, " : "") + (c.IpCountry != null ? c.IpCountry + " · " : "") + "Nord's IP check (api.nordvpn.com), only while connected"));
                if (c.Current != null && c.Current.Load >= 0) g.Children.Add(KV("Load", Ui.Row(Ui.LoadBar(c.Current.Load, 80), Pad(Ui.Num("  " + c.Current.Load + "%", 12.5)))));
                if (c.Current != null && c.Current.Ping >= 0) g.Children.Add(KV("Ping", Fmt.Ms(c.Current.Ping), "measured by this PC before connecting"));
                g.Children.Add(HealthRows());
                left.Children.Add(g);
            }
            card.Children.Add(left);
            var btns = new StackPanel { VerticalAlignment = VerticalAlignment.Top, MinWidth = 210 };
            Func<Button, Button> full = b => { b.HorizontalAlignment = HorizontalAlignment.Stretch; b.Margin = new Thickness(0, 0, 0, 8); b.Padding = new Thickness(14, 8, 14, 8); return b; };
            bool can = (c.HasAccount || (c.ProtoFor(false) == Controller.ProtoWg && c.NordLynxReady)) && c.Home.Current == null;
            if (c.Phase == Phase.Off)
            {
                var t = Target.Parse(c.S.LastTarget);
                var cb = full(Ui.Btn("Connect", () => Act(c.Connect(t, true)), true)); cb.Content = "Connect · " + TargetLabel(t); cb.IsEnabled = can; btns.Children.Add(cb);
                if (t.Kind != "fastest") { var f = full(Ui.Btn("Fastest server", () => Act(c.Connect(new Target { Kind = "fastest" }, true)))); f.IsEnabled = can; btns.Children.Add(f); }
                var gb = full(Ui.Btn("Best for gaming", () => Act(c.Connect(new Target { Kind = "gaming" }, true)))); gb.IsEnabled = can && t.Kind != "gaming"; btns.Children.Add(gb);
                var pb = full(Ui.Btn("Best for torrents", () => Act(c.Connect(new Target { Kind = "p2p" }, true)))); pb.IsEnabled = can && t.Kind != "p2p"; btns.Children.Add(pb);
                btns.Children.Add(full(Ui.Btn("Pick a server", () => Show("servers"))));
            }
            else if (c.Phase == Phase.Connecting) btns.Children.Add(full(Ui.Btn("Cancel", () => c.Cancel())));
            else if (c.Phase == Phase.On)
            {
                btns.Children.Add(full(Ui.Danger("Disconnect", () => c.Disconnect(true))));
                if (c.CurrentTarget == null || c.CurrentTarget.Kind != "gaming") btns.Children.Add(full(Ui.Btn("Best for gaming", () => Act(c.Connect(new Target { Kind = "gaming" }, true)))));
                btns.Children.Add(full(Ui.Btn("Check my IP again", () => c.FetchIp())));
            }
            Grid.SetColumn(btns, 1); card.Children.Add(btns);
            sp.Children.Add(Ui.Card(card, 20));
            if (c.LastError != null) { var e = Ui.Note(c.LastError, "Red", "RedBg"); e.Margin = new Thickness(0, 10, 0, 0); sp.Children.Add(e); if (c.Phase == Phase.Off) sp.Children.Add(Wrap(Go("fix", "Fix my connection"))); }
            if (c.Phase == Phase.On) LeakCard(sp);

            // favourites
            if (c.S.Favourites.Count > 0)
            {
                sp.Children.Add(Section("Favourites"));
                var w = new WrapPanel();
                foreach (var f in c.S.Favourites)
                {
                    var ff = f;
                    var b = new Button { Style = Ui.S("Btn"), Margin = new Thickness(0, 0, 8, 8), Padding = new Thickness(10, 6, 12, 6), IsEnabled = can && c.Phase != Phase.Connecting && c.Phase != Phase.Disconnecting };
                    var row = new StackPanel { Orientation = Orientation.Horizontal }; var tile = Ui.CountryTile(f.Code, 18); tile.Margin = new Thickness(0, 0, 8, 0); row.Children.Add(tile);
                    row.Children.Add(new TextBlock { Text = f.Name ?? f.Host, VerticalAlignment = VerticalAlignment.Center }); b.Content = row;
                    b.Click += (s, e) => Act(c.Connect(Target.Parse("server:" + ff.Host), true));
                    w.Children.Add(b);
                }
                sp.Children.Add(w);
            }
            // the switches that shape the connection
            sp.Children.Add(Section("How it behaves"));
            var chips = new WrapPanel();
            chips.Children.Add(StatePill("Kill switch " + (c.KillSwitchOn ? "on" : c.KillSwitchPartial ? "partly installed" : "off"), c.KillSwitchOn ? "Green" : c.KillSwitchPartial ? "Amber" : "Muted", () => Show("killswitch")));
            chips.Children.Add(StatePill(Split.Label(c.S.SplitMode), c.S.SplitMode == Split.All ? "Muted" : "Lilac", () => Show("split")));
            var gm = c.EffGame();
            chips.Children.Add(StatePill("When I play: " + (gm == Games.ModePause ? "pause" : gm == Games.ModeLowPing ? "low-ping server" : "nothing"), gm == Games.ModeOff ? "Muted" : "Lilac", () => Show("gaming")));
            chips.Children.Add(StatePill("Auto-reconnect " + (c.S.AutoReconnect ? "on" : "off"), c.S.AutoReconnect ? "Green" : "Muted", () => Show("settings")));
            if (c.Game != null) chips.Children.Add(StatePill("Playing: " + c.Game, "AccentBrush", () => Show("gaming")));
            sp.Children.Add(chips);
            return Scroll(sp);
        }
        static FrameworkElement Pad(FrameworkElement e) { e.VerticalAlignment = VerticalAlignment.Center; return e; }
        Button StatePill(string text, string key, Action click)
        {
            var b = new Button { Style = Ui.S("Btn"), Margin = new Thickness(0, 0, 8, 8), Padding = new Thickness(10, 5, 12, 5) };
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(new Border { Width = 8, Height = 8, CornerRadius = new CornerRadius(4), Background = Ui.R(key), Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center });
            row.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center }); b.Content = row; b.Click += (s, e) => click(); return b;
        }
        string TargetLabel(Target t)
        {
            if (t == null) return "fastest";
            switch (t.Kind)
            {
                case "gaming": return "best for gaming";
                case "p2p": return "best for torrents";
                case "country": { var co = c.Book.Countries.FirstOrDefault(x => x.Code == t.Value); return "fastest in " + (co != null ? co.Name : t.Value); }
                case "server": { var f = c.S.Favourites.FirstOrDefault(x => x.Host == t.Value); return f != null && f.Name != null ? f.Name : t.Value.Split('.')[0]; }
            }
            return "fastest";
        }
        void Act(string err) { if (err != null) Say(err); }

        // ================= Servers
        string sel = "fastest"; string countryFilter = ""; List<Server> shown; string shownKey, listError; bool loading; DateTime? listFetched;
        FrameworkElement ServersPage()
        {
            var grid = new Grid { Margin = new Thickness(18, 16, 18, 12) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(250) }); grid.ColumnDefinitions.Add(new ColumnDefinition());
            // left: the picks and the countries
            var left = new DockPanel { Margin = new Thickness(0, 0, 14, 0) };
            var search = Ui.Field(countryFilter); search.ToolTip = "Find a country";
            var sbox = new Grid { Margin = new Thickness(0, 0, 0, 8) }; sbox.Children.Add(search);
            var hint = Ui.Muted("Find a country", 12); hint.Margin = new Thickness(11, 0, 0, 0); hint.VerticalAlignment = VerticalAlignment.Center; hint.IsHitTestVisible = false; hint.Visibility = countryFilter.Length == 0 ? Visibility.Visible : Visibility.Collapsed; sbox.Children.Add(hint);
            DockPanel.SetDock(sbox, Dock.Top); left.Children.Add(sbox);
            var picks = new StackPanel();
            Action<string, string, string> pick = (id, label, icon) =>
            {
                var b = new Button { Style = Ui.S("Nav"), Content = Icons.WithText(icon, label, 13, Ui.R("Text")), Tag = sel == id ? "on" : null, Padding = new Thickness(12, 7, 12, 7) };
                b.Click += (s, e) => { sel = id; BuildPage(); }; picks.Children.Add(b);
            };
            pick("fastest", "Fastest (recommended)", "bolt"); pick("gaming", "Best for gaming", "gamepad"); pick("p2p", "Best for torrents (P2P)", "share-nodes"); pick("favourites", "Favourites (" + c.S.Favourites.Count + ")", "star");
            DockPanel.SetDock(picks, Dock.Top); left.Children.Add(picks);
            var cl = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };
            Action fillCountries = () =>
            {
                cl.Children.Clear();
                foreach (var co in c.Book.Countries.Where(x => countryFilter.Length == 0 || x.Name.IndexOf(countryFilter, StringComparison.OrdinalIgnoreCase) >= 0 || string.Equals(x.Code, countryFilter, StringComparison.OrdinalIgnoreCase)))
                {
                    var id = "country:" + co.Code; var row = new StackPanel { Orientation = Orientation.Horizontal };
                    var tile = Ui.CountryTile(co.Code, 16); tile.Margin = new Thickness(0, 0, 8, 0); row.Children.Add(tile); row.Children.Add(new TextBlock { Text = co.Name, VerticalAlignment = VerticalAlignment.Center });
                    var b = new Button { Style = Ui.S("Nav"), Content = row, Tag = sel == id ? "on" : null, Padding = new Thickness(12, 5, 12, 5) };
                    b.Click += (s, e) => { sel = id; BuildPage(); }; cl.Children.Add(b);
                }
                if (cl.Children.Count == 0) cl.Children.Add(Ui.Muted(c.Book.Countries.Count == 0 ? "Loading Nord's country list…" : "No country matches.", 12));
            };
            fillCountries();
            search.TextChanged += (s, e) => { countryFilter = search.Text.Trim(); hint.Visibility = search.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed; fillCountries(); };
            left.Children.Add(Scroll(cl));
            grid.Children.Add(left);
            if (c.Book.Countries.Count == 0) LoadCountries();

            // right: the list
            var right = new DockPanel(); Grid.SetColumn(right, 1);
            var top = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
            var refresh = Ui.Btn("Refresh", () => LoadServers(true)); refresh.Margin = new Thickness(8, 0, 0, 0); refresh.VerticalAlignment = VerticalAlignment.Top; refresh.IsEnabled = sel != "favourites" && !loading;
            DockPanel.SetDock(refresh, Dock.Right); top.Children.Add(refresh);
            string title = sel == "fastest" ? "Fastest" : sel == "gaming" ? "Best for gaming" : sel == "p2p" ? "Best for torrents" : sel == "favourites" ? "Favourites" : "";
            if (sel.StartsWith("country:")) { var co = c.Book.Countries.FirstOrDefault(x => "country:" + x.Code == sel); title = co != null ? co.Name : sel.Substring(8); }
            var tsp = new StackPanel();
            tsp.Children.Add(Ui.T(title, 20, true));
            string about = sel == "fastest" ? "Nord's recommended IKEv2 servers for where you are, ordered by the ping this PC measures plus their load."
                : sel == "gaming" ? "Nearby, quiet servers: under 40% load, then the lowest ping this PC measures. What \"Best for gaming\" connects to."
                : sel == "p2p" ? "Nord's P2P servers (the servers Nord allows torrenting on), ordered by ping plus load. What \"Best for torrents\" and Hallow Torrents' \"connect p2p\" use."
                : sel == "favourites" ? "Servers you starred. Click the star on any server to add it."
                : "Nord's recommended IKEv2 servers in this country, ordered by ping plus load.";
            tsp.Children.Add(Ui.Muted(about, 12));
            if (sel != "favourites") tsp.Children.Add(Ui.Muted((listFetched != null && shownKey == KeyFor(sel) ? "List from " + Fmt.Ago(listFetched.Value, DateTime.Now) + " · " : "") + "cached, refreshed from api.nordvpn.com at most every 3 hours · pings measured by this PC (ICMP, or TCP 443)", 11.5));
            top.Children.Add(tsp);
            DockPanel.SetDock(top, Dock.Top); right.Children.Add(top);
            if (listError != null && shownKey == KeyFor(sel)) { var n = Ui.Note(listError, "Amber", "AmberBg"); DockPanel.SetDock(n, Dock.Top); right.Children.Add(n); }

            if (Snap.On && sel != "favourites" && (shownKey != KeyFor(sel) || shown == null)) LoadServers(false);
            var list = new StackPanel();
            if (sel == "favourites")
            {
                if (c.S.Favourites.Count == 0) list.Children.Add(Ui.Muted("No favourites yet.", 12.5));
                foreach (var f in c.S.Favourites) list.Children.Add(ServerRow(c.Book.Find(f.Host) ?? new Server { Host = f.Host, Name = f.Name, Country = f.Country, Code = f.Code, City = f.City, Load = -1 }, false));
            }
            else if (shownKey != KeyFor(sel) || shown == null) { list.Children.Add(Ui.Muted("Getting servers and measuring pings…", 12.5)); if (!loading) LoadServers(false); }
            else
            {
                var l = sel == "gaming" ? shown.Where(s => s.Load < 40).OrderBy(s => s.Ping >= 0 ? s.Ping : 9999).ThenBy(s => s.Load).ToList() : shown.OrderBy(Nord.FastScore).ToList();
                if (sel == "gaming" && l.Count == 0) l = shown.OrderBy(s => s.Ping >= 0 ? s.Ping : 9999).ToList();
                bool first = true;
                foreach (var s in l) { list.Children.Add(ServerRow(s, first)); first = false; }
                if (l.Count == 0) list.Children.Add(Ui.Muted("Nord sent no servers.", 12.5));
            }
            right.Children.Add(Scroll(list));
            grid.Children.Add(right);
            return grid;
        }
        public void SnapServers(string s) { sel = s; }
        static string KeyFor(string sel) { return sel == "gaming" ? "fastest" : sel; }
        FrameworkElement ServerRow(Server s, bool best)
        {
            var g = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            foreach (var w in new[] { 34.0, 40, -1, 120, 70, 120 }) g.ColumnDefinitions.Add(new ColumnDefinition { Width = w < 0 ? new GridLength(1, GridUnitType.Star) : new GridLength(w) });
            bool fav = c.IsFavourite(s.Host);
            var star = new Button { Style = Ui.S("Head"), Content = Icons.Make("star", 13, fav ? Ui.R("AccentBrush") : Ui.R("Faint")), ToolTip = fav ? "Remove from favourites" : "Add to favourites", VerticalAlignment = VerticalAlignment.Center };
            star.Click += (o, e) => c.ToggleFavourite(s);
            g.Children.Add(star);
            var tile = Ui.CountryTile(s.Code, 18); Grid.SetColumn(tile, 1); g.Children.Add(tile);
            var name = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) };
            // a WrapPanel, not a horizontal StackPanel: on a narrower window the pills move to the next line instead of being cut off
            var nl = new WrapPanel { Orientation = Orientation.Horizontal };
            nl.Children.Add(new TextBlock { Text = s.Name ?? s.Host, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 1, 8, 1), VerticalAlignment = VerticalAlignment.Center });
            if (best) { var p = Ui.Pill(sel == "gaming" ? "best for gaming" : sel == "p2p" ? "best for torrents" : "fastest", "AccentBrush", "AccentGlow"); p.Margin = new Thickness(0, 1, 6, 1); nl.Children.Add(p); }
            if (s.P2P && sel != "p2p") { var p = Ui.Pill("P2P", "Lilac", "LilacBg"); p.Margin = new Thickness(0, 1, 6, 1); nl.Children.Add(p); }
            name.Children.Add(nl);
            name.Children.Add(Ui.Muted(s.Host + (s.City != null ? " · " + s.City : ""), 11.5));
            Grid.SetColumn(name, 2); g.Children.Add(name);
            var load = Ui.Row(Ui.LoadBar(s.Load, 54), Pad(Ui.Num(s.Load >= 0 ? "  " + s.Load + "%" : "  -", 12))); load.VerticalAlignment = VerticalAlignment.Center; Grid.SetColumn(load, 3); g.Children.Add(load);
            var ping = Ui.Num(Fmt.Ms(s.Ping), 12, s.Ping < 0 ? Ui.R("Faint") : s.Ping < 50 ? Ui.R("Green") : s.Ping < 120 ? Ui.R("Amber") : Ui.R("Red")); ping.VerticalAlignment = VerticalAlignment.Center; Grid.SetColumn(ping, 4); g.Children.Add(ping);
            bool can = (c.HasAccount || (c.ProtoFor(false) == Controller.ProtoWg && c.NordLynxReady)) && c.Phase != Phase.Connecting && c.Phase != Phase.Disconnecting && c.Home.Current == null;
            bool here = c.Phase == Phase.On && c.Current != null && c.Current.Host == s.Host;
            var go = Ui.Btn(here ? "Connected" : "Connect", () => Act(c.Connect(Target.Parse("server:" + s.Host), true))); go.IsEnabled = can && !here; go.HorizontalAlignment = HorizontalAlignment.Right; go.Margin = new Thickness(0);
            Grid.SetColumn(go, 5); g.Children.Add(go);
            return new Border { Child = g, Background = Ui.R("Panel"), BorderBrush = here ? Ui.R("Green") : Ui.R("LineDim"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(3), Padding = new Thickness(6, 7, 10, 7), Margin = new Thickness(0, 0, 0, 6) };
        }
        void LoadCountries()
        {
            Action work = () => { string e; c.Book.LoadCountries(false, out e); };
            if (Snap.On) { work(); return; }
            new Thread(() => { work(); Dispatcher.BeginInvoke(new Action(() => { if (page == "servers") BuildPage(); })); }) { IsBackground = true, Priority = ThreadPriority.BelowNormal }.Start();
        }
        void LoadServers(bool force)
        {
            var key = KeyFor(sel); var s = sel; loading = true;
            Action work = () =>
            {
                string err = null; List<Server> l;
                int cid = 0;
                if (key.StartsWith("country:")) { var co = c.Book.FindCountry(key.Substring(8)); cid = co == null ? -1 : co.Id; }
                if (cid < 0) { l = new List<Server>(); err = "Nord's country list doesn't have that country."; }
                else { l = c.Book.List(cid, force, out err, key == "p2p"); c.Book.Measure(l.OrderBy(x => x.Load).ToList(), Snap.On ? 40 : 16); }
                Action fin = () => { loading = false; shown = l; shownKey = key; listError = err; listFetched = c.Book.Fetched(key == "p2p" ? "p2p" : ServerBook.Key(cid)); if (page == "servers" && !Snap.On) BuildPage(); };
                if (Snap.On) fin(); else Dispatcher.BeginInvoke(fin);
            };
            if (Snap.On) work(); else new Thread(() => work()) { IsBackground = true, Priority = ThreadPriority.BelowNormal, Name = "servers" }.Start();
        }

        // ================= Split tunnel
        string splitDraft; string splitMode;
        FrameworkElement SplitPage()
        {
            var sp = PageBody("Split tunnel", "Choose what goes through the VPN.");
            sp.Children.Add(Ui.Note("Honest limit: Windows' built-in VPN client splits by ADDRESS (routes), not by app. Per-app split tunnelling needs a network driver, and Hallow VPN installs none. So: pick addresses / websites, or let Hallow VPN pause the VPN while you game.", "Lilac", "LilacBg"));
            var mode = splitMode ?? c.S.SplitMode;
            Action<string> set = m => { splitMode = m; BuildPage(); };
            sp.Children.Add(Choice("Everything through the VPN", "The usual: all your traffic goes through Nord.", mode == Split.All, () => set(Split.All)));
            sp.Children.Add(Choice("Only these go through the VPN", "Only the addresses, ranges and websites below use Nord; everything else (games included) goes out directly. Websites are looked up each time you connect - their addresses can change.", mode == Split.Only, () => set(Split.Only)));
            sp.Children.Add(Choice("Everything except games", "Everything goes through Nord, but the VPN pauses (disconnects) while a game runs and comes back 10 seconds after it closes. Uses the game list on the Gaming page.", mode == Split.ExceptGames, () => set(Split.ExceptGames)));
            TextBox box = null;
            if (mode == Split.Only)
            {
                sp.Children.Add(Section("Through the VPN"));
                sp.Children.Add(Ui.Muted("One per line: an address (203.0.113.5), a range (203.0.113.0/24, IPv6 too) or a website (example.com). Up to " + Split.MaxRoutes + ".", 12, 6));
                box = new TextBox { Text = splitDraft ?? string.Join("\r\n", c.S.SplitRoutes), AcceptsReturn = true, TextWrapping = TextWrapping.NoWrap, Height = 150, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Background = Ui.R("Well"), Foreground = Ui.R("Text"), BorderBrush = Ui.R("Line"), CaretBrush = Ui.R("AccentBrush"), Padding = new Thickness(8, 6, 8, 6), FontFamily = Ui.Mono, FontSize = 12.5 };
                box.TextChanged += (s, e) => splitDraft = box.Text;
                sp.Children.Add(box);
            }
            var msg = Ui.Muted("", 12); msg.Margin = new Thickness(0, 8, 0, 0);
            var save = Ui.Btn("Save", () =>
            {
                var bad = c.SetSplit(mode, box != null ? box.Text : string.Join("\n", c.S.SplitRoutes));
                splitDraft = null; splitMode = null;
                Say(bad.Count > 0 ? "Saved - these aren't addresses or websites and were left out: " + string.Join(", ", bad.Take(5)) : "Saved. It takes effect the next time you connect.");
            }, true);
            var row = Wrap(save);
            if (c.Phase == Phase.On) row.Children.Add(Ui.Btn("Save and reconnect", () => { c.SetSplit(mode, box != null ? box.Text : string.Join("\n", c.S.SplitRoutes)); splitDraft = null; splitMode = null; Act(c.Connect(c.CurrentTarget ?? Target.Parse(c.S.LastTarget), false)); }));
            sp.Children.Add(new Border { Height = 10 }); sp.Children.Add(row); sp.Children.Add(msg);
            sp.Children.Add(Section("Good to know"));
            sp.Children.Add(Bullets(new[] {
                "It's the Windows VPN profile's own split tunnelling (SplitTunneling + one route per entry) - set up without admin, for your Windows user only.",
                "With \"Only these\", DNS lookups use your normal connection; the traffic to those addresses goes through Nord.",
                "With the kill switch installed, \"Everything except games\" can't disconnect (no VPN = no internet), so it switches to a low-ping server instead." }));
            return Scroll(sp);
        }

        // ================= Gaming
        FrameworkElement GamingPage()
        {
            var sp = PageBody("Gaming", "What Hallow VPN does while you play. It never pops anything up during a game.");
            sp.Children.Add(Ui.Card(new StackPanel { Children = {
                Ui.T(c.Game != null ? "Playing: " + c.Game : "No game running", 15, true, c.Game != null ? Ui.R("AccentBrush") : null, 2),
                Ui.Muted("Watches for Delta Force (DeltaForceClient-Win64-Shipping.exe), Discovery (Discovery-d.exe), the games you add below and the game Hallow Hub says you're playing - a name check every 5 seconds, nothing more.", 12) } }, 16));
            sp.Children.Add(Section("When a game starts"));
            Action<string> set = m => { c.S.GameMode = m; c.S.Save(); BuildPage(); c.WriteStatus(); };
            var gm = c.S.GameMode;
            sp.Children.Add(Choice("Do nothing", "The VPN stays as it is.", gm == Games.ModeOff, () => set(Games.ModeOff)));
            sp.Children.Add(Choice("Disconnect while I play, reconnect after", "Your game goes out directly (lowest ping, no VPN). Hallow VPN reconnects to the same choice 10 seconds after the game closes.", gm == Games.ModePause, () => set(Games.ModePause)));
            sp.Children.Add(Choice("Stay connected on a low-ping server", "Hallow VPN switches to the best server for gaming (nearby, under 40% load, lowest ping) when the game starts, and back to your choice after.", gm == Games.ModeLowPing, () => set(Games.ModeLowPing)));
            var eff = Games.Effective(c.S.SplitMode, gm, c.KillSwitchOn);
            if ((c.Phase != Phase.Off && c.Proto == Controller.ProtoWg) || c.S.Protocol == Protocols.NordLynx) sp.Children.Add(Ui.Note("With NordLynx connected, nothing changes when a game starts: disconnecting or switching NordLynx asks Windows (UAC), and Hallow VPN never pops that up during a game. Use IKEv2 (NordLynx page) if you want it to pause or switch for games.", "Lilac", "LilacBg"));
            if (c.S.SplitMode == Split.ExceptGames && gm != Games.ModePause) sp.Children.Add(Ui.Note("Split tunnel is set to \"Everything except games\", so the VPN pauses while you play anyway.", "Lilac", "LilacBg"));
            if (eff == Games.ModeLowPing && (gm == Games.ModePause || c.S.SplitMode == Split.ExceptGames) && c.KillSwitchOn) sp.Children.Add(Ui.Note("The kill switch is installed: without the VPN there's no internet, so instead of disconnecting Hallow VPN switches to a low-ping server.", "Amber", "AmberBg"));
            sp.Children.Add(Section("More games"));
            sp.Children.Add(Ui.Muted("Other games' exe names, separated by commas (for example cs2, VALORANT-Win64-Shipping).", 12, 6));
            var box = Ui.Field(string.Join(", ", c.S.ExtraGames));
            box.LostFocus += (s, e) => { c.SetExtraGames(box.Text); };
            sp.Children.Add(box);
            sp.Children.Add(Wrap(Ui.Btn("Save", () => { c.SetExtraGames(box.Text); Say("Saved " + c.S.ExtraGames.Count + " extra game" + (c.S.ExtraGames.Count == 1 ? "" : "s") + "."); }, true)));
            return Scroll(sp);
        }

        // ================= Kill switch
        FrameworkElement KillSwitchPage()
        {
            var sp = PageBody("Kill switch", "Stops your traffic from leaving outside the VPN - with Windows Firewall rules, no driver.");
            string word = c.KillSwitchOn ? "Installed" : c.KillSwitchPartial ? "Partly installed" : "Not installed";
            var card = new StackPanel();
            card.Children.Add(Ui.T(word, 18, true, c.KillSwitchOn ? Ui.R("Green") : c.KillSwitchPartial ? Ui.R("Amber") : null, 4));
            card.Children.Add(Ui.Muted(c.KillSwitchOn ? "Windows Firewall blocks internet traffic on Wi-Fi and wired connections unless it goes through the VPN. No VPN = no internet." :
                c.KillSwitchPartial ? c.KillSwitchRules + " of " + KillSwitch.Rules().Count + " rules are there. Install it again to put them all back, or remove it." :
                "Your traffic goes out directly whenever the VPN isn't connected.", 12.5, 10));
            if (c.FirewallOn == false) card.Children.Add(Ui.Note("Windows Firewall is off for at least one network type, so the kill switch can't work there. Turn it on in Windows Security.", "Red", "RedBg"));
            if (c.KillSwitchOld) { card.Children.Add(Ui.Note("This is the kill switch from Hallow VPN 1.0. The 1.2 version is tighter: DNS only to NordVPN's servers, all public IPv6 blocked, and NordLynx (WireGuard) allowed. Upgrading swaps the rules in one go - Windows asks once (UAC).", "Amber", "AmberBg")); card.Children.Add(Wrap(Ui.Btn("Upgrade kill switch (asks Windows)", () => { Say("Windows will ask for permission..."); c.KillSwitchAsync(true, err => Say(err ?? "The kill switch is upgraded.")); }, true))); card.Children.Add(new Border { Height = 8 }); }
            var row = new WrapPanel();
            var inst = Ui.Btn(c.KillSwitchOn ? "Install again (asks Windows)" : "Install kill switch (asks Windows)", () =>
            {
                if (!Dialogs.Confirm(this, "Install the kill switch?", Program.KillSwitchText, "Install (asks Windows)", false)) return;
                Say("Windows will ask for permission...");
                c.KillSwitchAsync(true, err => Say(err ?? "The kill switch is installed."));
            }, !c.KillSwitchOn);
            if (c.KillSwitchOld) inst.Visibility = Visibility.Collapsed;
            row.Children.Add(inst);
            if (c.KillSwitchRules > 0 || c.OldKillSwitchRules > 0) row.Children.Add(Ui.Danger("Remove kill switch (asks Windows)", () =>
            {
                if (!Dialogs.Confirm(this, "Remove the kill switch?", "Windows will ask for permission (UAC). The Hallow VPN firewall rules are removed and your traffic can go out directly again when the VPN isn't connected.", "Remove (asks Windows)", true)) return;
                Say("Windows will ask for permission...");
                c.KillSwitchAsync(false, err => Say(err ?? "The kill switch is removed."));
            }));
            row.Children.Add(Ui.Btn("Check again", () => c.CheckAdminThings()));
            card.Children.Add(row);
            sp.Children.Add(Ui.Card(card, 18));
            sp.Children.Add(Section("Why it asks Windows"));
            sp.Children.Add(Ui.Muted("Firewall rules are a system setting, so adding or removing them needs admin. Hallow VPN never does it silently: you click, Windows shows its permission prompt (UAC), and you decide. Besides the kill switch, only Nord's certificate (once) and NordLynx connects ask Windows; everything else works without admin.", 12.5));
            sp.Children.Add(Section("What it blocks, and its limits"));
            sp.Children.Add(Bullets(KillSwitch.Limits));
            sp.Children.Add(Section("The rules"));
            sp.Children.Add(Ui.Muted(KillSwitch.Rules().Count + " outbound block rules named \"" + KillSwitch.Prefix + " - v2 ...\" (Wi-Fi and wired; TCP, UDP and ICMP; IPv4 and IPv6; UDP 500, 4500 and 51820 open; DNS only to " + string.Join(" and ", KillSwitch.NordDns) + ") - you can see them in Windows Defender Firewall's advanced settings. Uninstalling Hallow VPN offers to remove them too.", 12));
            return Scroll(sp);
        }

        // ================= Home (Tailscale)
        FrameworkElement HomePage()
        {
            var sp = PageBody("Home (Tailscale)", "Route through my home: use a computer at home as a Tailscale exit node.");
            sp.Children.Add(Ui.Note("This uses your HOME internet connection: websites see your home's address. It does NOT hide your traffic from your home internet provider (Nord does). Handy to look like you're at home, or to reach home things.", "Lilac", "LilacBg"));
            if (!c.HomeChecked) { sp.Children.Add(Ui.Muted("Checking for Tailscale…", 12.5)); return Scroll(sp); }
            if (!c.HomeInstalled)
            {
                sp.Children.Add(Ui.Card(new StackPanel { Children = { Ui.T("Tailscale isn't installed", 15, true, null, 4), Ui.Muted("Home mode needs Tailscale on this PC and on a computer at home that's set up as an exit node (Tailscale's \"Run as exit node\"). Hallow VPN doesn't install it.", 12.5) } }, 16));
                return Scroll(sp);
            }
            if (!c.Home.Running) sp.Children.Add(Ui.Note("Tailscale is installed but not connected" + (c.Home.Backend != null ? " (" + c.Home.Backend + ")" : "") + ". Sign in to Tailscale first.", "Amber", "AmberBg"));
            var cur = c.Home.Current;
            sp.Children.Add(Ui.Card(new StackPanel { Children = {
                Ui.T(cur != null ? "On: through " + cur.Name : "Off", 16, true, cur != null ? Ui.R("Green") : null, 2),
                Ui.Muted(cur != null ? "Your internet traffic goes through " + (cur.Dns ?? cur.Name) + "." : "Your traffic doesn't go through a home exit node.", 12.5) } }, 16));
            sp.Children.Add(Section("Exit nodes Tailscale offers"));
            if (c.Home.ExitNodes.Count == 0) sp.Children.Add(Ui.Muted("None of your Tailscale machines offers itself as an exit node. On the home computer, turn on Tailscale's \"Run as exit node\" and allow it in the Tailscale admin console.", 12.5));
            foreach (var n in c.Home.ExitNodes)
            {
                var nn = n;
                var d = new DockPanel();
                var b = n.Active ? Ui.Btn("Stop using it", () => c.UseHomeAsync(null, err => Say(err ?? "Home mode is off."))) : Ui.Btn("Use this", () =>
                {
                    if ((c.Phase == Phase.On || c.Phase == Phase.Connecting) && !Dialogs.Confirm(this, "Use " + nn.Name + "?", "Nord is connected. Hallow VPN disconnects it first - the two don't stack.", "Disconnect Nord and use it", false)) return;
                    c.UseHomeAsync(nn, err => Say(err ?? "Your traffic now goes through " + nn.Name + "."));
                }, true);
                b.IsEnabled = n.Active || (n.Online && c.Home.Running); b.Margin = new Thickness(0);
                DockPanel.SetDock(b, Dock.Right); d.Children.Add(b);
                var info = new StackPanel { Orientation = Orientation.Horizontal };
                info.Children.Add(new Border { Width = 9, Height = 9, CornerRadius = new CornerRadius(5), Background = Ui.R(n.Online ? "Green" : "Faint"), Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center });
                var t = new StackPanel(); t.Children.Add(new TextBlock { Text = n.Name, FontWeight = FontWeights.SemiBold });
                t.Children.Add(Ui.Muted((n.Online ? "online" : "offline") + " · " + n.Ip + (n.Os != null ? " · " + n.Os : ""), 11.5)); info.Children.Add(t);
                d.Children.Add(info);
                sp.Children.Add(Ui.Card(d, 12)); ((FrameworkElement)sp.Children[sp.Children.Count - 1]).Margin = new Thickness(0, 0, 0, 6);
            }
            sp.Children.Add(new Border { Height = 8 });
            sp.Children.Add(Ui.Switch("Keep my local network reachable (printers, NAS) while using an exit node", c.S.HomeAllowLan, on => { c.S.HomeAllowLan = on; c.S.Save(); }));
            sp.Children.Add(Wrap(Ui.Btn("Refresh", () => c.CheckHome())));
            sp.Children.Add(Ui.Muted("Hallow VPN runs \"tailscale set --exit-node=...\" only when you click. It lists only the machines Tailscale reports as exit-node capable. With the kill switch installed, remove it first - it blocks Tailscale's own connection.", 11.5));
            return Scroll(sp);
        }

        // ================= Account
        FrameworkElement AccountPage()
        {
            var sp = PageBody("Account", "Your NordVPN subscription, used through Nord's manual-setup (service) credentials.");
            sp.Children.Add(Ui.Note("These are NOT your Nord login. Nord gives separate SERVICE credentials for setting up a VPN by hand: on nordaccount.com go to NordVPN > \"Set up NordVPN manually\" (you may need to confirm by email), then copy the Service credentials - a username and a password made of letters and numbers.", "Lilac", "LilacBg"));
            var card = new StackPanel();
            if (c.HasAccount)
            {
                card.Children.Add(Ui.T("Saved", 15, true, Ui.R("Green"), 2));
                var cr = Snap.On ? new Credentials { User = "qP3xY7mN2kT9vR4wL6sZ8bJ1", Pass = "" } : Account.Load();
                card.Children.Add(Ui.Muted("Service username " + (cr != null ? Credentials.Mask(cr.User) : "(can't be read - save them again)") + " · encrypted with Windows DPAPI for your Windows user only, in " + (Snap.On ? @"%APPDATA%\HallowVPN" : VpnSettings.Dir) + ". Never logged, never sent to other apps.", 12.5, 10));
            }
            else card.Children.Add(Ui.T("Not set up yet", 15, true, null, 8));
            card.Children.Add(Ui.Muted("Service username", 12, 4));
            var user = Ui.Field(""); user.Width = 380; user.HorizontalAlignment = HorizontalAlignment.Left; card.Children.Add(user);
            card.Children.Add(new Border { Height = 8 });
            card.Children.Add(Ui.Muted("Service password", 12, 4));
            var pass = new PasswordBox { Width = 380, HorizontalAlignment = HorizontalAlignment.Left, Background = Ui.R("Well"), Foreground = Ui.R("Text"), BorderBrush = Ui.R("Line"), CaretBrush = Ui.R("AccentBrush"), Padding = new Thickness(8, 6, 8, 6), FontFamily = Ui.Mono, FontSize = 12.5 };
            card.Children.Add(pass);
            var row = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
            row.Children.Add(Ui.Btn(c.HasAccount ? "Replace" : "Save", () =>
            {
                var e = Account.Save(user.Text, pass.Password); pass.Clear();
                if (e != null) { Say(e); Dialogs.Info(this, "Not saved", e); return; }
                user.Text = ""; c.WriteStatus(); BuildPage(); Say("Saved, encrypted for your Windows user.");
            }, true));
            if (c.HasAccount) row.Children.Add(Ui.Danger("Remove", () =>
            {
                if (!Dialogs.Confirm(this, "Remove the saved credentials?", "Hallow VPN forgets them (the encrypted file is deleted). Your Nord account isn't affected; paste them again any time.", "Remove", true)) return;
                Account.Remove(); c.WriteStatus(); BuildPage(); Say("Removed.");
            }));
            card.Children.Add(row);
            sp.Children.Add(Ui.Card(card, 18));

            sp.Children.Add(Section("NordVPN's root certificate"));
            var ca = new StackPanel();
            ca.Children.Add(Ui.T(!c.CaChecked ? "Checking…" : c.CaInstalled ? "Installed - Windows trusts Nord's servers" : "Not installed", 14, true, c.CaInstalled ? Ui.R("Green") : c.CaChecked ? Ui.R("Amber") : null, 4));
            ca.Children.Add(Ui.Muted("Windows' IKEv2 client only connects to servers whose certificate it trusts. Nord's servers use NordVPN's own root certificate (\"NordVPN Root CA\"), which Windows doesn't include. Installing it puts it in this computer's Trusted Root store - a system change, so Windows asks for permission (UAC). Hallow VPN downloads it from Nord (downloads.nordcdn.com) only when you click, checks it really is NordVPN's root certificate and shows its fingerprint before anything is installed.", 12, 10));
            var crow = new WrapPanel();
            if (!c.CaInstalled) crow.Children.Add(Ui.Btn("Install Nord's certificate (asks Windows)", InstallCa, true));
            else crow.Children.Add(Ui.Btn("Remove Nord's certificate (asks Windows)", () =>
            {
                if (!Dialogs.Confirm(this, "Remove NordVPN's root certificate?", "Windows will ask for permission (UAC). After that, Windows won't trust Nord's servers and connecting fails until it's installed again.", "Remove (asks Windows)", true)) return;
                c.CaChangeAsync(false, e2 => Say(e2 ?? "NordVPN's root certificate is removed."));
            }));
            crow.Children.Add(Ui.Btn("Check again", () => c.CheckAdminThings()));
            ca.Children.Add(crow);
            sp.Children.Add(Ui.Card(ca, 18));
            return Scroll(sp);
        }

        // ================= Settings
        FrameworkElement SettingsPage()
        {
            var sp = PageBody("Settings", null);
            sp.Children.Add(Section("Look"));
            var looks = new WrapPanel();
            foreach (var l in new[] { Mode.Follow, Mode.Light, Mode.Dark })
            {
                var ll = l;
                looks.Children.Add(Chip(Mode.OwnLabel(l), Mode.Own(c.S.Look) == l, () => { c.S.Look = ll; c.S.Save(); c.ApplyAccent(); BuildPage(); }));
            }
            sp.Children.Add(looks);
            sp.Children.Add(Ui.Muted("Now " + (Mode.IsLight ? "light" : "dark") + " - " + Mode.Why(c.S.Look, Accent.ReadMode()) + ". \"Follow Hallow\" uses the switch in Hallow Toolkit (Light / Dark / Same as Windows).", 12, 4));
            sp.Children.Add(Ui.Switch("Use the Hallow accent colour (from Hallow Toolkit)", c.S.UseHallowAccent, on => { c.S.UseHallowAccent = on; c.S.Save(); c.ApplyAccent(); }));
            sp.Children.Add(Ui.Switch("Use the one Hallow icon (Hallow Hub's tray menu lists Hallow VPN)", c.S.UseHallowIcon, on => { c.S.UseHallowIcon = on; c.S.Save(); c.CheckHost(); }));
            sp.Children.Add(Section("Connection"));
            sp.Children.Add(Ui.Switch("Reconnect by itself when the connection drops", c.S.AutoReconnect, on => { c.S.AutoReconnect = on; c.S.Save(); c.WriteStatus(); }));
            sp.Children.Add(Ui.Muted("Connecting at sign-in and on untrusted Wi-Fi is on the Auto-connect page.", 12, 6));
            sp.Children.Add(Ui.Muted("Quitting Hallow VPN leaves the VPN as it is (Windows keeps the connection; you'll see \"Hallow VPN\" in Windows' network list). Auto-reconnect and game awareness only work while Hallow VPN runs.", 12, 6));
            sp.Children.Add(Section("Windows' VPN profile"));
            sp.Children.Add(Ui.Muted("Hallow VPN keeps one VPN connection called \"Hallow VPN\" for your Windows user (IKEv2, EAP, credentials not saved by Windows). It's created on the first connect and updated when you change server or split tunnel.", 12, 6));
            var prof = Ui.Btn("Remove the Windows VPN profile", () =>
            {
                if (!Dialogs.Confirm(this, "Remove the \"Hallow VPN\" profile?", "Windows' VPN connection called \"Hallow VPN\" is removed (only that one). It's created again the next time you connect.", "Remove", true)) return;
                Say(c.RemoveProfile() ?? "Removed.");
            });
            prof.IsEnabled = c.Phase == Phase.Off;
            sp.Children.Add(Wrap(prof));
            sp.Children.Add(Section("Help"));
            sp.Children.Add(Wrap(Go("setup", "Open the first-run setup"), Go("fix", "Fix my connection")));
            sp.Children.Add(Section("Hallow VPN"));
            sp.Children.Add(Wrap(Ui.Btn("Check for updates", () => c.CheckForUpdate(true)), Ui.Btn("Open data folder", () => { if (!Snap.On) try { System.IO.Directory.CreateDirectory(VpnSettings.Dir); Process.Start("explorer.exe", "\"" + VpnSettings.Dir + "\""); } catch { } })));
            sp.Children.Add(Ui.Muted("v" + Program.Version + ". Talks only to api.nordvpn.com (server list, and the IP check while connected), the servers it pings, downloads.nordcdn.com (only when you install Nord's certificate) and GitHub (updates). Crash reports stay on this PC and never contain your credentials.", 11.5, 0));
            return Scroll(sp);
        }
    }
}
