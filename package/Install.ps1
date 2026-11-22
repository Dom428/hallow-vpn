# Hallow VPN installer: copies the app to your user folder (no admin needed) and adds Start menu + Desktop shortcuts.
# It changes nothing else: no VPN profile, firewall rule or certificate is made until you click it in the window.
#   Install.ps1 [-NoStart]   (-NoStart: don't open the app afterwards)
param([switch]$NoStart)
$ErrorActionPreference = 'Stop'
$dest = Join-Path $env:LOCALAPPDATA 'Programs\Hallow VPN'
New-Item -ItemType Directory -Force $dest | Out-Null
$wasRunning = [bool](Get-Process HallowVPN -ErrorAction SilentlyContinue)
if ($wasRunning) { try { $old = Join-Path $dest "HallowVPN.exe"; if (Test-Path $old) { & $old --cmd quit | Out-Null; Start-Sleep -Milliseconds 1500 } } catch { } }   # the VPN connection itself stays up
Get-Process HallowVPN -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 500
Copy-Item (Join-Path $PSScriptRoot 'HallowVPN.exe') $dest -Force
foreach ($f in 'README.txt', 'Uninstall.ps1', 'FontAwesome-LICENSE.txt') { Copy-Item (Join-Path $PSScriptRoot $f) $dest -Force -ErrorAction SilentlyContinue }
$exe = Join-Path $dest 'HallowVPN.exe'
# Windows Settings > Apps entry (per user, no admin)
$u = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\HallowVPN'
New-Item $u -Force | Out-Null
$ver = (Get-Item $exe).VersionInfo.FileVersion -replace '\.0\.0$', ''
$vals = @{ DisplayName = 'Hallow VPN'; DisplayVersion = $ver; Publisher = 'Hallow'; DisplayIcon = $exe; InstallLocation = $dest
    UninstallString = "powershell.exe -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File `"$(Join-Path $dest 'Uninstall.ps1')`""
    URLInfoAbout = 'https://github.com/Dom428/hallow-vpn' }
foreach ($k in $vals.Keys) { Set-ItemProperty $u -Name $k -Value $vals[$k] }
Set-ItemProperty $u -Name NoModify -Value 1 -Type DWord; Set-ItemProperty $u -Name NoRepair -Value 1 -Type DWord
Set-ItemProperty $u -Name EstimatedSize -Value ([int]((Get-Item $exe).Length / 1KB)) -Type DWord
$w = New-Object -ComObject WScript.Shell
foreach ($dir in [Environment]::GetFolderPath('Programs'), [Environment]::GetFolderPath('Desktop')) {
    $s = $w.CreateShortcut((Join-Path $dir 'Hallow VPN.lnk'))
    $s.TargetPath = $exe; $s.WorkingDirectory = $dest; $s.Description = 'A game-aware VPN client for your NordVPN subscription'; $s.Save()
}
if ($NoStart) { Write-Host "Hallow VPN is installed. It's in your Start menu and on your Desktop."; return }
# after an update it comes back the way it was (in the tray if it was running); a fresh install opens the window
if ($wasRunning) { Start-Process $exe -ArgumentList '--tray' } else { Start-Process $exe }
Write-Host "Hallow VPN is installed. It's in your Start menu and on your Desktop."