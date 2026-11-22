// Hallow VPN 1.1 tests: the phonebook DNS edit (on temp copies only - never the real rasphone.pbk), P2P servers, the leak verdict,
// untrusted networks, "Fix my connection" with fakes, health numbers - and read-only looks at this PC (Wi-Fi state, interfaces,
// metrics). The pipe part (auto-connect, leak test, setup test, connect p2p...) runs against the fake test copy in VpnTests.cs.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;

namespace HallowVPN.Tests
{
    public static class V11
    {
        static Action<bool, string> Ok;
        public static void All(Action<bool, string> ok, string dir, string fx)
        {
            Ok = ok;
            PbkTests(dir); P2PTests(fx); NetTests(); DiagTests(); RealReadOnly();
        }

        static void PbkTests(string dir)
        {
            var e = VpnScript.Entry;
            var other = "[Work VPN]\r\nType=2\r\nIpNameAssign=1\r\nIpDnsAddress=0.0.0.0\r\nComment=café\r\n\r\n";
            var text = other + Pbk.SampleEntry(e, "nl1234.nordvpn.com") + "[Zeta]\r\nIpInterfaceMetric=7\r\n";
            var bad = Pbk.Check(text, e, Split.All);
            Ok(bad.Count == 4 && bad.Any(b => b.Contains("NordVPN's servers")) && bad.Any(b => b.Contains("metric")), "phonebook check: a fresh profile's DNS isn't Nord's yet (" + bad.Count + " things)");
            Ok(Pbk.Check(text, "Nope", Split.All).Single().StartsWith("There's no"), "a missing entry is reported");
            var next = Pbk.Apply(text, e, Pbk.Expected(Split.All));
            var s = Pbk.Section(next, e);
            Ok(Pbk.Value(s, "IpNameAssign") == "2" && Pbk.Value(s, "IpDnsAddress") == "103.86.96.100" && Pbk.Value(s, "IpDns2Address") == "103.86.99.100" && Pbk.Value(s, "IpInterfaceMetric") == "1", "Nord's DNS (103.86.96.100, 103.86.99.100) and the VPN first (metric 1)");
            Ok(Pbk.Check(next, e, Split.All).Count == 0, "after the edit the check is clean");
            Ok(next.StartsWith(other) && next.EndsWith("[Zeta]\r\nIpInterfaceMetric=7\r\n"), "other connections in the phonebook are untouched");
            Ok(Pbk.Value(Pbk.Section(next, "Work VPN"), "IpDnsAddress") == "0.0.0.0" && Pbk.Value(Pbk.Section(next, "Zeta"), "IpInterfaceMetric") == "7", "even keys with the same names in other entries");
            Ok(Pbk.Value(s, "PhoneNumber") == "nl1234.nordvpn.com" && Pbk.Value(s, "Ipv6NameAssign") == "1" && next.Split('\n').Length == text.Split('\n').Length, "nothing else in the entry changes, no lines added when the keys are there");
            var noKeys = "[" + e + "]\r\nType=2\r\nVpnStrategy=7\r\n\r\nDEVICE=vpn\r\nPhoneNumber=de1.nordvpn.com\r\n";
            var added = Pbk.Apply(noKeys, e, Pbk.Expected(Split.All));
            Ok(added.Contains("VpnStrategy=7\r\nIpNameAssign=2\r\nIpDnsAddress=103.86.96.100\r\nIpDns2Address=103.86.99.100\r\nIpInterfaceMetric=1\r\n\r\nDEVICE=vpn"), "missing keys are added at the end of the entry's first block");
            Ok(Pbk.Apply(Pbk.Apply(noKeys, e, Pbk.Expected(Split.All)), e, Pbk.Expected(Split.All)) == added, "applying twice changes nothing");
            var split = Pbk.Apply(next, e, Pbk.Expected(Split.Only)); var ss = Pbk.Section(split, e);
            Ok(Pbk.Value(ss, "IpNameAssign") == "1" && Pbk.Value(ss, "IpInterfaceMetric") == "0" && Pbk.Check(split, e, Split.Only).Count == 0, "split tunnel \"Only these\": DNS back to automatic (Nord's DNS isn't reachable outside the tunnel)");
            Ok(Pbk.Apply(text, "Nope", Pbk.Expected(Split.All)) == null, "no entry: nothing applied");
            Ok(Pbk.Check(text.Replace("VpnStrategy=7", "VpnStrategy=2"), e, Split.All).Any(x => x.Contains("IKEv2")) && Pbk.Check(text.Replace("PhoneNumber=nl1234.nordvpn.com", "PhoneNumber=evil.example.com"), e, Split.All).Any(x => x.Contains("Nord server")), "IKEv2 only and a Nord server are checked");

            // on a temp COPY: backup first, written atomically, bytes outside the entry kept (non-ASCII too), idempotent
            var pdir = Path.Combine(dir, "pbk"); Directory.CreateDirectory(pdir);
            var file = Path.Combine(pdir, "rasphone.pbk"); var bak = Path.Combine(pdir, "backup", "rasphone.pbk.bak");
            var raw = Encoding.GetEncoding(1252).GetBytes(text);
            File.WriteAllBytes(file, raw);
            bool changed; var err = Pbk.Ensure(file, e, Split.All, bak, out changed);
            Ok(err == null && changed && File.Exists(bak) && File.ReadAllBytes(bak).SequenceEqual(raw), "Ensure: the old file is backed up first, byte for byte");
            var after = File.ReadAllBytes(file);
            Ok(Pbk.Check(Pbk.Read(file), e, Split.All).Count == 0 && after.Take(other.Length).SequenceEqual(raw.Take(other.Length)) && Array.IndexOf(after, (byte)0xE9) > 0, "Ensure: the entry is fixed and the bytes before it (é included) are kept");
            Ok(!File.Exists(file + ".hallow-tmp"), "no temp file left behind");
            var t1 = File.GetLastWriteTimeUtc(file); File.Delete(bak);
            err = Pbk.Ensure(file, e, Split.All, bak, out changed);
            Ok(err == null && !changed && !File.Exists(bak) && File.GetLastWriteTimeUtc(file) == t1, "Ensure again: nothing to do, nothing written");
            Ok(Pbk.Ensure(Path.Combine(pdir, "none.pbk"), e, Split.All, bak, out changed) != null && !changed, "no phonebook: a reason, nothing created");
            File.WriteAllText(file, other); err = Pbk.Ensure(file, e, Split.All, bak, out changed);
            Ok(err != null && !changed && File.ReadAllText(file) == other, "no Hallow VPN entry: the file is left exactly as it was");
            Ok(!Pbk.Read(file).Contains("103.86") , "and nothing of Nord's was written into another connection");
        }

