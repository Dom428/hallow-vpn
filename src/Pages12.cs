// Hallow VPN 1.2 page: NordLynx - the protocol choice, WireGuard for Windows (install on a click), the access token, and what asks Windows.
using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace HallowVPN
{
    public partial class MainWindow
    {
        FrameworkElement NordLynxPage()
        {
            var sp = PageBody("NordLynx", "Nord's WireGuard protocol, through the official WireGuard for Windows - usually faster than IKEv2, and it connects by address.");
            sp.Children.Add(Section("Protocol"));
            Action<string> set = p => { c.SetProtocol(p); BuildPage(); };
            var pr = c.S.Protocol;
            sp.Children.Add(Choice("Automatic", "NordLynx when it's set up below, otherwise IKEv2. Things Hallow VPN does by itself (reconnecting, untrusted Wi-Fi, after a game) use IKEv2 when it's ready, so Windows never asks out of nowhere.", pr == Protocols.Auto, () => set(Protocols.Auto)));
            sp.Children.Add(Choice("NordLynx", "Always WireGuard. Each connect and disconnect asks Windows (UAC). While a game runs, Hallow VPN never starts it by itself - it waits for the game to close.", pr == Protocols.NordLynx, () => set(Protocols.NordLynx)));
            sp.Children.Add(Choice("IKEv2", "Always Windows' own IKEv2 client - no admin for connecting, game pausing and switching work fully.", pr == Protocols.Ikev2, () => set(Protocols.Ikev2)));
            sp.Children.Add(Ui.Muted("Next connect uses: " + c.ProtoFor(false) + (c.Phase == Phase.On ? "   ·   connected now with " + c.Proto : ""), 12, 6));

            sp.Children.Add(Section("Set up NordLynx"));
            // 1 - WireGuard for Windows
            if (c.WgInstalled) sp.Children.Add(Step("ok", "1  WireGuard for Windows", "Installed (" + (Snap.On ? @"C:\Program Files\WireGuard" : System.IO.Path.GetDirectoryName(WgSetup.Exe ?? @"C:\Program Files\WireGuard\wireguard.exe")) + ")."));
            else
            {
                var b = Ui.Btn(c.WgBusy ? "Getting WireGuard…" : "Install WireGuard (asks Windows)", () =>
                {
                    if (!Dialogs.Confirm(this, "Install WireGuard for Windows?", "Hallow VPN downloads the official installer from download.wireguard.com, checks that it matches the hash WireGuard publishes and that it's signed by WireGuard LLC, then runs it. Windows will ask for permission (UAC) - that click is yours. WireGuard installs its own service and adapter driver.", "Download and install", false)) return;
                    Say("Downloading WireGuard from download.wireguard.com...");
                    c.InstallWireGuardAsync(m => Say(m), err => Say(err ?? "WireGuard for Windows is installed."));
                }, true);
                b.IsEnabled = !c.WgBusy; b.HorizontalAlignment = HorizontalAlignment.Left;
                sp.Children.Add(Step("fail", "1  WireGuard for Windows", "Not installed. NordLynx uses the official WireGuard app's tunnel service (Hallow VPN installs no driver itself). Only when you click: Hallow VPN downloads its installer, checks it twice (WireGuard's published hash and the WireGuard LLC signature) and runs it.", b));
            }
            // 2 - the access token -> the NordLynx key
            if (NordLynx.Exists || (Snap.On && c.WgInstalled))
                sp.Children.Add(Step("ok", "2  Your NordLynx key", "Saved, encrypted for your Windows user (Windows DPAPI). Never logged or shown.", Wrap(Ui.Danger("Remove", () => { if (Dialogs.Confirm(this, "Remove the NordLynx key?", "Hallow VPN forgets the access token and the key. Your Nord account isn't affected.", "Remove", true)) c.RemoveAccessToken(); }))));
            else
            {
                var tok = new PasswordBox { Width = 420, HorizontalAlignment = HorizontalAlignment.Left, Background = Ui.R("Well"), Foreground = Ui.R("Text"), BorderBrush = Ui.R("Line"), CaretBrush = Ui.R("AccentBrush"), Padding = new Thickness(8, 6, 8, 6), FontFamily = Ui.Mono, FontSize = 12.5 };
                var save = Ui.Btn("Get my NordLynx key", () =>
                {
                    var t = tok.Password; tok.Clear(); Say("Asking Nord for your NordLynx key...");
                    c.SaveAccessToken(t, err => { Say(err ?? "Your NordLynx key is saved, encrypted for your Windows user."); if (err != null) Dialogs.Info(this, "Not saved", err); BuildPage(); });
                }, true);
                save.Margin = new Thickness(0, 10, 0, 0); save.HorizontalAlignment = HorizontalAlignment.Left;
                sp.Children.Add(Step("fail", "2  Your NordLynx key",
                    "Paste a Nord ACCESS TOKEN: on nordaccount.com go to NordVPN > \"Set up NordVPN manually\" > Generate new token. Hallow VPN asks Nord once (api.nordvpn.com) for your NordLynx private key and keeps both encrypted for your Windows user.",
                    Ui.Muted("Access token", 12, 4), tok, save));
            }
            bool ready = c.NordLynxReady;
            sp.Children.Add(Step(ready ? "ok" : "pending", "3  Connect", ready ? "Ready. Pick NordLynx or Automatic above and connect on the Connection page - Windows asks (UAC) each time." : "Do steps 1 and 2 first.", ready ? (UIElement)Wrap(Go("connect", "Open the Connection page")) : new Border()));

            sp.Children.Add(Section("What asks Windows, and why"));
            sp.Children.Add(Bullets(new[] {
                "Installing WireGuard for Windows: once (its installer).",
                "Every NordLynx connect and disconnect: WireGuard runs each tunnel as a Windows service, and adding or removing a service needs admin. Hallow VPN then runs itself elevated with one fixed command; that copy - still you - reads your key, writes the tunnel's settings into a folder only you, SYSTEM and Administrators can open, starts the tunnel and deletes the settings file right away.",
                "So Hallow VPN never connects, switches or pauses NordLynx by itself while a game runs, and with Automatic it uses IKEv2 for things it does by itself.",
                "WireGuard's own no-admin option: an administrator can set HKLM\\Software\\WireGuard\\LimitedOperatorUI = 1 and add you to Windows' \"Network Configuration Operators\" group; then WireGuard's OWN window lets you switch its tunnels without admin. That's a system change and it doesn't cover other apps starting tunnels, so Hallow VPN doesn't do it." }));
            sp.Children.Add(Section("Good to know"));
            sp.Children.Add(Bullets(new[] {
                "Settings Nord uses for NordLynx: address 10.5.0.2, DNS 103.86.96.100 and 103.86.99.100, the server's own public key from Nord's server list, port 51820. Everything goes through it (IPv4 and IPv6 - Nord has no IPv6, so IPv6 just stops).",
                "Split tunnel \"Only these\" applies to IKEv2 only; with Automatic, IKEv2 is used then.",
                "Hallow Torrents finds the tunnel by its name (" + NordLynx.Tunnel + "); status.json says protocol \"NordLynx\" and adapter \"" + NordLynx.Tunnel + "\".",
                "The kill switch from Hallow VPN 1.0 blocks NordLynx - upgrade it on the Kill switch page." }));
            return Scroll(sp);
        }
    }
}
