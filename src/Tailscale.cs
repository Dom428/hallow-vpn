// Hallow VPN - "Home" mode: route this PC's internet through a computer at home with Tailscale's exit nodes. Only when Tailscale is
// installed; only the machines Tailscale itself reports as exit-node capable ("ExitNodeOption": true in `tailscale status --json`)
// are listed. Using one = `tailscale set --exit-node=<its Tailscale IP> --exit-node-allow-lan-access=<true|false>`; stopping =
// `tailscale set --exit-node=`. It uses the home internet connection: websites see your home's address, and it does NOT hide your
// traffic from your home internet provider (unlike Nord). Hallow VPN disconnects Nord first - the two don't stack.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Web.Script.Serialization;

namespace HallowVPN
{
    public class TsNode
    {
        public string Name;    // HostName ("home-pc")
        public string Dns;     // home-pc.tailnet.ts.net
        public string Ip;      // its Tailscale IPv4 (100.x.y.z)
        public string Os;
        public bool Online, Active;
    }
    public class TsState
    {
        public bool Running; public string Backend; public List<TsNode> ExitNodes = new List<TsNode>();
        public TsNode Current { get { return ExitNodes.FirstOrDefault(n => n.Active); } }
    }

    public interface ITailscale
    {
        bool Installed();
        string StatusJson();                               // `tailscale status --json` (read-only); null = no answer
        string SetExitNode(string ip, bool allowLan);      // ip null = stop using one; null = done, else the error
    }

    public static class Tailscale
    {
        public static bool ValidIp(string ip)
        {
            IPAddress a; if (ip == null || !IPAddress.TryParse(ip, out a)) return false;
            var b = a.GetAddressBytes();
            if (b.Length == 4) return b[0] == 100 && (b[1] & 0xC0) == 64;   // 100.64.0.0/10
            return b.Length == 16 && b[0] == 0xFD && b[1] == 0x7A && b[2] == 0x11 && b[3] == 0x5C;   // fd7a:115c::/32
        }
        public static string SetArgs(string ip, bool allowLan)
        {
            if (ip == null) return "set --exit-node=";
            if (!ValidIp(ip)) throw new ArgumentException("not a Tailscale address");
            return "set --exit-node=" + ip + " --exit-node-allow-lan-access=" + (allowLan ? "true" : "false");
        }
        static object Get(object o, string k) { var d = o as Dictionary<string, object>; object v; return d != null && d.TryGetValue(k, out v) ? v : null; }
        static bool B(object o, string k) { var v = Get(o, k); return v is bool && (bool)v; }
        public static TsState Parse(string json)
        {
            var st = new TsState();
            if (string.IsNullOrWhiteSpace(json)) return st;
            object root; try { root = new JavaScriptSerializer { MaxJsonLength = 16 * 1024 * 1024 }.DeserializeObject(json); } catch { return st; }
            st.Backend = Get(root, "BackendState") as string; st.Running = st.Backend == "Running";
            var peers = Get(root, "Peer") as Dictionary<string, object>;
            if (peers == null) return st;
            foreach (var p in peers.Values)
            {
                if (!B(p, "ExitNodeOption")) continue;
                var ips = Get(p, "TailscaleIPs") as object[];
                var ip = ips == null ? null : ips.OfType<string>().FirstOrDefault(x => x.Contains(".") && ValidIp(x)) ?? ips.OfType<string>().FirstOrDefault(ValidIp);
                if (ip == null) continue;
                var dns = (Get(p, "DNSName") as string ?? "").TrimEnd('.');
                st.ExitNodes.Add(new TsNode { Name = Get(p, "HostName") as string ?? dns, Dns = dns, Ip = ip, Os = Get(p, "OS") as string, Online = B(p, "Online"), Active = B(p, "ExitNode") });
            }
            st.ExitNodes = st.ExitNodes.OrderByDescending(n => n.Active).ThenByDescending(n => n.Online).ThenBy(n => n.Name).ToList();
            return st;
        }
    }

    public class RealTailscale : ITailscale
    {
        public static string Exe
        {
            get
            {
                foreach (var d in new[] { Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), Environment.GetEnvironmentVariable("ProgramW6432") })
                    if (!string.IsNullOrEmpty(d) && File.Exists(Path.Combine(d, @"Tailscale\tailscale.exe"))) return Path.Combine(d, @"Tailscale\tailscale.exe");
                return null;
            }
        }
        public bool Installed() { return Exe != null; }
        string Run(string args, out int code)
        {
            code = -1; var exe = Exe; if (exe == null) return null;
            var psi = new ProcessStartInfo(exe, args) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            using (var p = Process.Start(psi))
            {
                var o = p.StandardOutput.ReadToEndAsync(); var e = p.StandardError.ReadToEndAsync();
                if (!p.WaitForExit(15000)) { try { p.Kill(); } catch { } return null; }
                code = p.ExitCode; return p.ExitCode == 0 ? o.Result : (e.Result ?? "").Trim();
            }
        }
        public string StatusJson() { try { int c; var o = Run("status --json", out c); return c == 0 ? o : null; } catch { return null; } }
        public string SetExitNode(string ip, bool allowLan)
        {
            try { int c; var o = Run(Tailscale.SetArgs(ip, allowLan), out c); return c == 0 ? null : "Tailscale said: " + Family.OneLine(o ?? "no answer"); }
            catch (Exception e) { return "Couldn't run Tailscale: " + e.Message; }
        }
    }
    public class FakeTailscale : ITailscale
    {
        public bool Is = true; public string Json; public readonly List<string> Calls = new List<string>(); public string Error;
        public bool Installed() { return Is; }
        public string StatusJson() { Calls.Add("status"); return Is ? Json : null; }
        public string SetExitNode(string ip, bool allowLan) { Calls.Add(Tailscale.SetArgs(ip, allowLan)); return Error; }
    }
}
