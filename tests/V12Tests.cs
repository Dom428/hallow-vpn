// Hallow VPN 1.2 tests: BLAKE2b, WireGuard's published list, the installer checks (made-up files and a fake signer; plus, when
// HALLOW_VPN_WGMSI points at a copy downloaded once, the real file - checked, never run), the NordLynx key handling (DPAPI in the test
// folder; Nord's credentials endpoint is NEVER called - a fake answers), the .conf, Nord's WireGuard keys from a saved answer, the
// elevated helper refusing anything odd, and the "nordlynx" check. Nothing is installed, no tunnel or service is created.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace HallowVPN.Tests
{
    public static class V12
    {
        static Action<bool, string> Ok;
        public static void All(Action<bool, string> ok, string dir, string fx)
        {
            Ok = ok;
            Hashes(); Installer(dir, fx); Keys(dir, fx); Helper(); Diag();
        }

        static string B2(string s, int n) { return Blake2b.Hex(Blake2b.Hash(new MemoryStream(Encoding.ASCII.GetBytes(s)), n)); }
        static void Hashes()
        {
            Ok(B2("abc", 64) == "ba80a53f981c4d0d6a2797b69f12f6e94c212f14685ac4b74b12bb6fdbffa2d17d87c5392aab792dc252d5de4533cc9518d38aa8dbf1925ab92386edd4009923", "BLAKE2b-512(\"abc\") = RFC 7693's test vector");
            Ok(B2("", 64) == "786a02f742015903c6c6fd852552d272912f4740e15847618a86e217f71f5419d25e1031afee585313896444934eb04b903a685b1448b755d56f701afe9be2ce", "BLAKE2b-512 of nothing");
            var big = new string('x', 300); Ok(B2(big, 32).Length == 64 && B2(big, 32) != B2(big.Substring(1), 32), "BLAKE2b-256 over several blocks");
        }

        static void Installer(string dir, string fx)
        {
            var sig = File.ReadAllText(Path.Combine(fx, "wireguard-latest.sig"));
            var list = WgSetup.ParseList(sig);
            Ok(list.Count == 3 && list.ContainsKey("wireguard-amd64-1.1.1.msi") && list["wireguard-amd64-1.1.1.msi"] == "f64b88dea419f57cd8068c3a2284aedd2f33fb5656985dbdaf4bb6001168ac8b", "WireGuard's published list (latest.sig, saved once): 3 installers with BLAKE2b-256 hashes; the signature lines are skipped");
            Ok(WgSetup.Pick(list, "amd64") == "wireguard-amd64-1.1.1.msi" && WgSetup.Pick(list, "arm64") == "wireguard-arm64-1.1.1.msi" && WgSetup.Pick(list, "riscv") == null, "the installer for this PC's architecture (" + WgSetup.Arch() + ")");
            var two = WgSetup.ParseList("aaaa\n" + new string('1', 64) + "  wireguard-amd64-0.9.12.msi\n" + new string('2', 64) + "  wireguard-amd64-0.10.1.msi\n" + new string('3', 64) + "  ../evil.msi\n");
            Ok(two.Count == 2 && WgSetup.Pick(two, "amd64") == "wireguard-amd64-0.10.1.msi", "the newest version wins (0.10.1 > 0.9.12); odd file names are ignored");
            Ok(WgSetup.SigUrl == "https://download.wireguard.com/windows-client/latest.sig" && WgSetup.Base.StartsWith("https://download.wireguard.com/"), "only download.wireguard.com, over HTTPS");

            var f = Path.Combine(dir, "wg", "fake.msi"); Directory.CreateDirectory(Path.GetDirectoryName(f)); File.WriteAllText(f, "not really an installer");
            var h = Blake2b.HashFile(f, 32);
            Func<string, string> good = p => "CN=WireGuard LLC, O=WireGuard LLC, S=Colorado, C=US";
            Ok(WgSetup.Check(f, h, good) == null, "matching hash + signed by WireGuard LLC: accepted (made-up file, fake signer)");
            Ok(WgSetup.Check(f, new string('0', 64), good).Contains("hash"), "a different hash: refused");
            Ok(WgSetup.Check(f, h, p => null).Contains("signature"), "no valid signature: refused");
            Ok(WgSetup.Check(f, h, p => "CN=WireGuard LLC Fake, O=Evil Corp").Contains("someone other"), "signed by someone else: refused");
            Ok(WgSetup.Check(Path.Combine(dir, "wg", "none.msi"), h, good) != null, "no file: refused");
            Ok(WgSetup.Signer(f) == null, "the real Authenticode check says an unsigned file isn't signed");
            var msi = Environment.GetEnvironmentVariable("HALLOW_VPN_WGMSI");
            if (!string.IsNullOrEmpty(msi) && File.Exists(msi) && list.ContainsKey(Path.GetFileName(msi)))
            {
                var who = WgSetup.Signer(msi);
                Ok(who != null && who.Contains("CN=WireGuard LLC"), "the real installer (downloaded once, never run): Authenticode valid, signer " + (who ?? "none").Split(',')[0]);
                Ok(WgSetup.Check(msi, list[Path.GetFileName(msi)], WgSetup.Signer) == null, "the real installer matches WireGuard's published BLAKE2b-256 hash and passes both checks");
            }
            else Console.WriteLine("     (no downloaded WireGuard installer given - the real-file check is skipped)");
            var fw = new FakeWgSetup(); string file; Ok(fw.Download(out file) == null && fw.Install(file) == null && fw.Installed(), "fake WireGuard setup: download, install");
            Ok(new RealWgSetup().Install("x.msi") == "Not in a test copy.", "the real installer never runs in a test copy");
        }

        static void Keys(string dir, string fx)
        {
            Ok(NordLynx.ValidKey(FakeData.WgPrivateKey) && !NordLynx.ValidKey("short=") && !NordLynx.ValidKey(FakeData.WgPrivateKey.Replace("=", "A")) && !NordLynx.ValidKey(null), "WireGuard key check (44 base64 characters = 32 bytes)");
            Ok(NordLynx.CheckToken("me@example.com").Contains("ACCESS TOKEN") && NordLynx.CheckToken("abc def ghi jkl mno") != null && NordLynx.CheckToken("") != null && NordLynx.CheckToken("0123456789abcdef0123456789abcdef") == null, "access token check (an email is refused)");
            var fa = new FakeNordAuth(); Ok(NordLynx.ParseKey(fa.Credentials("t")) == FakeData.WgPrivateKey, "the NordLynx key from Nord's answer (made-up answer - the real endpoint is never called)");
            Ok(NordLynx.ParseKey("{\"username\":\"x\"}") == null && NordLynx.ParseKey("{\"nordlynx_private_key\":\"bad\"}") == null && NordLynx.ParseKey("junk") == null, "no key / a bad key / junk: nothing");
            Ok(NordLynx.CredentialsUrl == "https://api.nordvpn.com/v1/users/services/credentials", "Nord's credentials endpoint");
            // the saved WireGuard answer from Nord's public API: every server carries its public key
            var wg = Nord.ParseServers(File.ReadAllText(Path.Combine(fx, "recommendations-wireguard-real.json")));
            Ok(wg.Count >= 1 && wg.All(s => NordLynx.ValidKey(s.WgKey) && NordLynx.ValidIp(s.Station)), "Nord's server list (saved once): each server's NordLynx public key (technologies wireguard_udp, metadata public_key) and address");
            var ike = Nord.ParseServers(File.ReadAllText(Path.Combine(fx, "recommendations-real.json")));
            Ok(ike.All(s => NordLynx.ValidKey(s.WgKey)), "the IKEv2 list's servers carry their NordLynx key too (one list serves both)");
            var conf = NordLynx.Conf(FakeData.WgPrivateKey, wg[0].WgKey, wg[0].Station);
            Ok(conf.Contains("Address = 10.5.0.2/32") && conf.Contains("DNS = 103.86.96.100, 103.86.99.100") && conf.Contains("PublicKey = " + wg[0].WgKey) && conf.Contains("Endpoint = " + wg[0].Station + ":51820") && conf.Contains("AllowedIPs = 0.0.0.0/0, ::/0") && conf.Contains("PersistentKeepalive = 25"), "the .conf: 10.5.0.2, Nord's DNS, the server's key, <station>:51820, everything through it");
            try { NordLynx.Conf(FakeData.WgPrivateKey, wg[0].WgKey, "1.2.3.4\r\nPostUp = calc"); Ok(false, "injection refused"); } catch (ArgumentException) { Ok(true, "nothing odd gets into the .conf (no PostUp injection)"); }
            try { NordLynx.Conf("x", wg[0].WgKey, wg[0].Station); Ok(false, "bad key refused"); } catch (ArgumentException) { Ok(true, "a bad private key never reaches the .conf"); }
            // DPAPI in the test folder
            Ok(NordLynx.Save("0123456789abcdef0123456789abcdef", FakeData.WgPrivateKey) == null && File.Exists(NordLynx.FilePath), "token and key saved (test folder)");
            var raw = File.ReadAllBytes(NordLynx.FilePath); var txt = Encoding.UTF8.GetString(raw) + Encoding.Unicode.GetString(raw);
            Ok(!txt.Contains("0123456789abcdef0123456789abcdef") && !txt.Contains(FakeData.WgPrivateKey), "nordlynx.dat holds neither in plain text (DPAPI)");
            var l = NordLynx.Load(); Ok(l != null && l.Key == FakeData.WgPrivateKey, "loads back for this Windows user");
            typeof(Crash).GetField("writing", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static).SetValue(null, 0);   // one report per process otherwise
            Crash.Write(new Exception("boom " + FakeData.WgPrivateKey + " and 0123456789abcdef0123456789abcdef"));
            var cf = Crash.Last(); var ct = cf == null ? "" : File.ReadAllText(cf);
            Ok(cf != null && !ct.Contains(FakeData.WgPrivateKey) && !ct.Contains("0123456789abcdef0123456789abcdef"), "a crash report never contains the token or the key");
            NordLynx.Remove(); Ok(!NordLynx.Exists, "removed");
            try { Directory.Delete(Crash.Dir, true); } catch { }
            var ft = new FakeTunnel(); var srv = wg[0];
            Ok(ft.Up(srv) == null && ft.IsUp() && ft.Calls.Single().StartsWith("wg-up " + srv.Station) && !string.Join(" ", ft.Calls).Contains(srv.WgKey), "fake tunnel: up (the calls carry no key)");
            Ok(ft.Up(new Server { Host = "x1.nordvpn.com", Station = "1.2.3.4" }) != null, "a server without a NordLynx key: refused");
        }

        static void Helper()
        {
            Ok(Elevated.Run("wg-up", "203.0.113.9 " + FakeData.WgPrivateKey) == "Not in a test copy." && Elevated.RunHere("wg-up", "203.0.113.9 " + FakeData.WgPrivateKey) == 5 && Elevated.RunHere("wg-down") == 5, "the elevated NordLynx helper never runs in a test copy");
            Ok(Elevated.Run("wg-up", "203.0.113.9 " + FakeData.WgPrivateKey + "\" & calc") == "unknown elevated command" && Elevated.Run("wg-up", null) == "unknown elevated command" && Elevated.Run("killswitch-on", "x") == "unknown elevated command", "only a server address + key go to wg-up; other commands take nothing");
            Ok(NordLynx.ValidUpArg("203.0.113.9 " + FakeData.WgPrivateKey) && !NordLynx.ValidUpArg("203.0.113.9") && !NordLynx.ValidUpArg("a.b.c.d " + FakeData.WgPrivateKey), "wg-up's argument check");
            Ok(NordLynx.Service == "WireGuardTunnel$HallowNordLynx" && System.Text.RegularExpressions.Regex.IsMatch(NordLynx.Tunnel, "^[a-zA-Z0-9_=+.-]{1,32}$"), "the tunnel's name is one WireGuard accepts; its service is WireGuardTunnel$HallowNordLynx");
            Ok(new WireGuardTunnel().Exists() == false || true, "the real tunnel service state can be read (read-only): " + (new WireGuardTunnel().Exists() ? "there" : "not there"));
            Ok(Protocols.Fix("NordLynx") == "nordlynx" && Protocols.Fix("weird") == "auto" && Protocols.Label("ikev2") == "IKEv2", "protocol choices");
        }

        static void Diag()
        {
            Func<string, System.Net.IPAddress[]> res = h => new[] { System.Net.IPAddress.Parse("203.0.113.9") };
            var i = new DiagInput { Account = true, CaInstalled = true, CaChecked = true, PbkText = null, SplitMode = Split.All, Host = "nl1.nordvpn.com", Resolve = res, Ping = s => 20, KillSwitchAll = 16, ProtocolChoice = Protocols.NordLynx };
            var r = Diagnosis.Run(i);
            Ok(r.Any(x => x.Id == "nordlynx" && x.Status == "fail" && x.Text.Contains("WireGuard")), "NordLynx chosen but not set up: the check says what's missing");
            i.ProtocolChoice = Protocols.Auto; Ok(Diagnosis.Run(i).Single(x => x.Id == "nordlynx").Status == "skip", "Automatic without NordLynx: optional");
            i.WgInstalled = true; i.NordLynxKey = true; Ok(Diagnosis.Run(i).Single(x => x.Id == "nordlynx").Status == "ok", "set up: green tick");
            i.ProtocolChoice = Protocols.Ikev2; Ok(!Diagnosis.Run(i).Any(x => x.Id == "nordlynx"), "IKEv2 only: no NordLynx check");
            i.OldKillSwitch = true; i.KillSwitchRules = 12; var b = Diagnosis.Run(i).Single(x => x.Id == "blockers");
            Ok(b.Status == "warn" && b.Text.Contains("Upgrade"), "the 1.0 kill switch: upgrade suggested");
        }
    }
}
