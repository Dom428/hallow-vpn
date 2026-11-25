// Hallow VPN 1.2 - NordLynx (Nord's WireGuard) through the official WireGuard for Windows, as a second protocol next to IKEv2.
//   * WireGuard for Windows: detected at %ProgramFiles%\WireGuard\wireguard.exe. If it's missing, a button downloads the official MSI
//     from download.wireguard.com - ONLY when you click - and checks it twice before running it: its BLAKE2b-256 hash must match the
//     list WireGuard publishes (latest.sig, fetched over HTTPS; that list's own signify signature isn't checked - .NET has no Ed25519),
//     and its Authenticode signature must be valid and from "WireGuard LLC". Then msiexec runs it and Windows asks (UAC): your click.
//   * The NordLynx key: you paste a Nord ACCESS TOKEN (Nord Account > NordVPN > Set up NordVPN manually > access token); Hallow VPN asks
//     Nord once (GET api.nordvpn.com/v1/users/services/credentials, HTTP basic auth "token:<access token>") for nordlynx_private_key.
//     Token and key are kept in %APPDATA%\HallowVPN\nordlynx.dat, encrypted with Windows DPAPI for your Windows user (like account.dat):
//     never logged, never in status, the pipe, crash reports (Crash.Secrets2) or a command line.
//   * Connecting: WireGuard's documented CLI, "wireguard.exe /installtunnelservice <conf>" and "/uninstalltunnelservice <name>". Both need
//     admin (the tunnel is a Windows service). So each NordLynx connect / disconnect runs "HallowVPN.exe --elevated wg-up <ip> <key>" /
//     "wg-down" after Windows' UAC prompt (your click). The elevated copy - still your Windows user - decrypts the key itself, writes the
//     .conf into %LOCALAPPDATA%\HallowVPN\wg (only you, SYSTEM and Administrators may open it), installs the tunnel service, waits until
//     it runs (it has read the file by then) and deletes the .conf. The tunnel is called "HallowNordLynx" (its adapter's name too).
//   * The .conf: Address 10.5.0.2/32, DNS 103.86.96.100 / 103.86.99.100, the server's public key from Nord's server list
//     (technologies: wireguard_udp, metadata public_key), Endpoint <station>:51820, AllowedIPs 0.0.0.0/0, ::/0, keepalive 25 s.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Principal;
using System.ServiceProcess;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace HallowVPN
{
    public class NordLynxSecret { public string Token, Key; }

    public static class NordLynx
    {
        public const string Tunnel = "HallowNordLynx", Service = "WireGuardTunnel$" + Tunnel;
        public const string Address = "10.5.0.2", Prefix = "10.5.0.2/32"; public const int Port = 51820;
        public static string CredentialsUrl { get { return Nord.Api + "/v1/users/services/credentials"; } }
        static readonly byte[] Entropy = Encoding.UTF8.GetBytes("HallowVPN.nordlynx.v1");
        public static string FilePath { get { return Path.Combine(VpnSettings.Dir, "nordlynx.dat"); } }

        public static bool ValidKey(string k)
        {
            if (k == null || k.Length != 44 || !Regex.IsMatch(k, "^[A-Za-z0-9+/]{43}=$")) return false;
            try { return Convert.FromBase64String(k).Length == 32; } catch { return false; }
        }
        public static bool ValidIp(string ip) { IPAddress a; return ip != null && Regex.IsMatch(ip, "^\\d{1,3}(\\.\\d{1,3}){3}$") && IPAddress.TryParse(ip, out a); }
        // null = it can be an access token
        public static string CheckToken(string t)
        {
            t = (t ?? "").Trim();
            if (t.Length == 0) return "Paste the access token.";
            if (t.Contains("@")) return "That looks like an email address. Hallow VPN needs an ACCESS TOKEN: Nord Account > NordVPN > Set up NordVPN manually > Generate new token.";
            if (!Regex.IsMatch(t, "^[A-Za-z0-9_\\-]{16,256}$")) return "That doesn't look like a Nord access token (letters and numbers, no spaces) - copy it again.";
            return null;
        }
        // Nord's answer -> the NordLynx private key (null = not there / not a key)
        public static string ParseKey(string json)
        {
            try
            {
                var d = new System.Web.Script.Serialization.JavaScriptSerializer().DeserializeObject(json ?? "") as Dictionary<string, object>; object v;
                if (d == null || !d.TryGetValue("nordlynx_private_key", out v)) return null;
                var k = v as string; return ValidKey(k) ? k : null;
            }
            catch { return null; }
        }
        public static string Conf(string key, string serverKey, string ip)
        {
            if (!ValidKey(key) || !ValidKey(serverKey) || !ValidIp(ip)) throw new ArgumentException("not a NordLynx server or key");
            return "[Interface]\r\nPrivateKey = " + key + "\r\nAddress = " + Prefix + "\r\nDNS = " + Pbk.Dns1 + ", " + Pbk.Dns2 + "\r\n\r\n" +   // 1.3: Nord's DNS or the chosen filtering DNS
                "[Peer]\r\nPublicKey = " + serverKey + "\r\nAllowedIPs = 0.0.0.0/0, ::/0\r\nEndpoint = " + ip + ":" + Port + "\r\nPersistentKeepalive = 25\r\n";
        }
        // the argument of "--elevated wg-up": "<station ip> <server public key>" - nothing secret, checked on both sides
        public static bool ValidUpArg(string a)
        {
            var p = (a ?? "").Split(' '); return p.Length == 2 && ValidIp(p[0]) && ValidKey(p[1]);
        }

        public static string Save(string token, string key)
        {
            var e = CheckToken(token); if (e != null) return e;
            if (!ValidKey(key)) return "Nord's answer had no NordLynx key.";
            if (VpnSettings.Frozen) return null;
            byte[] plain = null;
            try
            {
                plain = Encoding.UTF8.GetBytes(Family.Json(new Dictionary<string, object> { { "t", token.Trim() }, { "k", key } }));
                Directory.CreateDirectory(VpnSettings.Dir);
                var tmp = FilePath + ".tmp";
                File.WriteAllBytes(tmp, ProtectedData.Protect(plain, Entropy, DataProtectionScope.CurrentUser));
                if (File.Exists(FilePath)) File.Replace(tmp, FilePath, null); else File.Move(tmp, FilePath);
                Crash.Secrets2 = new[] { token.Trim(), key };
                return null;
            }
            catch (Exception ex) { return "Couldn't save it: " + ex.Message; }
            finally { if (plain != null) Array.Clear(plain, 0, plain.Length); }
        }
        public static NordLynxSecret Load()
        {
            byte[] plain = null;
            try
            {
                if (!File.Exists(FilePath)) return null;
                plain = ProtectedData.Unprotect(File.ReadAllBytes(FilePath), Entropy, DataProtectionScope.CurrentUser);
                var d = new System.Web.Script.Serialization.JavaScriptSerializer().Deserialize<Dictionary<string, object>>(Encoding.UTF8.GetString(plain)); object t, k;
                if (d == null || !d.TryGetValue("t", out t) || !d.TryGetValue("k", out k) || !(t is string) || !ValidKey(k as string)) return null;
                Crash.Secrets2 = new[] { (string)t, (string)k };
                return new NordLynxSecret { Token = (string)t, Key = (string)k };
            }
            catch { return null; }
            finally { if (plain != null) Array.Clear(plain, 0, plain.Length); }
        }
        public static bool Exists { get { try { return File.Exists(FilePath); } catch { return false; } } }
        public static void Remove() { if (VpnSettings.Frozen) return; try { if (File.Exists(FilePath)) File.Delete(FilePath); } catch { } Crash.Secrets2 = new string[0]; }
    }

    // Nord's credentials endpoint (needs the access token). Real only on the user's click; tests use the fake.
    public interface INordAuth { string Credentials(string token); }
    public class RealNordAuth : INordAuth
    {
        public string Credentials(string token)
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            var req = (HttpWebRequest)WebRequest.Create(NordLynx.CredentialsUrl);
            req.UserAgent = "HallowVPN/" + Program.Version; req.Timeout = 15000; req.Accept = "application/json";
            req.Headers[HttpRequestHeader.Authorization] = "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes("token:" + token.Trim()));
            try { using (var resp = (HttpWebResponse)req.GetResponse()) using (var r = new StreamReader(resp.GetResponseStream(), Encoding.UTF8)) return r.ReadToEnd(); }
            catch (WebException e)
            {
                var h = e.Response as HttpWebResponse;
                if (h != null && (h.StatusCode == HttpStatusCode.Unauthorized || h.StatusCode == HttpStatusCode.Forbidden)) throw new Exception("Nord didn't accept the access token - make a new one on Nord's website and paste it again.");
                throw new Exception("Couldn't reach Nord (api.nordvpn.com): " + e.Message);
            }
        }
    }
    public class FakeNordAuth : INordAuth
    {
        public string Answer = "{\"id\":1,\"created_at\":\"2026-01-01 00:00:00\",\"updated_at\":\"2026-01-01 00:00:00\",\"username\":\"fakeUser1234\",\"password\":\"fakePass5678\",\"nordlynx_private_key\":\"" + FakeData.WgPrivateKey + "\"}";
        public string Error; public int Calls;
        public string Credentials(string token) { Calls++; if (Error != null) throw new Exception(Error); return Answer; }
    }

    // ---- WireGuard for Windows: detect, download on click, check, install (msiexec -> UAC)
    public interface IWgSetup
    {
        bool Installed();
        string Download(out string file);     // fetch latest.sig + the MSI for this PC, check both; null = ready to install
        string Install(string file);          // msiexec /i (Windows asks - UAC); null = installed
    }
    public static class WgSetup
    {
        public const string Base = "https://download.wireguard.com/windows-client/";
        public static string SigUrl { get { return Base + "latest.sig"; } }
        public static string Exe
        {
            get
            {
                foreach (var d in new[] { Environment.GetEnvironmentVariable("ProgramW6432"), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles) })
                    if (!string.IsNullOrEmpty(d)) { var p = Path.Combine(d, @"WireGuard\wireguard.exe"); if (File.Exists(p)) return p; }
                return null;
            }
        }
        public static string Arch()
        {
            switch (RuntimeInformation.OSArchitecture) { case Architecture.Arm64: return "arm64"; case Architecture.X86: return "x86"; default: return "amd64"; }
        }
        // latest.sig: "untrusted comment...", the signature, then "<blake2b-256 hex>  <file name>" lines
        public static Dictionary<string, string> ParseList(string sig)
        {
            var r = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var l in (sig ?? "").Replace("\r", "").Split('\n'))
            {
                var m = Regex.Match(l.Trim(), "^([0-9a-f]{64})\\s+(wireguard-[a-z0-9]+-[0-9.]+\\.msi)$");
                if (m.Success) r[m.Groups[2].Value] = m.Groups[1].Value;
            }
            return r;
        }
        public static string Pick(Dictionary<string, string> list, string arch)
        {
            return list.Keys.Where(k => Regex.IsMatch(k, "^wireguard-" + Regex.Escape(arch) + "-[0-9.]+\\.msi$")).OrderByDescending(k => new Version(Regex.Match(k, "-([0-9.]+)\\.msi$").Groups[1].Value)).FirstOrDefault();
        }
        // null = the file is the one WireGuard lists AND is signed by WireGuard LLC
        public static string Check(string file, string blake2b, Func<string, string> signer)
        {
            if (!File.Exists(file)) return "The download isn't there.";
            if (new FileInfo(file).Length > 40 * 1024 * 1024) return "The download is far bigger than WireGuard's installer - not using it.";
            var h = Blake2b.HashFile(file, 32);
            if (!string.Equals(h, blake2b, StringComparison.OrdinalIgnoreCase)) return "The download doesn't match the hash WireGuard publishes - not installing it.";
            var who = signer(file);
            if (who == null) return "The download's digital signature isn't valid - not installing it.";
            if (!Regex.IsMatch(who, "(^|, )CN=WireGuard LLC(,|$)") || !Regex.IsMatch(who, "(^|, )O=WireGuard LLC(,|$)")) return "The download is signed by someone other than WireGuard LLC (" + who + ") - not installing it.";
            return null;
        }

        // Authenticode: WinVerifyTrust (no UI, no revocation lookups), then the signer's subject
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        class FileInfoW { public uint cbStruct = (uint)Marshal.SizeOf(typeof(FileInfoW)); public string pcwszFilePath; public IntPtr hFile = IntPtr.Zero; public IntPtr pgKnownSubject = IntPtr.Zero; }
        [StructLayout(LayoutKind.Sequential)]
        class TrustData { public uint cbStruct = (uint)Marshal.SizeOf(typeof(TrustData)); public IntPtr pPolicyCallbackData, pSIPClientData; public uint dwUIChoice = 2, fdwRevocationChecks = 0, dwUnionChoice = 1; public IntPtr pFile; public uint dwStateAction = 0; public IntPtr hWVTStateData, pwszURLReference; public uint dwProvFlags = 0x10, dwUIContext = 0; public IntPtr pSignatureSettings; }
        [DllImport("wintrust.dll", CharSet = CharSet.Unicode)] static extern int WinVerifyTrust(IntPtr hwnd, [MarshalAs(UnmanagedType.LPStruct)] Guid action, TrustData data);
        static readonly Guid VerifyV2 = new Guid("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");
        public static string Signer(string file)
        {
            var fi = new FileInfoW { pcwszFilePath = file }; var p = Marshal.AllocHGlobal(Marshal.SizeOf(fi));
            try
            {
                Marshal.StructureToPtr(fi, p, false);
                var td = new TrustData { pFile = p };
                if (WinVerifyTrust(new IntPtr(-1), VerifyV2, td) != 0) return null;
                return new X509Certificate2(X509Certificate.CreateFromSignedFile(file)).Subject;
            }
            catch { return null; }
            finally { Marshal.FreeHGlobal(p); }
        }
        // 1.2.6: the installer goes where Windows Installer (msiserver, a service) can read it: your Downloads folder, else
        // %ProgramData%\HallowVPN - not %LOCALAPPDATA%\HallowVPN\downloads. It's deleted after the install (the log too, if it worked).
        public static string DownloadDir { get { return PickDownloadDir(KnownDownloads, Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData)); } }
        public static string PickDownloadDir(Func<string> downloads, string programData)
        {
            string d = null; try { d = downloads == null ? null : downloads(); } catch { }
            if (!string.IsNullOrEmpty(d) && Directory.Exists(d)) return d;
            return Path.Combine(programData, "HallowVPN");
        }
        public const string FilePrefix = "HallowVPN-";   // never overwrites a WireGuard installer of your own in Downloads
        [DllImport("shell32.dll")] static extern int SHGetKnownFolderPath([MarshalAs(UnmanagedType.LPStruct)] Guid id, uint flags, IntPtr token, out IntPtr path);
        static string KnownDownloads()
        {
            IntPtr p; if (SHGetKnownFolderPath(new Guid("374DE290-123F-4565-9164-39C4925E467B"), 0, IntPtr.Zero, out p) != 0) return null;
            try { return Marshal.PtrToStringUni(p); } finally { Marshal.FreeCoTaskMem(p); }
        }
        public static string LogFor(string msi) { return msi + ".log"; }
        public static void CleanUp(string msi, bool keepLog)
        {
            if (msi == null) return;
            try { if (File.Exists(msi)) File.Delete(msi); } catch { }
            if (!keepLog) try { if (File.Exists(LogFor(msi))) File.Delete(LogFor(msi)); } catch { }
        }
        public static string Failed(string why, string msi)
        {
            var log = msi == null ? null : LogFor(msi);
            return log != null && File.Exists(log) ? why + " Windows Installer's log: " + log : why;
        }
    }
    public class RealWgSetup : IWgSetup
    {
        public bool Installed() { return WgSetup.Exe != null; }
        public string Download(out string file)
        {
            file = null;
            try
            {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                string sig; using (var wc = new WebClient()) { wc.Headers[HttpRequestHeader.UserAgent] = "HallowVPN/" + Program.Version; sig = wc.DownloadString(WgSetup.SigUrl); }
                var list = WgSetup.ParseList(sig); var name = WgSetup.Pick(list, WgSetup.Arch());
                if (name == null) return "WireGuard's download list has no installer for this PC (" + WgSetup.Arch() + ").";
                var dir = WgSetup.DownloadDir; Directory.CreateDirectory(dir); file = Path.Combine(dir, WgSetup.FilePrefix + name);
                using (var wc = new WebClient()) { wc.Headers[HttpRequestHeader.UserAgent] = "HallowVPN/" + Program.Version; wc.DownloadFile(WgSetup.Base + name, file); }
                var why = WgSetup.Check(file, list[name], WgSetup.Signer);
                if (why != null) { WgSetup.CleanUp(file, false); file = null; }
                return why;
            }
            catch (Exception e) { WgSetup.CleanUp(file, false); file = null; return "Couldn't get WireGuard from download.wireguard.com: " + e.Message; }
        }
        public string Install(string file)
        {
            if (VpnSettings.TestDir != null || Snap.On) return "Not in a test copy.";
            string why = null; bool running = false;
            try
            {
                // /l*v: Windows Installer's own log next to the file - shown if it fails, deleted if it worked
                var psi = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "msiexec.exe"), "/i \"" + file + "\" /l*v \"" + WgSetup.LogFor(file) + "\"") { UseShellExecute = true };
                using (var p = Process.Start(psi))
                {
                    if (!p.WaitForExit(15 * 60 * 1000)) { running = true; return "The WireGuard installer is still open."; }
                    if (p.ExitCode == 1602) why = "The WireGuard installer was cancelled.";
                    else if (p.ExitCode != 0 && p.ExitCode != 3010) why = WgSetup.Failed("The WireGuard installer stopped (code " + p.ExitCode + ").", file);
                }
                if (why == null && !Installed()) why = WgSetup.Failed("WireGuard doesn't seem to be installed.", file);
                return why;
            }
            catch (System.ComponentModel.Win32Exception e) { return why = e.NativeErrorCode == 1223 ? Elevated.Cancelled : "Couldn't start the installer: " + e.Message; }
            catch (Exception e) { return why = "Couldn't start the installer: " + e.Message; }
            finally { if (!running) WgSetup.CleanUp(file, why != null && why.Contains("Windows Installer's log")); }
        }
    }
    public class FakeWgSetup : IWgSetup
    {
        public bool In; public string DownloadError, InstallError; public readonly List<string> Calls = new List<string>();
        public bool Installed() { return In; }
        public string Download(out string file) { Calls.Add("download"); file = DownloadError == null ? "wireguard-amd64-1.1.1.msi" : null; return DownloadError; }
        public string Install(string file) { Calls.Add("install " + file); if (InstallError != null) return InstallError; In = true; return null; }
    }

    // ---- the tunnel
    public interface ITunnel
    {
        bool IsUp();
        string Up(Server s);       // UAC -> the tunnel service with this server; null = up
        string Down();             // UAC -> no tunnel service; null = down
        bool Stats(out long sent, out long received);
        bool Exists();             // the tunnel service is installed (running or not)
    }
    public class WireGuardTunnel : ITunnel
    {
        // read-only: the service's state (an InvalidOperationException = there's no such service)
        static ServiceControllerStatus? State() { try { using (var s = new ServiceController(NordLynx.Service)) return s.Status; } catch { return null; } }
        public bool Exists() { return State() != null; }
        public bool IsUp() { return State() == ServiceControllerStatus.Running; }
        public string Up(Server s)
        {
            if (s == null || !NordLynx.ValidIp(s.Station) || !NordLynx.ValidKey(s.WgKey)) return "That server has no NordLynx details in Nord's list.";
            var e = Elevated.Run("wg-up", s.Station + " " + s.WgKey);
            if (e != null) return e;
            return IsUp() ? null : "WireGuard didn't start the NordLynx tunnel.";
        }
        public string Down() { if (!Exists()) return null; return Elevated.Run("wg-down"); }
        public bool Stats(out long sent, out long received)
        {
            sent = received = -1;
            try
            {
                var n = NetworkInterface.GetAllNetworkInterfaces().FirstOrDefault(x => x.Name == NordLynx.Tunnel);
                if (n == null) return false;
                var st = n.GetIPStatistics(); sent = st.BytesSent; received = st.BytesReceived; return true;
            }
            catch { return false; }
        }

        // ---- inside "HallowVPN.exe --elevated wg-up|wg-down" (after UAC; never in a test copy)
        static int Wg(string exe, string args, StringBuilder log)
        {
            var psi = new ProcessStartInfo(exe, args) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            using (var p = Process.Start(psi)) { p.StandardOutput.ReadToEnd(); var err = p.StandardError.ReadToEnd(); p.WaitForExit(30000); log.AppendLine("wireguard " + args.Split(' ')[0] + " -> " + p.ExitCode + (err.Length > 0 ? " " + Family.OneLine(err) : "")); return p.ExitCode; }
        }
        static bool WaitFor(Func<bool> f, int ms) { var sw = Stopwatch.StartNew(); while (sw.ElapsedMilliseconds < ms) { if (f()) return true; Thread.Sleep(200); } return f(); }
        public static string ConfDir { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HallowVPN", "wg"); } }
        // only this user, SYSTEM (the tunnel service reads it) and Administrators; nothing inherited
        static void Protect(string dir)
        {
            Directory.CreateDirectory(dir);
            var ds = new DirectorySecurity(); ds.SetAccessRuleProtection(true, false);
            foreach (var sid in new[] { WindowsIdentity.GetCurrent().User, new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null) })
                ds.AddAccessRule(new FileSystemAccessRule(sid, FileSystemRights.FullControl, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
            Directory.SetAccessControl(dir, ds);
        }
        public static int ElevatedUp(string arg, StringBuilder log)
        {
            if (!NordLynx.ValidUpArg(arg)) { log.AppendLine("bad argument"); return 5; }
            var exe = WgSetup.Exe; if (exe == null) { log.AppendLine("WireGuard isn't installed"); return 7; }
            var sec = NordLynx.Load();
            if (sec == null) { log.AppendLine("the NordLynx key can't be read (UAC approved with another Windows account? DPAPI only opens it for yours)"); return 6; }
            var p = arg.Split(' ');
            var conf = Path.Combine(ConfDir, NordLynx.Tunnel + ".conf");
            try
            {
                if (new WireGuardTunnel().Exists()) { Wg(exe, "/uninstalltunnelservice " + NordLynx.Tunnel, log); WaitFor(() => !new WireGuardTunnel().Exists(), 10000); }
                Protect(ConfDir);
                File.WriteAllText(conf, NordLynx.Conf(sec.Key, p[1], p[0]), new UTF8Encoding(false));
                if (Wg(exe, "/installtunnelservice \"" + conf + "\"", log) != 0) return 8;
                bool up = WaitFor(() => new WireGuardTunnel().IsUp(), 25000);
                log.AppendLine(up ? "tunnel running" : "tunnel didn't start");
                return up ? 0 : 9;
            }
            finally { try { if (File.Exists(conf)) File.Delete(conf); log.AppendLine("conf deleted"); } catch (Exception e) { log.AppendLine("couldn't delete the conf: " + e.Message); } sec.Key = null; }
        }
        public static int ElevatedDown(StringBuilder log)
        {
            var exe = WgSetup.Exe; if (exe == null) { log.AppendLine("WireGuard isn't installed"); return 7; }
            if (!new WireGuardTunnel().Exists()) return 0;
            Wg(exe, "/uninstalltunnelservice " + NordLynx.Tunnel, log);
            return WaitFor(() => !new WireGuardTunnel().Exists(), 15000) ? 0 : 9;
        }
    }
    public class FakeTunnel : ITunnel
    {
        public bool Up_, Installed_; public string UpError; public readonly List<string> Calls = new List<string>();
        public long Sent = 1048576, Received = 20971520;
        public bool IsUp() { return Up_; }
        public bool Exists() { return Installed_; }
        public string Up(Server s)
        {
            if (s == null || !NordLynx.ValidIp(s.Station) || !NordLynx.ValidKey(s.WgKey)) return "That server has no NordLynx details in Nord's list.";
            NordLynx.Conf(FakeData.WgPrivateKey, s.WgKey, s.Station);   // the same checks as the real one
            lock (Calls) Calls.Add("wg-up " + s.Station + " (asks Windows)");
            if (UpError != null) return UpError;
            Up_ = true; Installed_ = true; return null;
        }
        public string Down() { lock (Calls) Calls.Add("wg-down (asks Windows)"); Up_ = false; Installed_ = false; return null; }
        public bool Stats(out long sent, out long received) { sent = Sent; received = Received; if (Up_) { Sent += 30720; Received += 512000; } return Up_; }
        public void Drop() { Up_ = false; lock (Calls) Calls.Add("(dropped)"); }
    }
}