        static void P2PTests(string fx)
        {
            var real = Nord.ParseServers(File.ReadAllText(Path.Combine(fx, "recommendations-p2p-real.json")));
            Ok(real.Count >= 1 && real.All(s => s.P2P), "the real P2P recommendations answer (saved once): " + real.Count + " servers, all in Nord's P2P group");
            var groups = File.ReadAllText(Path.Combine(fx, "groups-real.json"));
            Ok(groups.Contains("\"identifier\":\"" + Nord.P2PGroup + "\"") && groups.Contains("\"title\":\"P2P\""), "Nord's group list (saved once) has " + Nord.P2PGroup + " = P2P");
            Ok(Nord.RecommendationsUrl(0, 25, true) == "https://api.nordvpn.com/v1/servers/recommendations?filters[servers_technologies][identifier]=ikev2&filters[servers_groups][identifier]=legacy_p2p&limit=25", "the P2P recommendations URL (IKEv2 + the P2P group)");
            Ok(Target.Parse("p2p").Kind == "p2p" && Target.Parse("torrents").ToString() == "p2p", "connect p2p / torrents");
            var api = new FakeNordApi(); api.Add("legacy_p2p", File.ReadAllText(Path.Combine(fx, "recommendations-real.json")));
            var book = new ServerBook(api, new FakePinger(), null); string err;
            var l = book.List(0, false, out err, true);
            Ok(api.Calls.Single().Contains("legacy_p2p") && l.All(s => s.P2P), "a P2P list keeps only servers Nord lists as P2P (" + l.Count + ")");
            Ok(book.Fetched("p2p") != null && book.Fetched("all") == null, "cached on its own (\"p2p\")");
        }

