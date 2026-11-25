// Hallow VPN 1.3 - Threat protection without a driver. NordVPN's own Threat Protection blocks ads, trackers and malware sites with
// kernel drivers (mshield / travis); Nord's normal DNS servers (103.86.96.100 / 103.86.99.100) don't filter anything. Hallow VPN
// instead points the VPN connection's DNS at a well-known FILTERING DNS service while you're connected:
//   * AdGuard DNS (94.140.14.14 / 94.140.15.15) - ads, trackers and malware / phishing sites
//   * Quad9 (9.9.9.9 / 149.112.112.112) - malware and phishing sites only
// Lookups still travel inside the VPN tunnel (your internet provider sees nothing; the DNS service sees the VPN server's address,
// not yours). Off = Nord's DNS, as before. The same two addresses go into IKEv2's phonebook entry (Pbk.Expected - applied when the
// profile is checked after connecting, so a change takes effect at the next connect) and NordLynx's WireGuard config. The kill
// switch is unchanged: it only limits DNS on your normal adapters, never inside the tunnel. "Only these apps" split mode keeps the
// server's DNS (there the VPN isn't your DNS path at all).
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace HallowVPN
{
    public class DnsFilter { public string Id, Name, Blocks, Dns1, Dns2, Who; }

    public static class DnsFilters
    {
        public static readonly List<DnsFilter> All = new List<DnsFilter>
        {
            new DnsFilter { Id = "", Name = "Off - NordVPN's DNS", Blocks = "Nothing is blocked. Lookups go to NordVPN's own DNS servers, as before.", Dns1 = Pbk.NordDns1, Dns2 = Pbk.NordDns2, Who = "NordVPN" },
            new DnsFilter { Id = "adguard", Name = "AdGuard DNS - ads, trackers and malware", Blocks = "Blocks ad and tracker domains and known malware and phishing sites, in every app (games, launchers, browsers).", Dns1 = "94.140.14.14", Dns2 = "94.140.15.15", Who = "AdGuard" },
            new DnsFilter { Id = "quad9", Name = "Quad9 - malware only", Blocks = "Blocks known malware and phishing sites. Ads aren't touched - fewest surprises.", Dns1 = "9.9.9.9", Dns2 = "149.112.112.112", Who = "Quad9" },
        };
        public static DnsFilter Get(string id) { return All.FirstOrDefault(f => f.Id == (id ?? "")) ?? All[0]; }
        public static bool IsOn(string id) { return Get(id).Id != ""; }
    }
}

namespace HallowVPN
{
    public partial class Controller
    {
        // 1.3: pick the DNS filter; the IKEv2 profile is updated now (it takes effect at the next connect), NordLynx at its next connect
        public bool DnsPending;   // changed while connected: the connection still uses the old DNS until it reconnects
        public string SetDnsFilter(string id)
        {
            var f = DnsFilters.Get(id); var before = DnsFilters.Get(S.DnsFilter);
            S.DnsFilter = f.Id == "" ? null : f.Id; S.Save(); if (f.Id != before.Id && Phase == Phase.On) DnsPending = true;
            if (!SnapMode) try { ProtectDns(); } catch (Exception ex) { Log("threat protection: " + ex.Message); }
            Log("threat protection: " + (f.Id == "" ? "off" : f.Id)); Fire();
            return f.Id == before.Id ? null : (Phase == Phase.On ? "Saved - it starts at the next connect. Reconnect now to use it straight away." : null);
        }
    }
}

namespace HallowVPN
{
    public partial class MainWindow
    {
        FrameworkElement ThreatPage()
        {
            var sp = PageBody("Threat protection", "Block ads, trackers and malware sites for every app - while you're connected, with no driver.");
            var cur = DnsFilters.Get(c.S.DnsFilter);
            sp.Children.Add(Ui.Note("NordVPN's own Threat Protection does this with drivers inside Windows. Hallow VPN uses a filtering DNS service instead: a blocked site simply doesn't resolve. Nothing is installed and no admin is needed.", "Lilac", "LilacBg"));
            foreach (var f in DnsFilters.All)
            {
                var ff = f;
                sp.Children.Add(Choice(f.Name, f.Blocks + (f.Id == "" ? "" : "  (" + f.Dns1 + " and " + f.Dns2 + ")"), cur.Id == f.Id, () =>
                {
                    var r = c.SetDnsFilter(ff.Id);
                    BuildPage();
                    Say(r ?? (ff.Id == "" ? "Threat protection is off - NordVPN's DNS again." : ff.Who + " DNS is on - it's used every time you connect."));
                }));
            }
            if (c.Phase == Phase.On && c.DnsPending)
            {
                sp.Children.Add(Ui.Note("The connection still uses the DNS it started with. Reconnect to switch to " + cur.Who + "'s DNS.", "Amber", "AmberBg"));
                sp.Children.Add(Wrap(Ui.Btn("Reconnect now", () => Act(c.Connect(c.CurrentTarget ?? Target.Parse(c.S.LastTarget), false)), true)));
            }
            if (c.S.SplitMode == Split.Only)
                sp.Children.Add(Ui.Note("Split tunnel is set to \"Only these go through the VPN\", so Windows doesn't use the VPN for lookups and nothing is filtered. Pick \"Everything through the VPN\" on the Split tunnel page to use it.", "Amber", "AmberBg"));
            sp.Children.Add(Section("Good to know"));
            sp.Children.Add(Bullets(new[] {
                "Lookups still go inside the VPN tunnel: your internet provider sees nothing, and " + (cur.Id == "" ? "the DNS service" : cur.Who) + " sees the Nord server's address, not yours.",
                "It works only while Hallow VPN is connected. Disconnected, Windows uses your normal network's DNS.",
                "If a site or game login breaks, switch this off for a moment to check - a filter can block something it shouldn't.",
                "The leak test still checks that every lookup goes through the VPN.",
                "Hallow Browser's Shield blocks inside web pages too; this covers every other app.",
            }));
            return Scroll(sp);
        }
    }
}
