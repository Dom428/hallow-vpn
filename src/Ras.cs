// Hallow VPN - Windows' own IKEv2 VPN client, no drivers:
//   * the profile: ONE per-user VPN connection called "Hallow VPN" (PowerShell Add-VpnConnection / Set-VpnConnection WITHOUT
//     -AllUserConnection, so no admin): IKEv2, EAP (EAP-MSCHAPv2, Windows' default EAP for Add-VpnConnection), encryption required,
//     credentials NOT remembered by Windows, split tunnelling + routes as set on the Split tunnel page. The script carries no credentials.
//   * dialling: rasapi32's RasDial in THIS process with the service username / password in RASDIALPARAMS - so they are never on a
//     command line (no rasdial.exe <user> <pass>), never in a file Windows keeps, never in a log. szPhoneNumber = the server picked.
//   * hanging up: RasHangUp; "is it up?": RasEnumConnections (only this user's connections; read-only).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace HallowVPN
{
    public class ProfileSpec
    {
        public string Host; public bool SplitTunnel; public List<string> Routes = new List<string>();
        public string Key { get { return (Host ?? "") + "|" + (SplitTunnel ? "split" : "full") + "|" + string.Join(",", Routes); } }
    }

    public interface IVpn
    {
        bool ProfileExists();
        string ApplyProfile(ProfileSpec spec);       // creates / updates the per-user profile; null = done, else the error
        string Dial(string host, Credentials c);     // blocks until up or failed; null = connected, else the error
        string HangUp();                             // null = it's down
        bool IsUp();
        string RemoveProfile();
        int LastCode { get; }                         // Windows' error number of the last failed dial (0 = none)
        bool Stats(out long sent, out long received, out TimeSpan duration);   // RasGetConnectionStatistics while up
        string PbkPath { get; }                       // the phonebook file the profile lives in (null = none)
    }

    public static class VpnScript
    {
        public const string Entry = "Hallow VPN";
        static string Q(string s) { return "'" + (s ?? "").Replace("'", "''") + "'"; }
        // the PowerShell that creates or updates the per-user profile (validated host and routes only - nothing from outside goes in raw)
        public static string Profile(ProfileSpec p)
        {
            if (!Nord.ValidHost(p.Host)) throw new ArgumentException("not a Nord server name");
            foreach (var r in p.Routes) if (Split.Normalize(r) != r) throw new ArgumentException("not an address range: " + r);
            var sb = new StringBuilder();
            sb.AppendLine("$ErrorActionPreference = 'Stop'");
            sb.AppendLine("try {");
            sb.AppendLine("  $n = " + Q(Entry));
            sb.AppendLine("  $split = $" + (p.SplitTunnel ? "true" : "false"));
            sb.AppendLine("  $c = Get-VpnConnection -Name $n -ErrorAction SilentlyContinue");
            sb.AppendLine("  if ($c -and ($c.TunnelType -ne 'Ikev2' -or $c.AuthenticationMethod -notcontains 'Eap')) { Remove-VpnConnection -Name $n -Force; $c = $null }");
            sb.AppendLine("  if ($c) { Set-VpnConnection -Name $n -ServerAddress " + Q(p.Host) + " -SplitTunneling:$split -RememberCredential:$true -Force }");
            sb.AppendLine("  else { Add-VpnConnection -Name $n -ServerAddress " + Q(p.Host) + " -TunnelType Ikev2 -AuthenticationMethod Eap -EncryptionLevel Required -SplitTunneling:$split -RememberCredential:$true -Force }");
            // switches MUST be written -Switch:$value: "-RememberCredential $false" binds $false to another parameter and Windows
            // refuses the profile ("conflicting PPP Tunnel types") - the 1.0-1.2 bug that stopped every first connect
            // Windows' default IKEv2 proposals (3DES / SHA1 / DH group 2) are refused by Nord's servers - error 13868. Nord accepts
            // AES-256-GCM with SHA-256 and DH group 14; set on every connect (per-user entry, no admin), checked on a real PC.
            sb.AppendLine("  Set-VpnConnectionIPsecConfiguration -ConnectionName $n -AuthenticationTransformConstants GCMAES256 -CipherTransformConstants GCMAES256 -EncryptionMethod AES256 -IntegrityCheckMethod SHA256 -DHGroup Group14 -PfsGroup None -Force");
            sb.AppendLine("  $c = Get-VpnConnection -Name $n");
            sb.AppendLine("  foreach ($r in @($c.Routes)) { if ($r) { Remove-VpnConnectionRoute -ConnectionName $n -DestinationPrefix $r.DestinationPrefix -ErrorAction SilentlyContinue } }");
            if (p.SplitTunnel) foreach (var r in p.Routes) sb.AppendLine("  Add-VpnConnectionRoute -ConnectionName $n -DestinationPrefix " + Q(r));
            sb.AppendLine("  'ok'; exit 0");
            sb.AppendLine("} catch { 'ERR ' + $_.Exception.Message; exit 1 }");
            return sb.ToString();
        }
        public static string Remove()
        {
            return "$ErrorActionPreference = 'Stop'\r\ntry { if (Get-VpnConnection -Name " + Q(Entry) + " -ErrorAction SilentlyContinue) { Remove-VpnConnection -Name " + Q(Entry) + " -Force }; 'ok'; exit 0 } catch { 'ERR ' + $_.Exception.Message; exit 1 }\r\n";
        }
        // runs a script (no credentials in it) with powershell -EncodedCommand, hidden, at most 60 s
        public static string Run(string script)
        {
            try
            {
                // NOT through standard input: .NET Framework's Process.Start wraps stdin in a StreamWriter using the console input
                // encoding with AutoFlush on, so on PCs where that is UTF-8 a byte-order mark is written before we write anything.
                // PowerShell then reads "﻿$ErrorActionPreference" as a command and the whole script fails - the 1.0-1.2.1 bug
                // behind "Windows couldn't set up the VPN profile:". -EncodedCommand (UTF-16LE, base64) has no encoding to guess.
                // These scripts carry no credentials (a test checks), so having them on the command line is fine.
                var enc = Convert.ToBase64String(Encoding.Unicode.GetBytes(StripBom(script)));
                var psi = new ProcessStartInfo("powershell.exe", "-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand " + enc) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
                using (var p = Process.Start(psi))
                {
                    var o = p.StandardOutput.ReadToEndAsync(); var e = p.StandardError.ReadToEndAsync();
                    if (!p.WaitForExit(60000)) { try { p.Kill(); } catch { } return "Windows took too long to set up the VPN profile."; }
                    var outText = StripBom(o.Result ?? "").Trim();
                    if (p.ExitCode == 0 && outText.EndsWith("ok")) return null;
                    return "Windows couldn't set up the VPN profile: " + Reason(outText, StripBom(e.Result ?? "").Trim(), p.ExitCode);
                }
            }
            catch (Exception ex) { return "Couldn't run PowerShell to set up the VPN profile: " + ex.Message; }
        }
        public static string StripBom(string s) { return (s ?? "").Replace("﻿", ""); }
        // never an empty reason: the script's own ERR line, else PowerShell's first error line, else its last output, else the exit code
        public static string Reason(string outText, string errText, int exitCode)
        {
            var ln = (outText ?? "").Split('\n').Select(x => x.Trim()).LastOrDefault(x => x.StartsWith("ERR "));
            if (ln != null && ln.Length > 4) return Family.OneLine(ln.Substring(4));
            var err = (errText ?? "").Split('\n').Select(x => x.Trim()).FirstOrDefault(x => x.Length > 0 && !x.StartsWith("#< CLIXML") && !x.StartsWith("<"));
            if (err != null) return Family.OneLine(err);
            var last = (outText ?? "").Split('\n').Select(x => x.Trim()).LastOrDefault(x => x.Length > 0);
            if (last != null) return Family.OneLine(last);
            return "PowerShell stopped with exit code " + exitCode + " and gave no reason.";
        }
    }

    public static class Ras
    {
        // Windows' RAS / IKE error numbers -> what to do about them
        public static string Explain(int code)
        {
            switch (code)
            {
                case 691: return "Nord didn't accept the service username or password (error 691). Copy them again from Nord's website - they are NOT your Nord login.";
                case 13801: return "Windows doesn't trust the server's certificate (error 13801). Install NordVPN's root certificate on the Account page.";
                case 13806: case 13817: return "Windows couldn't check the server's certificate (error " + code + "). Install NordVPN's root certificate on the Account page.";
                case 809: return "The connection couldn't get through (error 809): a firewall or router is blocking IKEv2 (UDP 500 and 4500).";
                case 868: return "Windows couldn't look up the server's name (error 868). Check the internet connection.";
                case 800: return "Couldn't reach the server (error 800). Try another server.";
                case 623: return "The Windows VPN profile is missing (error 623) - it's set up again on the next connect.";
                case 631: case 831: case 815: case 629: return "The connection was ended (error " + code + ").";
                case 13868: return "The server and Windows couldn't agree on the encryption (error 13868). Click \"Recreate the VPN profile\" on Fix my connection - it sets the encryption Nord expects.";
                case 720: return "Windows couldn't set up the connection's network settings (error 720).";
            }
            return "Windows' VPN client reported error " + code + ".";
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode, Pack = 4)]
        public struct RASDIALPARAMS
        {
            public int dwSize;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 257)] public string szEntryName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 129)] public string szPhoneNumber;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 129)] public string szCallbackNumber;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 257)] public string szUserName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 257)] public string szPassword;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 16)] public string szDomain;
            public int dwSubEntry;
            public IntPtr dwCallbackId;
            public int dwIfIndex;
        }
        [StructLayout(LayoutKind.Sequential, Pack = 4)] public struct LUID { public int Low, High; }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode, Pack = 4)]
        public struct RASCONN
        {
            public int dwSize; public IntPtr hrasconn;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 257)] public string szEntryName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 17)] public string szDeviceType;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 129)] public string szDeviceName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szPhonebook;
            public int dwSubEntry; public Guid guidEntry; public int dwFlags; public LUID luid; public Guid guidCorrelationId;
        }
        // ras.h packs its structures to 4 bytes (pshpack4): RASDIALPARAMSW = 2112 bytes on x64 (Windows 7+ layout), RASCONNW = 1388
        [DllImport("rasapi32.dll", CharSet = CharSet.Unicode)] public static extern int RasDial(IntPtr ext, string phonebook, ref RASDIALPARAMS p, int notifierType, IntPtr notifier, out IntPtr h);
        [DllImport("rasapi32.dll", CharSet = CharSet.Unicode)] public static extern int RasHangUp(IntPtr h);
        // RAS_STATS: 15 DWORDs (size, bytes sent, bytes received, frames..., errors..., compression, bps, connect duration in ms)
        [DllImport("rasapi32.dll")] public static extern int RasGetConnectionStatistics(IntPtr h, [In, Out] int[] stats);
        [DllImport("rasapi32.dll", CharSet = CharSet.Unicode)] public static extern int RasEnumConnections([In, Out] RASCONN[] conns, ref int cb, ref int count);

        // this user's active RAS connections (entry name, handle); read-only. error = the RAS error number (0 = fine)
        public static List<KeyValuePair<string, IntPtr>> Active(out int error)
        {
            var r = new List<KeyValuePair<string, IntPtr>>();
            int size = Marshal.SizeOf(typeof(RASCONN));
            for (int n = 4; n <= 64; n *= 4)
            {
                var arr = new RASCONN[n]; arr[0].dwSize = size;
                int cb = size * n, count = 0;
                error = RasEnumConnections(arr, ref cb, ref count);
                if (error == 603) continue;   // buffer too small
                if (error != 0) return r;
                for (int i = 0; i < count; i++) r.Add(new KeyValuePair<string, IntPtr>(arr[i].szEntryName, arr[i].hrasconn));
                return r;
            }
            error = 603; return r;
        }
        public static int DialSize { get { return Marshal.SizeOf(typeof(RASDIALPARAMS)); } }

        // Windows' saved EAP login for the entry (what its own sign-in dialog stores when "remember" is on). Only the SIZE is read
        // here - never the data. size > 0 = Windows has a login to use for RasDial.
        [DllImport("rasapi32.dll", CharSet = CharSet.Unicode)] static extern int RasGetEapUserData(IntPtr token, string phonebook, string entry, byte[] data, ref int size);
        public static bool HasWindowsLogin(string pbk)
        {
            try { int size = 0; RasGetEapUserData(IntPtr.Zero, pbk, VpnScript.Entry, null, ref size); return size > 0; } catch { return false; }
        }
        // Windows' saved EAP login for the entry, copied into unmanaged memory for one RasDial (Wipe it); IntPtr.Zero = none saved
        public static IntPtr WindowsLoginBlob(string pbk, out int size)
        {
            size = 0;
            try
            {
                int sz = 0; RasGetEapUserData(IntPtr.Zero, pbk, VpnScript.Entry, null, ref sz); if (sz <= 0) return IntPtr.Zero;
                var data = new byte[sz]; if (RasGetEapUserData(IntPtr.Zero, pbk, VpnScript.Entry, data, ref sz) != 0) { Array.Clear(data, 0, data.Length); return IntPtr.Zero; }
                var mem = Marshal.AllocHGlobal(sz); Marshal.Copy(data, 0, mem, sz); Array.Clear(data, 0, data.Length);
                size = sz; return mem;
            }
            catch { size = 0; return IntPtr.Zero; }
        }
        public static void Wipe(IntPtr p, int size) { if (p == IntPtr.Zero) return; for (int i = 0; i < size; i++) Marshal.WriteByte(p, i, 0); Marshal.FreeHGlobal(p); }
        // RASDIALEXTENSIONS with RasEapInfo. ras.h packs to 4: x64 = dwSize 0, dwfOptions 4, hwndParent 8, reserved 16, reserved1 24,
        // RasEapInfo { dwSizeofEapInfo 32, pbEapInfo 36 }, fSkipPppAuth 44, RasDevSpecificInfo { 48, 52 } = 60 (accepted on a real PC);
        // "natural" (8-byte aligned, 64) is tried only if Windows answers 632. x86 = 40.
        public static IntPtr Extensions(IntPtr eap, int eapSize, bool natural, out int size)
        {
            int so, po, total;
            if (IntPtr.Size != 8) { so = 20; po = 24; total = 40; }
            else if (!natural) { so = 32; po = 36; total = 60; }
            else { so = 32; po = 40; total = 64; }
            var p = Marshal.AllocHGlobal(total);
            for (int i = 0; i < total; i++) Marshal.WriteByte(p, i, 0);
            Marshal.WriteInt32(p, 0, total); Marshal.WriteInt32(p, so, eapSize); Marshal.WriteIntPtr(p, po, eap);
            size = total; return p;
        }        public const string NeedsWindowsLogin = "Windows needs to remember your Nord login once (error 691): click \"Sign in through Windows\" on the Connection page, paste the same service username and password, and keep \"Save\" ticked. After that Hallow VPN connects by itself.";
        // Windows' own dial dialog for the entry (per-user, no admin): the user types the service login once and Windows saves it
        // 1.2.6: the box opened BEHIND Hallow VPN's window - so Hallow VPN (the foreground app, the user just clicked) lets it take the
        // foreground and brings its window to the front once it appears; closed() runs (on a background thread) when the box is gone.
        public static string WindowsSignIn(string pbk, Action closed = null)
        {
            if (VpnSettings.TestDir != null || Snap.On) return "Not in a test copy.";
            try
            {
                var exe = Path.Combine(Environment.SystemDirectory, "rasphone.exe");
                var args = (pbk != null ? "-f \"" + pbk + "\" " : "") + "-d \"" + VpnScript.Entry + "\"";
                var p = Process.Start(new ProcessStartInfo(exe, args) { UseShellExecute = false });
                try { AllowSetForegroundWindow(p.Id); } catch { }
                new Thread(() =>
                {
                    try
                    {
                        var sw = Stopwatch.StartNew(); IntPtr w = IntPtr.Zero;
                        while (sw.ElapsedMilliseconds < 10000 && w == IntPtr.Zero && !p.HasExited) { Thread.Sleep(100); w = PickWindow(WindowsOf(p.Id)); }
                        if (w != IntPtr.Zero) Front(w);
                        p.WaitForExit(30 * 60 * 1000);
                    }
                    catch { }
                    finally { try { p.Dispose(); } catch { } if (closed != null) try { closed(); } catch { } }
                }) { IsBackground = true, Name = "windows-sign-in" }.Start();
                return null;
            }
            catch (Exception e) { return "Couldn't open Windows' sign-in: " + e.Message; }
        }
        public class WinInfo { public IntPtr Handle; public bool Visible; public bool Owned; public string Title; }
        // the process's sign-in box: a visible, titled top-level window; unowned first (the dialog itself), else any visible titled one
        public static IntPtr PickWindow(IEnumerable<WinInfo> wins)
        {
            var l = (wins ?? Enumerable.Empty<WinInfo>()).Where(x => x != null && x.Visible && !string.IsNullOrEmpty(x.Title)).ToList();
            var w = l.FirstOrDefault(x => !x.Owned) ?? l.FirstOrDefault();
            return w == null ? IntPtr.Zero : w.Handle;
        }
        public static List<WinInfo> WindowsOf(int pid)
        {
            var r = new List<WinInfo>();
            EnumWindows((h, l) =>
            {
                uint wp; GetWindowThreadProcessId(h, out wp);
                if (wp == (uint)pid) { var sb = new StringBuilder(256); GetWindowText(h, sb, sb.Capacity); r.Add(new WinInfo { Handle = h, Visible = IsWindowVisible(h), Owned = GetWindow(h, 4) != IntPtr.Zero, Title = sb.ToString() }); }
                return true;
            }, IntPtr.Zero);
            return r;
        }
        static void Front(IntPtr w)
        {
            if (IsIconic(w)) ShowWindow(w, 9);   // SW_RESTORE
            BringWindowToTop(w); SetForegroundWindow(w);
        }
        delegate bool EnumProc(IntPtr h, IntPtr l);
        [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc f, IntPtr l);
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr h, StringBuilder sb, int max);
        [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
        [DllImport("user32.dll")] static extern bool IsIconic(IntPtr h);
        [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr h, uint cmd);
        [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr h, int cmd);
        [DllImport("user32.dll")] static extern bool BringWindowToTop(IntPtr h);
        [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);
        [DllImport("user32.dll")] static extern bool AllowSetForegroundWindow(int pid);    }

    public class WindowsVpn : IVpn
    {
        public static string Phonebook { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"Microsoft\Network\Connections\Pbk\rasphone.pbk"); } }
        public bool ProfileExists()
        {
            try { return File.Exists(Phonebook) && File.ReadAllLines(Phonebook).Any(l => l.Trim() == "[" + VpnScript.Entry + "]"); } catch { return false; }
        }
        public string ApplyProfile(ProfileSpec spec) { return VpnScript.Run(VpnScript.Profile(spec)); }
        public string RemoveProfile() { HangUp(); return VpnScript.Run(VpnScript.Remove()); }
        IntPtr Handle() { int e; var c = Ras.Active(out e).FirstOrDefault(x => x.Key == VpnScript.Entry); return c.Value; }
        public bool IsUp() { return Handle() != IntPtr.Zero; }
        public int LastCode { get; private set; }
        public string PbkPath { get { return Phonebook; } }
        public bool Stats(out long sent, out long received, out TimeSpan duration)
        {
            sent = received = -1; duration = TimeSpan.Zero;
            var h = Handle(); if (h == IntPtr.Zero) return false;
            var st = new int[15]; st[0] = 60;
            if (Ras.RasGetConnectionStatistics(h, st) != 0) return false;
            sent = (uint)st[1]; received = (uint)st[2]; duration = TimeSpan.FromMilliseconds((uint)st[14]); return true;
        }
        public string Dial(string host, Credentials c)
        {
            if (!Nord.ValidHost(host)) return "That isn't a Nord server name.";
            var p = new Ras.RASDIALPARAMS { dwSize = Ras.DialSize, szEntryName = VpnScript.Entry, szPhoneNumber = host, szCallbackNumber = "", szUserName = c.User, szPassword = c.Pass, szDomain = "" };
            IntPtr h = IntPtr.Zero; int err;
            var pbk = File.Exists(Phonebook) ? Phonebook : null;
            // Nord's login is EAP-MSCHAPv2 (Windows' legacy method). For it RasDial uses neither RASDIALPARAMS' user name / password
            // nor the entry's RAS credentials - Nord saw no login and said 691 while Windows' own dialog connected (1.2.5, real PC).
            // What Windows' dialog does: it passes the EAP login Windows saved for the entry in RASDIALEXTENSIONS.RasEapInfo. So: one
            // sign-in through Windows (WindowsSignIn, "Save" ticked), then every dial hands that saved blob to RasDial - checked on a
            // real PC: connected. The blob is Windows' own (we never parse it), copied to memory only for the call and wiped after.
            int blobSize; var blob = Ras.WindowsLoginBlob(pbk, out blobSize);
            try
            {
                if (blob != IntPtr.Zero)
                {
                    p.szUserName = ""; p.szPassword = "";
                    int extSize; var ext = Ras.Extensions(blob, blobSize, false, out extSize);
                    try { err = Ras.RasDial(ext, pbk, ref p, 0, IntPtr.Zero, out h); } finally { Marshal.FreeHGlobal(ext); }
                    if (err == 632 && IntPtr.Size == 8)   // size refused: the naturally aligned layout
                    {
                        if (h != IntPtr.Zero) { Ras.RasHangUp(h); WaitDown(h, 3000); h = IntPtr.Zero; }
                        ext = Ras.Extensions(blob, blobSize, true, out extSize);
                        try { err = Ras.RasDial(ext, pbk, ref p, 0, IntPtr.Zero, out h); } finally { Marshal.FreeHGlobal(ext); }
                    }
                }
                else err = Ras.RasDial(IntPtr.Zero, pbk, ref p, 0, IntPtr.Zero, out h);
            }
            finally { Ras.Wipe(blob, blobSize); }
            p.szPassword = null;
            if (err == 0) { LastCode = 0; return null; }
            LastCode = err;
            if (h != IntPtr.Zero) { Ras.RasHangUp(h); WaitDown(h, 3000); }
            if (err == 691 && !Ras.HasWindowsLogin(pbk)) return Ras.NeedsWindowsLogin;
            return Ras.Explain(err);
        }
        public string HangUp()
        {
            var h = Handle(); if (h == IntPtr.Zero) return null;
            var e = Ras.RasHangUp(h);
            if (!WaitDown(h, 6000)) return "Windows didn't close the connection (error " + e + ").";
            return null;
        }
        bool WaitDown(IntPtr h, int ms)
        {
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < ms) { int e; if (!Ras.Active(out e).Any(x => x.Value == h)) return true; Thread.Sleep(100); }
            return false;
        }
    }

    // tests / --snap: remembers calls; nothing real is created, dialled or closed
    public class FakeVpn : IVpn
    {
        public bool Profile, Up; public ProfileSpec Spec; public string Host;
        public string DialError, ProfileError; public int DialMs = 50; public string PbkPath { get; set; } public int LastCode { get; set; }
        public long Sent = 1843200, Received = 52428800; public DateTime UpAt;
        public bool Stats(out long sent, out long received, out TimeSpan duration) { sent = Sent; received = Received; duration = Up ? DateTime.Now - UpAt : TimeSpan.Zero; if (Up) { Sent += 20480; Received += 409600; } return Up; }
        public readonly List<string> Calls = new List<string>();
        void Call(string s) { lock (Calls) Calls.Add(s); }
        public bool ProfileExists() { return Profile; }
        public string ApplyProfile(ProfileSpec spec)
        {
            VpnScript.Profile(spec); Call("profile " + spec.Key); if (ProfileError != null) return ProfileError; Profile = true; Spec = spec;
            // like Add-/Set-VpnConnection: the entry appears in the (test) phonebook; Set-VpnConnection changes only the server
            if (PbkPath != null) { var t = Pbk.Read(PbkPath); File.WriteAllText(PbkPath, t == null || Pbk.Section(t, VpnScript.Entry) == null ? Pbk.SampleEntry(VpnScript.Entry, spec.Host) : System.Text.RegularExpressions.Regex.Replace(t, "PhoneNumber=.*", "PhoneNumber=" + spec.Host)); }
            return null;
        }
        public string Dial(string host, Credentials c)
        {
            Call("dial " + host + " as " + (c == null ? "nobody" : Credentials.Mask(c.User)));
            Thread.Sleep(DialMs);
            if (DialError != null) { var m = System.Text.RegularExpressions.Regex.Match(DialError, "error ([0-9]+)"); LastCode = m.Success ? int.Parse(m.Groups[1].Value) : 0; return DialError; }
            Up = true; Host = host; UpAt = DateTime.Now; LastCode = 0; return null;
        }
        public string HangUp() { Call("hangup"); Up = false; return null; }
        public bool IsUp() { return Up; }
        public string RemoveProfile() { Call("remove profile"); Up = false; Profile = false; if (PbkPath != null && File.Exists(PbkPath)) File.WriteAllText(PbkPath, ""); return null; }
        public void Drop() { Up = false; Call("(dropped)"); }   // tests: the connection drops by itself
    }
}
