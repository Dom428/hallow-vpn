// Hallow VPN - NordVPN's PUBLIC server API (no key, no account): IKEv2 server recommendations (all, or per country), the country
// list and, only while connected, Nord's IP check. Lists are cached in %APPDATA%\HallowVPN\servers.json and refreshed at most every
// 3 hours (a manual Refresh at most every 5 minutes). Pings are measured here, by this PC: ICMP echo to the server's address, or the
// time a TCP connection to port 443 takes when ICMP gets no answer.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace HallowVPN
{
    [DataContract]
    public class Server
    {
        [DataMember] public long Id;
        [DataMember] public string Name;      // "United States #1234"
        [DataMember] public string Host;      // us1234.nordvpn.com
        [DataMember] public string Station;   // its IPv4 address
        [DataMember] public int Load;         // %
        [DataMember] public string Status;    // online
        [DataMember] public string Country, Code, City;
        [DataMember] public int CountryId;
        [DataMember] public double Lat, Lon;
        [DataMember] public List<string> Groups;   // "Standard VPN servers", "P2P", ...
        public int Ping = -1;                      // ms, measured by this PC (-1 = not measured / no answer)
        public string Short { get { return Host == null ? "" : Host.Split('.')[0]; } }   // us1234
        public Server Copy() { var c = (Server)MemberwiseClone(); c.Groups = Groups == null ? new List<string>() : Groups.ToList(); return c; }
        public bool P2P { get { return Groups != null && Groups.Any(g => string.Equals(g, "P2P", StringComparison.OrdinalIgnoreCase)); } }
    }
    [DataContract]
    public class Country
    {
        [DataMember] public int Id;
        [DataMember] public string Name, Code;
        [DataMember] public int Servers;
    }

    // what "Connect" goes to
    public class Target
    {
        public string Kind;    // fastest / gaming / country / server
        public string Value;   // country code (US) or host (us1234.nordvpn.com)
        public override string ToString() { return Kind == "country" || Kind == "server" ? Kind + ":" + Value : Kind; }
        // "fastest", "gaming", "country:US", "US", "server:us1234.nordvpn.com", "us1234", "us1234.nordvpn.com"
        public static Target Parse(string s)
        {
            var v = (s ?? "").Trim(); var l = v.ToLowerInvariant();
            if (l == "" || l == "fastest" || l == "quick") return new Target { Kind = "fastest" };
            if (l == "gaming" || l == "game" || l == "best-for-gaming") return new Target { Kind = "gaming" };
            if (l == "p2p" || l == "torrents" || l == "torrent" || l == "best-for-torrents") return new Target { Kind = "p2p" };
            if (l.StartsWith("country:")) { var c = v.Substring(8).Trim().ToUpperInvariant(); return Regex.IsMatch(c, "^[A-Z]{2}$") ? new Target { Kind = "country", Value = c } : null; }
            if (l.StartsWith("server:")) l = l.Substring(7).Trim();
            if (Regex.IsMatch(l, "^[a-z]{2}$")) return new Target { Kind = "country", Value = l.ToUpperInvariant() };
            if (!l.EndsWith(".nordvpn.com")) l += ".nordvpn.com";
            return Nord.ValidHost(l) ? new Target { Kind = "server", Value = l } : null;
        }
    }

    public interface INordApi { string Get(string url); }
    public class RealNordApi : INordApi
    {
        public string Get(string url)
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            var req = (HttpWebRequest)WebRequest.Create(url);
            req.UserAgent = "HallowVPN/" + Program.Version; req.Timeout = 15000; req.ReadWriteTimeout = 15000; req.Accept = "application/json";
            req.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
            using (var resp = (HttpWebResponse)req.GetResponse()) using (var r = new StreamReader(resp.GetResponseStream(), Encoding.UTF8)) return r.ReadToEnd();
        }
    }
    // tests / --snap: canned answers by URL part; Calls lists what was asked
    public class FakeNordApi : INordApi
    {
        public readonly List<KeyValuePair<string, string>> Answers = new List<KeyValuePair<string, string>>();
        public readonly List<string> Calls = new List<string>(); public bool Down;
        public void Add(string urlPart, string json) { Answers.Add(new KeyValuePair<string, string>(urlPart, json)); }
        public string Get(string url)
        {
            lock (Calls) Calls.Add(url);
            if (Down) throw new WebException("The remote name could not be resolved: 'api.nordvpn.com'");
            foreach (var a in Answers) if (url.Contains(a.Key)) return a.Value;
            throw new WebException("404 (fake)");
        }
    }

    public static class Nord
    {
        public const string Api = "https://api.nordvpn.com";
        public static bool ValidHost(string h) { return h != null && h.Length <= 60 && Regex.IsMatch(h, "^[a-z0-9]+(-[a-z0-9]+)*\\.nordvpn\\.com$"); }
        // Nord's P2P server group (from /v1/servers/groups: id 15, identifier "legacy_p2p", title "P2P")
        public const string P2PGroup = "legacy_p2p";
        public static string RecommendationsUrl(int countryId, int limit, bool p2p = false)
        {
            return Api + "/v1/servers/recommendations?filters[servers_technologies][identifier]=ikev2" + (p2p ? "&filters[servers_groups][identifier]=" + P2PGroup : "") + (countryId > 0 ? "&filters[country_id]=" + countryId : "") + "&limit=" + limit;
        }
        public static string CountriesUrl { get { return Api + "/v1/servers/countries"; } }
        public static string IpUrl { get { return Api + "/v1/helpers/ips/insights"; } }

        static JavaScriptSerializer Js() { return new JavaScriptSerializer { MaxJsonLength = 64 * 1024 * 1024 }; }
        static object Get(object o, string key) { var d = o as Dictionary<string, object>; object v; return d != null && d.TryGetValue(key, out v) ? v : null; }
        static string Str(object o, string key) { var v = Get(o, key); return v == null ? null : Convert.ToString(v, CultureInfo.InvariantCulture); }
        static double Num(object o, string key) { var v = Get(o, key); try { return v == null ? 0 : Convert.ToDouble(v, CultureInfo.InvariantCulture); } catch { return 0; } }

        // the recommendations answer (a JSON array of servers) -> servers that offer IKEv2 and are online; anything odd is skipped
        public static List<Server> ParseServers(string json)
        {
            var r = new List<Server>();
            var arr = Js().DeserializeObject(json ?? "") as object[];
            if (arr == null) throw new FormatException("Nord's server list wasn't a list");
            foreach (var o in arr)
            {
                try
                {
                    var host = (Str(o, "hostname") ?? "").ToLowerInvariant();
                    if (!ValidHost(host)) continue;
                    var status = Str(o, "status");
                    if (status != null && status != "online") continue;
                    var tech = Get(o, "technologies") as object[];
                    if (tech != null && !tech.Any(t => Str(t, "identifier") == "ikev2")) continue;
                    var s = new Server { Id = (long)Num(o, "id"), Name = Str(o, "name") ?? host, Host = host, Station = Str(o, "station"), Load = (int)Num(o, "load"), Status = status ?? "online", Groups = new List<string>() };
                    var locs = Get(o, "locations") as object[];
                    if (locs != null && locs.Length > 0)
                    {
                        var c = Get(locs[0], "country"); s.Lat = Num(locs[0], "latitude"); s.Lon = Num(locs[0], "longitude");
                        s.Country = Str(c, "name"); s.Code = (Str(c, "code") ?? "").ToUpperInvariant(); s.CountryId = (int)Num(c, "id"); s.City = Str(Get(c, "city"), "name");
                    }
                    var groups = Get(o, "groups") as object[];
                    if (groups != null) foreach (var g in groups) { var t = Str(g, "title"); if (t != null) s.Groups.Add(t); }
                    if (s.Station != null && !Regex.IsMatch(s.Station, "^\\d{1,3}(\\.\\d{1,3}){3}$")) s.Station = null;
                    r.Add(s);
                }
                catch { }
            }
            return r;
        }
        public static List<Country> ParseCountries(string json)
        {
            var arr = Js().DeserializeObject(json ?? "") as object[];
            if (arr == null) throw new FormatException("Nord's country list wasn't a list");
            var r = new List<Country>();
            foreach (var o in arr)
            {
                var code = (Str(o, "code") ?? "").ToUpperInvariant(); var id = (int)Num(o, "id"); var name = Str(o, "name");
                if (id <= 0 || name == null || !Regex.IsMatch(code, "^[A-Z]{2}$")) continue;
                r.Add(new Country { Id = id, Name = name, Code = code, Servers = (int)Num(o, "serverCount") });
            }
            return r.OrderBy(c => c.Name).ToList();
        }
        // Nord's IP check: { "ip": "...", "country": "...", "protected": true, ... }
        public static string ParseIp(string json, out string country, out bool? isProtected)
        {
            country = null; isProtected = null;
            var o = Js().DeserializeObject(json ?? "");
            var ip = Str(o, "ip"); country = Str(o, "country");
            var p = Get(o, "protected"); if (p is bool) isProtected = (bool)p;
            IPAddress a; return ip != null && IPAddress.TryParse(ip, out a) ? ip : null;
        }

        // ---- picking a server from a measured list
        // Fastest: the lowest ping, with load counted in (a busy server is slower) - ping + 0.8 x load; unmeasured = 400 ms
        public static double FastScore(Server s) { return (s.Ping >= 0 ? s.Ping : 400) + 0.8 * s.Load; }
        // Best for gaming: nearby and quiet - only servers under 40 % load (if there are any), then the lowest ping, then the lower load
        public static Server Fastest(IEnumerable<Server> list) { return list.Where(s => s != null).OrderBy(FastScore).ThenBy(s => s.Load).FirstOrDefault(); }
        public static Server ForGaming(IEnumerable<Server> list)
        {
            var l = list.Where(s => s != null).ToList();
            var quiet = l.Where(s => s.Load < 40).ToList(); if (quiet.Count > 0) l = quiet;
            return l.OrderBy(s => s.Ping >= 0 ? s.Ping : 9999).ThenBy(s => s.Load).FirstOrDefault();
        }
        public static double Km(double lat1, double lon1, double lat2, double lon2)
        {
            Func<double, double> rad = d => d * Math.PI / 180;
            var a = Math.Pow(Math.Sin(rad(lat2 - lat1) / 2), 2) + Math.Cos(rad(lat1)) * Math.Cos(rad(lat2)) * Math.Pow(Math.Sin(rad(lon2 - lon1) / 2), 2);
            return 6371 * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        }
    }

    // ---- pings measured by this PC
    public interface IPinger { int Ping(Server s); }
    public class RealPinger : IPinger
    {
        public int Ping(Server s)
        {
            IPAddress ip;
            if (s.Station == null || !IPAddress.TryParse(s.Station, out ip)) return -1;
            try { using (var p = new System.Net.NetworkInformation.Ping()) { var r = p.Send(ip, 1000); if (r.Status == IPStatus.Success) return (int)Math.Max(1, r.RoundtripTime); } } catch { }
            try
            {
                using (var c = new TcpClient(AddressFamily.InterNetwork))
                {
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    var t = c.ConnectAsync(ip, 443);
                    if (t.Wait(1200) && c.Connected) return (int)Math.Max(1, sw.ElapsedMilliseconds);
                }
            }
            catch { }
            return -1;
        }
    }
    public class FakePinger : IPinger
    {
        public readonly Dictionary<string, int> Ms = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase); public int Calls;
        public int Ping(Server s) { Interlocked.Increment(ref Calls); int v; if (Ms.TryGetValue(s.Host, out v)) return v; return 20 + (int)((uint)s.Host.GetHashCode() % 140); }
    }

    // ---- the cache: servers.json in the data folder
    [DataContract] public class CachedList { [DataMember] public string Fetched; [DataMember] public List<Server> Servers; }
    [DataContract] public class CacheFile
    {
        [DataMember] public Dictionary<string, CachedList> Lists = new Dictionary<string, CachedList>();
        [DataMember] public string CountriesFetched; [DataMember] public List<Country> Countries;
    }

    public class ServerBook
    {
        public static readonly TimeSpan MaxAge = TimeSpan.FromHours(3), ManualGap = TimeSpan.FromMinutes(5);
        readonly INordApi api; readonly IPinger pinger; readonly string file; readonly object gate = new object();
        CacheFile cache = new CacheFile();
        public Func<DateTime> Now = () => DateTime.Now;
        readonly Dictionary<string, DateTime> lastFetch = new Dictionary<string, DateTime>();
        readonly Dictionary<string, KeyValuePair<int, DateTime>> pings = new Dictionary<string, KeyValuePair<int, DateTime>>(StringComparer.OrdinalIgnoreCase);
        public int Fetches;
        public ServerBook(INordApi api, IPinger pinger, string file)
        {
            this.api = api; this.pinger = pinger; this.file = file;
            try { if (file != null && File.Exists(file)) using (var f = File.OpenRead(file)) cache = (CacheFile)new DataContractJsonSerializer(typeof(CacheFile)).ReadObject(f) ?? new CacheFile(); } catch { cache = new CacheFile(); }
            if (cache.Lists == null) cache.Lists = new Dictionary<string, CachedList>();
        }
        void SaveCache()
        {
            if (file == null || VpnSettings.Frozen) return;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(file));
                var tmp = file + ".tmp";
                using (var f = File.Create(tmp)) new DataContractJsonSerializer(typeof(CacheFile)).WriteObject(f, cache);
                if (File.Exists(file)) File.Replace(tmp, file, null); else File.Move(tmp, file);
            }
            catch { }
        }
        static DateTime? When(string s) { DateTime t; return DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out t) ? t : (DateTime?)null; }
        public static string Key(int countryId) { return countryId > 0 ? "country:" + countryId : "all"; }
        public DateTime? Fetched(string key) { lock (gate) { CachedList c; return cache.Lists.TryGetValue(key, out c) ? When(c.Fetched) : null; } }
        public List<Country> Countries { get { lock (gate) return cache.Countries == null ? new List<Country>() : cache.Countries.ToList(); } }

        // a server list: from the cache while it's younger than 3 h; refreshed from Nord otherwise (force = the Refresh button, at most
        // every 5 minutes). error = why Nord couldn't be asked (the cached list is still returned when there is one).
        public List<Server> List(int countryId, bool force, out string error, bool p2p = false)
        {
            error = null; var key = p2p ? "p2p" : Key(countryId); if (p2p) countryId = 0; CachedList c; DateTime? at;
            lock (gate) { cache.Lists.TryGetValue(key, out c); at = c == null ? null : When(c.Fetched); }
            var now = Now(); DateTime last;
            bool stale = c == null || at == null || now - at.Value >= MaxAge || now < at.Value;
            bool manualOk = force && (!lastFetch.TryGetValue(key, out last) || now - last >= ManualGap) && (at == null || now - at.Value >= ManualGap);
            if (stale || manualOk)
            {
                try
                {
                    lastFetch[key] = now; Interlocked.Increment(ref Fetches);
                    var l = Nord.ParseServers(api.Get(Nord.RecommendationsUrl(countryId, countryId > 0 || p2p ? 25 : 40, p2p)));
                    if (p2p) l = l.Where(s => s.P2P).ToList();   // only servers Nord itself lists in the P2P group
                    if (l.Count == 0) throw new FormatException("Nord sent an empty list");
                    lock (gate) { c = new CachedList { Fetched = now.ToString("o"), Servers = l }; cache.Lists[key] = c; }
                    SaveCache();
                }
                catch (Exception e) { error = Friendly(e); }
            }
            else if (force) error = "The list is from " + Fmt.Ago(at.Value, now) + " - it refreshes at most every 5 minutes.";
            if (c == null || c.Servers == null) return new List<Server>();
            var copy = c.Servers.Select(Clone).ToList();
            foreach (var s in copy) { KeyValuePair<int, DateTime> p; lock (pings) if (pings.TryGetValue(s.Host, out p) && now - p.Value < TimeSpan.FromMinutes(10)) s.Ping = p.Key; }
            return copy;
        }
        public List<Country> LoadCountries(bool force, out string error)
        {
            error = null; DateTime? at; lock (gate) at = When(cache.CountriesFetched);
            var now = Now();
            if (cache.Countries == null || at == null || now - at.Value >= TimeSpan.FromHours(24) || (force && now - at.Value >= ManualGap))
            {
                try
                {
                    Interlocked.Increment(ref Fetches);
                    var l = Nord.ParseCountries(api.Get(Nord.CountriesUrl));
                    if (l.Count == 0) throw new FormatException("Nord sent an empty country list");
                    lock (gate) { cache.Countries = l; cache.CountriesFetched = now.ToString("o"); }
                    SaveCache();
                }
                catch (Exception e) { error = Friendly(e); }
            }
            // no list from Nord yet: the countries seen in the cached server lists
            var r = Countries;
            if (r.Count == 0)
                lock (gate) r = cache.Lists.Values.Where(x => x.Servers != null).SelectMany(x => x.Servers).Where(s => s.CountryId > 0 && s.Code != null)
                    .GroupBy(s => s.CountryId).Select(g => new Country { Id = g.Key, Name = g.First().Country, Code = g.First().Code }).OrderBy(x => x.Name).ToList();
            return r;
        }
        public Country FindCountry(string codeOrName)
        {
            var v = (codeOrName ?? "").Trim(); string err;
            var l = Countries; if (l.Count == 0) l = LoadCountries(false, out err);
            return l.FirstOrDefault(c => string.Equals(c.Code, v, StringComparison.OrdinalIgnoreCase)) ?? l.FirstOrDefault(c => string.Equals(c.Name, v, StringComparison.OrdinalIgnoreCase));
        }
        // ping up to max servers (in parallel, 1 s each); remembered for 10 minutes
        public void Measure(List<Server> list, int max)
        {
            var todo = list.Take(max).ToList();
            Parallel.ForEach(todo, new ParallelOptions { MaxDegreeOfParallelism = 8 }, s =>
            {
                int ms; try { ms = pinger.Ping(s); } catch { ms = -1; }
                s.Ping = ms;
                lock (pings) pings[s.Host] = new KeyValuePair<int, DateTime>(ms, Now());
            });
        }
        public Server Find(string host)
        {
            lock (gate) foreach (var l in cache.Lists.Values) if (l.Servers != null) { var s = l.Servers.FirstOrDefault(x => string.Equals(x.Host, host, StringComparison.OrdinalIgnoreCase)); if (s != null) return Clone(s); }
            return null;
        }
        static Server Clone(Server s) { return s.Copy(); }
        public static string Friendly(Exception e)
        {
            var w = e as WebException;
            if (w != null && (w.Status == WebExceptionStatus.NameResolutionFailure || w.Status == WebExceptionStatus.ConnectFailure || w.Status == WebExceptionStatus.Timeout)) return "Couldn't reach Nord's server list (api.nordvpn.com) - check the internet connection.";
            if (e is FormatException) return "Nord's server list came back in a form Hallow VPN doesn't understand (" + e.Message + ").";
            return "Couldn't get Nord's server list: " + e.Message;
        }
    }

    public static class Fmt
    {
        public static string Ago(DateTime t, DateTime now)
        {
            var d = now - t; if (d.TotalSeconds < 0) d = TimeSpan.Zero;
            if (d.TotalMinutes < 1) return "just now";
            if (d.TotalMinutes < 60) return (int)d.TotalMinutes + " min ago";
            if (d.TotalHours < 24) return (int)d.TotalHours + " h ago";
            return (int)d.TotalDays + " d ago";
        }
        public static string Span(TimeSpan d)
        {
            if (d.TotalSeconds < 0) d = TimeSpan.Zero;
            return d.TotalHours >= 1 ? ((int)d.TotalHours).ToString() + ":" + d.Minutes.ToString("00") + ":" + d.Seconds.ToString("00") : d.Minutes.ToString("00") + ":" + d.Seconds.ToString("00");
        }
        public static string Ms(int ms) { return ms < 0 ? "-" : ms + " ms"; }
    }
}
