// Hallow VPN 1.2 - the Controller's protocol choice (Automatic / NordLynx / IKEv2), the NordLynx access token, installing WireGuard
// for Windows on a click, and the kill switch upgrade.
// NordLynx connects and disconnects ask Windows (UAC) - so anything Hallow VPN does BY ITSELF never uses NordLynx while a game runs
// (it waits), and with "Automatic" it uses IKEv2 for automatic connects when IKEv2 is ready (no prompt out of nowhere).
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace HallowVPN
{
    public partial class Controller
    {
        public const string ProtoIke = "IKEv2", ProtoWg = "NordLynx";
        public readonly ITunnel Tunnel; public readonly IWgSetup Wg; public readonly INordAuth Auth;
        public string Proto = ProtoIke; string connectingProto = ProtoIke;
        public bool WgInstalled; public int OldKillSwitchRules;
        public bool NordLynxReady { get { return WgInstalled && (NordLynx.Exists || SnapMode); } }
        public bool IkeReady { get { return HasAccount && CaInstalled; } }
        bool AnyUp() { return Vpn.IsUp() || Tunnel.IsUp(); }

        // which protocol a connect uses. automatic = Hallow VPN does it by itself (auto-reconnect, auto-connect, after a game)
        public string ProtoFor(bool automatic)
        {
            switch (S.Protocol)
            {
                case Protocols.NordLynx: return ProtoWg;
                case Protocols.Ikev2: return ProtoIke;
            }
            if (!NordLynxReady) return ProtoIke;
            if (S.SplitMode == Split.Only && IkeReady) return ProtoIke;   // NordLynx here always tunnels everything
            if (automatic && IkeReady) return ProtoIke;                     // no UAC prompt out of nowhere
            return ProtoWg;
        }
        // what a game start does; with NordLynx connected, switching or pausing would ask Windows (UAC) mid-game - so nothing
        public string EffGame()
        {
            if (Phase != Phase.Off && Proto == ProtoWg) return Games.ModeOff;
            return Games.Effective(S.SplitMode, S.GameMode, KillSwitchOn);
        }
        public void SetProtocol(string p) { S.Protocol = Protocols.Fix(p); S.Save(); Log("protocol: " + S.Protocol); Fire(); }

        // the access token -> Nord's NordLynx key (one request to api.nordvpn.com, on the user's click) -> nordlynx.dat (DPAPI)
        public void SaveAccessToken(string token, Action<string> done, bool sync = false)
        {
            var bad = NordLynx.CheckToken(token); if (bad != null) { done(bad); return; }
            Log("NordLynx: asking Nord for the key");
            Action work = () =>
            {
                string err = null;
                try
                {
                    var key = NordLynx.ParseKey(Auth.Credentials(token.Trim()));
                    err = key == null ? "Nord's answer had no NordLynx key - is NordVPN part of this Nord account?" : NordLynx.Save(token, key);
                    key = null;
                }
                catch (Exception e) { err = e.Message; }
                Action fin = () => { Log("NordLynx key: " + (err == null ? "saved" : "not saved")); Fire(); done(err); };
                if (sync || SnapMode) fin(); else ui.BeginInvoke(fin);
            };
            if (sync || SnapMode) work(); else new Thread(() => work()) { IsBackground = true, Name = "nordlynx" }.Start();
        }
        public void RemoveAccessToken() { NordLynx.Remove(); Log("NordLynx key removed"); Fire(); }

        // WireGuard for Windows: download on click, check (BLAKE2b list + Authenticode "WireGuard LLC"), run the MSI (Windows asks)
        public bool WgBusy;
        public void InstallWireGuardAsync(Action<string> progress, Action<string> done)
        {
            if (WgBusy) return; WgBusy = true; Fire();
            new Thread(() =>
            {
                string file, err = Wg.Download(out file);
                if (err == null) { ui.BeginInvoke(new Action(() => progress("Checked: signed by WireGuard LLC and matching WireGuard's list. Windows will ask for permission..."))); err = Wg.Install(file); }
                bool now = false; try { now = Wg.Installed(); } catch { }
                ui.BeginInvoke(new Action(() => { WgBusy = false; WgInstalled = now; Log("WireGuard install: " + (err ?? "done")); Fire(); done(err); }));
            }) { IsBackground = true, Name = "wireguard" }.Start();
        }

        void Status12(Dictionary<string, object> d)
        {
            d["adapter"] = Phase == Phase.On ? (Proto == ProtoWg ? NordLynx.Tunnel : VpnScript.Entry) : null;   // Hallow Torrents binds to it
            d["protocolChoice"] = S.Protocol;
            d["dnsFilter"] = DnsFilters.IsOn(S.DnsFilter) ? S.DnsFilter : "off";   // 1.3 Threat protection
            d["killSwitchUpgrade"] = KillSwitchOld;
        }
    }

    public static class Protocols
    {
        public const string Auto = "auto", NordLynx = "nordlynx", Ikev2 = "ikev2";
        public static string Fix(string p) { var v = (p ?? "").Trim().ToLowerInvariant(); return v == NordLynx || v == Ikev2 ? v : Auto; }
        public static string Label(string p) { p = Fix(p); return p == NordLynx ? "NordLynx" : p == Ikev2 ? "IKEv2" : "Automatic"; }
    }
}
