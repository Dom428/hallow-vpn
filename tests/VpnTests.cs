// Hallow VPN unit tests. Everything that would change this PC is FAKED: Nord's API answers come from fixtures (one is a real answer
// saved once, read-only), the VPN layer, the firewall, Tailscale and the games are fakes. The only real calls are READ-ONLY:
// RasEnumConnections (this user's active connections), RasDial against an EMPTY phonebook file in the test folder (it can't find
// the entry - this checks Windows accepts the structure; nothing is dialled), reading the firewall rule list, reading the certificate
// store, and `tailscale status --json` (only counted). No VPN profile is created, nothing connects, no firewall rule, route or
// Tailscale setting is changed.
//   tests\VpnTests.exe
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Media;

namespace HallowVPN.Tests
{
    public static class Run
    {
        static int pass, fail;
        static void Ok(bool c, string name) { if (c) { pass++; Console.WriteLine("ok   " + name); } else { fail++; Console.WriteLine("FAIL " + name); } }
        static string Dir, Fx;

        [STAThread]
        public static int Main(string[] args)
        {
            Dir = Path.Combine(Path.GetTempPath(), "HallowVPNTest" + Process.GetCurrentProcess().Id);
            Environment.SetEnvironmentVariable("HALLOW_VPN_TESTDIR", Dir);
            Fx = Path.Combine(Path.GetDirectoryName(typeof(Run).Assembly.Location), "fixtures");
            // 1.2.6: the whole run stops itself after 5 minutes (the ship script kills it then too)
            new Thread(() => { Thread.Sleep(TimeSpan.FromMinutes(5)); Console.WriteLine("FAIL the tests took longer than 5 minutes - stopped"); Console.WriteLine(pass + " passed, " + (fail + 1) + " failed"); Environment.Exit(99); }) { IsBackground = true }.Start();
            var appExe = Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(typeof(Run).Assembly.Location)), "HallowVPN.exe");
            try { Parsing(); Targets(); Picking(); Cache(); SplitTests(); Scripts(); AccountTests(); KillSwitchTests(); CaTests(); GameTests(); TailscaleTests(); ModeTests(); Misc(); V11.All(Ok, Dir, Fx); V12.All(Ok, Dir, Fx); RealReadOnly(); PipeAndApp(); V126.All(Ok, Dir, appExe); }
            catch (Exception e) { fail++; Console.WriteLine("FAIL exception: " + e); }
            try { if (Directory.Exists(Dir)) Directory.Delete(Dir, true); } catch { }
            Console.WriteLine(pass + " passed, " + fail + " failed");
            return fail == 0 ? 0 : 1;
        }
        static string F(string name) { return File.ReadAllText(Path.Combine(Fx, name)); }

        // ---------------- Nord's API answers
        static void Parsing()
        {
            var l = Nord.ParseServers(F("recommendations-real.json"));
            Ok(l.Count >= 1, "the real recommendations answer (saved once) parses: " + l.Count + " servers");
            var s = l[0];
            Ok(Nord.ValidHost(s.Host) && Regex.IsMatch(s.Short, "^[a-z]{2}\\d+$"), "host and short name: " + s.Host + " / " + s.Short);
            Ok(s.Country != null && Regex.IsMatch(s.Code, "^[A-Z]{2}$") && s.CountryId > 0 && s.City != null, "country, code, id and city from locations[0].country: " + s.Country + " " + s.Code + " " + s.CountryId + " " + s.City);
            Ok(s.Load >= 0 && s.Load <= 100 && s.Station != null && IPAddress.Parse(s.Station) != null, "load and station address");
            Ok(s.Groups.Count > 0 && s.Lat != 0, "groups (" + string.Join(", ", s.Groups) + ") and coordinates");
            var odd = Nord.ParseServers(F("recommendations-odd.json"));
            Ok(odd.Count == 1 && odd[0].Host == "de4.nordvpn.com", "odd answer: a host with a quote, an offline server, a server without IKEv2 and junk are skipped; upper case is lowered");
            Ok(odd[0].Station == null && odd[0].Code == "DE" && odd[0].P2P && odd[0].Load == 71, "a bad station address is dropped; code upper-cased; P2P group seen");
            try { Nord.ParseServers("{\"error\":\"nope\"}"); Ok(false, "an object instead of a list throws"); } catch (FormatException) { Ok(true, "an object instead of a list throws FormatException"); }
            var cs = Nord.ParseCountries(F("countries.json"));
            Ok(cs.Count == 3 && cs[0].Name == "Albania" && cs.Any(c => c.Code == "US" && c.Id == 228 && c.Servers == 1900), "countries: 3 good ones, sorted; bad id / code skipped");
            string country; bool? prot;
            Ok(Nord.ParseIp(F("ip-insights.json"), out country, out prot) == "203.0.113.84" && country == "Netherlands" && prot == true, "Nord's IP check: ip, country, protected");
            Ok(Nord.ParseIp("{\"ip\":\"not an ip\"}", out country, out prot) == null, "a bad IP is ignored");
            Ok(Nord.RecommendationsUrl(0, 40) == "https://api.nordvpn.com/v1/servers/recommendations?filters[servers_technologies][identifier]=ikev2&limit=40", "the recommendations URL (IKEv2 only)");
            Ok(Nord.RecommendationsUrl(81, 25).Contains("&filters[country_id]=81&limit=25"), "country filter in the URL");
            Ok(!Nord.ValidHost("us1.nordvpn.com.evil.com") && !Nord.ValidHost("us1.nordvpn.com'") && !Nord.ValidHost("US1.nordvpn.com") && Nord.ValidHost("uk-nl1.nordvpn.com"), "host check (only *.nordvpn.com, lower case, no quotes)");
        }
        static void Targets()
        {
            Func<string, string> t = x => { var r = Target.Parse(x); return r == null ? "null" : r.ToString(); };
            Ok(t("") == "fastest" && t("fastest") == "fastest" && t("Gaming") == "gaming", "fastest / gaming");
            Ok(t("us") == "country:US" && t("country:de") == "country:DE" && t("country:Germany") == "null", "country codes");
            Ok(t("us1234") == "server:us1234.nordvpn.com" && t("server:DE77.nordvpn.com") == "server:de77.nordvpn.com" && t("uk-nl1") == "server:uk-nl1.nordvpn.com", "servers, short or full");
            Ok(t("evil.com") == "null" && t("us1; calc") == "null" && t("us1.nordvpn.com/x") == "null", "anything else is refused");
        }
        static List<Server> Fakes()
        {
            return new List<Server> {
                new Server { Host = "a1.nordvpn.com", Load = 10, Ping = 80 }, new Server { Host = "a2.nordvpn.com", Load = 60, Ping = 20 },
                new Server { Host = "a3.nordvpn.com", Load = 35, Ping = 25 }, new Server { Host = "a4.nordvpn.com", Load = 5, Ping = -1 }, new Server { Host = "a5.nordvpn.com", Load = 90, Ping = 15 } };
        }
        static void Picking()
        {
            Ok(Nord.Fastest(Fakes()).Host == "a3.nordvpn.com", "Fastest = ping + 0.8 x load (25 + 28 beats 20 + 48 and 15 + 72)");
            Ok(Nord.ForGaming(Fakes()).Host == "a3.nordvpn.com", "Best for gaming = under 40 % load, then lowest ping");
            var busy = new List<Server> { new Server { Host = "b1.nordvpn.com", Load = 80, Ping = 30 }, new Server { Host = "b2.nordvpn.com", Load = 50, Ping = 40 } };
            Ok(Nord.ForGaming(busy).Host == "b1.nordvpn.com", "all busy: lowest ping anyway");
            Ok(Nord.Fastest(new List<Server>()) == null && Nord.ForGaming(new List<Server>()) == null, "empty list: nothing");
            Ok(Math.Abs(Nord.Km(52.37, 4.89, 51.51, -0.13) - 358) < 10, "distance Amsterdam - London ~358 km");
        }
        static void Cache()
        {
            var api = new FakeNordApi(); api.Add("/v1/servers/countries", F("countries.json")); api.Add("filters[country_id]=228&", F("recommendations-real.json")); api.Add("/v1/servers/recommendations", F("recommendations-real.json"));
            var file = Path.Combine(Dir, "cache", "servers.json"); Directory.CreateDirectory(Path.GetDirectoryName(file));
            var pinger = new FakePinger();
            var book = new ServerBook(api, pinger, file); var now = new DateTime(2026, 11, 21, 12, 0, 0); book.Now = () => now;
            string err;
            var l = book.List(0, false, out err);
            Ok(l.Count >= 1 && err == null && api.Calls.Count == 1 && File.Exists(file), "first list: fetched from Nord and cached in servers.json");
            now = now.AddHours(2.9); book.List(0, false, out err);
            Ok(api.Calls.Count == 1, "2.9 hours later: from the cache, no fetch");
            book.List(0, true, out err);
            Ok(api.Calls.Count == 2, "Refresh after 2.9 hours: fetched");
            now = now.AddMinutes(2); book.List(0, true, out err);
            Ok(api.Calls.Count == 2 && err != null && err.Contains("5 minutes"), "Refresh again 2 minutes later: refused politely (" + err + ")");
            now = now.AddHours(3); api.Down = true; l = book.List(0, false, out err);
            Ok(api.Calls.Count == 3 && l.Count >= 1 && err != null && err.Contains("api.nordvpn.com"), "3 hours later with Nord unreachable: the cached list + an error");
            api.Down = false;
            var fetchedAt = book.Fetched("all").Value; var book2 = new ServerBook(api, pinger, file); book2.Now = () => fetchedAt.AddMinutes(30); book2.List(0, false, out err);
            Ok(api.Calls.Count == 3, "a new start reads servers.json (no fetch while it's fresh)");
            var cs = book.LoadCountries(false, out err); Ok(cs.Count == 3 && api.Calls.Count == 4, "countries fetched");
            book.LoadCountries(false, out err); Ok(api.Calls.Count == 4, "countries cached");
            var us = book.FindCountry("us"); Ok(us != null && us.Id == 228 && book.FindCountry("United States") != null && book.FindCountry("Narnia") == null, "find a country by code or name");
            l = book.List(228, false, out err); Ok(api.Calls.Last().Contains("filters[country_id]=228"), "a country's list uses the country filter");
            book.Measure(l, 12); Ok(l.All(x => x.Ping >= 0) && pinger.Calls >= l.Count, "pings measured (fake pinger)");
            var calls = pinger.Calls; var again = book.List(228, false, out err); Ok(again.All(x => x.Ping >= 0) && pinger.Calls == calls, "pings remembered for 10 minutes");
            Ok(book.Find(l[0].Host) != null && book.Find("zz9.nordvpn.com") == null, "find a cached server by host");
            var empty = new ServerBook(new FakeNordApi { Down = true }, pinger, null); Ok(empty.List(0, false, out err).Count == 0 && err != null, "no cache and no Nord: empty list and a reason");
            Ok(api.Calls.All(u => u.StartsWith("https://api.nordvpn.com/")), "only api.nordvpn.com is asked");
        }

        // ---------------- split tunnelling
        static void SplitTests()
        {
            Ok(Split.Normalize("203.0.113.5") == "203.0.113.5/32" && Split.Normalize(" 203.0.113.77/24 ") == "203.0.113.0/24", "addresses and ranges, tidied");
            Ok(Split.Normalize("2001:db8::1/32") == "2001:db8::/32", "IPv6 ranges");
            Ok(Split.Normalize("0.0.0.0/0") == null && Split.Normalize("1.2.3.4/33") == null && Split.Normalize("1.2.3") == null, "/0, /33 and half addresses refused");
            Ok(Split.Normalize("Example.COM") == "example.com" && Split.Normalize("https://www.example.com/path") == "www.example.com", "websites (and URLs -> their host)");
            Ok(Split.Normalize("bad host!") == null && Split.Normalize("x'; calc") == null && Split.Normalize("") == null, "junk refused");
            List<string> failed;
            var r = Split.Routes(new[] { "example.com", "10.0.0.0/8", "nowhere.invalid", "198.51.100.9" }, h => h == "example.com" ? new[] { IPAddress.Parse("93.184.215.14"), IPAddress.Parse("2606:2800:21f:cb07::1") } : new IPAddress[0], out failed);
            Ok(r.SequenceEqual(new[] { "93.184.215.14/32", "2606:2800:21f:cb07::1/128", "10.0.0.0/8", "198.51.100.9/32" }) && failed.SequenceEqual(new[] { "nowhere.invalid" }), "routes: websites looked up (fake), ranges kept, failures listed");
            var spec = Split.Spec("nl1.nordvpn.com", Split.Only, r); Ok(spec.SplitTunnel && spec.Routes.Count == 4, "\"Only these\" = split tunnelling with routes");
            Ok(!Split.Spec("nl1.nordvpn.com", Split.All, r).SplitTunnel && !Split.Spec("nl1.nordvpn.com", Split.ExceptGames, r).SplitTunnel && !Split.Spec("nl1.nordvpn.com", Split.Only, new List<string>()).SplitTunnel, "everything / except games / an empty list = full tunnel");
            var s = new VpnSettings { SplitRoutes = new List<string> { "1.2.3.4", "junk!!", "1.2.3.4/32" }, SplitMode = "weird", GameMode = "x", LastTarget = "evil.com", Favourites = new List<Favourite> { new Favourite { Host = "bad" }, new Favourite { Host = "us1.nordvpn.com" } } };
            s.Fix();
            Ok(s.SplitRoutes.SequenceEqual(new[] { "1.2.3.4/32" }) && s.SplitMode == Split.All && s.GameMode == Games.ModeOff && s.LastTarget == "fastest" && s.Favourites.Count == 1, "settings are cleaned on load");
        }
        static void Scripts()
        {
            var p = VpnScript.Profile(new ProfileSpec { Host = "nl1234.nordvpn.com", SplitTunnel = true, Routes = new List<string> { "203.0.113.0/24", "2001:db8::/32" } });
            Ok(!p.Contains("AllUserConnection"), "profile: per-user (never -AllUserConnection, so no admin)");
            Ok(p.Contains("-TunnelType Ikev2") && p.Contains("-AuthenticationMethod Eap") && p.Contains("-EncryptionLevel Required") && p.Contains("-RememberCredential:$true") && !p.Contains("-RememberCredential:$false"), "profile: IKEv2, EAP, encryption required, Windows remembers the EAP login (Nord's EAP only works with Windows' saved login)");
            Ok(!Regex.IsMatch(p, @"-(SplitTunneling|RememberCredential|Force|PassThru|AllUserConnection|UseWinlogonCredential) \$"), "profile: switches written -Switch:$value (with a space PowerShell binds the value to another parameter and Windows refuses the profile)");
            // real PowerShell through VpnScript.Run with harmless scripts (nothing on the system changes): the first line must
            // survive (a byte-order mark on stdin broke "$ErrorActionPreference" in 1.0-1.2.1), and a failure always has a reason
            var harmless = "$ErrorActionPreference = 'Stop'\r\ntry {\r\n  $x = 1\r\n  'ok'; exit 0\r\n} catch { 'ERR ' + $_.Exception.Message; exit 1 }\r\n";
            Ok(VpnScript.Run(harmless) == null, "Run: a multi-line script starting with $ErrorActionPreference runs in real PowerShell (no BOM on stdin)");
            Ok(VpnScript.Run("﻿" + harmless) == null, "Run: a byte-order mark in the script is removed before PowerShell sees it");
            var failing = VpnScript.Run("$ErrorActionPreference = 'Stop'\r\ntry {\r\n  throw 'made-up failure'\r\n} catch { 'ERR ' + $_.Exception.Message; exit 1 }\r\n");
            Ok(failing != null && failing.Contains("made-up failure"), "Run: the script's own error reaches the app (" + failing + ")");
            Ok(VpnScript.Reason("", "", 5).Contains("exit code 5") && VpnScript.Reason("", "#< CLIXML\r\nboom", 1) == "boom" && VpnScript.Reason("x\nERR bad", "", 1) == "bad", "Reason: never empty (ERR line, then stderr, then output, then the exit code)");
            Ok(p.Contains("Set-VpnConnectionIPsecConfiguration -ConnectionName $n") && p.Contains("-CipherTransformConstants GCMAES256") && p.Contains("-DHGroup Group14") && p.IndexOf("Set-VpnConnectionIPsecConfiguration") > p.IndexOf("Add-VpnConnection"), "profile: Nord's IKEv2 encryption (AES-256-GCM, SHA-256, DH 14) set after the entry exists - Windows' defaults give error 13868");
            Ok(Account.Check(" user1 ", " pass1 ") == null && Account.Unprotect(Account.Protect(new Credentials { User = "u1 ", Pass = " p1 " })).Pass == "p1", "account: spaces copied around the password are trimmed (they gave error 691)");
            Ok(!Ras.HasWindowsLogin(Path.Combine(Path.GetTempPath(), "hallow-no-such-" + Guid.NewGuid().ToString("N") + ".pbk")), "Windows login check: a phonebook without the entry has no saved login (size-only read)");
            Ok(Ras.NeedsWindowsLogin.Contains("Sign in through Windows") && Ras.NeedsWindowsLogin.Contains("691"), "691 without Windows' saved login says exactly what to click");
            { int bs; var none = Ras.WindowsLoginBlob(Path.Combine(Path.GetTempPath(), "hallow-no-such-" + Guid.NewGuid().ToString("N") + ".pbk"), out bs); Ok(none == IntPtr.Zero && bs == 0, "saved-login blob: none for a phonebook without the entry");
              int es; var ext = Ras.Extensions(new IntPtr(0x1234), 77, false, out es);
              Ok(es == (IntPtr.Size == 8 ? 60 : 40) && System.Runtime.InteropServices.Marshal.ReadInt32(ext, 0) == es && System.Runtime.InteropServices.Marshal.ReadInt32(ext, IntPtr.Size == 8 ? 32 : 20) == 77 && System.Runtime.InteropServices.Marshal.ReadIntPtr(ext, IntPtr.Size == 8 ? 36 : 24) == new IntPtr(0x1234), "RASDIALEXTENSIONS packed like ras.h (60 bytes on x64, RasEapInfo at 32 / 36)");
              System.Runtime.InteropServices.Marshal.FreeHGlobal(ext);
              var nat = Ras.Extensions(new IntPtr(0x1234), 77, true, out es); Ok(IntPtr.Size != 8 || (es == 64 && System.Runtime.InteropServices.Marshal.ReadIntPtr(nat, 40) == new IntPtr(0x1234)), "RASDIALEXTENSIONS natural layout (64) for the 632 retry"); System.Runtime.InteropServices.Marshal.FreeHGlobal(nat); }
            Ok(p.Contains("-SplitTunneling:$split") && p.Split(new[] { "-SplitTunneling:$split" }, StringSplitOptions.None).Length == 3, "profile: both Add- and Set-VpnConnection pass split tunnelling as a switch value");
            Ok(p.Contains("'Hallow VPN'") && p.Contains("-ServerAddress 'nl1234.nordvpn.com'") && p.Contains("$split = $true"), "profile: name, server, split tunnelling");
            Ok(p.Contains("Add-VpnConnectionRoute -ConnectionName $n -DestinationPrefix '203.0.113.0/24'") && p.Contains("'2001:db8::/32'") && p.Contains("Remove-VpnConnectionRoute"), "profile: old routes out, new routes in");
            Ok(!Regex.IsMatch(p, "(?i)password|-Credential\\b|UsernamePassword"), "profile script carries no credentials");
            var full = VpnScript.Profile(new ProfileSpec { Host = "nl1.nordvpn.com" }); Ok(full.Contains("$split = $false") && !full.Contains("Add-VpnConnectionRoute"), "full tunnel: no routes");
            try { VpnScript.Profile(new ProfileSpec { Host = "x'; Remove-Item C:\\ ;'" }); Ok(false, "bad host refused"); } catch (ArgumentException) { Ok(true, "a bad host never reaches the script"); }
            try { VpnScript.Profile(new ProfileSpec { Host = "nl1.nordvpn.com", SplitTunnel = true, Routes = new List<string> { "1.2.3.4/32'; calc" } }); Ok(false, "bad route refused"); } catch (ArgumentException) { Ok(true, "a bad route never reaches the script"); }
            Ok(VpnScript.Remove().Contains("Remove-VpnConnection -Name 'Hallow VPN' -Force") && !VpnScript.Remove().Contains("AllUserConnection"), "removing touches only the per-user \"Hallow VPN\" profile");
            Ok(Ras.Explain(691).Contains("NOT your Nord login") && Ras.Explain(13801).Contains("root certificate") && Ras.Explain(809).Contains("UDP 500") && Ras.Explain(12345).Contains("12345"), "error explanations");
            var fv = new FakeVpn(); fv.ApplyProfile(new ProfileSpec { Host = "nl1.nordvpn.com" }); fv.Dial("nl1.nordvpn.com", new Credentials { User = "qP3xY7mN2kT9", Pass = "s3cretPass" });
            Ok(fv.Up && fv.Calls.Count == 2 && !string.Join(" ", fv.Calls).Contains("s3cretPass") && !string.Join(" ", fv.Calls).Contains("qP3xY7mN2kT9"), "fake VPN layer records calls without the credentials");
            Ok(Ras.DialSize == (IntPtr.Size == 8 ? 2112 : 2108), "RASDIALPARAMS is the Windows 7+ size: " + Ras.DialSize);
        }

        // ---------------- account (DPAPI)
        static void AccountTests()
        {
            Ok(Account.Check("me@example.com", "x") != null && Account.Check("me@example.com", "x").Contains("SERVICE"), "an email address is refused (that's the Nord login)");
            Ok(Account.Check("", "x") != null && Account.Check("abc", "") != null && Account.Check("a b", "x") != null && Account.Check("abc", "x\ny") != null, "empty / spaces / line breaks refused");
            Ok(Account.Check("qP3xY7mN2kT9vR4w", "Zx8!pL2#") == null, "service-style credentials accepted");
            Ok(Account.Save("qP3xY7mN2kT9vR4w", "s3cret-Pass-42") == null && File.Exists(Account.FilePath), "saved");
            var raw = File.ReadAllBytes(Account.FilePath); var asText = Encoding.UTF8.GetString(raw) + Encoding.Unicode.GetString(raw);
            Ok(!asText.Contains("s3cret-Pass-42") && !asText.Contains("qP3xY7mN2kT9vR4w"), "account.dat holds neither in plain text (DPAPI)");
            var c = Account.Load(); Ok(c != null && c.User == "qP3xY7mN2kT9vR4w" && c.Pass == "s3cret-Pass-42", "loads back for this Windows user");
            try { ProtectedData.Unprotect(raw, null, DataProtectionScope.CurrentUser); Ok(false, "needs the entropy"); } catch (CryptographicException) { Ok(true, "it can't be decrypted without Hallow VPN's entropy"); }
            Ok(Credentials.Mask("qP3xY7mN2kT9vR4w") == "qP3\u2022\u2022\u2022\u2022\u2022\u2022vR4w".Substring(0, 9) + "4w", "the username is masked when shown: " + Credentials.Mask("qP3xY7mN2kT9vR4w"));
            Crash.Write(new Exception("boom with s3cret-Pass-42 inside"));
            var cf = Crash.Last(); var ct = cf == null ? "" : File.ReadAllText(cf);
            Ok(cf != null && !ct.Contains("s3cret-Pass-42") && ct.Contains("<secret>"), "a crash report never contains the password");
            File.WriteAllBytes(Account.FilePath, new byte[] { 1, 2, 3 }); Ok(Account.Load() == null, "a broken account.dat = no account (no crash)");
            Account.Remove(); Ok(!File.Exists(Account.FilePath) && !Account.Exists, "removed");
            try { Directory.Delete(Crash.Dir, true); } catch { }
        }

        // ---------------- kill switch (rules built, never added) and the elevated helper
        static uint V4(string s) { var b = IPAddress.Parse(s).GetAddressBytes(); return (uint)(b[0] << 24 | b[1] << 16 | b[2] << 8 | b[3]); }
        static bool InRanges(string ranges, string ip) { var x = V4(ip); return ranges.Split(',').Any(r => { var p = r.Split('-'); return x >= V4(p[0]) && x <= V4(p[1]); }); }
        static bool PortIn(string ranges, int port) { return ranges.Split(',').Any(r => { var p = r.Split('-'); return port >= int.Parse(p[0]) && port <= int.Parse(p.Length > 1 ? p[1] : p[0]); }); }
        static void KillSwitchTests()
        {
            var rules = KillSwitch.Rules();
            Ok(rules.Count == 16 && rules.All(r => r.Name.StartsWith(KillSwitch.Prefix + " - v2 ")) && rules.Select(r => r.Name).Distinct().Count() == 16, "16 rules (1.2), all named \"Hallow VPN kill switch - v2 ...\"");
            Ok(rules.All(r => r.InterfaceType == "wireless" || r.InterfaceType == "lan"), "only Wi-Fi and wired connections (the VPN's own \"ras\" connection is never blocked)");
            Ok(rules.All(r => r.AddArgs().Contains(" dir=out action=block ")), "outbound block rules");
            var udp4 = rules.Where(r => r.Protocol == "UDP" && r.RemoteIp.Contains(".")).ToList();
            Ok(udp4.Count == 4 && udp4.All(r => !PortIn(r.RemotePort, 500) && !PortIn(r.RemotePort, 4500) && !PortIn(r.RemotePort, 51820) && PortIn(r.RemotePort, 443) && PortIn(r.RemotePort, 501) && PortIn(r.RemotePort, 51821) && PortIn(r.RemotePort, 65535)), "UDP IPv4: 500 / 4500 (IKEv2) and 51820 (NordLynx) stay open, the rest is blocked");
            Ok(udp4.Where(r => r.Name.EndsWith("UDP IPv4")).All(r => PortIn(r.RemotePort, 53)) && udp4.Where(r => r.Name.EndsWith("Nord DNS")).All(r => !PortIn(r.RemotePort, 53) && r.RemoteIp == "103.86.96.100,103.86.99.100"), "DNS (53) is blocked to every public address except NordVPN's two DNS servers");
            Ok(rules.Where(r => r.Protocol == "TCP" && r.Name.EndsWith("TCP IPv4")).All(r => r.RemotePort == null && !InRanges(r.RemoteIp, "103.86.96.100") && InRanges(r.RemoteIp, "103.86.96.99") && InRanges(r.RemoteIp, "103.86.96.101") && InRanges(r.RemoteIp, "8.8.8.8")) && rules.Where(r => r.Protocol == "TCP" && r.Name.EndsWith("Nord DNS")).All(r => !PortIn(r.RemotePort, 53) && PortIn(r.RemotePort, 443)), "TCP IPv4: everything blocked, except DNS to Nord's DNS servers");
            Ok(rules.Where(r => r.RemoteIp == KillSwitch.PublicV6).Count() == 6 && rules.Where(r => r.RemoteIp == KillSwitch.PublicV6).All(r => r.RemotePort == null && r.LocalIp == null), "public IPv6: TCP, UDP and ICMP blocked on every port (DNS and IKEv2 too)");
            Ok(rules.Where(r => r.RemoteIp.Contains(".")).All(r => r.LocalIp == KillSwitch.NotTunnel && !InRanges(r.LocalIp, "10.5.0.2") && InRanges(r.LocalIp, "10.5.0.1") && InRanges(r.LocalIp, "192.168.1.23") && r.AddArgs().Contains(" localip=" + KillSwitch.NotTunnel)), "IPv4 rules leave the NordLynx tunnel's own address (10.5.0.2) alone");
            var legacy = KillSwitch.Legacy();
            Ok(legacy.Count == 12 && legacy.All(r => r.Name.StartsWith(KillSwitch.Prefix + " - ") && !r.Name.Contains(" v2 ")) && !legacy.Select(r => r.Name).Intersect(rules.Select(r => r.Name)).Any(), "the 1.0 rules (12) are known by name, and none share a name with 1.2's");
            var netsh = "\r\nRule Name:                            " + legacy[0].Name + "\r\n-----\r\nEnabled:                              Yes\r\n\r\nRegelname:                            " + rules[0].Name + "\r\nRule Name:                            " + KillSwitch.Prefix + " - Wi-Fi TCP IPv4 (copy)\r\n";
            Ok(KillSwitch.Count(netsh, legacy) == 1 && KillSwitch.Count(netsh, rules) == 1, "rule names are counted whole, whatever language netsh speaks");
            Ok(KillSwitch.Subtract("1.0.0.0-1.0.0.255", new[] { "1.0.0.0", "1.0.0.7", "1.0.0.255" }) == "1.0.0.1-1.0.0.6,1.0.0.8-1.0.0.254", "range subtraction");
            foreach (var ip in new[] { "10.1.2.3", "192.168.1.1", "172.16.5.5", "172.31.255.255", "100.64.0.1", "100.127.255.254", "127.0.0.1", "169.254.1.1", "224.0.0.251", "255.255.255.255" })
                Ok(!InRanges(KillSwitch.PublicV4, ip), "kill switch leaves " + ip + " alone");
            foreach (var ip in new[] { "8.8.8.8", "1.1.1.1", "100.63.0.1", "100.128.0.1", "172.32.0.1", "203.0.113.84", "11.0.0.1" })
                Ok(InRanges(KillSwitch.PublicV4, ip), "kill switch covers " + ip);
            Ok(rules.All(r => !r.AddArgs().Contains("\"\"")) && rules.All(r => r.DeleteArgs() == "advfirewall firewall delete rule name=\"" + r.Name + "\""), "netsh arguments are well formed");
            var ff = new FakeFirewall(); Ok(ff.Install() == null && ff.InstalledRules() == 16 && ff.Remove() == null && ff.InstalledRules() == 0, "fake firewall: install / remove");
            foreach (var r in legacy) ff.Rules.Add(r.Name); Ok(ff.OldRules() == 12 && ff.InstalledRules() == 0 && ff.Install() == null && ff.OldRules() == 0 && ff.InstalledRules() == 16, "upgrading: installing replaces the 1.0 rules with 1.2's");
            ff.Cancel = true; Ok(ff.Install() == Elevated.Cancelled, "UAC cancelled -> a clear message");
            Ok(Elevated.Run("killswitch-on") == "Not in a test copy." && Elevated.RunHere("killswitch-on") == 5 && Elevated.RunHere("format c:") == 5, "the elevated helper refuses to run in a test copy (and any unknown command)");
            Ok(Games.Effective(Split.All, Games.ModePause, true) == Games.ModeLowPing && Games.Effective(Split.ExceptGames, Games.ModeOff, true) == Games.ModeLowPing, "with the kill switch, \"disconnect for games\" becomes \"low-ping server\"");
        }
        static void CaTests()
        {
            using (var rsa = RSA.Create(2048))
            {
                var req = new CertificateRequest("CN=NordVPN Root CA, O=NordVPN, C=PA", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
                req.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
                var withKey = req.CreateSelfSigned(DateTimeOffset.Now.AddDays(-1), DateTimeOffset.Now.AddYears(5));
                var pub = new X509Certificate2(withKey.RawData);
                Ok(NordCa.Check(pub, DateTime.Now) == null, "a self-signed \"NordVPN Root CA\" certificate authority passes the check (made up in memory)");
                Ok(NordCa.Check(withKey, DateTime.Now) != null, "one carrying a private key is refused");
                Ok(NordCa.Check(pub, DateTime.Now.AddYears(6)) != null, "an expired one is refused");
                var req2 = new CertificateRequest("CN=Evil Root CA", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1); req2.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
                Ok(NordCa.Check(new X509Certificate2(req2.CreateSelfSigned(DateTimeOffset.Now.AddDays(-1), DateTimeOffset.Now.AddYears(1)).RawData), DateTime.Now) != null, "another subject is refused");
                var req3 = new CertificateRequest("CN=NordVPN Root CA", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
                Ok(NordCa.Check(new X509Certificate2(req3.CreateSelfSigned(DateTimeOffset.Now.AddDays(-1), DateTimeOffset.Now.AddYears(1)).RawData), DateTime.Now) != null, "a certificate that isn't a CA is refused");
            }
            var fc = new FakeCa(); string t; Ok(fc.Fetch(out t) == null && t.Length == 40 && fc.Install() == null && fc.Installed() && fc.Remove() == null && !fc.Installed(), "fake certificate install / remove");
        }

        // ---------------- games, Tailscale, light / dark
        static void GameTests()
        {
            Ok(Games.Effective(Split.All, Games.ModeOff, false) == Games.ModeOff && Games.Effective(Split.All, Games.ModePause, false) == Games.ModePause && Games.Effective(Split.All, Games.ModeLowPing, false) == Games.ModeLowPing, "game modes");
            Ok(Games.Effective(Split.ExceptGames, Games.ModeOff, false) == Games.ModePause && Games.Effective(Split.ExceptGames, Games.ModeLowPing, false) == Games.ModePause, "\"Everything except games\" pauses while a game runs");
            Ok(Games.Title("DeltaForceClient-Win64-Shipping.exe") == "Delta Force" && Games.Title("discovery-d") == "Discovery" && Games.Title("cs2.exe") == "cs2", "game names");
            var hub = Path.Combine(Dir, "HallowHub"); Directory.CreateDirectory(hub);
            File.WriteAllText(Path.Combine(hub, "status.json"), "{\"running\":true,\"playing\":[{\"name\":\"Arc Raiders\"}]}");
            Ok(HubStatus.Playing(hub).SequenceEqual(new[] { "Arc Raiders" }), "Hub's status.json \"playing\"");
            File.WriteAllText(Path.Combine(hub, "status.json"), "{\"running\":false,\"playing\":[\"Arc Raiders\"]}");
            Ok(HubStatus.Playing(hub).Count == 0, "Hub not running: nothing");
            File.WriteAllText(Path.Combine(hub, "status.json"), "{broken"); Ok(HubStatus.Playing(hub).Count == 0, "a broken file: nothing (no crash)");
        }
        static void TailscaleTests()
        {
            var st = Tailscale.Parse(FakeData.TailscaleJson);
            Ok(st.Running && st.ExitNodes.Count == 2 && st.ExitNodes.All(n => n.Name != "mums-laptop"), "only machines Tailscale reports as exit-node capable are listed");
            Ok(st.ExitNodes[0].Name == "home-server" && st.ExitNodes[0].Ip == "100.88.12.4" && st.ExitNodes[0].Online && st.ExitNodes[0].Dns == "home-server.example.ts.net", "online first, IPv4 address, DNS name without the dot");
            Ok(st.Current == null, "no exit node in use");
            var on = Tailscale.Parse(FakeData.TailscaleJson.Replace("\"ExitNode\":false,\"ExitNodeOption\":true}", "\"ExitNode\":true,\"ExitNodeOption\":true}"));
            Ok(on.Current != null, "the exit node in use is seen");
            Ok(Tailscale.Parse("").ExitNodes.Count == 0 && Tailscale.Parse("{junk").ExitNodes.Count == 0 && !Tailscale.Parse("{\"BackendState\":\"Stopped\"}").Running, "nothing / junk / stopped");
            Ok(Tailscale.SetArgs("100.88.12.4", true) == "set --exit-node=100.88.12.4 --exit-node-allow-lan-access=true" && Tailscale.SetArgs(null, false) == "set --exit-node=", "tailscale set arguments");
            try { Tailscale.SetArgs("8.8.8.8 --reset", true); Ok(false, "refused"); } catch (ArgumentException) { Ok(true, "only Tailscale addresses go into the command"); }
            Ok(Tailscale.ValidIp("100.64.0.1") && !Tailscale.ValidIp("100.128.0.1") && Tailscale.ValidIp("fd7a:115c:a1e0::4") && !Tailscale.ValidIp("10.0.0.1"), "Tailscale address check");
            var ft = new FakeTailscale(); ft.SetExitNode("100.88.12.4", false); Ok(ft.Calls.Single() == "set --exit-node=100.88.12.4 --exit-node-allow-lan-access=false", "fake Tailscale records the change instead of making it");
        }
        static Dictionary<string, string> Brushes(string xaml)
        {
            var d = new Dictionary<string, string>();
            foreach (Match m in Regex.Matches(xaml, "<SolidColorBrush x:Key=\"(\\w+)\" Color=\"(#[0-9A-Fa-f]{6,8})\"/>")) d[m.Groups[1].Value] = m.Groups[2].Value;
            return d;
        }
        static double Contrast(string fg, string bg) { return Mode.Ratio(fg.Length == 9 ? "#" + fg.Substring(3) : fg, bg); }
        static void ModeTests()
        {
            var sys = new FakeSystemTheme();
            Ok(!Mode.Resolve(null, null, sys) && !Mode.Resolve("follow", "dark", sys), "missing / dark -> dark");
            Ok(Mode.Resolve("follow", "light", sys) && !Mode.Resolve("dark", "light", sys) && Mode.Resolve("light", "dark", sys), "the app's own Light / Dark wins");
            sys.Light = true; Ok(Mode.Resolve("follow", "system", sys) && sys.Reads == 1, "system -> Windows' app mode (fake registry)");
            sys.Light = null; Ok(!Mode.Resolve("follow", "system", sys), "system, unknown -> dark");
            Ok(Mode.TextAccent("#F07820").ToUpperInvariant() != "#F07820" && Mode.Ratio(Mode.TextAccent("#F07820"), "#EEEAF5") >= 4.5, "the orange is darkened for text on light (" + Mode.TextAccent("#F07820") + ")");
            var app = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            foreach (var light in new[] { false, true })
                foreach (var accent in new[] { "#F07820", "#8B5CF6", "#22C55E", "#FFE600" })
                {
                    var x = Program.ThemeXaml(light, accent).Replace("assembly=HallowVPN", "assembly=" + typeof(Run).Assembly.GetName().Name);
                    try { XamlReader.Parse(x); } catch (Exception e) { Ok(false, "theme parses: " + e.Message); continue; }
                    var b = Brushes(x); var tag = (light ? "light" : "dark") + " " + accent;
                    var bgs = new[] { "Bg", "Panel", "PanelHi", "Side" };
                    bool okText = bgs.All(bg => Contrast(b["Text"], b[bg]) >= 4.5 && Contrast(b["Muted"], b[bg]) >= 4.5 && Contrast(b["TextSoft"], b[bg]) >= 4.5);
                    Ok(okText, tag + ": text, soft and muted text >= 4.5:1 on every surface");
                    Ok(bgs.All(bg => Contrast(b["AccentBrush"], b[bg]) >= 4.5 && Contrast(b["Lilac"], b[bg]) >= 4.5), tag + ": accent and lilac text >= 4.5:1");
                    Ok(new[] { "Green", "Amber", "Red" }.All(k => Contrast(b[k], b["Panel"]) >= (light ? 4.5 : 3.0)), tag + ": status colours readable on panels");
                    if (light) Ok(!Regex.IsMatch(x, "#(14101B|1C1726|ECE8F4)\\b", RegexOptions.IgnoreCase), tag + ": no dark-only colours left");
                }
        }
        static void Misc()
        {
            bool on, lists;
            Controller.HubHosting("{\"on\":true,\"apps\":[\"Mixer\",\"VPN\"]}", out on, out lists); Ok(on && lists, "Hub lists VPN by name");
            Controller.HubHosting("{\"on\":true,\"generic\":true}", out on, out lists); Ok(on && lists, "Hub 2.13+ generic host");
            Controller.HubHosting("{\"on\":true,\"apps\":[\"Mixer\"]}", out on, out lists); Ok(on && !lists, "Hub on but not listing VPN: keep our icon");
            Controller.HubHosting("junk", out on, out lists); Ok(!on && !lists, "junk answer");
            Ok(Family.PipeName("VPN").StartsWith("HallowFamily.VPN." + Environment.UserName + ".test-"), "test pipes never reach the real app");
            Ok(Fmt.Span(TimeSpan.FromSeconds(2832)) == "47:12" && Fmt.Span(TimeSpan.FromSeconds(3725)) == "1:02:05" && Fmt.Ms(-1) == "-" && Fmt.Ms(18) == "18 ms", "formats");
            Ok(MainWindow.PageId("Kill switch") == "killswitch" && MainWindow.PageId("tailscale") == "home" && MainWindow.PageId("nowhere") == null, "page names");
        }

        // ---------------- READ-ONLY checks on this PC (nothing is created, dialled or changed)
        static void RealReadOnly()
        {
            int err; var act = Ras.Active(out err);
            Ok(err == 0 && System.Runtime.InteropServices.Marshal.SizeOf(typeof(Ras.RASCONN)) == (IntPtr.Size == 8 ? 1388 : 1384), "RasEnumConnections accepts the RASCONN structure (read-only): code " + err + ", " + act.Count + " active connection(s), size " + System.Runtime.InteropServices.Marshal.SizeOf(typeof(Ras.RASCONN)));
            var pbk = Path.Combine(Dir, "empty.pbk"); File.WriteAllText(pbk, "");
            var p = new Ras.RASDIALPARAMS { dwSize = Ras.DialSize, szEntryName = "Hallow VPN test - does not exist", szPhoneNumber = "nl1.nordvpn.com", szCallbackNumber = "", szUserName = "nobody", szPassword = "x", szDomain = "" };
            IntPtr h; int code = Ras.RasDial(IntPtr.Zero, pbk, ref p, 0, IntPtr.Zero, out h);
            if (h != IntPtr.Zero) Ras.RasHangUp(h);
            Ok(code == 623 || code == 621, "RasDial accepts RASDIALPARAMS and finds no entry in an empty test phonebook (code " + code + ", 632 would mean a wrong size) - nothing dialled");
            var wf = new WindowsFirewall(); var n = wf.InstalledRules(); var o = wf.OldRules(); Ok(n == 0 && o == 0, "real firewall rule list read (read-only): " + n + " v2 and " + o + " 1.0 Hallow VPN kill switch rules");
            Ok(wf.FirewallOn() != null, "Windows Firewall state read: " + (wf.FirewallOn() == true ? "on" : "off for some profile"));
            Console.WriteLine("     (NordVPN Root CA in the computer's Trusted Root store: " + NordCa.InStore(StoreLocation.LocalMachine) + " - read only)");
            var ts = new RealTailscale();
            if (ts.Installed()) { var st = Tailscale.Parse(ts.StatusJson()); Ok(true, "real `tailscale status --json` read (read-only): running " + st.Running + ", " + st.ExitNodes.Count + " exit-node capable machine(s)"); }
            else Console.WriteLine("     (Tailscale isn't installed - skipped)");
            Console.WriteLine("     (a \"Hallow VPN\" profile in this user's phonebook: " + new WindowsVpn().ProfileExists() + " - read only)");
        }

        // ---------------- the family pipe of a test copy: everything faked (no VPN, no firewall, no Tailscale, no real games)
        static string Cmd(string exe, string dir, string line)
        {
            var psi = new ProcessStartInfo(exe, "--cmd \"" + line + "\"") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true };
            psi.EnvironmentVariables["HALLOW_VPN_TESTDIR"] = dir;
            using (var p = Process.Start(psi)) { var o = p.StandardOutput.ReadToEnd().Trim(); p.WaitForExit(10000); return o; }
        }
        static string Until(string exe, string dir, string field, string want)
        {
            string r = null;
            for (int i = 0; i < 40; i++) { r = Cmd(exe, dir, "status"); if (r.Contains("\"" + field + "\":" + want)) return r; Thread.Sleep(150); }
            return r;
        }
        static void PipeAndApp()
        {
            var exe = Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(typeof(Run).Assembly.Location)), "HallowVPN.exe");
            if (!File.Exists(exe)) { Ok(false, "HallowVPN.exe is built"); return; }
            var dir = Path.Combine(Dir, "app"); Directory.CreateDirectory(dir);
            // the elevated helper of a test copy does nothing
            var ep = new ProcessStartInfo(exe, "--elevated killswitch-on") { UseShellExecute = false, CreateNoWindow = true }; ep.EnvironmentVariables["HALLOW_VPN_TESTDIR"] = dir;
            using (var e = Process.Start(ep)) { e.WaitForExit(10000); Ok(e.ExitCode == 5, "\"--elevated\" in a test copy exits at once (5) - no netsh, no UAC"); }
            var psi = new ProcessStartInfo(exe, "--tray") { UseShellExecute = false, CreateNoWindow = true };
            psi.EnvironmentVariables["HALLOW_VPN_TESTDIR"] = dir; psi.EnvironmentVariables["HALLOW_VPN_FAKE"] = "1"; psi.EnvironmentVariables["HALLOW_VPN_HEADLESS"] = "1";
            var app = Process.Start(psi);
            try
            {
                string r = null; for (int i = 0; i < 40 && (r == null || !r.StartsWith("ok")); i++) { Thread.Sleep(250); r = Cmd(exe, dir, "ping"); }
                Ok(r == "ok {\"app\":\"VPN\",\"version\":\"" + Program.Version + "\"}", "ping: " + r);
                r = Cmd(exe, dir, "status");
                foreach (var k in new[] { "version", "updated", "connected", "connecting", "server", "country", "protocol", "killSwitch", "splitMode", "since", "p2p", "leakCheck", "leakCheckAt", "setupDone", "untrustedNetwork", "adapter", "protocolChoice", "killSwitchUpgrade" }) Ok(r.Contains("\"" + k + "\":"), "status has \"" + k + "\"");
                Ok(r.Contains("\"connected\":false") && r.Contains("\"protocol\":\"IKEv2\"") && r.Contains("\"splitMode\":\"all\"") && r.Contains("\"account\":false"), "status: not connected, IKEv2, everything through the VPN, no account");
                Ok(Cmd(exe, dir, "connect fastest").StartsWith("error Add your Nord service credentials"), "connect without an account: refused");
                Ok(Cmd(exe, dir, "test-account me@example.com pw").StartsWith("error That looks like your Nord login"), "the Nord login is refused");
                Ok(Cmd(exe, dir, "test-account qP3xY7mN2kT9 s3cretPass") == "ok", "service credentials saved (test folder)");
                Ok(!File.ReadAllText(Path.Combine(dir, "account.dat"), Encoding.Default).Contains("s3cretPass"), "account.dat is encrypted");
                Ok(Cmd(exe, dir, "connect evil.com").StartsWith("error unknown server") && Cmd(exe, dir, "connect ZZ").StartsWith("error Nord's country list doesn't have"), "connect to junk / an unknown country: refused");
                Ok(Cmd(exe, dir, "connect fastest") == "ok connecting", "connect fastest");
                r = Until(exe, dir, "connected", "true");
                Ok(r.Contains("\"connected\":true") && Regex.IsMatch(r, "\"server\":\"[a-z]{2}\\d+\"") && Regex.IsMatch(r, "\"country\":\"[A-Za-z ]+\"") && Regex.IsMatch(r, "\"since\":\"\\d{4}-\\d\\d-\\d\\dT"), "connected (fake VPN layer): server, country, since");
                var sj = File.ReadAllText(Path.Combine(dir, "status.json"));
                Ok(sj.Contains("\"connected\":true") && !sj.Contains("s3cretPass") && !sj.Contains("qP3xY7mN2kT9") && !r.Contains("s3cretPass") && !r.Contains("qP3xY7mN2kT9"), "status / status.json never carry the credentials");
                r = Cmd(exe, dir, "test-calls"); Ok(r.Contains("profile ") && r.Contains("|full|") && r.Contains("dial ") && r.Contains("as qP3") && !r.Contains("s3cretPass"), "it set up the profile (full tunnel) and dialled with the masked user: " + r);
                // a game: pause, then reconnect 10 s after it closes
                Ok(Cmd(exe, dir, "test-gamemode pause") == "ok pause", "game mode: pause");
                r = Cmd(exe, dir, "test-game Delta Force"); Ok(r.Contains("paused"), "a game starts -> paused: " + r);
                r = Until(exe, dir, "connected", "false"); Ok(r.Contains("\"paused\":true") && r.Contains("\"game\":\"Delta Force\"") && r.Contains("\"since\":null"), "status while paused");
                Cmd(exe, dir, "test-game none"); Cmd(exe, dir, "test-tick 1");
                Ok(Cmd(exe, dir, "status").Contains("\"connected\":false"), "not straight back (waits 10 s)");
                Cmd(exe, dir, "test-clock 15"); Cmd(exe, dir, "test-tick 1");
                r = Until(exe, dir, "connected", "true"); Ok(r.Contains("\"connected\":true") && r.Contains("\"paused\":false"), "15 s after the game closed: reconnected");
                // the connection drops: auto-reconnect
                Cmd(exe, dir, "test-clock 0");
                Ok(Cmd(exe, dir, "test-drop") == "ok Off", "the connection drops (fake)");
                Cmd(exe, dir, "test-clock 20"); Cmd(exe, dir, "test-tick 1");
                r = Until(exe, dir, "connected", "true"); Ok(r.Contains("\"connected\":true") && r.Contains("\"notifications\":1"), "auto-reconnected, one notification");
                // kill switch installed: games switch server instead of disconnecting
                Cmd(exe, dir, "test-clock 0");
                Ok(Cmd(exe, dir, "test-killswitch on") == "ok True" && Cmd(exe, dir, "status").Contains("\"killSwitch\":true"), "kill switch on (fake firewall)");
                r = Cmd(exe, dir, "test-game Delta Force"); Ok(r.Contains("switched") && !r.Contains("paused"), "with the kill switch a game switches server instead: " + r);
                r = Until(exe, dir, "connecting", "false"); Ok(Cmd(exe, dir, "test-current").Contains(" gaming") && r.Contains("\"connected\":true") && r.Contains("\"gameMode\":\"lowping\""), "on the best server for gaming");
                Cmd(exe, dir, "test-game none"); Cmd(exe, dir, "test-clock 15"); Cmd(exe, dir, "test-tick 1"); Until(exe, dir, "connecting", "false");
                Ok(Cmd(exe, dir, "test-current").EndsWith(" fastest"), "back to your choice after the game");
                Cmd(exe, dir, "test-clock 0"); Cmd(exe, dir, "test-killswitch off");
                // split tunnel
                r = Cmd(exe, dir, "test-split only example.com 10.0.0.0/8 bogus!!");
                Ok(r.Contains("\"mode\":\"only\"") && r.Contains("\"example.com\"") && r.Contains("\"10.0.0.0/8\"") && r.Contains("\"bad\":[\"bogus!!\"]"), "split tunnel list saved: " + r);
                Ok(Cmd(exe, dir, "connect de1234") == "ok connecting", "connect to a server by name");
                Until(exe, dir, "connecting", "false");
                r = Cmd(exe, dir, "test-calls"); Ok(r.Contains("de1234.nordvpn.com|split|198.51.100.7/32,10.0.0.0/8"), "profile updated: split tunnelling with the routes (websites resolved by a fake)");
                Ok(Cmd(exe, dir, "status").Contains("\"server\":\"de1234\"") && Cmd(exe, dir, "status").Contains("\"splitMode\":\"only\""), "status: de1234, split mode only");
                Ok(Cmd(exe, dir, "connect gaming") == "ok connecting", "connect gaming over the pipe"); Until(exe, dir, "connecting", "false");
                r = Cmd(exe, dir, "menu"); Ok(r.StartsWith("ok {\"app\":\"VPN\",\"items\":[") && r.Contains("\"id\":\"gaming\"") && r.Contains("\"id\":\"disconnect\"") && r.Contains("\"id\":\"killswitch\""), "menu (Hub 2.13+ lists any app answering it)");
                Ok(Cmd(exe, dir, "open servers") == "ok" && Cmd(exe, dir, "open nowhere").StartsWith("error"), "open <page>");
                Ok(Cmd(exe, dir, "disconnect") == "ok", "disconnect");
                r = Until(exe, dir, "connected", "false"); Ok(r.Contains("\"connected\":false") && r.Contains("\"server\":null"), "disconnected");
                // ---- 1.1: DNS in the (test) phonebook, leak test, P2P, health, auto-connect, Fix my connection, setup
                Cmd(exe, dir, "test-split all");
                Ok(Cmd(exe, dir, "connect p2p") == "ok connecting", "connect p2p over the pipe");
                r = Until(exe, dir, "connecting", "false");
                Ok(r.Contains("\"connected\":true") && r.Contains("\"p2p\":true") && Cmd(exe, dir, "test-current").EndsWith(" p2p"), "on a P2P server: status says p2p true");
                Ok(Cmd(exe, dir, "test-pbk") == "ok []", "the test phonebook's entry now has Nord's DNS and the VPN first");
                Ok(File.Exists(Path.Combine(dir, "rasphone.pbk.bak")) && File.ReadAllText(Path.Combine(dir, "rasphone.pbk")).Contains("IpDnsAddress=103.86.96.100"), "the phonebook was backed up before the change (test folder only)");
                r = Until(exe, dir, "leakCheck", "\"ok\"");
                Ok(r.Contains("\"leakCheck\":\"ok\"") && Regex.IsMatch(r, "\"leakCheckAt\":\"\\d{4}-") && !r.Contains("203.0.113.84") && !r.Contains("198.51.100"), "leak test ok in status, with a time - never the IPs");
                Cmd(exe, dir, "test-dnsbad on"); Cmd(exe, dir, "leaktest"); r = Until(exe, dir, "leakCheck", "\"warn\"");
                Ok(r.Contains("\"leakCheck\":\"warn\""), "another network's DNS asked first: warn");
                Cmd(exe, dir, "test-dnsbad off"); Cmd(exe, dir, "test-ipv6 on"); Thread.Sleep(300); Cmd(exe, dir, "leaktest"); r = Until(exe, dir, "leakCheck", "\"warn\"");
                Ok(r.Contains("\"leakCheck\":\"warn\""), "IPv6 internet without the kill switch: warn");
                Cmd(exe, dir, "test-ipv6 off"); Thread.Sleep(300); Cmd(exe, dir, "leaktest"); Until(exe, dir, "leakCheck", "\"ok\"");
                r = Cmd(exe, dir, "test-realip"); Ok(r.StartsWith("ok 198.51.100.23") && !Cmd(exe, dir, "status").Contains("198.51.100.23") && !File.ReadAllText(Path.Combine(dir, "status.json")).Contains("198.51.100.23"), "real IP only on request (fake), never in status: " + r);
                r = Cmd(exe, dir, "test-health"); Ok(Regex.IsMatch(r, "^ok \\d+ \\d+ \\d+$"), "health: bytes up / down and a live ping: " + r);
                r = Cmd(exe, dir, "diagnose"); Ok(r.StartsWith("ok {\"steps\":[{\"id\":\"account\",\"status\":\"ok\"}") && r.Contains("\"id\":\"profile\",\"status\":\"ok\"") && r.Contains("\"id\":\"blockers\""), "Fix my connection over the pipe: " + r);
                Ok(Cmd(exe, dir, "test-repair profile") == "error Disconnect first - the connection is in use.", "recreating the profile waits until disconnected");
                Cmd(exe, dir, "disconnect"); Until(exe, dir, "connected", "false");
                Ok(Cmd(exe, dir, "test-repair profile") == "ok" && Cmd(exe, dir, "test-pbk") == "ok []", "profile recreated (fake layer, test phonebook) with Nord's DNS again");
                r = Cmd(exe, dir, "test-calls"); Ok(r.Contains("remove profile"), "it removed and set up the per-user profile");
                // auto-connect on untrusted Wi-Fi
                Ok(Cmd(exe, dir, "test-autonet on") == "ok True", "auto-connect on untrusted Wi-Fi: on");
                Cmd(exe, dir, "test-net open Cafe Guest"); r = Until(exe, dir, "connected", "true");
                Ok(r.Contains("\"connected\":true") && r.Contains("\"untrustedNetwork\":true") && !r.Contains("Cafe"), "joined an open Wi-Fi: connected by itself (the Wi-Fi name isn't in status)");
                Cmd(exe, dir, "disconnect"); Until(exe, dir, "connected", "false"); Cmd(exe, dir, "test-net open Cafe Guest"); Cmd(exe, dir, "test-tick 6"); Thread.Sleep(300);
                Ok(Cmd(exe, dir, "status").Contains("\"connected\":false"), "disconnected by hand on that network: it stays off");
                Ok(Cmd(exe, dir, "test-trust Home-5G") == "ok Home-5G", "a Wi-Fi name trusted");
                Cmd(exe, dir, "test-net wifi Home-5G"); Thread.Sleep(300); r = Cmd(exe, dir, "status");
                Ok(r.Contains("\"connected\":false") && r.Contains("\"untrustedNetwork\":false"), "trusted Wi-Fi: no auto-connect");
                Cmd(exe, dir, "test-net wired"); Thread.Sleep(300); Ok(Cmd(exe, dir, "status").Contains("\"connected\":false"), "wired (trusted by default): no auto-connect");
                Cmd(exe, dir, "test-gamemode pause"); Cmd(exe, dir, "test-game Delta Force");
                Cmd(exe, dir, "test-net wifi Airport"); Thread.Sleep(300);
                Ok(Cmd(exe, dir, "status").Contains("\"connected\":false"), "a game runs with \"pause\": untrusted Wi-Fi waits, nothing dialled");
                Cmd(exe, dir, "test-game none"); r = Until(exe, dir, "connected", "true");
                Ok(r.Contains("\"connected\":true"), "the game closed: connected on the untrusted Wi-Fi");
                Cmd(exe, dir, "test-gamemode off"); Cmd(exe, dir, "test-autonet off"); Cmd(exe, dir, "disconnect"); Until(exe, dir, "connected", "false");
                // setup test (step 3) and Windows' last error
                Cmd(exe, dir, "test-dialerror Windows doesn't trust the server's certificate (error 13801). Install NordVPN's root certificate on the Account page.");
                Ok(Cmd(exe, dir, "test-setuptest").StartsWith("ok running"), "setup step 3: test connection to Fastest");
                Thread.Sleep(600); Ok(Cmd(exe, dir, "test-setup") == "ok True fail", "a failed test says fail");
                r = Cmd(exe, dir, "diagnose"); Ok(r.Contains("\"id\":\"lasterror\",\"status\":\"warn\""), "Fix my connection explains the last Windows error (13801)");
                Cmd(exe, dir, "test-dialerror none"); Cmd(exe, dir, "test-setuptest"); Thread.Sleep(600);
                Ok(Cmd(exe, dir, "test-setup") == "ok True pass", "then it passes");
                r = Cmd(exe, dir, "status"); Ok(r.Contains("\"setupDone\":true") && r.Contains("\"autoConnect\":false"), "status: setupDone, autoConnect");
                Cmd(exe, dir, "disconnect"); Until(exe, dir, "connected", "false");
                Ok(Cmd(exe, dir, "status").Contains("\"p2p\":false") && Cmd(exe, dir, "status").Contains("\"leakCheck\":null"), "not connected: p2p false, no leak result");
                // ---- 1.2: NordLynx (fake tunnel, fake WireGuard setup, fake Nord credentials endpoint) and the kill switch upgrade
                r = Cmd(exe, dir, "protocol"); Ok(r == "ok {\"choice\":\"auto\",\"next\":\"IKEv2\",\"nordlynxReady\":false}", "protocol: Automatic, IKEv2 until NordLynx is set up: " + r);
                Ok(Cmd(exe, dir, "protocol wireguard-ish").StartsWith("error") && Cmd(exe, dir, "protocol nordlynx").Contains("\"choice\":\"nordlynx\""), "protocol nordlynx (junk refused)");
                Ok(Cmd(exe, dir, "connect fastest").Contains("WireGuard for Windows"), "NordLynx without WireGuard: says to install it");
                Cmd(exe, dir, "test-wg on"); Thread.Sleep(300);
                Ok(Cmd(exe, dir, "connect fastest").Contains("access token"), "without the key: says to paste the access token");
                Ok(Cmd(exe, dir, "test-token me@example.com").Contains("ACCESS TOKEN"), "an email instead of the token: refused");
                Ok(Cmd(exe, dir, "test-token 0123456789abcdef0123456789abcdef") == "ok" && File.Exists(Path.Combine(dir, "nordlynx.dat")), "the access token -> the NordLynx key from Nord (fake endpoint), saved in the test folder");
                Ok(!File.ReadAllText(Path.Combine(dir, "nordlynx.dat"), Encoding.Default).Contains("0123456789abcdef"), "nordlynx.dat is encrypted");
                Ok(Cmd(exe, dir, "protocol").Contains("\"nordlynxReady\":true"), "NordLynx ready");
                Ok(Cmd(exe, dir, "connect fastest") == "ok connecting", "connect over NordLynx");
                r = Until(exe, dir, "connected", "true");
                Ok(r.Contains("\"protocol\":\"NordLynx\"") && r.Contains("\"adapter\":\"HallowNordLynx\"") && r.Contains("\"p2p\":"), "status: protocol NordLynx, adapter HallowNordLynx (for Hallow Torrents), p2p still there");
                r = Cmd(exe, dir, "test-tunnel"); Ok(r.StartsWith("ok NordLynx True wg-up 203.0.113.") && r.Contains("(asks Windows)"), "it asked for the tunnel (fake - nothing installed): " + r);
                var all = Cmd(exe, dir, "status") + File.ReadAllText(Path.Combine(dir, "status.json")) + Cmd(exe, dir, "test-tunnel") + Cmd(exe, dir, "test-calls");
                Ok(!all.Contains("0123456789abcdef") && !all.Contains("aGFsbG93LWZha2Ut"), "the token and the private key are never in status, status.json or any call");
                Cmd(exe, dir, "leaktest"); r = Until(exe, dir, "leakCheck", "\"ok\""); Ok(r.Contains("\"leakCheck\":\"ok\""), "leak test over NordLynx: ok");
                r = Cmd(exe, dir, "test-health"); Ok(Regex.IsMatch(r, "^ok [1-9]\\d* [1-9]\\d* \\d+$"), "health over NordLynx (the adapter's byte counts): " + r);
                Cmd(exe, dir, "test-gamemode pause"); r = Cmd(exe, dir, "test-game Delta Force");
                Ok(!r.Contains("paused") && !r.Contains("switched") && Cmd(exe, dir, "status").Contains("\"gameMode\":\"off\"") && Cmd(exe, dir, "status").Contains("\"connected\":true"), "a game starts while on NordLynx: nothing changes (no UAC during a game)");
                Ok(Cmd(exe, dir, "test-tunneldrop") == "ok Off", "the tunnel stops (fake) during the game");
                Cmd(exe, dir, "test-clock 40"); Cmd(exe, dir, "test-tick 2"); Thread.Sleep(400);
                Ok(Cmd(exe, dir, "status").Contains("\"connected\":false") && !Cmd(exe, dir, "test-tunnel").Contains("wg-up 203.0.113.84"), "no reconnect (and no UAC) while the game runs");
                Cmd(exe, dir, "test-game none"); Cmd(exe, dir, "test-clock 0"); Cmd(exe, dir, "test-gamemode off");
                Cmd(exe, dir, "disconnect"); Until(exe, dir, "connecting", "false");
                // Automatic: NordLynx for clicks, IKEv2 for things Hallow VPN does by itself
                Cmd(exe, dir, "protocol auto"); r = Cmd(exe, dir, "protocol"); Ok(r.Contains("\"next\":\"NordLynx\""), "Automatic with NordLynx set up: a click uses NordLynx");
                Cmd(exe, dir, "test-autonet on"); Cmd(exe, dir, "test-net open Station WiFi"); r = Until(exe, dir, "connected", "true");
                Ok(r.Contains("\"protocol\":\"IKEv2\""), "auto-connect on untrusted Wi-Fi uses IKEv2 (no UAC prompt out of nowhere)");
                Cmd(exe, dir, "test-autonet off"); Cmd(exe, dir, "disconnect"); Until(exe, dir, "connected", "false");
                // the 1.0 kill switch: upgrade
                r = Cmd(exe, dir, "test-oldks on"); Ok(r == "ok True True" && Cmd(exe, dir, "status").Contains("\"killSwitchUpgrade\":true"), "the 1.0 kill switch is recognised (on, upgrade offered)");
                Cmd(exe, dir, "protocol nordlynx"); Ok(Cmd(exe, dir, "connect fastest").Contains("Upgrade it"), "NordLynx with the 1.0 kill switch: says to upgrade first");
                r = Cmd(exe, dir, "diagnose"); Ok(r.Contains("\"id\":\"nordlynx\",\"status\":\"ok\"") && r.Contains("\"id\":\"blockers\",\"status\":\"warn\""), "Fix my connection: NordLynx ok, the old kill switch flagged");
                Cmd(exe, dir, "test-ksinstall"); Thread.Sleep(500); r = Cmd(exe, dir, "status");
                Ok(r.Contains("\"killSwitch\":true") && r.Contains("\"killSwitchUpgrade\":false"), "upgraded (fake firewall: the 1.0 rules out, 1.2's in)");
                Cmd(exe, dir, "test-killswitch off"); Cmd(exe, dir, "protocol ikev2");
                Ok(Cmd(exe, dir, "status").Contains("\"protocol\":\"IKEv2\"") && Cmd(exe, dir, "status").Contains("\"adapter\":null"), "back to IKEv2; not connected = no adapter");
                Ok(Cmd(exe, dir, "invoke nothing") == "error unknown item" && Cmd(exe, dir, "frobnicate") == "error unknown command", "unknown item / command");
                Ok(Cmd(exe, dir, "test-autostart on") == "ok True" && Cmd(exe, dir, "test-autostart off") == "ok False", "Start with Windows (test file, never the real Run key)");
                r = Cmd(exe, dir, "test-calls"); Ok(!r.Contains("s3cretPass"), "no call ever carried the password");
                Ok(Cmd(exe, dir, "quit") == "ok", "quit");
                Ok(app.WaitForExit(8000), "the test copy exits");
                Ok(File.ReadAllText(Path.Combine(dir, "status.json")).Contains("\"running\":false"), "status.json says it isn't running");
                var cp = new ProcessStartInfo(exe, "--cmd ping") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true }; cp.EnvironmentVariables["HALLOW_VPN_TESTDIR"] = dir;
                using (var c = Process.Start(cp)) { c.StandardOutput.ReadToEnd(); c.WaitForExit(10000); Ok(c.ExitCode == 2, "--cmd when it isn't running -> exit 2"); }
            }
            finally { try { if (!app.HasExited) app.Kill(); } catch { } }
        }
    }
}