        static NetIf If(string name, int idx, int metric, string dns, bool vpn = false) { var i = new NetIf { Name = name, Index = idx, Metric = metric, Up = true, Vpn = vpn, Tunnel = vpn }; foreach (var d in dns.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)) i.Dns.Add(IPAddress.Parse(d)); return i; }
        static void NetTests()
        {
            Ok(NetCheck.IsGlobalV6(IPAddress.Parse("2001:db8::1")) && !NetCheck.IsGlobalV6(IPAddress.Parse("fe80::1")) && !NetCheck.IsGlobalV6(IPAddress.Parse("fd7a:115c::1")) && !NetCheck.IsGlobalV6(IPAddress.Parse("1.2.3.4")), "global IPv6 (2000::/3) vs link-local / unique-local");
            var w = If("Wi-Fi", 12, 35, "192.168.1.1"); w.V6Global.Add(IPAddress.Parse("2001:db8::5"));
            Ok(!NetCheck.HasGlobalIpv6(new[] { w }), "IPv6 address but no IPv6 gateway: no IPv6 internet");
            w.V6Gateway = true; Ok(NetCheck.HasGlobalIpv6(new[] { w }), "IPv6 address + gateway: IPv6 internet");
            var vpn = If("Hallow VPN", 40, 1, "103.86.96.100,103.86.99.100", true);
            Func<IPAddress, int> best = a => NetCheck.IsPrivateV4(a) ? 12 : 40;
            var rep = NetCheck.Dns(new List<NetIf> { w, vpn }, best);
            Ok(rep.VpnFirst && rep.VpnHasDns && rep.AllVpnDnsThroughVpn && rep.Outside.Single().StartsWith("192.168.1.1"), "DNS: the VPN's Nord DNS is asked first and routed through the VPN; the router's is a fallback");
            var notes = new List<string>();
            Ok(NetCheck.Verdict(true, rep, false, false, false, notes) == "ok" && notes.Any(n => n.Contains("fall back")), "verdict ok (with the fallback note)");
            Ok(NetCheck.Verdict(true, rep, true, false, false, notes) == "warn" && notes.Any(n => n.Contains("IPv6")), "IPv6 internet without the kill switch: warn");
            Ok(NetCheck.Verdict(true, rep, true, true, false, notes) == "ok", "IPv6 with the kill switch (blocks public IPv6): ok");
            Ok(NetCheck.Verdict(false, rep, false, false, false, notes) == "fail", "Nord says not protected: fail");
            Ok(NetCheck.Verdict(null, rep, false, false, false, notes) == "warn", "Nord's check didn't answer: warn");
            vpn.Metric = 50; var rep2 = NetCheck.Dns(new List<NetIf> { w, vpn }, best);
            Ok(!rep2.VpnFirst && NetCheck.Verdict(true, rep2, false, false, false, notes) == "warn", "another network's DNS asked first: warn");
            var bare = If("Hallow VPN", 40, 1, "", true); var rep3 = NetCheck.Dns(new List<NetIf> { w, bare }, best);
            Ok(!rep3.VpnHasDns && NetCheck.Verdict(true, rep3, false, false, false, notes) == "warn" && NetCheck.Verdict(true, rep3, false, false, true, notes) == "ok", "no VPN DNS: warn (but expected with \"Only these\")");
            Ok(NetCheck.Dns(new List<NetIf> { w }, best).VpnHasDns == false, "not connected: no VPN interface");
            var trusted = new[] { "Home-5G" };
            Ok(!NetCheck.Untrusted(new NetNow { Kind = "wifi", Ssid = "Home-5G" }, trusted, true), "trusted Wi-Fi");
            Ok(NetCheck.Untrusted(new NetNow { Kind = "wifi", Ssid = "Cafe" }, trusted, true), "other Wi-Fi: untrusted");
            Ok(NetCheck.Untrusted(new NetNow { Kind = "wifi", Ssid = "Home-5G", Open = true }, trusted, true), "an OPEN Wi-Fi is untrusted even with a trusted name");
            Ok(!NetCheck.Untrusted(new NetNow { Kind = "wired" }, trusted, true) && NetCheck.Untrusted(new NetNow { Kind = "wired" }, trusted, false), "wired: trusted by default, a setting changes it");
            Ok(!NetCheck.Untrusted(new NetNow(), trusted, false) && !NetCheck.Untrusted(new NetNow { Kind = "wifi", Ssid = "home-5g" }, trusted, true) == false, "no network: nothing; names are exact (case matters)");
        }

