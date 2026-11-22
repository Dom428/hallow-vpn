// Hallow VPN 1.1 - the Controller's new parts: DNS protection in the profile, the leak check, your real IP (only on a click),
// connection health, untrusted networks / auto-connect, "Fix my connection" and the first-run setup's test.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Windows.Threading;

namespace HallowVPN
{
    public partial class Controller
    {
        // ---------------- DNS in the per-user profile (no admin): Nord's DNS and the VPN first (Pbk.cs). Runs on the connect thread.
        public string DnsNote;
        void ProtectDns()
        {
            if (Vpn.PbkPath == null) return;
            bool changed; var e = Pbk.Ensure(Vpn.PbkPath, VpnScript.Entry, S.SplitMode, Path.Combine(VpnSettings.Dir, "rasphone.pbk.bak"), out changed);
            DnsNote = e;
            if (e != null) Log("DNS protection: " + e);
            else if (changed) Log("profile DNS: " + (S.SplitMode == Split.Only ? "automatic (split tunnel)" : "NordVPN's DNS, VPN first"));
        }

        // ---------------- the leak check (while connected): Nord's IP check + the DNS servers in use + IPv6
        public string LeakState; public DateTime? LeakAt; public List<string> LeakNotes = new List<string>(); public NetCheck.DnsReport LeakDns; public bool Ipv6;
        bool leakRunning;
        public void RunLeakCheck()
        {
            if (leakRunning || Phase != Phase.On) return;
            leakRunning = true;
            var prot = IpProtected; bool ks = KillSwitchOn, split = S.SplitMode == Split.Only;
            var fn = Net as FakeNetInfo; if (fn != null) fn.VpnUp = true;
            Action work = () =>
            {
                List<NetIf> ifs; try { ifs = Net.Interfaces(); } catch { ifs = new List<NetIf>(); }
                var dns = NetCheck.Dns(ifs, a => { try { return Net.BestInterface(a); } catch { return -1; } });
                var v6 = NetCheck.HasGlobalIpv6(ifs) && Proto != ProtoWg;   // NordLynx takes ::/0 into its tunnel too (and drops it): no IPv6 around it
                var notes = new List<string>();
                var v = NetCheck.Verdict(prot, dns, v6, ks, split, notes);
                Action fin = () => { leakRunning = false; if (Phase != Phase.On) return; LeakState = v; LeakAt = Now(); LeakNotes = notes; LeakDns = dns; Ipv6 = v6; Log("leak check: " + v); Fire(); };
                if (SnapMode) fin(); else ui.BeginInvoke(fin);
            };
            if (SnapMode) work(); else ThreadPool.QueueUserWorkItem(_ => work());
        }

        // ---------------- your real IP: only when you click. While connected that one request deliberately goes out through your normal
        // network (bound to its address), so you can compare; never saved, never in status.
        public string RealIp, RealIpIsp, RealIpError; public bool RealIpLoading;
        public void FetchRealIp()
        {
            if (RealIpLoading) return; RealIpLoading = true; RealIp = null; RealIpError = null; Fire();
            if (SnapMode) { FetchRealIpNow(); Fire(); return; }
            ThreadPool.QueueUserWorkItem(_ => { FetchRealIpNow(); ui.BeginInvoke(new Action(Fire)); });
        }
        public string FetchRealIpNow()
        {
            string err = null, ip = null, isp = null;
            try
            {
                string json = SnapMode || VpnSettings.Fake ? FakeData.RealIpJson : Phase != Phase.On ? Api.Get(Nord.IpUrl) : Outside(Nord.IpUrl);
                string country; bool? prot; ip = Nord.ParseIp(json, out country, out prot);
                var d = new System.Web.Script.Serialization.JavaScriptSerializer().DeserializeObject(json) as Dictionary<string, object>; object v;
                if (d != null && d.TryGetValue("isp", out v)) isp = v as string;
                if (ip == null) err = "Nord's IP check didn't say.";
            }
            catch (Exception e)
            {
                err = KillSwitchOn && Phase == Phase.On ? "The kill switch blocks traffic outside the VPN, so your real address can't be asked for while connected - that's the kill switch working." : "Couldn't ask Nord's IP check: " + e.Message;
            }
            RealIp = ip; RealIpIsp = isp; RealIpError = err; RealIpLoading = false;
            return err;
        }
        string Outside(string url)
        {
            var phys = Net.Interfaces().Where(i => i.Up && !i.Vpn && !i.Tunnel && i.Gateway && i.V4.Count > 0).OrderBy(i => i.Metric < 0 ? 9999 : i.Metric).FirstOrDefault();
            if (phys == null) throw new Exception("no network outside the VPN was found");
            var local = phys.V4.FirstOrDefault(a => !a.ToString().StartsWith("169.254.")); if (local == null) throw new Exception("no address outside the VPN");
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            var req = (HttpWebRequest)WebRequest.Create(url);
            req.UserAgent = "HallowVPN/" + Program.Version; req.Timeout = 10000; req.ReadWriteTimeout = 10000; req.KeepAlive = false; req.ConnectionGroupName = "hallow-outside";
            req.ServicePoint.BindIPEndPointDelegate = (sp, remote, retry) => remote.AddressFamily == AddressFamily.InterNetwork && retry < 2 ? new IPEndPoint(local, 0) : null;
            try { using (var resp = (HttpWebResponse)req.GetResponse()) using (var r = new StreamReader(resp.GetResponseStream(), Encoding.UTF8)) return r.ReadToEnd(); }
            finally { req.ServicePoint.BindIPEndPointDelegate = null; }
        }

