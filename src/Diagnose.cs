// Hallow VPN 1.1 - "Fix my connection": read-only checks in order, each with a green tick or a plain-English fix, and the one-click
// repairs that need no admin (recreate the per-user "Hallow VPN" profile, refresh Nord's server list). Also the connection health
// numbers (RasGetConnectionStatistics: bytes, duration) and the "slow server?" hint.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;

namespace HallowVPN
{
    public class DiagStep
    {
        public string Id, Title, Status, Text, Repair;   // Status: ok / warn / fail / skip; Repair: profile / servers / null
        public DiagStep(string id, string title) { Id = id; Title = title; }
    }
    public class DiagInput
    {
        public bool Account, CaInstalled, CaChecked; public string PbkText; public string SplitMode;
        public string Host; public Func<string, IPAddress[]> Resolve; public Func<Server, int> Ping;
        public int LastDialCode; public string LastDialWhen; public string HomeNode; public int KillSwitchRules, KillSwitchAll; public bool? FirewallOn;
        public bool Connected;
    }

    public static class Diagnosis
    {
        public static List<DiagStep> Run(DiagInput i)
        {
            var r = new List<DiagStep>();
            var s = new DiagStep("account", "Nord service credentials");
            if (i.Account) { s.Status = "ok"; s.Text = "Saved, encrypted for your Windows user."; }
            else { s.Status = "fail"; s.Text = "Not saved yet. On nordaccount.com: NordVPN > \"Set up NordVPN manually\" > Service credentials (not your Nord login). Paste them in step 1 of the setup."; }
            r.Add(s);

            s = new DiagStep("certificate", "NordVPN's root certificate");
            if (!i.CaChecked) { s.Status = "skip"; s.Text = "Still checking."; }
            else if (i.CaInstalled) { s.Status = "ok"; s.Text = "Installed - Windows trusts Nord's servers."; }
            else { s.Status = "fail"; s.Text = "Missing - this is what causes error 13801. Click \"Install Nord's certificate\" (Windows asks for permission; that's your click)."; }
            r.Add(s);

            s = new DiagStep("profile", "The \"Hallow VPN\" connection in Windows");
            if (i.PbkText == null) { s.Status = i.Connected ? "warn" : "skip"; s.Text = "Not created yet - it's made on the first connect (no admin)."; s.Repair = "profile"; }
            else
            {
                var bad = Pbk.Check(i.PbkText, VpnScript.Entry, i.SplitMode);
                if (bad.Count == 0) { s.Status = "ok"; s.Text = "There, IKEv2, Nord server, DNS " + (i.SplitMode == Split.Only ? "automatic (split tunnel)" : "set to NordVPN's servers") + "."; }
                else { s.Status = bad[0].StartsWith("There's no") ? "skip" : "warn"; s.Text = string.Join(" ", bad) + " Recreate it below (no admin), or just connect - Hallow VPN fixes it then."; s.Repair = "profile"; }
            }
            r.Add(s);

            s = new DiagStep("server", "Reaching a Nord server");
            if (i.Host == null) { s.Status = "warn"; s.Text = "No Nord server list yet. Refresh the server list below."; s.Repair = "servers"; }
            else
            {
                IPAddress[] a = null; try { a = i.Resolve(i.Host); } catch { }
                var v4 = a == null ? null : a.FirstOrDefault(x => x.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork);
                if (v4 == null) { s.Status = "fail"; s.Text = "Windows couldn't look up " + i.Host + " - check the internet connection (error 868 means the same)."; s.Repair = "servers"; }
                else
                {
                    int ms = -1; try { ms = i.Ping(new Server { Host = i.Host, Station = v4.ToString() }); } catch { }
                    if (ms >= 0) { s.Status = "ok"; s.Text = i.Host + " answers in " + ms + " ms. (Whether IKEv2's UDP 500 / 4500 gets through can only be seen by connecting - error 809 means it doesn't.)"; }
                    else { s.Status = "warn"; s.Text = i.Host + " was found (" + v4 + ") but didn't answer a ping. Some networks block pings; if connecting fails with 809, the network blocks IKEv2 (UDP 500 / 4500) - try another network or a phone hotspot."; }
                }
            }
            r.Add(s);

            s = new DiagStep("lasterror", "Windows' last VPN error");
            if (i.LastDialCode == 0) { s.Status = "ok"; s.Text = "None recorded."; }
            else { s.Status = (i.LastDialCode == 631 || i.LastDialCode == 831 || i.LastDialCode == 815 || i.LastDialCode == 629) ? "ok" : "warn"; s.Text = Ras.Explain(i.LastDialCode) + (i.LastDialWhen != null ? " (" + i.LastDialWhen + ")" : ""); }
            r.Add(s);

            s = new DiagStep("blockers", "Tailscale exit node and kill switch");
            var notes = new List<string>(); s.Status = "ok";
            if (i.HomeNode != null) { s.Status = "fail"; notes.Add("Tailscale's exit node (" + i.HomeNode + ") is in use - the two don't stack. Stop it on the Home page."); }
            if (i.KillSwitchRules > 0 && i.KillSwitchRules < i.KillSwitchAll) { if (s.Status == "ok") s.Status = "warn"; notes.Add("The kill switch is only partly installed (" + i.KillSwitchRules + " of " + i.KillSwitchAll + " rules) - install it again or remove it on the Kill switch page."); }
            else if (i.KillSwitchRules >= i.KillSwitchAll && i.KillSwitchAll > 0) notes.Add("The kill switch is on: it leaves IKEv2 (UDP 500 / 4500) and DNS open, so it doesn't stop the VPN from connecting - but there's no internet until it does.");
            if (i.FirewallOn == false && i.KillSwitchRules > 0) notes.Add("Windows Firewall is off for some network, so the kill switch can't work there.");
            if (notes.Count == 0) notes.Add("Nothing in the way.");
            s.Text = string.Join(" ", notes); r.Add(s);
            return r;
        }
        public static string Summary(List<DiagStep> steps)
        {
            var f = steps.FirstOrDefault(x => x.Status == "fail") ?? steps.FirstOrDefault(x => x.Status == "warn");
            return f == null ? "Everything checks out." : f.Title + ": " + f.Text;
        }

        // "slow server? switch": ping over 150 ms or load 75 % and more
        public static bool Slow(int ping, int load) { return ping > 150 || load >= 75; }
        public static string Bytes(long b)
        {
            if (b < 0) return "-";
            if (b < 1024) return b + " B";
            if (b < 1024 * 1024) return (b / 1024.0).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " KB";
            if (b < 1024L * 1024 * 1024) return (b / 1048576.0).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " MB";
            return (b / 1073741824.0).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + " GB";
        }
    }
}
