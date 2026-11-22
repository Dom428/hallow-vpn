// Hallow VPN 1.1 - reading this PC's network (read-only): which interfaces are up, their DNS servers and metrics, whether there's
// global IPv6, which network you're on (Wi-Fi name and whether it's open, or wired) - and what Hallow VPN makes of it:
//   * the leak check (DNS servers in use go through the VPN? Nord says "protected"? IPv6 around the VPN?) -> ok / warn / fail
//   * untrusted networks for auto-connect (open Wi-Fi is always untrusted; wired is trusted unless you say otherwise)
// The Wi-Fi name comes from Windows' Native Wifi API (WlanQueryInterface, current connection) - never a profile, key or password.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;

namespace HallowVPN
{
    public class NetIf
    {
        public string Name, Description; public int Index; public bool Up, Vpn, Wireless, Wired, Tunnel;
        public int Metric = -1;                          // IPv4 interface metric (-1 = unknown)
        public List<IPAddress> Dns = new List<IPAddress>(), V4 = new List<IPAddress>(), V6Global = new List<IPAddress>();
        public bool Gateway, V6Gateway;
    }
    // which network this PC is on: wifi (Ssid, Open) / wired / none
    public class NetNow
    {
        public string Kind = "none", Ssid; public bool Open;
        public string Key { get { return Kind + "|" + (Ssid ?? ""); } }
    }
    public interface INetInfo
    {
        List<NetIf> Interfaces();
        int BestInterface(IPAddress dest);   // the interface index a packet to dest leaves through (-1 = unknown)
        NetNow Current();
    }

    public static class NetCheck
    {
        public static bool IsGlobalV6(IPAddress a)
        {
            if (a.AddressFamily != AddressFamily.InterNetworkV6) return false;
            var b = a.GetAddressBytes(); return (b[0] & 0xE0) == 0x20;   // 2000::/3
        }
        public static bool IsPrivateV4(IPAddress a)
        {
            if (a.AddressFamily != AddressFamily.InterNetwork) return false;
            var b = a.GetAddressBytes();
            return b[0] == 10 || b[0] == 127 || (b[0] == 172 && (b[1] & 0xF0) == 16) || (b[0] == 192 && b[1] == 168) || (b[0] == 169 && b[1] == 254) || (b[0] == 100 && (b[1] & 0xC0) == 64);
        }
        // global IPv6 on a real (non-VPN, non-tunnel) up interface with an IPv6 gateway: IPv6 traffic could go around the IKEv2 tunnel
        public static bool HasGlobalIpv6(IEnumerable<NetIf> ifs) { return ifs.Any(i => i.Up && !i.Vpn && !i.Tunnel && i.V6Global.Count > 0 && i.V6Gateway); }

        public class DnsReport { public bool VpnFirst, VpnHasDns, AllVpnDnsThroughVpn; public List<string> Outside = new List<string>(); public List<string> Vpn = new List<string>(); }
        // the DNS servers in use: Windows asks the interface with the lowest metric first. ok when that's the VPN and every VPN DNS server is
        // routed through the VPN; DNS servers of other interfaces that would be reached outside the VPN are listed (Windows may fall back to them)
        public static DnsReport Dns(List<NetIf> ifs, Func<IPAddress, int> best)
        {
            var r = new DnsReport();
            var vpn = ifs.FirstOrDefault(i => i.Vpn && i.Up);
            var withDns = ifs.Where(i => i.Up && i.Dns.Count > 0).OrderBy(i => i.Metric < 0 ? 9999 : i.Metric).ThenBy(i => i.Vpn ? 0 : 1).ToList();
            if (vpn == null) return r;
            r.VpnHasDns = vpn.Dns.Count > 0;
            r.VpnFirst = withDns.Count > 0 && withDns[0] == vpn;
            r.Vpn = vpn.Dns.Select(x => x.ToString()).ToList();
            r.AllVpnDnsThroughVpn = r.VpnHasDns && vpn.Dns.All(d => d.AddressFamily != AddressFamily.InterNetwork || best(d) == vpn.Index || best(d) < 0);
            foreach (var i in withDns.Where(x => x != vpn)) foreach (var d in i.Dns) { var b = best(d); if (b != vpn.Index) r.Outside.Add(d + " (" + i.Name + ")"); }
            return r;
        }

