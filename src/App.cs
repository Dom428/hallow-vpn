// Hallow VPN - a game-aware VPN client for your EXISTING NordVPN subscription, on Windows' own IKEv2 VPN client: no drivers, nothing
// else to download, no admin for everyday use. Servers come from Nord's public server API (cached, refreshed at most every 3 hours),
// pings are measured by this PC, the connection is a per-user Windows VPN profile dialled with your Nord SERVICE credentials
// (DPAPI-encrypted, never logged, never on a command line). Game-aware (pause or switch to a low-ping server while you play),
// route-based split tunnelling, an optional Windows Firewall kill switch (UAC), "Home" mode through a Tailscale exit node, and the
// Hallow family link.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Threading;
using WF = System.Windows.Forms;

[assembly: AssemblyTitle("Hallow VPN")]
[assembly: AssemblyProduct("Hallow VPN")]
[assembly: AssemblyDescription("A game-aware VPN client for NordVPN on Windows' built-in IKEv2")]
[assembly: AssemblyVersion("1.2.0.0")]
[assembly: AssemblyFileVersion("1.2.0.0")]

namespace HallowVPN
{
    public static class Program
    {
        public const string Version = "1.2";
        [DllImport("user32.dll")] static extern bool SetProcessDpiAwarenessContext(IntPtr v);

