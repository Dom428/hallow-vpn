# Hallow VPN

**A game-aware VPN client for your NordVPN subscription - on Windows' own IKEv2 VPN client.**

Part of the Hallow family of Windows apps. Hallow VPN uses the NordVPN subscription you already have, through the VPN client
built into Windows: **no NordVPN app, no drivers, nothing else to download, no admin** for everyday use. It knows when you're
playing: it can pause the VPN for your game and reconnect after, or move you to a low-ping server.

![Connected (made-up data)](docs/connect.png)

## What it does

- **Account**: paste your Nord **service credentials** (Nord Account > NordVPN > *Set up NordVPN manually* > Service credentials).
  They are **not your Nord login** - Hallow VPN refuses an email address and says where to find the right ones. They're stored in
  `%APPDATA%\HallowVPN\account.dat`, encrypted with **Windows DPAPI for your Windows user** (CurrentUser), and never logged, never
  in status or the family pipe, never in crash reports and never on a command line.
- **Servers** from Nord's **public server API** (no key): `v1/servers/recommendations` with the IKEv2 filter, per country with
  `filters[country_id]`, and the country list. Cached in `servers.json` and refreshed **at most every 3 hours** (a manual Refresh at
  most every 5 minutes). Each server shows its country, city, **load** and a **ping measured by this PC** (ICMP echo, or the time a
  TCP connection to port 443 takes when ICMP gets no answer). Choices: **Fastest** (ping + 0.8 x load), **by country**,
  **favourites** (star any server) and **Best for gaming** (nearby and quiet: under 40 % load, then the lowest ping).

  ![Best for gaming (made-up data)](docs/servers-gaming.png)
- **Connect** with Windows' own IKEv2 client: one **per-user** VPN connection called "Hallow VPN" (PowerShell `Add-VpnConnection`
  / `Set-VpnConnection` **without** `-AllUserConnection`, so no admin): IKEv2, EAP (EAP-MSCHAPv2), encryption required, credentials
  **not** remembered by Windows. It's dialled with `RasDial` **inside Hallow VPN** with the service credentials, so the password is
  never on a command line (no `rasdial <user> <pass>`). The Connection page shows the server, protocol, **connected time** and your
  **public IP** - fetched from Nord's own IP check (`api.nordvpn.com/v1/helpers/ips/insights`) **only while connected**.
  **Auto-reconnect** (3 s, 10 s, 30 s ... up to 5 minutes) when the connection drops.
- **NordVPN's root certificate**: Windows' IKEv2 client only accepts servers whose certificate it trusts, and Nord's servers use
  NordVPN's own root ("NordVPN Root CA"), which Windows doesn't include. Hallow VPN **checks** for it (read-only) and offers an
  **Install** button: it downloads Nord's file (`downloads.nordcdn.com`) only when you click, checks it is NordVPN's self-signed root
  CA without a private key, shows its fingerprint, and then Windows asks (UAC) - it's a change to the computer's Trusted Root store.
  Never silently. A matching **Remove** button.
