// Hallow VPN 1.3 tests: Threat protection by DNS - the IKEv2 phonebook entry and the NordLynx config both use the chosen
// filtering DNS (or NordVPN's when off), unknown ids fall back to off, "only these" split mode never sets DNS, and the
// phonebook check names the right provider. Pure - nothing is dialled or written outside the test folder.
using System;
using System.Collections.Generic;
using System.Linq;

namespace HallowVPN.Tests
{
    public static class V13
    {
        static Server Wg() { return Nord.ParseServers(System.IO.File.ReadAllText(System.IO.Path.Combine(Fx, "recommendations-wireguard-real.json"))).First(s => s.WgKey != null); }
        static string Fx;
        public static void All(Action<bool, string> ok, string fx)
        {
            Fx = fx; var keep = Pbk.FilterOf;
            try
            {
                Pbk.FilterOf = () => "";
                var off = Pbk.Expected(Split.All);
                ok(off["IpDnsAddress"] == "103.86.96.100" && off["IpDns2Address"] == "103.86.99.100" && off["IpNameAssign"] == "2", "1.3 off: the IKEv2 entry keeps NordVPN's DNS, as before");
                ok(NordLynx.Conf(FakeData.WgPrivateKey, Wg().WgKey, Wg().Station).Contains("DNS = 103.86.96.100, 103.86.99.100"), "1.3 off: NordLynx keeps NordVPN's DNS");
                Pbk.FilterOf = () => "adguard";
                var ag = Pbk.Expected(Split.All);
                ok(ag["IpDnsAddress"] == "94.140.14.14" && ag["IpDns2Address"] == "94.140.15.15", "1.3 AdGuard: the IKEv2 entry uses AdGuard DNS");
                ok(NordLynx.Conf(FakeData.WgPrivateKey, Wg().WgKey, Wg().Station).Contains("DNS = 94.140.14.14, 94.140.15.15"), "1.3 AdGuard: NordLynx uses AdGuard DNS");
                ok(Pbk.Expected(Split.ExceptGames)["IpDnsAddress"] == "94.140.14.14", "1.3: \"except games\" uses it too");
                var only = Pbk.Expected(Split.Only); ok(!only.ContainsKey("IpDnsAddress") && only["IpNameAssign"] == "1", "1.3: \"only these\" split mode never sets DNS");
                Pbk.FilterOf = () => "quad9";
                ok(Pbk.Dns1 == "9.9.9.9" && Pbk.Dns2 == "149.112.112.112", "1.3 Quad9: 9.9.9.9 and 149.112.112.112");
                Pbk.FilterOf = () => "something-else"; ok(Pbk.Dns1 == "103.86.96.100", "1.3: an unknown filter id falls back to off (NordVPN's DNS)");
                Pbk.FilterOf = () => null; ok(Pbk.Dns1 == "103.86.96.100", "1.3: no setting = off");
                ok(DnsFilters.All.Count == 3 && !DnsFilters.IsOn(null) && DnsFilters.IsOn("adguard") && DnsFilters.All.All(f => System.Net.IPAddress.Parse(f.Dns1) != null && System.Net.IPAddress.Parse(f.Dns2) != null), "1.3: three choices, all valid addresses");
                ok(KillSwitch.NordDns.SequenceEqual(new[] { "103.86.96.100", "103.86.99.100" }), "1.3: the kill switch rules are unchanged (they only limit DNS outside the tunnel)");
            }
            finally { Pbk.FilterOf = keep; }
        }
    }
}
