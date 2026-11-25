// Hallow VPN 1.1 - DNS leak protection in the per-user phonebook, without admin.
// Windows keeps this user's VPN connections in %APPDATA%\Microsoft\Network\Connections\Pbk\rasphone.pbk (an INI-like text file).
// PowerShell's Set-VpnConnection has no DNS server option, so for the "Hallow VPN" entry ONLY Hallow VPN sets these keys itself:
//   IpNameAssign=2              use the DNS servers below instead of whatever is offered
//   IpDnsAddress=103.86.96.100  NordVPN's DNS
//   IpDns2Address=103.86.99.100 NordVPN's second DNS
//   IpInterfaceMetric=1         the VPN interface comes first, so Windows asks its DNS servers first
// With "Only these go through the VPN" (split tunnel) Nord's DNS wouldn't be reachable outside the tunnel, so the entry is put back
// to automatic DNS (IpNameAssign=1, IpInterfaceMetric=0) instead. Every change: the file is backed up first
// (%APPDATA%\HallowVPN\rasphone.pbk.bak), written to rasphone.pbk.hallow-tmp next to it, then swapped in (File.Replace) - never half
// written. Lines outside the [Hallow VPN] section are kept byte for byte (read and written as Latin-1, which maps bytes 1:1).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace HallowVPN
{
    public static class Pbk
    {
        public const string NordDns1 = "103.86.96.100", NordDns2 = "103.86.99.100";
        // 1.3: the DNS the VPN connection uses - NordVPN's, or a filtering service (Protect13.cs); set by the controller
        public static Func<string> FilterOf = () => "";
        public static string Dns1 { get { return DnsFilters.Get(FilterOf()).Dns1; } }
        public static string Dns2 { get { return DnsFilters.Get(FilterOf()).Dns2; } }
        static readonly Encoding Bytes = Encoding.GetEncoding(28591);   // Latin-1: every byte round-trips unchanged

        // what the "Hallow VPN" entry should say for the split mode
        public static Dictionary<string, string> Expected(string splitMode)
        {
            if (splitMode == Split.Only)
                return new Dictionary<string, string> { { "IpNameAssign", "1" }, { "IpInterfaceMetric", "0" } };
            return new Dictionary<string, string> { { "IpNameAssign", "2" }, { "IpDnsAddress", Dns1 }, { "IpDns2Address", Dns2 }, { "IpInterfaceMetric", "1" } };
        }

        // the lines of one [section] (without the header); null = no such section
        public static List<string> Section(string text, string entry)
        {
            List<string> r = null;
            foreach (var raw in Lines(text))
            {
                var t = raw.Trim();
                if (t.StartsWith("[") && t.EndsWith("]")) { if (r != null) break; if (t == "[" + entry + "]") r = new List<string>(); continue; }
                if (r != null) r.Add(raw);
            }
            return r;
        }
        static string[] Lines(string text) { return (text ?? "").Replace("\r\n", "\n").Split('\n'); }
        // the first value of a key in a section's lines (null = not there)
        public static string Value(List<string> section, string key)
        {
            if (section == null) return null;
            foreach (var l in section) { int eq = l.IndexOf('='); if (eq > 0 && l.Substring(0, eq).Trim() == key) return l.Substring(eq + 1).Trim(); }
            return null;
        }

        // what differs from what Hallow VPN expects (plain words); empty = all good. Checked: the entry, IKEv2 only (VpnStrategy=7), a Nord
        // server as its address and the DNS keys for the split mode.
        public static List<string> Check(string text, string entry, string splitMode)
        {
            var bad = new List<string>();
            var s = Section(text, entry);
            if (s == null) { bad.Add("There's no \"" + entry + "\" connection in your phonebook yet."); return bad; }
            var vs = Value(s, "VpnStrategy"); if (vs != null && vs != "7") bad.Add("The connection isn't set to IKEv2 only (VpnStrategy=" + vs + ").");
            var ph = Value(s, "PhoneNumber"); if (ph != null && !Nord.ValidHost(ph.ToLowerInvariant())) bad.Add("The connection's server isn't a Nord server.");
            foreach (var kv in Expected(splitMode))
            {
                var v = Value(s, kv.Key);
                if (v != kv.Value) bad.Add(Describe(kv.Key, v, kv.Value));
            }
            return bad;
        }
        static string Describe(string key, string have, string want)
        {
            switch (key)
            {
                case "IpNameAssign": return want == "2" ? "DNS isn't set to NordVPN's servers yet." : "DNS should be automatic with split tunnelling.";
                case "IpDnsAddress": case "IpDns2Address": return "A DNS server is " + (string.IsNullOrEmpty(have) ? "missing" : have) + " instead of " + DnsFilters.Get(FilterOf()).Who + "'s " + want + ".";
                case "IpInterfaceMetric": return want == "1" ? "Windows doesn't ask the VPN's DNS first (interface metric " + (have ?? "not set") + ")." : "The interface metric should be automatic with split tunnelling.";
            }
            return key + " is " + (have ?? "missing") + ", not " + want + ".";
        }

        // the text with the entry's keys set (first occurrence replaced; missing keys added at the end of the entry's first block).
        // Nothing outside [entry] changes. null = there's no such entry.
        public static string Apply(string text, string entry, Dictionary<string, string> values)
        {
            var nl = (text ?? "").Contains("\r\n") ? "\r\n" : "\n";
            var lines = Lines(text).ToList();
            int start = -1, end = lines.Count;
            for (int i = 0; i < lines.Count; i++)
            {
                var t = lines[i].Trim();
                if (t.StartsWith("[") && t.EndsWith("]")) { if (start >= 0) { end = i; break; } if (t == "[" + entry + "]") start = i; }
            }
            if (start < 0) return null;
            var todo = new Dictionary<string, string>(values);
            int firstBlank = -1;
            for (int i = start + 1; i < end; i++)
            {
                var l = lines[i]; if (l.Trim().Length == 0) { if (firstBlank < 0) firstBlank = i; continue; }
                int eq = l.IndexOf('='); if (eq <= 0) continue;
                var k = l.Substring(0, eq).Trim(); string v;
                if (todo.TryGetValue(k, out v)) { lines[i] = k + "=" + v; todo.Remove(k); }
            }
            if (todo.Count > 0)
            {
                int at = firstBlank >= 0 ? firstBlank : end;
                // don't add after the trailing blank lines of the last section
                while (firstBlank < 0 && at > start + 1 && lines[at - 1].Trim().Length == 0) at--;
                lines.InsertRange(at, values.Keys.Where(todo.ContainsKey).Select(k => k + "=" + todo[k]));
            }
            return string.Join(nl, lines);
        }

        public static string Read(string path) { return path != null && File.Exists(path) ? File.ReadAllText(path, Bytes) : null; }

        // makes the entry say what Expected() says. null = done or nothing to do; else why not (nothing was changed then).
        // backup = where the copy of the old file goes (once per change). changed = whether the file was rewritten.
        public static string Ensure(string path, string entry, string splitMode, string backup, out bool changed)
        {
            changed = false;
            try
            {
                var text = Read(path);
                if (text == null) return "The phonebook file isn't there yet.";
                if (Section(text, entry) == null) return "There's no \"" + entry + "\" connection in the phonebook.";
                if (Check(text, entry, splitMode).Count(x => x.Contains("DNS") || x.Contains("metric")) == 0) return null;
                var next = Apply(text, entry, Expected(splitMode));
                if (next == null) return "There's no \"" + entry + "\" connection in the phonebook.";
                if (next == text) return null;
                if (backup != null) { Directory.CreateDirectory(Path.GetDirectoryName(backup)); File.Copy(path, backup, true); }
                var tmp = path + ".hallow-tmp";
                File.WriteAllText(tmp, next, Bytes);
                File.Replace(tmp, path, null);
                changed = true;
                return null;
            }
            catch (Exception e) { try { if (File.Exists(path + ".hallow-tmp")) File.Delete(path + ".hallow-tmp"); } catch { } return "Couldn't update the phonebook: " + e.Message; }
        }

        // tests and the fake VPN layer: an entry shaped like the one Add-VpnConnection writes (made up, no real data)
        public static string SampleEntry(string entry, string host)
        {
            return "[" + entry + "]\r\nEncoding=1\r\nPBVersion=8\r\nType=2\r\nAutoLogon=0\r\nUseRasCredentials=1\r\nVpnStrategy=7\r\nExcludedProtocols=0\r\nDataEncryption=256\r\n" +
                "PreferredDevice=WAN Miniport (IKEv2)\r\nIpPrioritizeRemote=1\r\nIpInterfaceMetric=0\r\nIpHeaderCompression=0\r\nIpAddress=0.0.0.0\r\nIpDnsAddress=0.0.0.0\r\nIpDns2Address=0.0.0.0\r\n" +
                "IpWinsAddress=0.0.0.0\r\nIpWins2Address=0.0.0.0\r\nIpAssign=1\r\nIpNameAssign=1\r\nIpDnsFlags=0\r\nIpNBTFlags=1\r\nIpDnsSuffix=\r\nIpv6Assign=1\r\nIpv6PrioritizeRemote=1\r\n" +
                "Ipv6InterfaceMetric=0\r\nIpv6NameAssign=1\r\nIpv6DnsAddress=::\r\nCacheCredentials=0\r\nPowershellCreatedProfile=1\r\n\r\nNETCOMPONENTS=\r\nms_msclient=1\r\nms_server=1\r\n\r\n" +
                "MEDIA=rastapi\r\nPort=VPN2-0\r\nDevice=WAN Miniport (IKEv2)\r\n\r\nDEVICE=vpn\r\nPhoneNumber=" + host + "\r\nAreaCode=\r\nCountryCode=0\r\n\r\n";
        }
    }
}