        // the leak check verdict. protectedByNord = Nord's IP check "protected" (null = couldn't ask)
        public static string Verdict(bool? protectedByNord, DnsReport dns, bool ipv6, bool killSwitch, bool splitOnly, List<string> notes)
        {
            notes.Clear();
            string v = "ok";
            if (protectedByNord == false) { notes.Add("Nord's IP check says your traffic doesn't come from a Nord server."); v = "fail"; }
            else if (protectedByNord == null) { notes.Add("Nord's IP check didn't answer, so the public address couldn't be checked."); v = "warn"; }
            if (splitOnly) notes.Add("Split tunnel \"Only these\": DNS uses your normal connection on purpose.");
            else if (!dns.VpnHasDns) { notes.Add("The VPN connection has no DNS servers - lookups go to your normal network."); if (v == "ok") v = "warn"; }
            else if (!dns.VpnFirst || !dns.AllVpnDnsThroughVpn) { notes.Add("Windows asks another network's DNS first - lookups may go around the VPN. Reconnect so Hallow VPN can set Nord's DNS."); if (v == "ok") v = "warn"; }
            if (!splitOnly && dns.Outside.Count > 0) notes.Add("Windows can still fall back to " + string.Join(", ", dns.Outside.Take(3)) + " if Nord's DNS is slow (that's Windows' smart multi-homed name resolution; turning it off is a Group Policy, which needs admin).");
            if (ipv6 && !killSwitch) { notes.Add("This PC has IPv6 internet. Nord's IKEv2 carries IPv4 only, so IPv6 traffic can go around the VPN. The kill switch blocks public IPv6."); if (v == "ok") v = "warn"; }
            return v;
        }

        // untrusted network? (open Wi-Fi always; other Wi-Fi unless its name is in the trusted list; wired only if you said so)
        public static bool Untrusted(NetNow n, IEnumerable<string> trusted, bool trustWired)
        {
            if (n == null || n.Kind == "none") return false;
            if (n.Kind == "wired") return !trustWired;
            if (n.Open) return true;
            return !(trusted ?? new string[0]).Any(t => string.Equals(t, n.Ssid, StringComparison.Ordinal));
        }
    }

    // ---- the real thing (read-only)
    public class RealNetInfo : INetInfo
    {
        [DllImport("iphlpapi.dll")] static extern int GetBestInterface(uint dest, out int index);
        [DllImport("iphlpapi.dll")] static extern int GetIpInterfaceEntry(IntPtr row);
        // MIB_IPINTERFACE_ROW: Family at 0, InterfaceIndex at 16, Metric at 148 (x64 and x86 alike - the struct has a ULONG64 so it's 8-aligned)
        public static int Metric(int index)
        {
            var p = Marshal.AllocHGlobal(256);
            try
            {
                for (int i = 0; i < 256; i++) Marshal.WriteByte(p, i, 0);
                Marshal.WriteInt16(p, 0, 2); Marshal.WriteInt32(p, 16, index);
                return GetIpInterfaceEntry(p) == 0 ? Marshal.ReadInt32(p, 148) : -1;
            }
            catch { return -1; }
            finally { Marshal.FreeHGlobal(p); }
        }
        public int BestInterface(IPAddress dest)
        {
            if (dest.AddressFamily != AddressFamily.InterNetwork) return -1;
            var b = dest.GetAddressBytes(); int idx;
            return GetBestInterface(BitConverter.ToUInt32(b, 0), out idx) == 0 ? idx : -1;
        }
        public List<NetIf> Interfaces()
        {
            var r = new List<NetIf>();
            foreach (var n in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (n.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                var i = new NetIf { Name = n.Name, Description = n.Description, Up = n.OperationalStatus == OperationalStatus.Up };
                i.Vpn = (n.Name == VpnScript.Entry && n.NetworkInterfaceType == NetworkInterfaceType.Ppp) || n.Name == NordLynx.Tunnel;
                i.Wireless = n.NetworkInterfaceType == NetworkInterfaceType.Wireless80211;
                i.Tunnel = n.NetworkInterfaceType == NetworkInterfaceType.Tunnel || n.NetworkInterfaceType == NetworkInterfaceType.Ppp;
                i.Wired = !i.Wireless && !i.Tunnel && (n.NetworkInterfaceType == NetworkInterfaceType.Ethernet || n.NetworkInterfaceType == NetworkInterfaceType.GigabitEthernet) && !Virtual(n.Description);
                try
                {
                    var p = n.GetIPProperties();
                    try { i.Index = p.GetIPv4Properties().Index; } catch { i.Index = -1; }
                    foreach (var d in p.DnsAddresses) if (d.AddressFamily == AddressFamily.InterNetwork || !d.IsIPv6SiteLocal) i.Dns.Add(d);
                    foreach (var u in p.UnicastAddresses) { if (u.Address.AddressFamily == AddressFamily.InterNetwork) i.V4.Add(u.Address); else if (NetCheck.IsGlobalV6(u.Address)) i.V6Global.Add(u.Address); }
                    foreach (var g in p.GatewayAddresses) { if (g.Address.AddressFamily == AddressFamily.InterNetwork && !g.Address.Equals(IPAddress.Any)) i.Gateway = true; if (g.Address.AddressFamily == AddressFamily.InterNetworkV6) i.V6Gateway = true; }
                }
                catch { }
                if (i.Index > 0) i.Metric = Metric(i.Index);
                r.Add(i);
            }
            return r;
        }
        static bool Virtual(string d) { d = (d ?? "").ToLowerInvariant(); return new[] { "virtual", "hyper-v", "vmware", "virtualbox", "tap-", "tailscale", "wireguard", "wintun", "loopback", "bluetooth" }.Any(d.Contains); }
        public NetNow Current()
        {
            var w = Wlan.Current();
            if (w != null) return w;
            try { if (Interfaces().Any(i => i.Up && i.Wired && i.Gateway)) return new NetNow { Kind = "wired" }; } catch { }
            return new NetNow();
        }
    }

