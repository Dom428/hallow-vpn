// Hallow VPN 1.2.6 - what Hallow Remote and Hallow Hub need to show the right buttons: status.json always says which protocol the
// next connect uses ("nextProtocol") and whether a connect / disconnect makes Windows ask (UAC, "needsUac"); the remembered target
// ("target") and whether Windows has the saved Nord login ("windowsLogin"). The pipe's "connect <target> --keep-target" connects
// once without changing what "Connect" means. Fields are only ever added - "protocol" keeps its old meaning.
using System;
using System.Collections.Generic;
using System.Linq;

namespace HallowVPN
{
    public partial class Controller
    {
        // NordLynx up / down asks Windows (UAC); IKEv2 never does. Connecting while NordLynx is up also takes it down first.
        public static Dictionary<string, object> UacNeeds(Phase phase, string proto, string connecting, string next)
        {
            bool wgNow = phase == Phase.On ? proto == ProtoWg : phase == Phase.Off ? false : (connecting == ProtoWg || proto == ProtoWg);
            return new Dictionary<string, object> { { "connect", next == ProtoWg || wgNow }, { "disconnect", wgNow } };
        }
        public Dictionary<string, object> NeedsUac() { return UacNeeds(Phase, Proto, connectingProto, ProtoFor(false)); }
        public bool WindowsLogin()
        {
            if (SnapMode) return S.SetupDone;
            try { return Vpn.PbkPath != null && Ras.HasWindowsLogin(Vpn.PbkPath); } catch { return false; }
        }
        void Status126(Dictionary<string, object> d)
        {
            d["nextProtocol"] = ProtoFor(false);
            d["needsUac"] = NeedsUac();
            d["target"] = S.LastTarget;
            d["windowsLogin"] = WindowsLogin();
        }
        // "fastest --keep-target" -> "fastest", keep = true ("--once" means the same)
        public static string KeepTarget(string arg, out bool keep)
        {
            var parts = (arg ?? "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).ToList();
            int n = parts.RemoveAll(p => string.Equals(p, "--keep-target", StringComparison.OrdinalIgnoreCase) || string.Equals(p, "--once", StringComparison.OrdinalIgnoreCase));
            keep = n > 0; return string.Join(" ", parts);
        }
        // the window asks for a fresh look (e.g. after Windows' sign-in box closed)
        public void Poke() { Fire(); }
    }
}
