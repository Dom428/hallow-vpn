# Hallow VPN roadmap

## Done

| Version | What |
|---|---|
| 1.0 | NordVPN through Windows' built-in IKEv2 client, no drivers and no admin for everyday use. Account: Nord service credentials (an email is refused as the Nord login), DPAPI CurrentUser in account.dat, never logged / in status / in crash reports / on a command line. Servers: Nord's public API (recommendations with the IKEv2 filter, per country, the country list), cached in servers.json and refreshed at most every 3 h (manual Refresh at most every 5 min); load; ping measured by this PC (ICMP, else TCP 443 connect time); Fastest (ping + 0.8 x load), by country, favourites, Best for gaming (under 40 % load, lowest ping). Connect: one per-user "Hallow VPN" profile (Add-/Set-VpnConnection without -AllUserConnection: IKEv2, EAP-MSCHAPv2, encryption required, RememberCredential off), dialled with RasDial in-process (no password on a command line), RasEnumConnections / RasHangUp; status with server, connected time and public IP from Nord's IP check only while connected; auto-reconnect with back-off; Windows error numbers explained (691, 13801, 809...). NordVPN root certificate: read-only check, Install / Remove buttons (download on click, checked, fingerprint shown, UAC). Split tunnel: everything / only these (SplitTunneling + routes, websites looked up per connect) / everything except games. Game-aware: off / pause and reconnect 10 s after / low-ping server and back; notifications held during games. Kill switch: 12 Windows Firewall block rules (Wi-Fi + wired; TCP, UDP, ICMP; IPv4 + IPv6; public addresses only; UDP 500 / 4500 and port 53 open) added / removed by an elevated copy with fixed commands after UAC. Home: Tailscale exit nodes (only exit-node capable ones), tailscale set --exit-node. Family link (ping, status, connect, disconnect, open, menu, invoke, quit, --cmd, status.json for Hallow Torrents), Hub tray hosting and notify, accent, light / dark mode, crash reports, updater |

## Later

| Idea | Why it isn't in 1.0 |
|---|---|
| Per-app split tunnelling | Needs a network (WFP callout) driver. 1.0 splits by address, and pauses for games. |
| WireGuard / NordLynx | Needs the WireGuard driver or NordVPN's own app. 1.0 sticks to Windows' built-in IKEv2. |
| Kill switch that allows only the VPN's server address | Each server change would need admin again. 1.0 keeps UDP 500 / 4500 open to any address instead. |
| Change the kill switch without UAC | Firewall rules are a system setting; a service could do it, but Hallow apps don't install services. |
| Nord's Threat Protection / Meshnet / dedicated IP | Need Nord's app or account API (login), which Hallow VPN deliberately never uses. |
| Pick a city | Nord's recommendations are per country; a city filter needs the full server list (several MB). |
| Overlay / Remote cards | status.json already has what they need; Hallow Overlay and Hallow Remote can show it later. |