    // Windows' Native Wifi API: the current connection's SSID and whether it has security. Read-only; no profiles, no keys.
    public static class Wlan
    {
        [DllImport("wlanapi.dll")] static extern int WlanOpenHandle(int ver, IntPtr reserved, out int negotiated, out IntPtr h);
        [DllImport("wlanapi.dll")] static extern int WlanCloseHandle(IntPtr h, IntPtr reserved);
        [DllImport("wlanapi.dll")] static extern int WlanEnumInterfaces(IntPtr h, IntPtr reserved, out IntPtr list);
        [DllImport("wlanapi.dll")] static extern int WlanQueryInterface(IntPtr h, ref Guid iface, int opcode, IntPtr reserved, out int size, out IntPtr data, IntPtr type);
        [DllImport("wlanapi.dll")] static extern void WlanFreeMemory(IntPtr p);
        // WLAN_CONNECTION_ATTRIBUTES offsets: isState 0, SSID length 520, SSID 524 (32 bytes), bSecurityEnabled 588
        public static NetNow Current()
        {
            IntPtr h = IntPtr.Zero, list = IntPtr.Zero;
            try
            {
                int neg; if (WlanOpenHandle(2, IntPtr.Zero, out neg, out h) != 0) return null;
                if (WlanEnumInterfaces(h, IntPtr.Zero, out list) != 0) return null;
                int n = Marshal.ReadInt32(list, 0);
                for (int i = 0; i < n; i++)
                {
                    var item = list + 8 + i * 532;   // WLAN_INTERFACE_INFO: GUID (16) + WCHAR[256] (512) + state (4)
                    var guid = (Guid)Marshal.PtrToStructure(item, typeof(Guid));
                    if (Marshal.ReadInt32(item, 528) != 1) continue;   // wlan_interface_state_connected
                    int size; IntPtr data;
                    if (WlanQueryInterface(h, ref guid, 7, IntPtr.Zero, out size, out data, IntPtr.Zero) != 0) continue;   // wlan_intf_opcode_current_connection
                    try
                    {
                        if (size < 604) continue;
                        int len = Math.Min(32, Marshal.ReadInt32(data, 520));
                        var b = new byte[len]; Marshal.Copy(data + 524, b, 0, len);
                        return new NetNow { Kind = "wifi", Ssid = Encoding.UTF8.GetString(b), Open = Marshal.ReadInt32(data, 588) == 0 };
                    }
                    finally { WlanFreeMemory(data); }
                }
                return null;
            }
            catch { return null; }
            finally { if (list != IntPtr.Zero) WlanFreeMemory(list); if (h != IntPtr.Zero) WlanCloseHandle(h, IntPtr.Zero); }
        }
    }

    // tests / --snap: a made-up PC
    public class FakeNetInfo : INetInfo
    {
        public List<NetIf> Ifs = new List<NetIf>(); public NetNow Now = new NetNow { Kind = "wifi", Ssid = "Home-5G" }; public bool VpnUp;
        public FakeNetInfo(bool ipv6 = false) { Set(ipv6); }
        public void Set(bool ipv6)
        {
            var wifi = new NetIf { Name = "Wi-Fi", Index = 12, Up = true, Wireless = true, Metric = 35, Gateway = true, V4 = { IPAddress.Parse("192.168.1.23") }, Dns = { IPAddress.Parse("192.168.1.1") } };
            if (ipv6) { wifi.V6Global.Add(IPAddress.Parse("2001:db8:5::23")); wifi.V6Gateway = true; }
            Ifs = new List<NetIf> { wifi };
        }
        public bool VpnDnsGood = true;
        public List<NetIf> Interfaces()
        {
            var l = Ifs.ToList();
            if (VpnUp) l.Add(new NetIf { Name = VpnScript.Entry, Index = 40, Up = true, Vpn = true, Tunnel = true, Metric = VpnDnsGood ? 1 : 50, V4 = { IPAddress.Parse("10.8.3.7") }, Dns = { IPAddress.Parse(Pbk.NordDns1), IPAddress.Parse(Pbk.NordDns2) } });
            return l;
        }
        public int BestInterface(IPAddress dest) { if (NetCheck.IsPrivateV4(dest)) return 12; return VpnUp ? 40 : 12; }
        public NetNow Current() { return Now; }
    }
}
