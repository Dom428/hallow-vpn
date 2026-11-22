// Hallow VPN settings: %APPDATA%\HallowVPN\settings.json. Nothing secret is kept here - the Nord service credentials are in
// account.dat, encrypted with Windows DPAPI for this Windows user (Account.cs).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Threading;

namespace HallowVPN
{
    [DataContract]
    public class Favourite
    {
        [DataMember] public string Host;      // us1234.nordvpn.com
        [DataMember] public string Name;      // United States #1234
        [DataMember] public string Country;   // United States
        [DataMember] public string Code;      // US
        [DataMember] public string City;
    }

    [DataContract]
    public class VpnSettings
    {
        // developer tests: HALLOW_VPN_TESTDIR = data folder (and a separate pipe); HALLOW_VPN_FAKE=1 = made-up servers, a fake VPN layer,
        // fake firewall, fake Tailscale and fake games (nothing real is created, dialled or changed); HALLOW_VPN_HEADLESS=1 = no windows or tray.
        public static readonly string TestDir = Env("HALLOW_VPN_TESTDIR");
        public static readonly bool Fake = TestDir != null && Env("HALLOW_VPN_FAKE") == "1";
        public static readonly bool Headless = TestDir != null && Env("HALLOW_VPN_HEADLESS") == "1";
        public static bool Frozen;   // --snap: never saved
        static string Env(string n) { var v = Environment.GetEnvironmentVariable(n); return string.IsNullOrWhiteSpace(v) ? null : v; }
        public static string Dir { get { return TestDir ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HallowVPN"); } }
        static string FilePath { get { return Path.Combine(Dir, "settings.json"); } }

        [DataMember] public bool UseHallowIcon = true;
        [DataMember] public bool UseHallowAccent = true;
        [DataMember] public string Look = "follow";            // follow / light / dark
        [DataMember] public bool AutoReconnect = true;
        [DataMember] public bool ConnectOnStart;               // connect when Hallow VPN starts (off by default)
        [DataMember] public string LastTarget = "fastest";     // what "Connect" connects to: fastest / gaming / country:<CODE> / server:<host>
        [DataMember] public List<Favourite> Favourites;
        [DataMember] public string SplitMode = Split.All;      // all / only / except-games
        [DataMember] public List<string> SplitRoutes;          // "Only these go through the VPN": addresses / ranges (CIDR)
        [DataMember] public string GameMode = Games.ModeOff;   // off / pause / lowping
        [DataMember] public List<string> ExtraGames;           // more exe names treated as games
        [DataMember] public bool HomeAllowLan = true;          // Tailscale exit node: keep the LAN reachable
        [DataMember] public string ProfileSpec;                // what the Windows VPN profile was last set up with (to know when to update it)
        [DataMember] public string LastUpdateCheck;
        [DataMember] public string LastCrashShown;
        [DataMember] public bool TrayHintShown;
        // 1.1
        [DataMember] public bool SetupDone;                    // the first successful connect happened (the setup screen stops opening)
        [DataMember] public bool AutoConnectUntrusted;         // connect by itself on Wi-Fi that isn't trusted (open Wi-Fi is never trusted)
        [DataMember] public bool TrustWired = true;            // wired connections count as trusted
        [DataMember] public List<string> TrustedNetworks;      // Wi-Fi names (SSIDs) only - never keys or passwords
        [DataMember] public int LastDialCode;                  // Windows' error number of the last failed connect (for "Fix my connection")
        [DataMember] public string LastDialWhen;
        [DataMember] public string LastServer;                 // the last server connected to (for the checks and recreating the profile)
        // 1.2
        [DataMember] public string Protocol = Protocols.Auto;   // auto (NordLynx when set up, else IKEv2) / nordlynx / ikev2

        [OnDeserializing] void Defaults(StreamingContext c) { UseHallowIcon = true; UseHallowAccent = true; Look = "follow"; AutoReconnect = true; LastTarget = "fastest"; SplitMode = Split.All; GameMode = Games.ModeOff; HomeAllowLan = true; TrustWired = true; Protocol = Protocols.Auto; }

        public static VpnSettings Load()
        {
            try
            {
                if (File.Exists(FilePath))
                    using (var f = File.OpenRead(FilePath))
                    {
                        var s = (VpnSettings)new DataContractJsonSerializer(typeof(VpnSettings)).ReadObject(f);
                        if (s != null) { s.Fix(); return s; }
                    }
            }
            catch { }
            var d = new VpnSettings(); d.Fix(); return d;
        }
        public void Fix()
        {
            if (Favourites == null) Favourites = new List<Favourite>();
            Favourites.RemoveAll(f => f == null || !Nord.ValidHost(f.Host));
            if (SplitRoutes == null) SplitRoutes = new List<string>();
            SplitRoutes = SplitRoutes.Select(Split.Normalize).Where(x => x != null).Distinct().Take(Split.MaxRoutes).ToList();
            if (ExtraGames == null) ExtraGames = new List<string>();
            ExtraGames.RemoveAll(string.IsNullOrWhiteSpace);
            if (TrustedNetworks == null) TrustedNetworks = new List<string>();
            TrustedNetworks = TrustedNetworks.Where(x => !string.IsNullOrEmpty(x) && x.Length <= 32).Distinct().Take(50).ToList();
            if (LastServer != null && !Nord.ValidHost(LastServer)) LastServer = null;
            Protocol = Protocols.Fix(Protocol);
            if (!Split.Modes.Contains(SplitMode)) SplitMode = Split.All;
            if (GameMode != Games.ModePause && GameMode != Games.ModeLowPing) GameMode = Games.ModeOff;
            if (Target.Parse(LastTarget) == null) LastTarget = "fastest";
            Look = Mode.Own(Look);
        }

        static readonly object saveLock = new object();
        public void Save()
        {
            if (Frozen) return;
            lock (saveLock)
            {
                try
                {
                    Directory.CreateDirectory(Dir);
                    var tmp = FilePath + ".tmp";
                    using (var f = File.Create(tmp)) new DataContractJsonSerializer(typeof(VpnSettings)).WriteObject(f, this);
                    for (int i = 0; ; i++) { try { if (File.Exists(FilePath)) File.Replace(tmp, FilePath, null); else File.Move(tmp, FilePath); break; } catch { if (i >= 5) throw; Thread.Sleep(50); } }
                }
                catch { }
            }
        }
    }
}
