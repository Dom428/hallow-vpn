HALLOW VPN - a game-aware VPN client for your NordVPN subscription

Uses your EXISTING NordVPN subscription through Windows' own IKEv2 VPN client. No NordVPN app, no
drivers, nothing else to download, no admin for everyday use.

HOW TO INSTALL
1. Right-click the zip > Properties > tick "Unblock" > OK
2. Right-click the zip > Extract All
3. Double-click INSTALL.bat (no admin needed)

FIRST TIME (the First-run setup screen walks you through it)
1. Paste your Nord SERVICE credentials. They are NOT your Nord login: on nordaccount.com go to
   NordVPN > "Set up NordVPN manually" and copy the Service credentials. They're kept encrypted
   (Windows DPAPI) for your Windows user only, never logged.
2. Click "Install Nord's certificate". Hallow VPN downloads NordVPN's root certificate from Nord,
   checks it and shows its fingerprint; Windows then asks for permission (UAC) because it's a
   system change. Without it, connecting fails with error 13801.
3. Click "Test connection to Fastest". Pass = you're done. Fail = click "Fix my connection".

WHAT IT DOES
- Servers from Nord's public server list (IKEv2 only), cached and refreshed at most every 3 hours,
  with the load and a ping this PC measures. Fastest, by country, favourites, Best for gaming.
- Status: server, connected time, public IP (Nord's IP check, only while connected).
- Auto-reconnect when the connection drops.
- Split tunnel: everything, only some addresses / websites, or everything except games.
- Gaming: while Delta Force, Discovery, your extra games or the game Hallow Hub says you're
  playing runs, it can disconnect (and reconnect after) or move to a low-ping server. It never
  pops anything up during a game.
- Kill switch (optional, asks Windows): firewall rules that block traffic outside the VPN.
- NordLynx (optional): Nord's WireGuard through the official WireGuard for Windows.
- Home: route through a computer at home with Tailscale (if Tailscale is installed).
- Hallow family: Hallow Hub's tray menu lists it, the Hallow accent, light / dark, status for
  other apps (Hallow Torrents can read it).

GOOD TO KNOW (honest limits)
- Split tunnelling is by ADDRESS, not by app: Windows' built-in VPN client splits by routes, and
  per-app splitting needs a network driver, which Hallow VPN doesn't install. "Everything except
  games" works by pausing the VPN while a game runs.
- The kill switch is Windows Firewall rules (block TCP / UDP / ICMP to public addresses on Wi-Fi and
  wired, except IKEv2's UDP 500 / 4500, NordLynx's UDP 51820, and DNS only to NordVPN's DNS servers;
  all public IPv6 is blocked). Rarer IP protocols aren't blocked, it
  needs Windows Firewall on, and while it's installed no VPN = no internet. Installing and removing
  it asks Windows for permission (UAC).
- NordLynx asks Windows (UAC) on every connect and disconnect, so Hallow VPN never starts or
  switches it by itself during a game, and game modes don't apply while it's connected.
- Home mode uses your home connection: it doesn't hide your traffic from your home internet
  provider.
- Quitting Hallow VPN leaves the VPN connection as it is (Windows keeps it).
- Nothing is sent anywhere except to Nord (server list, IP check, the VPN itself), the servers it
  pings, and GitHub for updates. Crash reports stay on this PC and never contain your credentials.

WHAT'S NEW IN 1.2.2 - the first connect works (really)
- Fixed: the script that creates the "Hallow VPN" connection reached PowerShell with an invisible byte-order mark
  in front, so its first line failed and Windows never got the request ("Windows couldn't set up the VPN profile:"
  with no reason). It's handed to PowerShell a safer way now, and an error always says why.
- Server list: the "best for gaming" / "P2P" badges wrap to a new line instead of being cut off.

WHAT'S NEW IN 1.2.1 - the first connect works
- Fixed: Windows refused to create the "Hallow VPN" connection ("conflicting PPP Tunnel types"), so Connect and
  Test connection never dialled. Two on/off settings were passed to PowerShell the wrong way; found on a real PC.

WHAT'S NEW IN 1.2 - NordLynx, and a tighter kill switch
- NordLynx (Nord's WireGuard) next to IKEv2, through the official WireGuard for Windows. Protocol:
  Automatic / NordLynx / IKEv2 (NordLynx page). "Install WireGuard" downloads the official installer
  from download.wireguard.com only when you click, checks WireGuard's published hash and its
  WireGuard LLC signature, then Windows asks. Paste a Nord ACCESS TOKEN once for your NordLynx key
  (kept encrypted for your Windows user). Each NordLynx connect / disconnect asks Windows (UAC):
  WireGuard's tunnels are Windows services. Never during a game.
- Kill switch 1.2: DNS only to NordVPN's DNS servers, all public IPv6 blocked, NordLynx allowed.
  Had the 1.0 kill switch? Click "Upgrade kill switch" (Windows asks once).

WHAT'S NEW IN 1.1 - first connect just works, and nothing leaks
- First-run setup: credentials, Nord's certificate, then a test connection - each with a green
  tick or a plain-English fix.
- Fix my connection: checks everything in order and offers the repairs that need no admin.
- DNS leak protection: the "Hallow VPN" connection uses NordVPN's DNS and asks it first (no admin;
  only that entry of your own phonebook is changed, after a backup).
- Leak test while connected (public IP, DNS, an IPv6 warning). Your real IP only when you click.
- Auto-connect: at sign-in, and on Wi-Fi you don't trust (open Wi-Fi is never trusted).
- Best for torrents: Nord's P2P servers ("connect p2p" for Hallow Torrents).
- Connection health: data up / down, live ping, and a hint when the server is slow.

WHAT'S NEW IN 1.0
- The first version: Nord servers (fastest, by country, favourites, best for gaming), connect
  through Windows' IKEv2, auto-reconnect, split tunnel, game awareness, kill switch, Home mode
  (Tailscale) and the family link.

CREDITS
Icons: Font Awesome Free 7.3.1 by Fonticons, Inc. (https://fontawesome.com), licensed CC BY 4.0 - see FontAwesome-LICENSE.txt.
Hallow VPN isn't made by or affiliated with Nord Security; NordVPN is their trademark.