        static void DiagTests()
        {
            Func<string, IPAddress[]> res = h => h == "nowhere.nordvpn.com" ? new IPAddress[0] : new[] { IPAddress.Parse("203.0.113.9") };
            var good = new DiagInput { Account = true, CaInstalled = true, CaChecked = true, PbkText = Pbk.Apply(Pbk.SampleEntry(VpnScript.Entry, "nl1.nordvpn.com"), VpnScript.Entry, Pbk.Expected(Split.All)), SplitMode = Split.All, Host = "nl1.nordvpn.com", Resolve = res, Ping = s => 21, KillSwitchAll = 12, ProtocolChoice = Protocols.Ikev2 };
            var r = Diagnosis.Run(good);
            Ok(r.Select(x => x.Id).SequenceEqual(new[] { "account", "certificate", "profile", "server", "lasterror", "blockers" }), "the checks run in the briefed order");
            Ok(r.All(x => x.Status == "ok") && Diagnosis.Summary(r) == "Everything checks out.", "all good: green ticks");
            var b = new DiagInput { Account = false, CaInstalled = false, CaChecked = true, PbkText = Pbk.SampleEntry(VpnScript.Entry, "nl1.nordvpn.com"), SplitMode = Split.All, Host = "nowhere.nordvpn.com", Resolve = res, Ping = s => -1, LastDialCode = 13801, HomeNode = "home-server", KillSwitchRules = 5, KillSwitchAll = 12, ProtocolChoice = Protocols.Ikev2 };
            r = Diagnosis.Run(b);
            Ok(r[0].Status == "fail" && r[0].Text.Contains("Set up NordVPN manually"), "no credentials: where to find them");
            Ok(r[1].Status == "fail" && r[1].Text.Contains("13801"), "no certificate: says it's what causes 13801");
            Ok(r[2].Status == "warn" && r[2].Repair == "profile" && r[2].Text.Contains("NordVPN's servers"), "profile without Nord's DNS: warn + the recreate repair");
            Ok(r[3].Status == "fail" && r[3].Text.Contains("look up"), "server name doesn't resolve: fail");
            Ok(r[4].Status == "warn" && r[4].Text.Contains("certificate"), "last error 13801 explained");
            Ok(r[5].Status == "fail" && r[5].Text.Contains("Tailscale") && r[5].Text.Contains("partly"), "Tailscale exit node and a half-installed kill switch are in the way");
            Ok(Diagnosis.Summary(r).StartsWith("Nord service credentials"), "summary = the first problem");
            b.Host = "nl1.nordvpn.com"; r = Diagnosis.Run(b); Ok(r[3].Status == "warn" && r[3].Text.Contains("809"), "found but no ping answer: warn, mentions 809");
            b.Host = null; Ok(Diagnosis.Run(b)[3].Repair == "servers", "no server list: refresh repair");
            b.PbkText = null; Ok(Diagnosis.Run(b)[2].Status == "skip" && Diagnosis.Run(b)[2].Repair == "profile", "no profile yet: made on the first connect");
            Ok(Diagnosis.Slow(160, 10) && Diagnosis.Slow(40, 80) && !Diagnosis.Slow(40, 30) && !Diagnosis.Slow(-1, 30), "slow server: ping over 150 ms or load 75 %+");
            Ok(Diagnosis.Bytes(512) == "512 B" && Diagnosis.Bytes(182452224) == "174.0 MB" && Diagnosis.Bytes(1932735283) == "1.80 GB" && Diagnosis.Bytes(-1) == "-", "byte counts");
        }

        // READ-ONLY looks at this PC: nothing is changed. The Wi-Fi name is never printed.
        static void RealReadOnly()
        {
            var n = Wlan.Current();
            Ok(true, "Native Wifi read (read-only): " + (n == null ? "no Wi-Fi connection" : "connected to Wi-Fi (" + (n.Open ? "open" : "secured") + ", name not shown)"));
            var ni = new RealNetInfo(); var ifs = ni.Interfaces();
            Ok(ifs.Count > 0, "network interfaces read: " + ifs.Count + ", " + ifs.Count(i => i.Up) + " up; global IPv6 internet: " + NetCheck.HasGlobalIpv6(ifs));
            var one = ifs.FirstOrDefault(i => i.Up && i.Index > 0 && i.Metric >= 0);
            if (one != null)
            {
                var psi = new ProcessStartInfo("powershell.exe", "-NoProfile -NonInteractive -Command \"(Get-NetIPInterface -InterfaceIndex " + one.Index + " -AddressFamily IPv4).InterfaceMetric\"") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true };
                string o; using (var p = Process.Start(psi)) { o = p.StandardOutput.ReadToEnd().Trim(); p.WaitForExit(30000); }
                Ok(o == one.Metric.ToString(), "GetIpInterfaceEntry reads the IPv4 metric right (interface " + one.Index + ": " + one.Metric + ", PowerShell says " + o + ")");
            }
            var best = ni.BestInterface(IPAddress.Parse("8.8.8.8"));
            Ok(best == -1 || ifs.Any(i => i.Index == best), "GetBestInterface answers with a known interface (" + best + ")");
            var st = new int[15]; st[0] = 60;
            Ok(Ras.RasGetConnectionStatistics(IntPtr.Zero, st) != 0, "RasGetConnectionStatistics with no connection: an error code, no crash");
        }
    }
}
