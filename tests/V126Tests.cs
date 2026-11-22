// Hallow VPN 1.2.6 tests: status.json's nextProtocol / needsUac / target / windowsLogin, the pipe's "connect <target> --keep-target",
// picking Windows' sign-in box to bring to the front (made-up window lists - no window is opened, rasphone never starts in a test
// copy), and where the WireGuard installer goes (Downloads, else %ProgramData%\HallowVPN) plus its clean-up. Everything runs against
// a test copy with the VPN layer, tunnel and WireGuard setup faked: nothing is dialled, installed or changed.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;

namespace HallowVPN.Tests
{
    public static class V126
    {
        static Action<bool, string> Ok;
        public static void All(Action<bool, string> ok, string dir, string exe)
        {
            Ok = ok;
            Keep(); Uac(); Windows(); Installer(dir); Pipe(dir, exe);
        }

        static void Keep()
        {
            bool k; string a;
            a = Controller.KeepTarget("fastest --keep-target", out k); Ok(a == "fastest" && k, "\"fastest --keep-target\" -> fastest, keeps the target");
            a = Controller.KeepTarget("--once us", out k); Ok(a == "us" && k, "\"--once us\" means the same");
            a = Controller.KeepTarget("de1234", out k); Ok(a == "de1234" && !k, "no flag: remembered as before");
            a = Controller.KeepTarget("p2p --KEEP-TARGET", out k); Ok(a == "p2p" && k, "the flag in any case");
            a = Controller.KeepTarget("", out k); Ok(a == "" && !k, "empty");
            a = Controller.KeepTarget(null, out k); Ok(a == "" && !k, "null");
        }
        static string U(Dictionary<string, object> d) { return (bool)d["connect"] + "/" + (bool)d["disconnect"]; }
        static void Uac()
        {
            string ike = Controller.ProtoIke, wg = Controller.ProtoWg;
            Ok(U(Controller.UacNeeds(Phase.Off, ike, ike, ike)) == "False/False", "off, next IKEv2: no UAC either way");
            Ok(U(Controller.UacNeeds(Phase.Off, wg, wg, ike)) == "False/False", "off after a NordLynx session, next IKEv2: no UAC");
            Ok(U(Controller.UacNeeds(Phase.Off, ike, ike, wg)) == "True/False", "off, next NordLynx: connect asks Windows");
            Ok(U(Controller.UacNeeds(Phase.On, wg, wg, wg)) == "True/True", "on NordLynx: reconnect and disconnect ask Windows");
            Ok(U(Controller.UacNeeds(Phase.On, wg, wg, ike)) == "True/True", "on NordLynx, next IKEv2: switching takes NordLynx down (asks)");
            Ok(U(Controller.UacNeeds(Phase.On, ike, ike, ike)) == "False/False", "on IKEv2: no UAC");
            Ok(U(Controller.UacNeeds(Phase.On, ike, ike, wg)) == "True/False", "on IKEv2, next NordLynx: connect asks, disconnect doesn't");
            Ok(U(Controller.UacNeeds(Phase.Connecting, ike, wg, wg)) == "True/True", "connecting over NordLynx: disconnecting may ask");
            Ok(U(Controller.UacNeeds(Phase.Connecting, ike, ike, ike)) == "False/False", "connecting over IKEv2: no UAC");
        }
        static void Windows()
        {
            var a = new IntPtr(11); var b = new IntPtr(22); var c = new IntPtr(33);
            Ok(Ras.PickWindow(null) == IntPtr.Zero && Ras.PickWindow(new Ras.WinInfo[0]) == IntPtr.Zero, "no windows yet: nothing to bring forward (keeps looking)");
            Ok(Ras.PickWindow(new[] { new Ras.WinInfo { Handle = a, Visible = false, Title = "Connect Hallow VPN" } }) == IntPtr.Zero, "a hidden window is skipped");
            Ok(Ras.PickWindow(new[] { new Ras.WinInfo { Handle = a, Visible = true, Title = "" } }) == IntPtr.Zero, "an untitled helper window is skipped");
            Ok(Ras.PickWindow(new[] { new Ras.WinInfo { Handle = a, Visible = true, Owned = true, Title = "Details" }, new Ras.WinInfo { Handle = b, Visible = true, Title = "Connect Hallow VPN" } }) == b, "the unowned box comes first");
            Ok(Ras.PickWindow(new[] { new Ras.WinInfo { Handle = c, Visible = true, Owned = true, Title = "Connect Hallow VPN" } }) == c, "only an owned one: that one");
            List<Ras.WinInfo> mine = null; try { mine = Ras.WindowsOf(Process.GetCurrentProcess().Id); } catch { }
            Ok(mine != null, "listing a process's windows works (read-only): " + (mine == null ? "-" : mine.Count.ToString()));
            Ok(Ras.WindowsSignIn(Path.Combine(Path.GetTempPath(), "none.pbk")) == "Not in a test copy.", "a test copy never opens Windows' sign-in (rasphone)");
        }
        static void Installer(string dir)
        {
            var root = Path.Combine(dir, "v126"); var dl = Path.Combine(root, "Downloads"); var pd = Path.Combine(root, "ProgramData"); Directory.CreateDirectory(dl);
            Ok(WgSetup.PickDownloadDir(() => dl, pd) == dl, "the installer goes to your Downloads folder");
            Ok(WgSetup.PickDownloadDir(() => null, pd) == Path.Combine(pd, "HallowVPN"), "no Downloads folder: %ProgramData%\\HallowVPN");
            Ok(WgSetup.PickDownloadDir(() => Path.Combine(root, "gone"), pd) == Path.Combine(pd, "HallowVPN"), "a Downloads folder that doesn't exist: %ProgramData%\\HallowVPN");
            Ok(WgSetup.PickDownloadDir(() => { throw new InvalidOperationException(); }, pd) == Path.Combine(pd, "HallowVPN"), "the lookup failing: %ProgramData%\\HallowVPN");
            var real = WgSetup.DownloadDir; var lad = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            Ok(!real.StartsWith(lad, StringComparison.OrdinalIgnoreCase), "the real download folder isn't under %LOCALAPPDATA% (Windows Installer can read it): " + real);
            Ok(WgSetup.FilePrefix == "HallowVPN-", "its file is called HallowVPN-<name> (never overwrites one of yours)");
            var msi = Path.Combine(dl, WgSetup.FilePrefix + "wireguard-amd64-0.0.0.msi");
            File.WriteAllText(msi, "made-up"); File.WriteAllText(WgSetup.LogFor(msi), "made-up log");
            Ok(WgSetup.Failed("The WireGuard installer stopped (code 1603).", msi).EndsWith("Windows Installer's log: " + WgSetup.LogFor(msi)), "a failed install shows the msiexec log's path");
            WgSetup.CleanUp(msi, true); Ok(!File.Exists(msi) && File.Exists(WgSetup.LogFor(msi)), "after a failed install: the installer is deleted, the log kept");
            Ok(WgSetup.Failed("x.", msi) == "x. Windows Installer's log: " + WgSetup.LogFor(msi), "the log is still there to read");
            File.WriteAllText(msi, "made-up"); WgSetup.CleanUp(msi, false);
            Ok(!File.Exists(msi) && !File.Exists(WgSetup.LogFor(msi)), "after a good install: installer and log deleted");
            Ok(WgSetup.Failed("x.", msi) == "x.", "no log: no path in the message");
            WgSetup.CleanUp(null, false); Ok(true, "clean-up of nothing is fine");
            File.WriteAllText(msi, "made-up");
            Ok(new RealWgSetup().Install(msi) == "Not in a test copy." && File.Exists(msi), "a test copy never runs msiexec (and leaves the file alone)");
            File.Delete(msi);
        }

