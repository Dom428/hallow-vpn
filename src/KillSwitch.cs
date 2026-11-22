// Hallow VPN - the kill switch and NordVPN's root certificate: the two things that need admin. Both are buttons with an explanation;
// Windows asks (UAC) and the user clicks. Hallow VPN then runs ITSELF elevated with one fixed command ("--elevated killswitch-on",
// "killswitch-off", "ca-install", "ca-remove") - never anything typed or downloaded as a command. Never in a test folder.
//
// The kill switch = Windows Firewall block rules ("Hallow VPN kill switch - ..."), outbound, on the Wi-Fi and wired connections only
// (interface types wireless / lan - the VPN's own connection is "ras" and isn't blocked), to public internet addresses only (your home
// network, Tailscale's 100.64.0.0/10 and link-local stay reachable), for TCP, UDP and ICMP - EXCEPT:
//   * UDP 500 and 4500: IKEv2 to the VPN server (any server, so you can switch servers without another UAC prompt)
//   * port 53 (DNS): so Windows can look up Nord's server names before the VPN is up (lookups only, not your traffic)
// Limits (said in the app): Windows Firewall block rules always win over allow rules, so it is done by blocking everything else
// rather than "allow only the VPN"; other IP protocols (not TCP / UDP / ICMP) aren't blocked; it only works while Windows Firewall
// is on; and while it's installed, disconnecting the VPN means no internet - that's the point.
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace HallowVPN
{
    public class FwRule
    {
        public string Name, Protocol, InterfaceType, RemoteIp, RemotePort;
        // netsh advfirewall firewall add rule ...
        public string AddArgs()
        {
            var sb = new StringBuilder("advfirewall firewall add rule name=\"" + Name + "\" dir=out action=block enable=yes profile=any interfacetype=" + InterfaceType + " protocol=" + Protocol);
            if (RemotePort != null) sb.Append(" remoteport=" + RemotePort);
            sb.Append(" remoteip=" + RemoteIp + " description=\"" + KillSwitch.Description + "\"");
            return sb.ToString();
        }
        public string DeleteArgs() { return "advfirewall firewall delete rule name=\"" + Name + "\""; }
    }

    public static class KillSwitch
    {
        public const string Prefix = "Hallow VPN kill switch", Description = "Hallow VPN kill switch: blocks internet traffic outside the VPN. Remove it in Hallow VPN (Kill switch page).";
        // public IPv4 space without: 0/8, 10/8, 100.64/10 (CGNAT / Tailscale), 127/8, 169.254/16, 172.16/12, 192.168/16, 224+ (multicast / broadcast)
        public static readonly string PublicV4 = "1.0.0.0-9.255.255.255,11.0.0.0-100.63.255.255,100.128.0.0-126.255.255.255,128.0.0.0-169.253.255.255,169.255.0.0-172.15.255.255,172.32.0.0-192.167.255.255,192.169.0.0-223.255.255.255";
        // IPv6 global unicast (2000::/3) - link-local fe80::/10, unique-local fc00::/7 (Tailscale's fd7a::) and multicast stay open
        public static readonly string PublicV6 = "2000::-3fff:ffff:ffff:ffff:ffff:ffff:ffff:ffff";
        public const string TcpPorts = "1-52,54-65535", UdpPorts = "1-52,54-499,501-4499,4501-65535";

        public static List<FwRule> Rules()
        {
            var r = new List<FwRule>();
            foreach (var it in new[] { new[] { "wireless", "Wi-Fi" }, new[] { "lan", "wired" } })
            {
                Func<string, string, string, string, FwRule> rule = (what, proto, ip, port) => new FwRule { Name = Prefix + " - " + it[1] + " " + what, Protocol = proto, InterfaceType = it[0], RemoteIp = ip, RemotePort = port };
                r.Add(rule("TCP IPv4", "TCP", PublicV4, TcpPorts)); r.Add(rule("TCP IPv6", "TCP", PublicV6, TcpPorts));
                r.Add(rule("UDP IPv4", "UDP", PublicV4, UdpPorts)); r.Add(rule("UDP IPv6", "UDP", PublicV6, UdpPorts));
                r.Add(rule("ICMP IPv4", "icmpv4", PublicV4, null)); r.Add(rule("ICMP IPv6", "icmpv6", PublicV6, null));
            }
            return r;
        }
        public static readonly string[] Limits = {
            "It works by connection type (Wi-Fi / wired) and address, not by program - Windows Firewall rules can't tell which app sends what through the VPN.",
            "IKEv2 to any VPN server (UDP 500 and 4500) and DNS lookups (port 53) still go out, so Windows can find and reach the server.",
            "Your home network, Tailscale addresses and link-local addresses stay reachable.",
            "Only TCP, UDP and ICMP are blocked; rarer IP protocols are not.",
            "It only works while Windows Firewall is on.",
            "While it's installed, no VPN = no internet - also for games. So \"disconnect while gaming\" switches to a low-ping server instead, and Home mode (Tailscale) needs it removed first." };

        // ---- elevated helper (runs inside "HallowVPN.exe --elevated <cmd>" after the user accepted UAC)
        static int Netsh(string args, StringBuilder log)
        {
            var psi = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "netsh.exe"), args) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            using (var p = Process.Start(psi)) { var o = p.StandardOutput.ReadToEnd(); p.WaitForExit(30000); log.AppendLine("netsh " + args.Split(' ').Take(5).Aggregate((a, b) => a + " " + b) + " ... -> " + p.ExitCode); return p.ExitCode; }
        }
        public static int ElevatedOn(StringBuilder log)
        {
            ElevatedOff(log);
            foreach (var r in Rules()) if (Netsh(r.AddArgs(), log) != 0) { log.AppendLine("failed: " + r.Name); ElevatedOff(log); return 2; }
            return 0;
        }
        public static int ElevatedOff(StringBuilder log)
        {
            foreach (var r in Rules()) Netsh(r.DeleteArgs(), log);   // "no rules match" is fine
            return 0;
        }
    }

    public interface IFirewall
    {
        int InstalledRules();          // how many of the kill switch rules exist (read-only)
        bool? FirewallOn();            // all three profiles on? null = couldn't tell (read-only)
        string Install();              // UAC -> rules added; null = done
        string Remove();               // UAC -> rules removed; null = done
    }
    public class FakeFirewall : IFirewall
    {
        public readonly List<string> Calls = new List<string>(); public bool Cancel; public bool? On = true; public HashSet<string> Rules = new HashSet<string>();
        public int InstalledRules() { return Rules.Count; }
        public bool? FirewallOn() { return On; }
        public string Install() { Calls.Add("install"); if (Cancel) return Elevated.Cancelled; foreach (var r in KillSwitch.Rules()) { r.AddArgs(); Rules.Add(r.Name); } return null; }
        public string Remove() { Calls.Add("remove"); if (Cancel) return Elevated.Cancelled; Rules.Clear(); return null; }
    }
    public class WindowsFirewall : IFirewall
    {
        static string Run(string args, out int code)
        {
            var psi = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "netsh.exe"), args) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            using (var p = Process.Start(psi)) { var o = p.StandardOutput.ReadToEnd(); p.WaitForExit(15000); code = p.ExitCode; return o; }
        }
        public int InstalledRules()
        {
            try { int code; var o = Run("advfirewall firewall show rule name=all dir=out", out code); return KillSwitch.Rules().Count(r => o.IndexOf(r.Name, StringComparison.Ordinal) >= 0); }
            catch { return 0; }
        }
        public bool? FirewallOn()
        {
            try
            {
                int code; var o = Run("advfirewall show allprofiles state", out code);
                var states = System.Text.RegularExpressions.Regex.Matches(o, "^State\\s+(\\S+)", System.Text.RegularExpressions.RegexOptions.Multiline).Cast<System.Text.RegularExpressions.Match>().Select(m => m.Groups[1].Value.ToUpperInvariant()).ToList();
                if (states.Count == 0) return null;
                return states.All(s => s == "ON");
            }
            catch { return null; }
        }
        public string Install() { return Elevated.Run("killswitch-on"); }
        public string Remove() { return Elevated.Run("killswitch-off"); }
    }

    // ---- NordVPN's root certificate ("NordVPN Root CA"): Windows needs it in the computer's Trusted Root store to accept Nord's
    // IKEv2 server certificates. Hallow VPN checks for it (read-only); installing it is a button: it downloads Nord's own file
    // (downloads.nordcdn.com) only when clicked, checks it IS NordVPN's self-signed root, shows its fingerprint, then UAC.
    public interface ICa
    {
        bool Installed();
        string Fetch(out string thumbprint);   // downloads + checks Nord's root certificate into the data folder; null = fine
        string Install();                      // UAC -> into the computer's Trusted Root store
        string Remove();                       // UAC -> out again
    }
    public static class NordCa
    {
        public const string Subject = "CN=NordVPN Root CA", Url = "https://downloads.nordcdn.com/certificates/root.der";
        public static string FilePath { get { return Path.Combine(VpnSettings.Dir, "nordvpn-root.der"); } }
        // null = it's NordVPN's root certificate (self-signed, a CA, still valid); else why not
        public static string Check(X509Certificate2 c, DateTime now)
        {
            if (c == null) return "That isn't a certificate.";
            if (!c.Subject.StartsWith(Subject, StringComparison.OrdinalIgnoreCase) || c.Subject != c.Issuer) return "That isn't NordVPN's root certificate (it's \"" + c.Subject + "\").";
            if (c.HasPrivateKey) return "That certificate carries a private key - not installing it.";
            if (now > c.NotAfter || now < c.NotBefore) return "That certificate isn't valid right now.";
            var bc = c.Extensions.OfType<X509BasicConstraintsExtension>().FirstOrDefault();
            if (bc == null || !bc.CertificateAuthority) return "That certificate isn't a certificate authority.";
            return null;
        }
        public static bool InStore(StoreLocation where)
        {
            try
            {
                using (var s = new X509Store(StoreName.Root, where))
                {
                    s.Open(OpenFlags.ReadOnly | OpenFlags.OpenExistingOnly);
                    return s.Certificates.Cast<X509Certificate2>().Any(c => c.Subject.StartsWith(Subject, StringComparison.OrdinalIgnoreCase) && c.Subject == c.Issuer);
                }
            }
            catch { return false; }
        }
        public static int ElevatedInstall(StringBuilder log)
        {
            var c = new X509Certificate2(File.ReadAllBytes(FilePath));
            var why = Check(c, DateTime.Now); if (why != null) { log.AppendLine(why); return 3; }
            using (var s = new X509Store(StoreName.Root, StoreLocation.LocalMachine)) { s.Open(OpenFlags.ReadWrite); s.Add(c); }
            log.AppendLine("added " + c.Thumbprint); return 0;
        }
        public static int ElevatedRemove(StringBuilder log)
        {
            using (var s = new X509Store(StoreName.Root, StoreLocation.LocalMachine))
            {
                s.Open(OpenFlags.ReadWrite);
                foreach (var c in s.Certificates.Cast<X509Certificate2>().Where(c => c.Subject.StartsWith(Subject, StringComparison.OrdinalIgnoreCase) && c.Subject == c.Issuer).ToList()) { s.Remove(c); log.AppendLine("removed " + c.Thumbprint); }
            }
            return 0;
        }
    }
    public class WindowsCa : ICa
    {
        public bool Installed() { return NordCa.InStore(StoreLocation.LocalMachine); }
        public string Fetch(out string thumbprint)
        {
            thumbprint = null;
            try
            {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                byte[] data; using (var wc = new WebClient()) { wc.Headers[HttpRequestHeader.UserAgent] = "HallowVPN/" + Program.Version; data = wc.DownloadData(NordCa.Url); }
                if (data.Length > 16384) return "Nord's file is bigger than a certificate - not using it.";
                var c = new X509Certificate2(data);
                var why = NordCa.Check(c, DateTime.Now); if (why != null) return why;
                Directory.CreateDirectory(VpnSettings.Dir); File.WriteAllBytes(NordCa.FilePath, c.RawData);
                thumbprint = c.Thumbprint; return null;
            }
            catch (Exception e) { return "Couldn't get Nord's certificate from downloads.nordcdn.com: " + e.Message; }
        }
        public string Install() { return Elevated.Run("ca-install"); }
        public string Remove() { return Elevated.Run("ca-remove"); }
    }
    public class FakeCa : ICa
    {
        public bool In; public string FetchError; public bool Cancel; public readonly List<string> Calls = new List<string>();
        public bool Installed() { return In; }
        public string Fetch(out string thumbprint) { Calls.Add("fetch"); thumbprint = FetchError == null ? "8B1F9F2C0B7E7C5A1F3D0E6B9A2C4D5E6F708192" : null; return FetchError; }
        public string Install() { Calls.Add("install"); if (Cancel) return Elevated.Cancelled; In = true; return null; }
        public string Remove() { Calls.Add("remove"); if (Cancel) return Elevated.Cancelled; In = false; return null; }
    }

    // runs this exe elevated with one fixed command; Windows shows UAC and the user decides
    public static class Elevated
    {
        public const string Cancelled = "Windows didn't get permission (UAC was cancelled), so nothing changed.";
        public static readonly string[] Commands = { "killswitch-on", "killswitch-off", "ca-install", "ca-remove" };
        public static string Run(string cmd)
        {
            if (!Commands.Contains(cmd)) return "unknown elevated command";
            if (VpnSettings.TestDir != null || Snap.On) return "Not in a test copy.";
            try
            {
                var psi = new ProcessStartInfo(Assembly.GetExecutingAssembly().Location, "--elevated " + cmd) { UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden };
                using (var p = Process.Start(psi))
                {
                    if (!p.WaitForExit(120000)) return "Windows took too long.";
                    return p.ExitCode == 0 ? null : "It didn't work (code " + p.ExitCode + "). Details: " + Path.Combine(VpnSettings.Dir, "elevated.log");
                }
            }
            catch (Win32Exception e) { return e.NativeErrorCode == 1223 ? Cancelled : "Couldn't ask Windows for permission: " + e.Message; }
            catch (Exception e) { return "Couldn't ask Windows for permission: " + e.Message; }
        }
        // "HallowVPN.exe --elevated <cmd>": only the four fixed commands, never in a test folder; a short log in the data folder
        public static int RunHere(string cmd)
        {
            if (VpnSettings.TestDir != null || !Commands.Contains(cmd)) return 5;
            var log = new StringBuilder(DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss") + " " + cmd + Environment.NewLine); int code;
            try
            {
                switch (cmd)
                {
                    case "killswitch-on": code = KillSwitch.ElevatedOn(log); break;
                    case "killswitch-off": code = KillSwitch.ElevatedOff(log); break;
                    case "ca-install": code = NordCa.ElevatedInstall(log); break;
                    default: code = NordCa.ElevatedRemove(log); break;
                }
            }
            catch (Exception e) { log.AppendLine("error: " + e.Message); code = 4; }
            try { Directory.CreateDirectory(VpnSettings.Dir); File.WriteAllText(Path.Combine(VpnSettings.Dir, "elevated.log"), log.ToString()); } catch { }
            return code;
        }
    }
}
