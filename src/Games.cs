// Hallow VPN - game awareness: the family game list (DeltaForceClient-Win64-Shipping.exe, Discovery-d.exe, your own extra exe
// names) plus Hallow Hub's status.json "playing" names. A cheap name lookup every 5 seconds - nothing else. While a game runs,
// Hallow VPN never pops anything up. What it does when a game starts (Gaming page):
//   off      nothing
//   pause    disconnect while the game runs, reconnect to the same choice after it closes (also what "Everything except games" does)
//   lowping  stay connected, but on the best server for gaming (low ping, low load, nearby)
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace HallowVPN
{
    public interface IGameSource { string Running(IEnumerable<string> extra); }   // the game's name, or null

    public static class Games
    {
        public const string ModeOff = "off", ModePause = "pause", ModeLowPing = "lowping";
        public static readonly string[][] Known = { new[] { "DeltaForceClient-Win64-Shipping", "Delta Force" }, new[] { "Discovery-d", "Discovery" } };
        public static string Label(string m) { return m == ModePause ? "Disconnect while I play, reconnect after" : m == ModeLowPing ? "Stay connected on a low-ping server" : "Do nothing"; }
        public static string Exe(string s) { var v = (s ?? "").Trim(); if (v.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) v = v.Substring(0, v.Length - 4); return v; }
        public static string Title(string exe)
        {
            var k = Known.FirstOrDefault(x => string.Equals(x[0], Exe(exe), StringComparison.OrdinalIgnoreCase));
            return k != null ? k[1] : Exe(exe);
        }
        // what happens when a game starts, given the split mode, the game mode and whether the kill switch is installed
        public static string Effective(string splitMode, string gameMode, bool killSwitch)
        {
            var m = splitMode == Split.ExceptGames ? ModePause : gameMode;
            if (m == ModePause && killSwitch) return ModeLowPing;   // with the kill switch, no VPN = no internet - so never disconnect for a game
            return m == ModePause || m == ModeLowPing ? m : ModeOff;
        }
    }

    public class RealGames : IGameSource
    {
        public string Running(IEnumerable<string> extra)
        {
            foreach (var n in Games.Known.Select(k => k[0]).Concat(extra ?? new string[0]).Select(Games.Exe).Where(x => x.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                Process[] ps = null;
                try { ps = Process.GetProcessesByName(n); if (ps.Length > 0) return Games.Title(n); }
                catch { }
                finally { if (ps != null) foreach (var p in ps) p.Dispose(); }
            }
            var hub = HubStatus.Playing(Family.DataDir("Hub"));
            return hub.Count > 0 ? hub[0] : null;
        }
    }
    public class FakeGames : IGameSource
    {
        public string Game; public int Calls;
        public string Running(IEnumerable<string> extra) { Calls++; return Game; }
    }

    // Hallow Hub's status.json: which games it says are being played (only while Hub runs)
    public static class HubStatus
    {
        public static List<string> Playing(string hubDir)
        {
            var r = new List<string>();
            try
            {
                var f = Path.Combine(hubDir, "status.json"); if (!File.Exists(f)) return r;
                var d = new System.Web.Script.Serialization.JavaScriptSerializer().Deserialize<Dictionary<string, object>>(File.ReadAllText(f)); object v;
                if (d == null || !d.TryGetValue("running", out v) || !(v is bool) || !(bool)v) return r;
                if (d.TryGetValue("playing", out v) && v is System.Collections.IEnumerable && !(v is string))
                    foreach (var x in (System.Collections.IEnumerable)v)
                    {
                        if (x is string) r.Add((string)x);
                        else { var o = x as Dictionary<string, object>; object n; if (o != null && o.TryGetValue("name", out n) && n is string) r.Add((string)n); }
                    }
            }
            catch { }
            return r;
        }
    }
}