        // ---- the pipe of a test copy (fake VPN layer, fake tunnel, fake WireGuard setup)
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
        static void Pipe(string root, string exe)
        {
            if (!File.Exists(exe)) { Ok(false, "HallowVPN.exe is built"); return; }
            var dir = Path.Combine(root, "app126"); Directory.CreateDirectory(dir);
            var psi = new ProcessStartInfo(exe, "--tray") { UseShellExecute = false, CreateNoWindow = true };
            psi.EnvironmentVariables["HALLOW_VPN_TESTDIR"] = dir; psi.EnvironmentVariables["HALLOW_VPN_FAKE"] = "1"; psi.EnvironmentVariables["HALLOW_VPN_HEADLESS"] = "1";
            var app = Process.Start(psi);
            try
            {
                string r = null; for (int i = 0; i < 40 && (r == null || !r.StartsWith("ok")); i++) { Thread.Sleep(250); r = Cmd(exe, dir, "ping"); }
                Ok(r != null && r.StartsWith("ok"), "1.2.6 test copy answers: " + r);
                r = Cmd(exe, dir, "status");
                foreach (var k in new[] { "nextProtocol", "needsUac", "target", "windowsLogin", "protocol" }) Ok(r.Contains("\"" + k + "\":"), "status has \"" + k + "\"");
                Ok(r.Contains("\"nextProtocol\":\"IKEv2\"") && r.Contains("\"needsUac\":{\"connect\":false,\"disconnect\":false}") && r.Contains("\"target\":\"fastest\"") && r.Contains("\"windowsLogin\":false"), "fresh: next IKEv2, no UAC, target fastest, no Windows login (test phonebook): " + r);
                var sj = File.ReadAllText(Path.Combine(dir, "status.json")); Ok(sj.Contains("\"nextProtocol\":") && sj.Contains("\"needsUac\":{"), "status.json carries them too");
                Ok(Cmd(exe, dir, "test-account qP3xY7mN2kT9 s3cretPass") == "ok", "service credentials saved (test folder)");
                Ok(Cmd(exe, dir, "connect gaming") == "ok connecting", "connect gaming (remembered)"); r = Until(exe, dir, "connecting", "false");
                Ok(r.Contains("\"connected\":true") && r.Contains("\"target\":\"gaming\"") && r.Contains("\"nextProtocol\":\"IKEv2\""), "connected; target gaming; nextProtocol also while connected");
                Cmd(exe, dir, "disconnect"); Until(exe, dir, "connected", "false");
                Ok(Cmd(exe, dir, "connect junk.example --keep-target").StartsWith("error unknown server"), "junk with --keep-target: refused");
                Ok(Cmd(exe, dir, "connect p2p --keep-target") == "ok connecting", "connect p2p --keep-target"); r = Until(exe, dir, "connecting", "false");
                Ok(r.Contains("\"connected\":true") && Cmd(exe, dir, "test-current").EndsWith(" p2p"), "on a P2P server");
                Ok(r.Contains("\"target\":\"gaming\"") && File.ReadAllText(Path.Combine(dir, "settings.json")).Contains("gaming"), "the remembered target is still gaming (status and settings)");
                Ok(Cmd(exe, dir, "test-calls").Contains("dial "), "dialled the FAKE VPN layer only");
                Cmd(exe, dir, "disconnect"); Until(exe, dir, "connected", "false");
                // NordLynx (fake): connect / disconnect ask Windows
                Cmd(exe, dir, "test-wg on"); Thread.Sleep(300);
                Ok(Cmd(exe, dir, "test-token 0123456789abcdef0123456789abcdef") == "ok", "NordLynx key (fake endpoint, test folder)");
                Cmd(exe, dir, "protocol nordlynx"); r = Cmd(exe, dir, "status");
                Ok(r.Contains("\"nextProtocol\":\"NordLynx\"") && r.Contains("\"needsUac\":{\"connect\":true,\"disconnect\":false}"), "NordLynx next: connect asks Windows, nothing to disconnect: " + r);
                Ok(Cmd(exe, dir, "connect fastest --keep-target") == "ok connecting", "connect fastest --keep-target over NordLynx"); r = Until(exe, dir, "connecting", "false");
                Ok(r.Contains("\"connected\":true") && r.Contains("\"protocol\":\"NordLynx\"") && r.Contains("\"needsUac\":{\"connect\":true,\"disconnect\":true}"), "on NordLynx: disconnect asks Windows too: " + r);
                Ok(r.Contains("\"target\":\"gaming\""), "still remembers gaming");
                Ok(Cmd(exe, dir, "test-tunnel").Contains("wg-up"), "the FAKE tunnel was asked (nothing installed)");
                Cmd(exe, dir, "protocol ikev2"); r = Cmd(exe, dir, "status");
                Ok(r.Contains("\"protocol\":\"NordLynx\"") && r.Contains("\"nextProtocol\":\"IKEv2\""), "connected with NordLynx, next connect IKEv2: protocol and nextProtocol differ");
                Cmd(exe, dir, "disconnect"); r = Until(exe, dir, "connected", "false");
                Ok(r.Contains("\"needsUac\":{\"connect\":false,\"disconnect\":false}"), "off again with IKEv2 next: no UAC");
                Ok(Cmd(exe, dir, "connect us") == "ok connecting", "plain connect still works");
                r = Until(exe, dir, "connecting", "false"); Ok(r.Contains("\"target\":\"country:US\"") || r.Contains("\"target\":\"US\"") || r.Contains("\"target\":\"country:us\""), "without the flag the target changes as before: " + r);
                Cmd(exe, dir, "disconnect"); Until(exe, dir, "connected", "false");
            }
            finally { try { if (!app.HasExited) app.Kill(); } catch { } }
        }
    }
}