        // ---------------- connection health: bytes / duration (RasGetConnectionStatistics) and a live ping (at most every 4 s).
        // The window asks every 2 s, only while it's showing the Connection page and isn't minimised.
        public long HSent = -1, HRecv = -1; public TimeSpan HDur; public int LivePing = -1; DateTime lastPing = DateTime.MinValue; bool healthBusy;
        public event Action HealthChanged;
        public void PollHealth()
        {
            if (Phase != Phase.On || healthBusy) return;
            healthBusy = true;
            ThreadPool.QueueUserWorkItem(_ => { try { PollHealthNow(); } catch { } ui.BeginInvoke(new Action(() => { healthBusy = false; var h = HealthChanged; if (h != null) h(); })); });
        }
        public void PollHealthNow()
        {
            long s, r; TimeSpan d;
            try
            {
                if (Proto == ProtoWg) { if (Tunnel.Stats(out s, out r)) { HSent = s; HRecv = r; HDur = Since == null ? TimeSpan.Zero : Now() - Since.Value; } }
                else if (Vpn.Stats(out s, out r, out d)) { HSent = s; HRecv = r; HDur = d; }
            }
            catch { }
            var cur = Current;
            if (cur != null && (DateTime.Now - lastPing).TotalSeconds >= 4) { lastPing = DateTime.Now; try { LivePing = Pinger.Ping(cur); } catch { LivePing = -1; } }
        }
        public bool SlowServer { get { return Phase == Phase.On && Current != null && Diagnosis.Slow(LivePing, Current.Load); } }

        // ---------------- networks and auto-connect
        public NetNow NetCurrent; public bool NetUntrusted; string netKey, userOffNet; bool pendingAuto;
        void NetObserved(NetNow n)
        {
            if (n == null) return;
            NetCurrent = n; var was = NetUntrusted; NetUntrusted = NetCheck.Untrusted(n, S.TrustedNetworks, S.TrustWired);
            bool changed = n.Key != netKey;
            if (changed) { netKey = n.Key; userOffNet = null; pendingAuto = false; Log("network: " + n.Kind + (NetUntrusted ? " (untrusted)" : " (trusted)")); }
            if (SnapMode) return;
            if (changed || was != NetUntrusted) Fire();
            if (changed && NetUntrusted && S.AutoConnectUntrusted) AutoConnect(n.Kind == "wifi" ? (n.Open ? "open Wi-Fi" : "untrusted Wi-Fi") : "untrusted network");
        }
        void AutoConnect(string why)
        {
            if (Phase != Phase.Off || !HasAccount || Home.Current != null || !NetUntrusted || !S.AutoConnectUntrusted) return;
            if (netKey != null && userOffNet == netKey) return;   // you disconnected on this network: leave it
            if (Game != null && EffGame() == Games.ModePause) { pendingAuto = true; Log("auto-connect waits for the game to close"); return; }
            Log("auto-connect: " + why);
            if (Connect(Target.Parse(S.LastTarget), false) == null) Notify("Connecting on an untrusted network", "Hallow VPN connects by itself because this network isn't on your trusted list.");
        }
        public string TrustNetwork(string ssid, bool trust)
        {
            if (string.IsNullOrEmpty(ssid) || ssid.Length > 32) return "That isn't a Wi-Fi name.";
            if (trust && NetCurrent != null && NetCurrent.Ssid == ssid && NetCurrent.Open) return "This Wi-Fi is open (no password): anyone nearby can see its traffic, so it can't be trusted.";
            S.TrustedNetworks.RemoveAll(x => x == ssid); if (trust) S.TrustedNetworks.Add(ssid); S.Fix(); S.Save();
            if (NetCurrent != null) NetUntrusted = NetCheck.Untrusted(NetCurrent, S.TrustedNetworks, S.TrustWired);
            Fire(); return null;
        }
        public void TrustChanged() { if (NetCurrent != null) NetUntrusted = NetCheck.Untrusted(NetCurrent, S.TrustedNetworks, S.TrustWired); Fire(); }

