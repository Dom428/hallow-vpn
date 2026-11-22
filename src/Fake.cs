// Hallow VPN - made-up data for --snap and the developer test copy (HALLOW_VPN_FAKE=1): servers, countries, an IP check answer and
// Tailscale machines. Nothing here is real; it's built as the same JSON Nord / Tailscale send, so it goes through the real parsers.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace HallowVPN
{
    public static class FakeData
    {
        // id, name, code, lat, lon, city
        static readonly object[][] Countries = {
            new object[] { 228, "United States", "US", 40.71, -74.01, "New York" }, new object[] { 227, "United Kingdom", "GB", 51.51, -0.13, "London" },
            new object[] { 81, "Germany", "DE", 50.11, 8.68, "Frankfurt" }, new object[] { 153, "Netherlands", "NL", 52.37, 4.89, "Amsterdam" },
            new object[] { 74, "France", "FR", 48.86, 2.35, "Paris" }, new object[] { 208, "Sweden", "SE", 59.33, 18.07, "Stockholm" },
            new object[] { 38, "Canada", "CA", 43.65, -79.38, "Toronto" }, new object[] { 108, "Japan", "JP", 35.69, 139.69, "Tokyo" },
            new object[] { 13, "Australia", "AU", -33.87, 151.21, "Sydney" }, new object[] { 202, "Spain", "ES", 40.42, -3.70, "Madrid" },
            new object[] { 106, "Italy", "IT", 45.46, 9.19, "Milan" }, new object[] { 174, "Poland", "PL", 52.23, 21.01, "Warsaw" } };

        static string Esc(string s) { return s.Replace("\\", "\\\\").Replace("\"", "\\\""); }
        static string ServerJson(int id, string name, string host, string ip, int load, object[] c, bool p2p)
        {
            var groups = "[{\"id\":11,\"title\":\"Standard VPN servers\"}" + (p2p ? ",{\"id\":15,\"title\":\"P2P\"}" : "") + "]";
            return "{\"id\":" + id + ",\"name\":\"" + Esc(name) + "\",\"station\":\"" + ip + "\",\"hostname\":\"" + host + "\",\"load\":" + load + ",\"status\":\"online\"," +
                "\"locations\":[{\"latitude\":" + ((double)c[3]).ToString(CultureInfo.InvariantCulture) + ",\"longitude\":" + ((double)c[4]).ToString(CultureInfo.InvariantCulture) +
                ",\"country\":{\"id\":" + c[0] + ",\"name\":\"" + c[1] + "\",\"code\":\"" + c[2] + "\",\"city\":{\"id\":1,\"name\":\"" + c[5] + "\"}}}]," +
                "\"groups\":" + groups + ",\"technologies\":[{\"id\":1,\"identifier\":\"ikev2\"},{\"id\":35,\"identifier\":\"wireguard_udp\",\"metadata\":[{\"name\":\"public_key\",\"value\":\"" + WgKeyFor(host) + "\"}]}]}";
        }
        // made up: a valid-looking WireGuard key ("hallow-fake-private-key-00000000" in base64) and server keys derived from the host name
        public const string WgPrivateKey = "aGFsbG93LWZha2UtcHJpdmF0ZS1rZXktMDAwMDAwMDA=";
        public static string WgKeyFor(string host) { using (var h = System.Security.Cryptography.SHA256.Create()) return Convert.ToBase64String(h.ComputeHash(System.Text.Encoding.UTF8.GetBytes(host))); }
        // servers in one country (or, for 0, the "recommended" mix: mostly the nearest countries)
        public static string ServersJson(int countryId)
        {
            var list = new List<string>(); var rnd = new Random(countryId * 7919 + 3);
            var cs = countryId > 0 ? Countries.Where(c => (int)c[0] == countryId).ToList() : Countries.Take(5).ToList();
            int per = countryId > 0 ? 12 : 4;
            foreach (var c in cs)
                for (int i = 0; i < per; i++)
                {
                    int num = 1000 + rnd.Next(9000); var code = ((string)c[2]).ToLowerInvariant(); if (code == "gb") code = "uk";
                    list.Add(ServerJson(900000 + num, c[1] + " #" + num, code + num + ".nordvpn.com", "203.0.113." + rnd.Next(1, 254), 5 + rnd.Next(80), c, i % 3 == 0));
                }
            return "[" + string.Join(",", list) + "]";
        }
        public static string CountriesJson()
        {
            return "[" + string.Join(",", Countries.Select(c => "{\"id\":" + c[0] + ",\"name\":\"" + c[1] + "\",\"code\":\"" + c[2] + "\",\"serverCount\":" + (40 + (int)c[0] % 300) + ",\"cities\":[]}")) + "]";
        }
        public const string RealIpJson = "{\"ip\":\"198.51.100.23\",\"country\":\"United Kingdom\",\"isp\":\"Example Broadband\",\"protected\":false}";
        public const string IpJson = "{\"ip\":\"203.0.113.84\",\"country\":\"Netherlands\",\"country_code\":\"NL\",\"city\":\"Amsterdam\",\"isp\":\"Example Hosting\",\"protected\":true}";
        public static FakeNordApi Api()
        {
            var a = new FakeNordApi();
            a.Add("/v1/helpers/ips/insights", IpJson);
            a.Add("/v1/servers/countries", CountriesJson());
            foreach (var c in Countries) a.Add("filters[country_id]=" + c[0] + "&", ServersJson((int)c[0]));
            a.Add("filters[servers_groups][identifier]=" + Nord.P2PGroup, ServersJson(0));   // the P2P ones are kept by ServerBook
            a.Add("/v1/servers/recommendations", ServersJson(0));
            return a;
        }
        public const string TailscaleJson = "{\"Version\":\"1.90.0\",\"BackendState\":\"Running\",\"Self\":{\"HostName\":\"gaming-pc\",\"TailscaleIPs\":[\"100.101.1.2\"]},\"Peer\":{" +
            "\"nodekey:aa\":{\"HostName\":\"home-server\",\"DNSName\":\"home-server.example.ts.net.\",\"OS\":\"linux\",\"TailscaleIPs\":[\"100.88.12.4\",\"fd7a:115c:a1e0::4\"],\"Online\":true,\"ExitNode\":false,\"ExitNodeOption\":true}," +
            "\"nodekey:bb\":{\"HostName\":\"mums-laptop\",\"DNSName\":\"mums-laptop.example.ts.net.\",\"OS\":\"windows\",\"TailscaleIPs\":[\"100.70.3.9\"],\"Online\":true,\"ExitNode\":false,\"ExitNodeOption\":false}," +
            "\"nodekey:cc\":{\"HostName\":\"cabin-router\",\"DNSName\":\"cabin-router.example.ts.net.\",\"OS\":\"linux\",\"TailscaleIPs\":[\"100.99.7.1\"],\"Online\":false,\"ExitNode\":false,\"ExitNodeOption\":true}}}";
    }
}