- **Split tunnel** - honest version: Windows' built-in client splits by **address** (routes), not by app; per-app split
  tunnelling needs a network driver and Hallow VPN installs none. So: **Everything through the VPN**, **Only these go through the
  VPN** (addresses, ranges, websites - the profile's SplitTunneling + one route each, websites looked up on each connect), or
  **Everything except games** (everything through Nord, but the VPN pauses while a game runs).

  ![Split tunnel (made-up data)](docs/split.png)
- **Game-aware**: the family game list (DeltaForceClient-Win64-Shipping.exe, Discovery-d.exe, your own exe names) plus Hallow Hub's
  `playing`. When a game starts: **do nothing**, **disconnect and reconnect 10 s after it closes**, or **stay connected on a low-ping
  server** (and back to your choice after). Nothing pops up while you play - notifications are held.
- **Kill switch** (optional, **asks Windows**): Windows Firewall outbound **block** rules on Wi-Fi and wired connections for TCP, UDP
  and ICMP to public internet addresses, **except** IKEv2 to the VPN server (UDP 500 / 4500) and DNS (port 53). The VPN's own
  connection, your home network, Tailscale's 100.64.0.0/10 and link-local addresses stay reachable. Installing and removing it
  runs Hallow VPN elevated with one fixed command after Windows' UAC prompt - the user clicks. See the limits below.
- **Home** (Tailscale): if Tailscale is installed, "Route through my home" lists only the machines Tailscale reports as **exit-node
  capable** and switches with `tailscale set --exit-node=<ip>` (and back with `--exit-node=`). It uses your **home** connection -
  websites see your home's address, and it **doesn't hide your traffic from your home internet provider**.
- **Hallow family**: the pipe `HallowFamily.VPN.<user>` (`ping`, `status`, `connect [fastest|gaming|<country code>|<server>]`,
  `disconnect`, `menu`, `open [page]`, `invoke <id>`, `quit`), `HallowVPN.exe --cmd "<line>"`, `%APPDATA%\HallowVPN\status.json`
  (`connected`, `connecting`, `server`, `country`, `protocol`, `killSwitch`, `splitMode`, `since` - Hallow Torrents reads these),
  Hallow Hub's tray menu (Hub 2.13+ lists any app answering `menu`), the Hallow accent and **light / dark mode** from
  `%APPDATA%\Hallow\theme.json` (or its own Look setting), crash reports kept on this PC, and the GitHub updater (checks the
  download's SHA-256).

  ![Light mode (made-up data)](docs/connect-light.png)

## Limits (honest)

- **Split tunnelling is route-based, not per app** (see above). With "Only these", DNS lookups use your normal connection.
- **Kill switch**: Windows Firewall block rules always win over allow rules, so it's done by blocking everything else. Rarer IP
  protocols (not TCP / UDP / ICMP) aren't blocked; DNS lookups (port 53) and IKEv2 (UDP 500 / 4500) to any address still go out; it
  works only while Windows Firewall is on. While it's installed, **no VPN = no internet** - so "disconnect while gaming" becomes
  "low-ping server", and Home mode needs it removed first.
- Quitting Hallow VPN leaves the VPN connection as it is (Windows keeps it). Auto-reconnect and game awareness only work while
  Hallow VPN runs.
- Hallow VPN isn't made by or affiliated with Nord Security.

## Install

1. Download `Hallow.VPN.zip` from the [latest release](https://github.com/Dom428/hallow-vpn/releases/latest).
2. Right-click the zip > Properties > tick **Unblock** > OK, then Extract All.
3. Double-click `INSTALL.bat` (no admin needed). It installs to `%LOCALAPPDATA%\Programs\Hallow VPN` with Start menu and
   Desktop shortcuts and an entry in Windows' Apps list. Settings live in `%APPDATA%\HallowVPN`. Uninstalling removes the
   "Hallow VPN" connection and offers to remove the kill switch.

Or install it from [Hallow Setup](https://github.com/Dom428/hallow-setup).

## Build

Built with the C# compiler that ships with Windows (.NET Framework 4.8): run `build.ps1` (`build.ps1 -Tests` also builds
`tests\VpnTests.exe`). WPF in code, `src\Theme.xaml` embedded (light mode swaps its values). `HallowVPN.exe --snap <folder>`
renders every page in dark and light with made-up data.

Tests (`tests\VpnTests.exe`): Nord API parsing from fixtures (one is a real answer saved once), picking, the 3-hour cache,
split-tunnel parsing and the profile script, DPAPI storage and crash-report blanking, the kill switch rules (built, never added),
the certificate check (with certificates made up in memory), Tailscale parsing, game modes, light / dark contrast, and the family
pipe of a test copy where the VPN layer, firewall, Tailscale and games are **fakes**. The only real calls are read-only:
`RasEnumConnections`, `RasDial` against an empty test phonebook (no entry, nothing dialled), the firewall rule list, the
certificate store and `tailscale status --json`.

## Credits

Icons: [Font Awesome Free](https://fontawesome.com) 7.3.1 by Fonticons, Inc., licensed CC BY 4.0 - see `FontAwesome-LICENSE.txt`.
NordVPN is a trademark of Nord Security; Hallow VPN only uses your own subscription through Nord's manual-setup credentials.