        // ---------------- Fix my connection
        public List<DiagStep> Diag; public bool DiagRunning; public DateTime? DiagAt;
        public List<DiagStep> RunDiagnosisNow()
        {
            string e; string host = Current != null ? Current.Host : S.LastServer;
            if (host == null) { var l = Book.List(0, false, out e); if (l.Count > 0) host = l.OrderBy(x => x.Load).First().Host; }
            var inp = new DiagInput
            {
                Account = HasAccount, CaInstalled = CaInstalled, CaChecked = CaChecked, PbkText = SnapMode ? (HasAccount ? Pbk.Apply(Pbk.SampleEntry(VpnScript.Entry, "nl1234.nordvpn.com"), VpnScript.Entry, Pbk.Expected(Split.All)) : null) : Pbk.Read(Vpn.PbkPath),
                SplitMode = SnapMode ? Split.All : S.SplitMode, Host = host, Resolve = resolve, Ping = s => Pinger.Ping(s),
                LastDialCode = S.LastDialCode, LastDialWhen = S.LastDialWhen, HomeNode = Home.Current == null ? null : Home.Current.Name,
                KillSwitchRules = KillSwitchOn ? KillSwitch.Rules().Count : KillSwitchRules + OldKillSwitchRules, KillSwitchAll = KillSwitch.Rules().Count, FirewallOn = FirewallOn, Connected = Phase == Phase.On,
                ProtocolChoice = S.Protocol, WgInstalled = WgInstalled, NordLynxKey = NordLynx.Exists || SnapMode, OldKillSwitch = KillSwitchOld
            };
            return Diagnosis.Run(inp);
        }
        public void RunDiagnosis(Action done)
        {
            if (DiagRunning) return;
            DiagRunning = true; if (!SnapMode) Fire();
            Action work = () =>
            {
                List<DiagStep> r; try { r = RunDiagnosisNow(); } catch (Exception ex) { r = new List<DiagStep> { new DiagStep("error", "The checks") { Status = "warn", Text = "They stopped: " + ex.Message } }; }
                Action fin = () => { Diag = r; DiagAt = DateTime.Now; DiagRunning = false; Log("checks: " + Diagnosis.Summary(r)); Fire(); if (done != null) done(); };
                if (SnapMode) fin(); else ui.BeginInvoke(fin);
            };
            if (SnapMode) work(); else new Thread(() => work()) { IsBackground = true, Name = "checks" }.Start();
        }
        // repair: remove and set up the per-user profile again (no admin), with DNS protection
        public string RecreateProfile()
        {
            if (Phase != Phase.Off) return "Disconnect first - the connection is in use.";
            string host = S.LastServer, e;
            if (host == null) { var l = Book.List(0, false, out e); if (l.Count == 0) return e ?? "No Nord server list yet - refresh it first."; host = l.OrderBy(x => x.Load).First().Host; }
            var rm = Vpn.RemoveProfile(); if (rm != null) return rm;
            List<string> failed = null; List<string> routes = null;
            if (S.SplitMode == Split.Only) routes = Split.Routes(S.SplitRoutes, SnapMode || VpnSettings.Fake ? (Func<string, IPAddress[]>)(h => new[] { IPAddress.Parse("198.51.100.7") }) : Split.Dns, out failed);
            var spec = Split.Spec(host, S.SplitMode, routes);
            var err = Vpn.ApplyProfile(spec);
            if (err != null) { S.ProfileSpec = null; S.Save(); Log("recreate profile: " + err); return err; }
            S.ProfileSpec = spec.Key; S.Save(); ProtectDns(); Log("profile recreated");
            return DnsNote;
        }
        public void RepairAsync(string what, Action<string> done)
        {
            Log("repair: " + what);
            Action work = () =>
            {
                string err; try { err = what == "profile" ? RecreateProfile() : RefreshServers(); } catch (Exception ex) { err = ex.Message; }
                Action fin = () => { Fire(); done(err); RunDiagnosis(null); };
                if (SnapMode) fin(); else ui.BeginInvoke(fin);
            };
            if (SnapMode) work(); else new Thread(() => work()) { IsBackground = true, Name = "repair" }.Start();
        }
        public string RefreshServers() { string e; Book.List(0, true, out e); Book.LoadCountries(true, out e); return null; }

        // ---------------- the first-run setup's step 3: connect to Fastest and say pass / fail
        public string SetupTest;   // null / running / pass / fail
        public string StartSetupTest()
        {
            if (Phase == Phase.On) { SetupTest = "pass"; Fire(); return null; }
            SetupTest = "running";
            var e = Connect(new Target { Kind = "fastest" }, false);
            if (e != null) { SetupTest = "fail"; Fire(); }
            return e;
        }

        void Status11(Dictionary<string, object> d)
        {
            d["p2p"] = Phase == Phase.On && Current != null && Current.P2P;
            d["leakCheck"] = Phase == Phase.On ? LeakState : null;
            d["leakCheckAt"] = Phase == Phase.On && LeakAt != null ? Family.Time(LeakAt.Value) : null;
            d["setupDone"] = S.SetupDone;
            d["untrustedNetwork"] = NetCurrent == null ? (object)null : NetUntrusted;
            d["autoConnect"] = S.AutoConnectUntrusted;
        }
    }
}