        [STAThread]
        public static void Main(string[] args)
        {
            int ci = Array.IndexOf(args, "--cmd");
            if (ci >= 0) { Environment.Exit(Family.RunCmd(ci + 1 < args.Length ? args[ci + 1] : "")); return; }
            int ei = Array.IndexOf(args, "--elevated");
            if (ei >= 0) { Environment.Exit(Elevated.RunHere(ei + 1 < args.Length ? args[ei + 1] : "", ei + 2 < args.Length ? args[ei + 2] : null)); return; }
            try { SetProcessDpiAwarenessContext(new IntPtr(-4)); } catch { }
            int si = Array.IndexOf(args, "--snap");
            if (si >= 0 && si + 1 < args.Length)
            {
                Snap.On = true; VpnSettings.Frozen = true;
                var sa = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                SnapAll(sa, args[si + 1]); return;
            }
            var leaf = VpnSettings.TestDir != null ? "Test" + Path.GetFileName(VpnSettings.TestDir.TrimEnd('\\')) : "";
            bool created;
            var mutex = new Mutex(true, @"Local\HallowVPN" + leaf, out created);
            var showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\HallowVPNShow" + leaf);
            if (!created) { showEvent.Set(); return; }
            Crash.Install();
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown }; LoadTheme(app);
            app.DispatcherUnhandledException += (s, e) => { Crash.Write(e.Exception); e.Handled = true; Environment.Exit(1); };
            var c = new Controller(args.Contains("--tray"));
            new Thread(() => { while (showEvent.WaitOne()) app.Dispatcher.BeginInvoke(new Action(() => c.ShowMain(null))); }) { IsBackground = true }.Start();
            app.Run();
            GC.KeepAlive(mutex);
        }
        public static string AccentHex = Accent.Default;
        // the family console look with the Hallow accent; light mode swaps the values (Mode.LightTheme)
        public static void LoadTheme(Application app, bool? light = null)
        {
            var st = VpnSettings.Load();
            AccentHex = Accent.Current(st.UseHallowAccent);
            Mode.IsLight = light ?? Mode.Resolve(st.Look, Accent.ReadMode(), Mode.Sys);
            var x = Res("HallowVPN.Theme.xaml");
            x = Mode.IsLight ? Accent.ApplyTo(Mode.LightTheme(x, AccentHex), AccentHex) : Accent.ApplyTo(x, Ui.Readable(AccentHex));
            app.Resources.MergedDictionaries.Clear();
            app.Resources.MergedDictionaries.Add((ResourceDictionary)XamlReader.Parse(x));
        }
        public static string ThemeXaml(bool light, string accent)
        {
            var x = Res("HallowVPN.Theme.xaml");
            return light ? Accent.ApplyTo(Mode.LightTheme(x, accent), accent) : Accent.ApplyTo(x, Ui.Readable(accent));
        }
        static string Res(string name) { using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream(name)) using (var r = new StreamReader(s)) return r.ReadToEnd(); }

        // --snap <folder>: every page off-screen, in dark and light, with made-up servers / account / Tailscale (never this PC's)
        static void SnapAll(Application app, string dir)
        {
            foreach (var light in new[] { false, true })
            {
                LoadTheme(app, light);
                var sfx = light ? "-light" : "";
                var c = new Controller(true, true); c.SnapSetup(true);
                var w = new MainWindow(c);
                c.RunDiagnosis(null);
                foreach (var p in MainWindow.Pages) { w.Show(p[0]); Snap.Save(w, Path.Combine(dir, p[0] + sfx + ".png"), 1180, 760); }
                w.SnapServers("p2p"); w.Show("servers"); Snap.Save(w, Path.Combine(dir, "servers-p2p" + sfx + ".png"), 1180, 760);
                w.SnapServers("gaming"); w.Show("servers"); Snap.Save(w, Path.Combine(dir, "servers-gaming" + sfx + ".png"), 1180, 760);
                w.SnapServers("country:DE"); w.Show("servers"); Snap.Save(w, Path.Combine(dir, "servers-country" + sfx + ".png"), 1180, 760); w.SnapServers("fastest");
                // not connected yet: no account, no certificate
                var c2 = new Controller(true, true); c2.SnapSetup(false);
                var w2 = new MainWindow(c2); w2.Show("connect"); Snap.Save(w2, Path.Combine(dir, "connect-new" + sfx + ".png"), 1180, 760);
                w2.Show("account"); Snap.Save(w2, Path.Combine(dir, "account-new" + sfx + ".png"), 1180, 760);
                w2.Show("setup"); Snap.Save(w2, Path.Combine(dir, "setup-new" + sfx + ".png"), 1180, 760);
                c2.RunDiagnosis(null); w2.Show("fix"); Snap.Save(w2, Path.Combine(dir, "fix-new" + sfx + ".png"), 1180, 760);
                // the setup's test passed
                c.SetupTest = "pass"; w.Show("setup"); Snap.Save(w, Path.Combine(dir, "setup-pass" + sfx + ".png"), 1180, 760); c.SetupTest = null;
                // paused for a game
                c.SnapPaused(); w.Show("connect"); Snap.Save(w, Path.Combine(dir, "connect-paused" + sfx + ".png"), 1180, 760);
                // 1.2: NordLynx connected; NordLynx not set up; the 1.0 kill switch to upgrade
                var c3 = new Controller(true, true); c3.SnapSetup(true, true);
                var w3 = new MainWindow(c3); w3.Show("connect"); Snap.Save(w3, Path.Combine(dir, "connect-nordlynx" + sfx + ".png"), 1180, 760);
                w3.Show("nordlynx"); Snap.Save(w3, Path.Combine(dir, "nordlynx-ready" + sfx + ".png"), 1180, 760);
                w2.Show("nordlynx"); Snap.Save(w2, Path.Combine(dir, "nordlynx-new" + sfx + ".png"), 1180, 760);
                c2.SnapOldKillSwitch(); w2.Show("killswitch"); Snap.Save(w2, Path.Combine(dir, "killswitch-upgrade" + sfx + ".png"), 1180, 760);
                Snap.Save(Dialogs.ConfirmWindow("Install the kill switch?", KillSwitchText, "Install (asks Windows)", false, null), Path.Combine(dir, "dialog-killswitch" + sfx + ".png"), 480, 0);
                Snap.Save(CrashNotice.Make(Path.Combine(dir, "example.txt")), Path.Combine(dir, "crash" + sfx + ".png"), 440, 0);
            }
        }
        public const string KillSwitchText = "Windows will ask for permission (UAC). Hallow VPN then adds Windows Firewall rules that block internet traffic on Wi-Fi and wired connections unless it goes through the VPN. While they're in place, no VPN means no internet. You can remove them here at any time (Windows asks again).";
    }

    public enum Phase { Off, Connecting, On, Disconnecting }

    // the app: tray icon, family pipe, the connection, game watch, kill switch, home mode, updates
    public partial class Controller
    {
        public readonly VpnSettings S = VpnSettings.Load();
        public readonly bool SnapMode;
        readonly Dispatcher ui;
        MainWindow main; WF.NotifyIcon tray; bool hubHosts, hubLists, quitting, balloonOwn;
        public readonly IVpn Vpn; public readonly INordApi Api; public readonly ServerBook Book; public readonly IFirewall Fw; public readonly ICa Ca;
        public readonly ITailscale Ts; public readonly IGameSource GameSrc;
        public readonly INetInfo Net; public readonly IPinger Pinger; readonly Func<string, System.Net.IPAddress[]> resolve;
        readonly List<string> log = new List<string>();
        public Func<DateTime> Now = () => DateTime.Now;

        // the connection
        public Phase Phase { get; private set; }
        public Server Current; public Target CurrentTarget; public DateTime? Since; public string LastError;
        public string PublicIp, IpCountry; public bool? IpProtected; public bool IpLoading;
        bool wanted, cancel; int retries; DateTime? retryAt;
        // the game
        public string Game; public bool PausedForGame, SwitchedForGame; Target resumeTarget; DateTime? resumeAt;
        // admin things (read-only checks) and home
        public int KillSwitchRules; public bool? FirewallOn; public bool CaInstalled; public bool CaChecked;
        public TsState Home = new TsState(); public bool HomeInstalled, HomeChecked;
        public bool HasAccount { get { return Account.Exists || snapAccount; } }
        bool snapAccount; Credentials snapCreds;
        public event Action Changed;
        DispatcherTimer tick; FileSystemWatcher accentWatch; bool? lastLight;

        public Controller(bool trayOnly, bool snapMode = false)
        {
            SnapMode = snapMode; ui = Dispatcher.CurrentDispatcher;
            bool fake = snapMode || VpnSettings.Fake;
            Api = fake ? (INordApi)FakeData.Api() : new RealNordApi();
            var pinger = fake ? (IPinger)new FakePinger() : new RealPinger(); Pinger = pinger;
            Book = new ServerBook(Api, pinger, snapMode ? null : Path.Combine(VpnSettings.Dir, "servers.json"));
            Vpn = fake ? (IVpn)new FakeVpn { PbkPath = snapMode ? null : Path.Combine(VpnSettings.Dir, "rasphone.pbk") } : new WindowsVpn();
            Net = fake ? (INetInfo)new FakeNetInfo() : new RealNetInfo();
            resolve = fake ? (Func<string, System.Net.IPAddress[]>)(h => h == "nowhere.nordvpn.com" ? new System.Net.IPAddress[0] : new[] { System.Net.IPAddress.Parse("203.0.113.40") }) : Split.Dns;
            Fw = fake ? (IFirewall)new FakeFirewall() : new WindowsFirewall();
            Ca = fake ? (ICa)new FakeCa { In = true } : new WindowsCa();
            Ts = fake ? (ITailscale)new FakeTailscale { Json = FakeData.TailscaleJson } : new RealTailscale();
            GameSrc = fake ? (IGameSource)new FakeGames() : new RealGames();
            Tunnel = fake ? (ITunnel)new FakeTunnel() : new WireGuardTunnel(); Wg = fake ? (IWgSetup)new FakeWgSetup() : new RealWgSetup(); Auth = fake ? (INordAuth)new FakeNordAuth() : new RealNordAuth();
            if (snapMode) return;
            if (Account.Exists) Account.Load();   // so a crash report can blank them out (Crash.Secrets)
            if (NordLynx.Exists) NordLynx.Load();   // the same for the NordLynx token and key (Crash.Secrets2)
            Family.Serve("VPN", Command);
            CheckAdminThings(); CheckHome();
            tick = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) }; tick.Tick += (s, e) => Tick(); tick.Start();
            if (Tunnel.IsUp()) { Proto = ProtoWg; Phase = Phase.On; Since = Now(); wanted = true; CurrentTarget = Target.Parse(S.LastTarget); Log("NordLynx already connected at start"); FetchIp(); }
            else if (Vpn.IsUp()) { Proto = ProtoIke; Phase = Phase.On; Since = Now(); wanted = true; CurrentTarget = Target.Parse(S.LastTarget); Log("already connected at start"); if (!S.SetupDone) { S.SetupDone = true; S.Save(); } FetchIp(); }
            if (!VpnSettings.Headless)
            {
                SetupTray();
                var ht = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) }; ht.Tick += (s, e) => CheckHost(); ht.Start(); CheckHost();
                accentWatch = Accent.Watch(() => ui.BeginInvoke(new Action(ApplyAccent)));
                if (VpnSettings.TestDir == null || Environment.GetEnvironmentVariable("HALLOW_VPN_API") != null)
                {
                    var upd = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
                    upd.Tick += (s, e) => { upd.Interval = TimeSpan.FromHours(6); DateTime last; if (!DateTime.TryParse(S.LastUpdateCheck, out last) || (DateTime.Now - last).TotalHours >= 24) CheckForUpdate(false); };
                    upd.Start();
                }
                ui.BeginInvoke(new Action(ShowCrashNotice), DispatcherPriority.ApplicationIdle);
            }
            WriteStatus();
            if (S.ConnectOnStart && HasAccount && Phase == Phase.Off)
            {
                var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
                t.Tick += (s, e) => { t.Stop(); if (Game == null || EffGame() != Games.ModePause) Connect(Target.Parse(S.LastTarget), true); };
                t.Start();
            }
            if (!trayOnly && !VpnSettings.Headless) ShowMain(S.SetupDone ? null : "setup");
        }
        void Log(string s) { lock (log) { log.Add(DateTime.Now.ToString("HH:mm:ss") + " " + s); if (log.Count > 100) log.RemoveAt(0); } }
        void Fire() { WriteStatus(); var h = Changed; if (h != null) h(); }
        public bool KillSwitchOn { get { return KillSwitchRules >= KillSwitch.Rules().Count || OldKillSwitchRules >= KillSwitch.Legacy().Count; } }
        public bool KillSwitchPartial { get { return (KillSwitchRules > 0 || OldKillSwitchRules > 0) && !KillSwitchOn; } }
        public bool KillSwitchOld { get { return OldKillSwitchRules > 0 && KillSwitchRules < KillSwitch.Rules().Count; } }   // 1.0's rules: "Upgrade kill switch"

        // ---------------- the connection
        Credentials Creds() { return SnapMode ? snapCreds : Account.Load(); }
        // connect to a target (fastest / gaming / country / server). remember = it becomes what "Connect" means (a click, the pipe)
        public string Connect(Target t, bool remember)
        {
            if (t == null) return "Unknown server.";
            if (Phase == Phase.Connecting || Phase == Phase.Disconnecting) return "Hallow VPN is busy - wait a moment.";
            var proto = ProtoFor(!remember); Credentials creds = null;
            if (proto == ProtoWg)
            {
                if (!WgInstalled) { LastError = "NordLynx needs WireGuard for Windows - install it on the NordLynx page (or pick IKEv2 there)."; Fire(); return LastError; }
                if (!NordLynx.Exists && !SnapMode) { LastError = "Add your Nord access token on the NordLynx page first."; Fire(); return LastError; }
                if (KillSwitchOld) { LastError = "The kill switch from Hallow VPN 1.0 blocks NordLynx (UDP 51820). Upgrade it on the Kill switch page first."; Fire(); return LastError; }
                if (!remember && Game != null) { pendingAuto = true; Log("NordLynx waits for the game to close (it asks Windows)"); return "NordLynx asks Windows (UAC), so it waits until the game closes."; }
            }
            else
            {
                creds = Creds();
                if (creds == null) { LastError = "Add your Nord service credentials first (Account page)."; Fire(); return LastError; }
            }
            if (Home.Current != null) { LastError = "Home mode is on (Tailscale exit node " + Home.Current.Name + "). Stop it on the Home page first - the two don't stack."; Fire(); return LastError; }
            if (remember) { S.LastTarget = t.ToString(); S.Save(); PausedForGame = false; SwitchedForGame = false; resumeAt = null; }
            wanted = true; cancel = false; retryAt = null; LastError = null;
            Phase = Phase.Connecting; CurrentTarget = t; connectingProto = proto; Log("connect " + t + " over " + proto + (remember ? "" : " (automatic)"));
            Fire();
            Action work = () =>
            {
                Server srv = null; string err = null;
                try
                {
                    srv = Pick(t, out err, proto == ProtoWg);
                    if (srv != null && !cancel && proto == ProtoWg)
                    {
                        if (Vpn.IsUp()) Vpn.HangUp();   // from IKEv2 to NordLynx
                        err = Tunnel.Up(srv);           // Windows asks (UAC): the user's click
                        if (err == null && cancel) { Tunnel.Down(); err = "Cancelled."; }
                    }
                    else if (srv != null && !cancel)
                    {
                        if (Tunnel.IsUp()) { err = Tunnel.Down(); if (err != null) err = "NordLynx is still connected: " + err; }   // from NordLynx to IKEv2 (asks Windows)
                        List<string> failed = null; List<string> routes = null;
                        if (S.SplitMode == Split.Only) { routes = Split.Routes(S.SplitRoutes, SnapMode || VpnSettings.Fake ? (Func<string, System.Net.IPAddress[]>)(h => new[] { System.Net.IPAddress.Parse("198.51.100.7") }) : Split.Dns, out failed); if (routes.Count == 0) err = "\"Only these go through the VPN\" has nothing in its list that works - add an address or switch to Everything on the Split tunnel page."; }
                        if (err == null)
                        {
                            var spec = Split.Spec(srv.Host, S.SplitMode, routes);
                            if (!Vpn.ProfileExists() || spec.Key != S.ProfileSpec) { err = Vpn.ApplyProfile(spec); if (err == null) { S.ProfileSpec = spec.Key; S.Save(); Log("profile set up: " + (spec.SplitTunnel ? spec.Routes.Count + " routes" : "everything")); } }
                            if (failed != null && failed.Count > 0) Log("couldn't look up: " + string.Join(", ", failed));
                        }
                        if (err == null && !cancel) ProtectDns();   // IKEv2 only: NordLynx's DNS is in its own config
                        if (err == null && !cancel)
                        {
                            if (Vpn.IsUp()) Vpn.HangUp();   // switching servers
                            err = Vpn.Dial(srv.Host, creds);
                            if (err == null && cancel) { Vpn.HangUp(); err = "Cancelled."; }
                        }
                    }
                }
                catch (Exception e) { err = "Connecting failed: " + e.Message; }
                Action fin = () => Connected(srv, err);
                if (SnapMode) fin(); else ui.BeginInvoke(fin);
            };
            if (SnapMode) work(); else new Thread(() => work()) { IsBackground = true, Name = "connect" }.Start();
            return null;
        }
        void Connected(Server srv, string err)
        {
            if (err == null)
            {
                Phase = Phase.On; Current = srv; Since = Now(); retries = 0; LastError = null; Proto = connectingProto;
                HSent = -1; LivePing = -1; lastPing = DateTime.MinValue; LeakState = null; LeakAt = null; RealIp = null; RealIpError = null;
                Log("connected: " + srv.Short); FetchIp();
                if (!SnapMode) { S.SetupDone = true; S.LastServer = srv.Host; S.LastDialCode = 0; S.Save(); }
                if (SetupTest == "running") SetupTest = "pass";
            }
            else
            {
                Phase = AnyUp() ? Phase.On : Phase.Off; if (Phase == Phase.Off) { Since = null; PublicIp = null; }
                LastError = err; Log("connect failed: " + err);
                if (Vpn.LastCode != 0 && !SnapMode) { S.LastDialCode = Vpn.LastCode; S.LastDialWhen = DateTime.Now.ToString("d MMM HH:mm", System.Globalization.CultureInfo.InvariantCulture); S.Save(); }
                if (SetupTest == "running") SetupTest = "fail";
                if (cancel || err == "Cancelled.") wanted = false;
                else if (S.AutoReconnect && wanted && retries < 6 && IsRetryable(err)) ScheduleRetry();
                else wanted = false;
            }
            Fire();
        }
        static bool IsRetryable(string err) { return !(err.Contains("691") || err.Contains("13801") || err.Contains("13806") || err.Contains("credentials") || err.Contains("Split tunnel") || err.Contains("Home mode") || err.Contains("profile") || err.Contains("UAC") || err.Contains("permission") || err.Contains("NordLynx")); }
        void ScheduleRetry()
        {
            var waits = new[] { 3, 10, 30, 60, 120, 300 };
            retryAt = Now().AddSeconds(waits[Math.Min(retries, waits.Length - 1)]); retries++;
            Log("reconnect in " + (int)(retryAt.Value - Now()).TotalSeconds + " s (try " + retries + ")");
        }
        public Server Pick(Target t, out string err, bool wg = false)
        {
            var s0 = PickAny(t, out err, wg);
            return s0;
        }
        Server PickAny(Target t, out string err, bool wg)
        {
            err = null; List<Server> list; string e2;
            switch (t.Kind)
            {
                case "server":
                    {
                        var s = Book.Find(t.Value);
                        if (s == null) { var f = S.Favourites.FirstOrDefault(x => x.Host == t.Value); s = new Server { Host = t.Value, Name = f != null ? f.Name : t.Value.Split('.')[0], Country = f != null ? f.Country : null, Code = f != null ? f.Code : null, City = f != null ? f.City : null, Load = -1 }; }
                        if (wg && (s.WgKey == null || s.Station == null)) { err = "Nord's list has no NordLynx details for " + s.Short + " yet - pick it from the Servers page, or use IKEv2."; return null; }
                        return s;
                    }
                case "country":
                    {
                        var c = Book.FindCountry(t.Value);
                        if (c == null) { err = "Nord's country list doesn't have \"" + t.Value + "\"."; return null; }
                        list = Book.List(c.Id, false, out e2); break;
                    }
                case "p2p": list = Book.List(0, false, out e2, true); break;
                default: list = Book.List(0, false, out e2); break;
            }
            if (wg) list = list.Where(x => x.WgKey != null && x.Station != null).ToList();
            if (list.Count == 0) { err = e2 ?? (wg ? "Nord sent no NordLynx servers." : "Nord sent no IKEv2 servers."); return null; }
            Book.Measure(list, 12);
            var pick = t.Kind == "gaming" ? Nord.ForGaming(list.Take(12)) : Nord.Fastest(list.Take(12));
            if (pick == null) err = "No server answered.";
            return pick;
        }
        public void Cancel() { if (Phase != Phase.Connecting) return; cancel = true; wanted = false; Log("cancel"); }
        public string Disconnect(bool user)
        {
            if (user) { wanted = false; retryAt = null; PausedForGame = false; SwitchedForGame = false; resumeAt = null; userOffNet = netKey; pendingAuto = false; }
            if (Phase == Phase.Connecting) { Cancel(); return null; }
            if (Phase == Phase.Disconnecting) return null;
            Phase = Phase.Disconnecting; Log("disconnect" + (user ? "" : " (automatic)")); Fire();
            Action work = () =>
            {
                string err; try { err = Proto == ProtoWg && Tunnel.IsUp() ? Tunnel.Down() : Vpn.HangUp(); } catch (Exception e) { err = e.Message; }
                Action fin = () => { Phase = AnyUp() ? Phase.On : Phase.Off; if (Phase == Phase.Off) { Since = null; PublicIp = null; IpCountry = null; IpProtected = null; } LastError = err; Fire(); };
                if (SnapMode) fin(); else ui.BeginInvoke(fin);
            };
            if (SnapMode) work(); else new Thread(() => work()) { IsBackground = true, Name = "disconnect" }.Start();
            return null;
        }
        // Nord's IP check (api.nordvpn.com) - only while connected
        public void FetchIp()
        {
            if (Phase != Phase.On || IpLoading) return;
            IpLoading = true; PublicIp = null;
            Action work = () =>
            {
                string ip = null, country = null; bool? prot = null;
                try { ip = Nord.ParseIp(Api.Get(Nord.IpUrl), out country, out prot); } catch { }
                Action fin = () => { IpLoading = false; if (Phase == Phase.On) { PublicIp = ip; IpCountry = country; IpProtected = prot; } Fire(); if (Phase == Phase.On) RunLeakCheck(); };
                if (SnapMode) fin(); else ui.BeginInvoke(fin);
            };
            if (SnapMode) work(); else new Thread(() => work()) { IsBackground = true, Name = "ip" }.Start();
        }

        // ---------------- once a second: the connection (every 3 s), the game (every 5 s), retries, the Windows theme
        int ticks; bool checking;
        void Tick()
        {
            if (quitting) return;
            ticks++;
            var now = Now();
            if (retryAt != null && now >= retryAt.Value && Phase == Phase.Off && wanted && Game == null) { retryAt = null; Connect(CurrentTarget ?? Target.Parse(S.LastTarget), false); }
            if (resumeAt != null && now >= resumeAt.Value && Game == null) { resumeAt = null; var t = resumeTarget; resumeTarget = null; if (t != null && (Phase == Phase.Off || SwitchedForGame)) { SwitchedForGame = false; Connect(t, false); } }
            if (ticks % 3 == 0 && !checking) Check(ticks % 5 == 0);
            if (ticks % 5 == 0 && !VpnSettings.Headless) { bool light = Mode.Resolve(S.Look, Accent.ReadMode(), Mode.Sys); if (lastLight != null && lastLight != light) ApplyAccent(); lastLight = light; }
            if (ticks % 60 == 0) WriteStatus();
            if (main != null && Phase == Phase.On) main.TickClock();
        }
        void Check(bool game)
        {
            checking = true;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                bool up = false; string g = null;
                try { up = AnyUp(); } catch { }
                if (game) { try { g = GameSrc.Running(S.ExtraGames); } catch { } }
                NetNow n = null; if (game) { try { n = Net.Current(); } catch { } }
                ui.BeginInvoke(new Action(() => { checking = false; Observe(up, game, g); if (n != null) NetObserved(n); }));
            });
        }
        // tests: the same, on this thread
        public void CheckNow() { bool up = AnyUp(); string g = GameSrc.Running(S.ExtraGames); Observe(up, true, g); NetObserved(Net.Current()); }
        void Observe(bool up, bool gameChecked, string g)
        {
            if (Phase == Phase.On && !up)
            {
                Log("connection dropped"); Phase = Phase.Off; Since = null; PublicIp = null;
                if (wanted && S.AutoReconnect && Game == null) { retries = 0; ScheduleRetry(); Notify("VPN connection dropped", "Hallow VPN is reconnecting."); }
                else if (wanted && !S.AutoReconnect) wanted = false;
                Fire();
            }
            else if (Phase == Phase.Off && up)
            {
                Proto = Tunnel.IsUp() ? ProtoWg : ProtoIke;
                Log("connected outside Hallow VPN (" + Proto + ")"); Phase = Phase.On; Since = Now(); wanted = true; FetchIp(); Fire();
            }
            if (gameChecked && g != Game) GameChanged(g);
        }
        void GameChanged(string g)
        {
            var was = Game; Game = g;
            var eff = EffGame();
            if (g != null && was == null)
            {
                Log("game: " + g + " (" + eff + ")");
                if (eff == Games.ModePause && (Phase == Phase.On || Phase == Phase.Connecting))
                {
                    PausedForGame = true; resumeTarget = CurrentTarget ?? Target.Parse(S.LastTarget); resumeAt = null; retryAt = null;
                    wanted = true; Disconnect(false);
                }
                else if (eff == Games.ModeLowPing && Phase == Phase.On && (CurrentTarget == null || CurrentTarget.Kind != "gaming"))
                {
                    SwitchedForGame = true; resumeTarget = CurrentTarget ?? Target.Parse(S.LastTarget); resumeAt = null;
                    Connect(new Target { Kind = "gaming" }, false);
                }
            }
            else if (g == null && was != null)
            {
                Log("game closed");
                if ((PausedForGame || SwitchedForGame) && resumeTarget != null) { resumeAt = Now().AddSeconds(10); PausedForGame = false; }
                else if (pendingAuto && Phase == Phase.Off) { pendingAuto = false; AutoConnect("the game closed, untrusted network"); }
            }
            Fire();
        }

        // ---------------- admin things: read-only checks; changes only on a click (UAC)
        public void CheckAdminThings()
        {
            Action work = () =>
            {
                int rules = 0, old = 0; bool? on = null; bool ca = false, wg = false;
                try { rules = Fw.InstalledRules(); old = Fw.OldRules(); } catch { }
                try { wg = Wg.Installed(); } catch { }
                try { on = Fw.FirewallOn(); } catch { }
                try { ca = Ca.Installed(); } catch { }
                bool v6 = false; try { v6 = NetCheck.HasGlobalIpv6(Net.Interfaces()); } catch { }
                Action fin = () => { KillSwitchRules = rules; OldKillSwitchRules = old; FirewallOn = on; CaInstalled = ca; CaChecked = true; Ipv6 = v6; WgInstalled = wg; Fire(); };
                if (SnapMode) fin(); else ui.BeginInvoke(fin);
            };
            if (SnapMode) work(); else new Thread(() => work()) { IsBackground = true, Name = "checks" }.Start();
        }
        public void KillSwitchAsync(bool on, Action<string> done)
        {
            Log("kill switch " + (on ? "install" : "remove") + " (asks Windows)");
            new Thread(() =>
            {
                string err; try { err = on ? Fw.Install() : Fw.Remove(); } catch (Exception e) { err = e.Message; }
                int rules = 0, old = 0; try { rules = Fw.InstalledRules(); old = Fw.OldRules(); } catch { }
                ui.BeginInvoke(new Action(() => { KillSwitchRules = rules; OldKillSwitchRules = old; Log("kill switch: " + (err ?? "done") + ", " + rules + " rules"); Fire(); done(err); }));
            }) { IsBackground = true, Name = "kill switch" }.Start();
        }
        public void CaFetchAsync(Action<string, string> done)
        {
            new Thread(() => { string t; string err; try { err = Ca.Fetch(out t); } catch (Exception e) { err = e.Message; t = null; } ui.BeginInvoke(new Action(() => done(err, t))); }) { IsBackground = true, Name = "ca" }.Start();
        }
        public void CaChangeAsync(bool install, Action<string> done)
        {
            Log("certificate " + (install ? "install" : "remove") + " (asks Windows)");
            new Thread(() =>
            {
                string err; try { err = install ? Ca.Install() : Ca.Remove(); } catch (Exception e) { err = e.Message; }
                bool now = false; try { now = Ca.Installed(); } catch { }
                ui.BeginInvoke(new Action(() => { CaInstalled = now; Fire(); done(err); }));
            }) { IsBackground = true, Name = "ca" }.Start();
        }

        // ---------------- home mode (Tailscale)
        public void CheckHome()
        {
            Action work = () =>
            {
                bool inst = false; TsState st = new TsState();
                try { inst = Ts.Installed(); if (inst) st = Tailscale.Parse(Ts.StatusJson()); } catch { }
                Action fin = () => { HomeInstalled = inst; Home = st; HomeChecked = true; Fire(); };
                if (SnapMode) fin(); else ui.BeginInvoke(fin);
            };
            if (SnapMode) work(); else new Thread(() => work()) { IsBackground = true, Name = "tailscale" }.Start();
        }
        public void UseHomeAsync(TsNode node, Action<string> done)
        {
            if (node != null && KillSwitchOn) { done("The kill switch blocks Tailscale's own connection. Remove the kill switch first (Kill switch page)."); return; }
            if (node != null && !node.Online) { done(node.Name + " is offline right now."); return; }
            if (node != null && (Phase == Phase.On || Phase == Phase.Connecting)) Disconnect(true);
            Log(node == null ? "home off" : "home on: exit node");
            new Thread(() =>
            {
                if (node != null) { var sw = Stopwatch.StartNew(); while (Vpn.IsUp() && sw.ElapsedMilliseconds < 8000) Thread.Sleep(200); }
                string err; try { err = Ts.SetExitNode(node == null ? null : node.Ip, S.HomeAllowLan); } catch (Exception e) { err = e.Message; }
                TsState st = null; try { st = Tailscale.Parse(Ts.StatusJson()); } catch { }
                ui.BeginInvoke(new Action(() => { if (st != null) Home = st; if (VpnSettings.Fake || SnapMode) FakeHome(node); Fire(); done(err); }));
            }) { IsBackground = true, Name = "tailscale" }.Start();
        }
        void FakeHome(TsNode node) { foreach (var n in Home.ExitNodes) n.Active = node != null && n.Ip == node.Ip; }

        // ---------------- favourites
        public bool IsFavourite(string host) { return S.Favourites.Any(f => f.Host == host); }
        public void ToggleFavourite(Server s)
        {
            var f = S.Favourites.FirstOrDefault(x => x.Host == s.Host);
            if (f != null) S.Favourites.Remove(f); else if (S.Favourites.Count < 30) S.Favourites.Add(new Favourite { Host = s.Host, Name = s.Name, Country = s.Country, Code = s.Code, City = s.City });
            S.Save(); Fire();
        }

        // ---------------- window
        public void ShowMain(string page)
        {
            if (VpnSettings.Headless || SnapMode) return;
            if (main == null) main = new MainWindow(this);
            if (page != null) main.Show(page);
            main.Show(); if (main.WindowState == WindowState.Minimized) main.WindowState = WindowState.Normal; main.Activate();
        }
        public void MainClosed()
        {
            main = null;
            if (!S.TrayHintShown && !quitting) { S.TrayHintShown = true; S.Save(); Notify("Hallow VPN is still here", "It keeps watching the connection from the tray. Right-click the icon to quit."); }
        }
        public void Quit()
        {
            if (quitting) return; quitting = true;
            if (tray != null) { tray.Visible = false; tray.Dispose(); }
            WriteStatus(false);
            if (main != null) main.Exiting = true;
            Application.Current.Shutdown();
        }
        public void Say(string text) { if (main != null) main.Say(text); }

        // ---------------- settings
        const string RunName = "HallowVPN";
        string RunFile { get { return Path.Combine(VpnSettings.Dir, "test-autostart.txt"); } }
        public bool StartsWithWindows()
        {
            if (VpnSettings.TestDir != null || SnapMode) return File.Exists(RunFile);
            try { using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run")) return k != null && k.GetValue(RunName) != null; } catch { return false; }
        }
        public void SetStartWithWindows(bool on)
        {
            if (SnapMode) return;
            if (VpnSettings.TestDir != null) { if (on) File.WriteAllText(RunFile, "\"HallowVPN.exe\" --tray"); else if (File.Exists(RunFile)) File.Delete(RunFile); return; }
            try { using (var k = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run")) { if (on) k.SetValue(RunName, "\"" + Assembly.GetExecutingAssembly().Location + "\" --tray"); else k.DeleteValue(RunName, false); } } catch { }
        }
        public void SetExtraGames(string text)
        {
            S.ExtraGames = (text ?? "").Split(new[] { ',', ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries).Select(x => Games.Exe(x)).Where(x => x.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).Take(30).ToList();
            S.Save();
        }
        // the split tunnel list from the page's text box; returns the lines it couldn't read
        public List<string> SetSplit(string mode, string text)
        {
            var bad = new List<string>(); var ok = new List<string>();
            foreach (var line in (text ?? "").Split(new[] { '\n', '\r', ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries))
            { var n = Split.Normalize(line); if (n == null) bad.Add(line.Trim()); else if (!ok.Contains(n)) ok.Add(n); }
            S.SplitMode = Split.Modes.Contains(mode) ? mode : Split.All; S.SplitRoutes = ok.Take(Split.MaxRoutes).ToList(); S.Save();
            Log("split: " + S.SplitMode + ", " + S.SplitRoutes.Count + " entries"); Fire();
            return bad;
        }
        public string RemoveProfile()
        {
            if (Phase != Phase.Off) return "Disconnect first.";
            var e = Vpn.RemoveProfile(); if (e == null) { S.ProfileSpec = null; S.Save(); }
            Log("remove profile: " + (e ?? "ok")); Fire(); return e;
        }

        // ---------------- tray, Hub hosting, notifications, accent, updates, crash notice
        void SetupTray()
        {
            tray = new WF.NotifyIcon { Text = "Hallow VPN", Visible = true };
            try { tray.Icon = System.Drawing.Icon.ExtractAssociatedIcon(Assembly.GetExecutingAssembly().Location); } catch { tray.Icon = System.Drawing.SystemIcons.Application; }
            var menu = new WF.ContextMenuStrip();
            menu.Renderer = new WF.ToolStripProfessionalRenderer(TrayColors.For(Mode.IsLight));
            menu.Opening += (s, e) => BuildTrayMenu(menu);
            BuildTrayMenu(menu);
            tray.ContextMenuStrip = menu;
            tray.MouseClick += (s, e) => { if (e.Button == WF.MouseButtons.Left) ShowMain(null); };
        }
        void BuildTrayMenu(WF.ContextMenuStrip menu)
        {
            menu.Items.Clear();
            var tc = TrayColors.For(Mode.IsLight); menu.Renderer = new WF.ToolStripProfessionalRenderer(tc); menu.BackColor = tc.Back; menu.ForeColor = tc.Fore;
            var open = new WF.ToolStripMenuItem("Open Hallow VPN"); open.Font = new System.Drawing.Font(open.Font, System.Drawing.FontStyle.Bold); open.Click += (s, e) => ShowMain("connect"); menu.Items.Add(open);
            foreach (var it in MenuItems().Cast<Dictionary<string, object>>())
            {
                var id = (string)it["id"]; if (id == "-") { menu.Items.Add(new WF.ToolStripSeparator()); continue; }
                var mi = new WF.ToolStripMenuItem((string)it["label"]); object en; if (it.TryGetValue("enabled", out en) && en is bool) mi.Enabled = (bool)en;
                mi.Click += (s, e) => Invoke(id); menu.Items.Add(mi);
            }
            menu.Items.Add(new WF.ToolStripSeparator());
            var quit = new WF.ToolStripMenuItem("Quit (the VPN stays as it is)"); quit.Click += (s, e) => Quit(); menu.Items.Add(quit);
            foreach (WF.ToolStripItem i in menu.Items) i.ForeColor = tc.Fore;
        }
        public void CheckHost()
        {
            if (tray == null) return;
            if (!S.UseHallowIcon) { SetHosted(false, false); return; }
            ThreadPool.QueueUserWorkItem(_ =>
            {
                bool on = false, lists = false;
                try { var r = Family.Send("Hub", "tray-host", 1500); if (r != null && r.StartsWith("ok {")) HubHosting(r.Substring(3), out on, out lists); } catch { }
                ui.BeginInvoke(new Action(() => SetHosted(on && S.UseHallowIcon, lists)));
            });
        }
        // Hub's "tray-host" answer: on = Hub's Hallow icon is showing; lists = Hub's menu has Hallow VPN ("apps" has "VPN", or
        // "generic":true - Hub 2.13+ lists any app answering "menu"). Hallow VPN only hides its own icon when Hub lists it.
        public static void HubHosting(string json, out bool on, out bool lists)
        {
            on = false; lists = false;
            try
            {
                var d = new System.Web.Script.Serialization.JavaScriptSerializer().Deserialize<Dictionary<string, object>>(json); object v;
                on = d != null && d.TryGetValue("on", out v) && v is bool && (bool)v;
                if (!on) return;
                if (d.TryGetValue("generic", out v) && v is bool && (bool)v) { lists = true; return; }
                if (d.TryGetValue("apps", out v) && v is System.Collections.IEnumerable && !(v is string))
                    foreach (var a in (System.Collections.IEnumerable)v) if (a is string && string.Equals((string)a, "VPN", StringComparison.OrdinalIgnoreCase)) lists = true;
            }
            catch { on = false; lists = false; }
        }
        void SetHosted(bool on, bool lists) { hubHosts = on; hubLists = on && lists; if (tray != null && !balloonOwn) tray.Visible = !hubLists; }
        public int Notifications, NotificationsHeld;
        // never anything on screen while a game runs: held notifications are only logged
        public void Notify(string title, string text)
        {
            if (Game != null) { NotificationsHeld++; Log("held (game running): " + title); return; }
            Notifications++; Log("notify: " + title);
            if (tray == null || VpnSettings.TestDir != null) return;
            if (!hubHosts) { tray.ShowBalloonTip(5000, title, text, WF.ToolTipIcon.None); return; }
            ThreadPool.QueueUserWorkItem(_ =>
            {
                if (Family.Error(Family.Send("Hub", "notify VPN\t" + title + "\t" + text, 2000), "no answer") == null) return;
                ui.BeginInvoke(new Action(() => { balloonOwn = true; tray.Visible = true; tray.ShowBalloonTip(5000, title, text, WF.ToolTipIcon.None); var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(7) }; t.Tick += (s, e) => { t.Stop(); balloonOwn = false; SetHosted(hubHosts, hubLists); }; t.Start(); }));
            });
        }
        public void ApplyAccent()
        {
            var a = Accent.Current(S.UseHallowAccent); bool light = Mode.Resolve(S.Look, Accent.ReadMode(), Mode.Sys);
            if (a == Program.AccentHex && light == Mode.IsLight) return;
            Program.LoadTheme(Application.Current);
            if (main != null) main.Rebuild();
        }
        GhRelease pending; public GhRelease PendingUpdate { get { return pending; } }
        public void CheckForUpdate(bool manual)
        {
            if (manual) Say("Checking GitHub for a newer Hallow VPN...");
            new Thread(() =>
            {
                var r = Updater.Latest();
                ui.BeginInvoke(new Action(() =>
                {
                    S.LastUpdateCheck = DateTime.Now.ToString("o"); S.Save();
                    if (r == null) { if (manual) Say("Couldn't reach GitHub - try again later."); return; }
                    if (!Updater.IsNewer(r)) { if (manual) Say("You have the newest version (v" + Program.Version + ")."); return; }
                    pending = r; if (main != null) main.ShowUpdate("Update to v" + r.Version.ToString(2));
                    Say("Hallow VPN v" + r.Version.ToString(2) + " is out - click Update at the bottom left.");
                }));
            }) { IsBackground = true }.Start();
        }
        public void InstallUpdate()
        {
            if (pending == null) return;
            if (!Dialogs.Confirm(main, "Install Hallow VPN v" + pending.Version.ToString(2) + "?", "It downloads from GitHub, checks the file and puts the new version in place. Your settings and account stay; the VPN connection stays as it is.", "Update", false)) return;
            var r = pending; Say("Updating...");
            new Thread(() => { var err = Updater.Install(r); ui.BeginInvoke(new Action(() => Say(err ?? "Installing the update..."))); }) { IsBackground = true }.Start();
        }
        void ShowCrashNotice()
        {
            var f = Crash.Last(); if (f == null) return;
            var name = Path.GetFileName(f); if (name == S.LastCrashShown) return;
            if (Game != null) return;   // never during a game; it's shown next time
            S.LastCrashShown = name; S.Save();
            CrashNotice.Make(f).Show();
        }

        // ---------------- family link: status.json and the pipe
        public Dictionary<string, object> StatusData(bool running)
        {
            var d = new Dictionary<string, object> { { "app", "VPN" }, { "version", Program.Version }, { "updated", Family.Time(DateTime.Now) }, { "running", running },
                { "connected", Phase == Phase.On }, { "connecting", Phase == Phase.Connecting },
                { "server", Phase == Phase.On && Current != null ? Current.Short : null }, { "country", Phase == Phase.On && Current != null ? Current.Country : null },
                { "protocol", Phase == Phase.On ? Proto : ProtoFor(false) }, { "killSwitch", KillSwitchOn }, { "splitMode", S.SplitMode },
                { "since", Phase == Phase.On && Since != null ? Family.Time(Since.Value) : null } };
            d["paused"] = PausedForGame; d["game"] = Game; d["gameMode"] = EffGame();
            d["home"] = Home.Current == null ? null : Home.Current.Name; d["account"] = HasAccount; d["autoReconnect"] = S.AutoReconnect;
            d["lastCrash"] = Crash.LastTime() == null ? null : Family.Time(Crash.LastTime().Value);
            Status11(d); Status12(d);
            if (VpnSettings.TestDir != null) { lock (log) d["log"] = log.ToArray(); d["notifications"] = Notifications; d["held"] = NotificationsHeld; }
            return d;
        }
        public void WriteStatus(bool running = true) { if (!SnapMode) Family.WriteStatus(Family.Json(StatusData(running))); }
        string Command(string line)
        {
            int sp = line.IndexOf(' '); var cmd = sp < 0 ? line : line.Substring(0, sp); var arg = sp < 0 ? "" : line.Substring(sp + 1).Trim();
            switch (cmd)
            {
                case "ping": return "ok " + Family.Json(new Dictionary<string, object> { { "app", "VPN" }, { "version", Program.Version } });
                case "diagnose": { var steps = RunDiagnosisNow(); return "ok " + Family.Json(new Dictionary<string, object> { { "steps", steps.Select(x => new Dictionary<string, object> { { "id", x.Id }, { "status", x.Status } }).ToList() }, { "summary", Diagnosis.Summary(steps) } }); }
                case "protocol": return OnUi(() => { if (arg.Length > 0) { var p = arg.ToLowerInvariant(); if (p != Protocols.Auto && p != Protocols.NordLynx && p != Protocols.Ikev2) return "error use auto, nordlynx or ikev2"; SetProtocol(p); } return "ok " + Family.Json(new Dictionary<string, object> { { "choice", S.Protocol }, { "next", ProtoFor(false) }, { "nordlynxReady", NordLynxReady } }); });
                case "leaktest": return OnUi(() => { if (Phase != Phase.On) return "error not connected"; RunLeakCheck(); return "ok checking"; });
                case "status": return OnUi(() => { var d = StatusData(true); Family.WriteStatus(Family.Json(d)); return "ok " + Family.Json(d); });
                case "connect":
                    return OnUi(() =>
                    {
                        var t = Target.Parse(arg); if (t == null) return "error unknown server - use fastest, gaming, p2p, a country code (US) or a server (us1234)";
                        if (t.Kind == "country" && Book.FindCountry(t.Value) == null) return "error Nord's country list doesn't have " + t.Value;
                        var e = Connect(t, true); return e == null ? "ok connecting" : "error " + e;
                    });
                case "disconnect": return OnUi(() => { Disconnect(true); return "ok"; });
                case "open": return OnUi(() => { var p = arg.Length > 0 ? MainWindow.PageId(arg) : null; if (arg.Length > 0 && p == null) return "error no page called " + arg; ShowMain(p); return "ok"; });
                case "menu": return OnUi(() => "ok " + Family.Json(new Dictionary<string, object> { { "app", "VPN" }, { "items", MenuItems() } }));
                case "invoke": return OnUi(() => Invoke(arg));
                case "quit": new Thread(() => { Thread.Sleep(300); ui.BeginInvoke(new Action(Quit)); }) { IsBackground = true }.Start(); return "ok";
            }
            if (VpnSettings.TestDir != null && cmd.StartsWith("test-")) return OnUi(() => TestCommand(cmd, arg));
            return "error unknown command";
        }
        string OnUi(Func<string> f)
        {
            string r = "error no answer in time";
            var op = ui.BeginInvoke(new Action(() => { try { r = f(); } catch (Exception e) { r = "error " + Family.OneLine(e.Message); } }));
            return op.Wait(TimeSpan.FromSeconds(5)) == DispatcherOperationStatus.Completed ? r : "error no answer in time";
        }
        public List<object> MenuItems()
        {
            bool busy = Phase == Phase.Connecting || Phase == Phase.Disconnecting;
            var l = new List<object> {
                new Dictionary<string, object> { { "id", "connect" }, { "label", Phase == Phase.On ? "Connected" + (Current != null ? " - " + Current.Short : "") : "Connect (fastest)" }, { "enabled", Phase == Phase.Off && HasAccount } },
                new Dictionary<string, object> { { "id", "gaming" }, { "label", "Best for gaming" }, { "enabled", !busy && HasAccount } },
                new Dictionary<string, object> { { "id", "p2p" }, { "label", "Best for torrents (P2P)" }, { "enabled", !busy && HasAccount } } };
            foreach (var f in S.Favourites.Take(3)) l.Add(new Dictionary<string, object> { { "id", "fav:" + f.Host }, { "label", "★ " + (f.Name ?? f.Host) }, { "enabled", !busy && HasAccount } });
            l.Add(new Dictionary<string, object> { { "id", "disconnect" }, { "label", "Disconnect" }, { "enabled", Phase != Phase.Off } });
            l.Add(new Dictionary<string, object> { { "id", "-" } });
            l.Add(new Dictionary<string, object> { { "id", "servers" }, { "label", "Servers" } });
            l.Add(new Dictionary<string, object> { { "id", "killswitch" }, { "label", "Kill switch: " + (KillSwitchOn ? "on" : "off") } });
            return l;
        }
        string Invoke(string id)
        {
            switch (id)
            {
                case "connect": { var e = Connect(Target.Parse(S.LastTarget), false); return e == null ? "ok" : "error " + e; }
                case "gaming": { var e = Connect(new Target { Kind = "gaming" }, true); return e == null ? "ok" : "error " + e; }
                case "p2p": { var e = Connect(new Target { Kind = "p2p" }, true); return e == null ? "ok" : "error " + e; }
                case "disconnect": Disconnect(true); return "ok";
                case "servers": ShowMain("servers"); return "ok";
                case "killswitch": ShowMain("killswitch"); return "ok";
            }
            if (id.StartsWith("fav:")) { var t = Target.Parse("server:" + id.Substring(4)); if (t == null || !IsFavourite(t.Value)) return "error no such favourite"; var e = Connect(t, true); return e == null ? "ok" : "error " + e; }
            return "error unknown item";
        }
        // developer tests only (HALLOW_VPN_TESTDIR): what a person would click, without windows. With HALLOW_VPN_FAKE=1 the VPN layer,
        // firewall, Tailscale and games are fakes - nothing real is created, dialled or changed.
        string TestCommand(string cmd, string arg)
        {
            switch (cmd)
            {
                case "test-autostart": SetStartWithWindows(arg == "on"); return "ok " + StartsWithWindows();
                case "test-split": { var p = arg.Split(new[] { ' ' }, 2); var bad = SetSplit(p[0], p.Length > 1 ? p[1] : ""); return "ok " + Family.Json(new Dictionary<string, object> { { "mode", S.SplitMode }, { "routes", S.SplitRoutes }, { "bad", bad } }); }
                case "test-gamemode": S.GameMode = arg; S.Fix(); S.Save(); return "ok " + S.GameMode;
                case "test-check": CheckNow(); return "ok " + Phase;
                case "test-tick": { int n; if (!int.TryParse(arg, out n)) n = 1; for (int i = 0; i < n; i++) Tick(); return "ok " + Phase; }
            }
            if (!VpnSettings.Fake) return "error unknown test command";
            var fv = (FakeVpn)Vpn; var fg = (FakeGames)GameSrc; var ff = (FakeFirewall)Fw;
            switch (cmd)
            {
                case "test-account": { if (arg == "none") { Account.Remove(); return "ok"; } var p = arg.Split(' '); var e = Account.Save(p[0], p.Length > 1 ? p[1] : ""); return e == null ? "ok" : "error " + e; }
                case "test-game": fg.Game = arg == "none" ? null : arg; CheckNow(); return "ok " + Phase + (PausedForGame ? " paused" : "") + (SwitchedForGame ? " switched" : "");
                case "test-drop": fv.Drop(); CheckNow(); return "ok " + Phase;
                case "test-dialerror": fv.DialError = arg == "none" ? null : arg; return "ok";
                case "test-calls": lock (fv.Calls) return "ok " + Family.Json(fv.Calls);
                case "test-clock": { var add = int.Parse(arg); Now = () => DateTime.Now.AddSeconds(add); return "ok"; }
                case "test-killswitch": ff.Rules.Clear(); if (arg == "on") foreach (var r in KillSwitch.Rules()) ff.Rules.Add(r.Name); KillSwitchRules = Fw.InstalledRules(); OldKillSwitchRules = Fw.OldRules(); Fire(); return "ok " + KillSwitchOn;
                case "test-current": return "ok " + (Current == null ? "none" : Current.Host) + " " + (CurrentTarget == null ? "-" : CurrentTarget.ToString());
                case "test-net":
                    {   // wired / none / wifi <name> / open <name>
                        var fn = (FakeNetInfo)Net; var p = arg.Split(new[] { ' ' }, 2);
                        fn.Now = p[0] == "wired" ? new NetNow { Kind = "wired" } : p[0] == "none" ? new NetNow() : new NetNow { Kind = "wifi", Ssid = p.Length > 1 ? p[1] : "x", Open = p[0] == "open" };
                        CheckNow(); return "ok " + Phase;
                    }
                case "test-autonet": S.AutoConnectUntrusted = arg == "on"; S.Save(); return "ok " + S.AutoConnectUntrusted;
                case "test-trust": { var e = TrustNetwork(arg, true); return e == null ? "ok " + string.Join(",", S.TrustedNetworks) : "error " + e; }
                case "test-trustwired": S.TrustWired = arg == "on"; S.Save(); return "ok " + S.TrustWired;
                case "test-ipv6": ((FakeNetInfo)Net).Set(arg == "on"); CheckAdminThings(); return "ok";
                case "test-dnsbad": ((FakeNetInfo)Net).VpnDnsGood = arg != "on"; return "ok";
                case "test-realip": { var r = FetchRealIpNow(); return "ok " + (RealIp ?? "none") + " " + (r ?? ""); }
                case "test-health": { PollHealthNow(); return "ok " + HSent + " " + HRecv + " " + LivePing; }
                case "test-repair": { var e = arg == "profile" ? RecreateProfile() : RefreshServers(); return e == null ? "ok" : "error " + e; }
                case "test-setuptest": { var e = StartSetupTest(); return e == null ? "ok " + SetupTest : "error " + e; }
                case "test-setup": return "ok " + S.SetupDone + " " + (SetupTest ?? "none");
                case "test-wg": ((FakeWgSetup)Wg).In = arg == "on"; CheckAdminThings(); return "ok";
                case "test-token": { string e = null; SaveAccessToken(arg, err => e = err, true); return e == null ? "ok" : "error " + e; }
                case "test-tunnel": return "ok " + Proto + " " + Tunnel.IsUp() + " " + string.Join(";", ((FakeTunnel)Tunnel).Calls);
                case "test-tunneldrop": ((FakeTunnel)Tunnel).Drop(); CheckNow(); return "ok " + Phase;
                case "test-tunnelerror": ((FakeTunnel)Tunnel).UpError = arg == "none" ? null : arg; return "ok";
                case "test-oldks": ff.Rules.Clear(); if (arg == "on") foreach (var r in KillSwitch.Legacy()) ff.Rules.Add(r.Name); KillSwitchRules = Fw.InstalledRules(); OldKillSwitchRules = Fw.OldRules(); Fire(); return "ok " + KillSwitchOn + " " + KillSwitchOld;
                case "test-ksinstall": KillSwitchAsync(true, err => { }); return "ok";
                case "test-pbk": { var t = Pbk.Read(Vpn.PbkPath); return "ok " + (t == null ? "none" : Family.Json(Pbk.Check(t, VpnScript.Entry, S.SplitMode))); }
            }
            return "error unknown test command";
        }

        // ---------------- --snap: made-up state
        public void SnapSetup(bool connected, bool nordlynx = false)
        {
            snapAccount = connected; snapCreds = connected ? new Credentials { User = "x", Pass = "y" } : null;
            ((FakeCa)Ca).In = connected; ((FakeWgSetup)Wg).In = connected; if (nordlynx) S.Protocol = Protocols.NordLynx; CheckAdminThings(); CheckHome();
            KillSwitchRules = connected ? KillSwitch.Rules().Count : 0; FirewallOn = true;
            S.Favourites = new List<Favourite> { new Favourite { Host = "nl1234.nordvpn.com", Name = "Netherlands #1234", Country = "Netherlands", Code = "NL", City = "Amsterdam" }, new Favourite { Host = "de5678.nordvpn.com", Name = "Germany #5678", Country = "Germany", Code = "DE", City = "Frankfurt" } };
            S.SplitMode = Split.Only; S.SplitRoutes = new List<string> { "netflix.com", "192.0.2.0/24", "198.51.100.7/32" }; S.GameMode = Games.ModeLowPing;
            S.ExtraGames = new List<string> { "cs2" };
            S.AutoConnectUntrusted = true; S.TrustedNetworks = new List<string> { "Home-5G", "Office" }; S.SetupDone = connected;
            ((FakeNetInfo)Net).Now = new NetNow { Kind = "wifi", Ssid = connected ? "Cafe Guest" : "Home-5G", Open = connected };
            ((FakeNetInfo)Net).Set(!connected); NetObserved(Net.Current());
            if (connected)
            {
                S.SplitMode = Split.All; var e = Connect(Target.Parse("country:NL"), false); S.SplitMode = Split.Only;
                Since = DateTime.Now.AddMinutes(-47).AddSeconds(-12);
                ((FakeVpn)Vpn).UpAt = Since.Value; ((FakeVpn)Vpn).Sent = 182452224; ((FakeVpn)Vpn).Received = 1932735283;
                PollHealthNow(); LivePing = 23;
            }
            else { S.LastDialCode = 13801; S.LastDialWhen = "21 Nov 20:14"; CheckAdminThings(); }
        }
        public void SnapOldKillSwitch() { KillSwitchRules = 0; OldKillSwitchRules = KillSwitch.Legacy().Count; Fire(); }
        public void SnapPaused() { var fg = (FakeGames)GameSrc; S.GameMode = Games.ModePause; KillSwitchRules = 0; fg.Game = "Delta Force"; CheckNow(); }
    }
}
