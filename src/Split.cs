// Hallow VPN - split tunnelling, the honest way. Windows' built-in VPN client splits by ADDRESS (routes), not by app: per-app
// split tunnelling needs a network driver, and Hallow VPN installs none. So the choices are:
//   all           everything goes through the VPN (the default)
//   only          only these addresses / ranges / websites go through the VPN (the profile's SplitTunneling + one route each)
//   except-games  everything goes through the VPN, but the VPN is paused (disconnected) while a game runs and comes back after it
// Websites are looked up when the profile is set up (on connect); their addresses can change, so they're looked up again each time.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;

namespace HallowVPN
{
    public static class Split
    {
        public const string All = "all", Only = "only", ExceptGames = "except-games";
        public static readonly string[] Modes = { All, Only, ExceptGames };
        public const int MaxRoutes = 64;
        public static string Label(string m) { return m == Only ? "Only these go through the VPN" : m == ExceptGames ? "Everything except games" : "Everything through the VPN"; }

        // an address ("1.2.3.4"), a range ("1.2.3.0/24", IPv6 too) or a website ("example.com") -> its tidy form; null = not one of those
        public static string Normalize(string s)
        {
            var v = (s ?? "").Trim().ToLowerInvariant();
            if (v.Length == 0 || v.Length > 100) return null;
            if (v.StartsWith("http://") || v.StartsWith("https://")) { try { v = new Uri(v).Host; } catch { return null; } }
            var slash = v.IndexOf('/'); IPAddress ip; int bits;
            var addr = slash < 0 ? v : v.Substring(0, slash);
            if (IPAddress.TryParse(addr, out ip) && (ip.AddressFamily == AddressFamily.InterNetwork ? Regex.IsMatch(addr, "^\\d{1,3}(\\.\\d{1,3}){3}$") : true))
            {
                int max = ip.AddressFamily == AddressFamily.InterNetwork ? 32 : 128;
                if (slash < 0) bits = max; else if (!int.TryParse(v.Substring(slash + 1), out bits)) return null;
                if (bits < 1 || bits > max) return null;   // /0 would be the whole internet: that's "Everything through the VPN"
                var b = ip.GetAddressBytes();
                for (int i = 0; i < b.Length; i++) { int keep = Math.Max(0, Math.Min(8, bits - i * 8)); b[i] = (byte)(b[i] & (0xFF << (8 - keep))); }
                return new IPAddress(b) + "/" + bits;
            }
            if (slash >= 0) return null;
            if (Regex.IsMatch(v, "^(?=.{3,100}$)([a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?\\.)+[a-z]{2,}$")) return v;
            return null;
        }
        public static bool IsWebsite(string normalized) { return normalized != null && normalized.IndexOf('/') < 0; }

        // the routes for the profile: ranges as they are, websites looked up now (each address /32 or /128). failed = websites that didn't resolve.
        public static List<string> Routes(IEnumerable<string> entries, Func<string, IPAddress[]> resolve, out List<string> failed)
        {
            failed = new List<string>(); var r = new List<string>();
            foreach (var e in entries)
            {
                var n = Normalize(e); if (n == null) continue;
                if (!IsWebsite(n)) { r.Add(n); continue; }
                IPAddress[] a = null; try { a = resolve(n); } catch { }
                if (a == null || a.Length == 0) { failed.Add(n); continue; }
                foreach (var ip in a.Take(8)) r.Add(ip + (ip.AddressFamily == AddressFamily.InterNetwork ? "/32" : "/128"));
            }
            return r.Distinct().Take(MaxRoutes).ToList();
        }
        public static IPAddress[] Dns(string host) { try { return System.Net.Dns.GetHostAddresses(host); } catch { return new IPAddress[0]; } }

        public static ProfileSpec Spec(string host, string mode, List<string> routes)
        {
            var only = mode == Only && routes != null && routes.Count > 0;
            return new ProfileSpec { Host = host, SplitTunnel = only, Routes = only ? routes.ToList() : new List<string>() };
        }
    }
}
