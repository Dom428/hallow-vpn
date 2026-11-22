# Hallow VPN uninstaller (run from Windows Settings > Apps, or directly).
$dest = Join-Path $env:LOCALAPPDATA 'Programs\Hallow VPN'
$exe = Join-Path $dest 'HallowVPN.exe'
Add-Type -AssemblyName System.Windows.Forms
if ([System.Windows.Forms.MessageBox]::Show("Uninstall Hallow VPN?`n`nIt disconnects the VPN and removes Windows' `"Hallow VPN`" connection.", 'Uninstall Hallow VPN', 'YesNo', 'Question') -ne 'Yes') { return }
# the kill switch: its firewall rules would keep blocking the internet without the VPN, so offer to remove them (Windows asks - UAC)
$ks = (netsh advfirewall firewall show rule name=all dir=out | Select-String -SimpleMatch 'Hallow VPN kill switch - ').Count
if ($ks -gt 0 -and (Test-Path $exe)) {
    if ([System.Windows.Forms.MessageBox]::Show("The Hallow VPN kill switch is installed ($ks firewall rules). Without the VPN they block your internet.`n`nRemove them now? Windows will ask for permission.", 'Hallow VPN', 'YesNo', 'Warning') -eq 'Yes') {
        try { $p = Start-Process $exe -ArgumentList '--elevated', 'killswitch-off' -Verb RunAs -WindowStyle Hidden -PassThru -Wait } catch { }
    }
}
if (Get-Process HallowVPN -ErrorAction SilentlyContinue) { try { & $exe --cmd quit | Out-Null; Start-Sleep -Milliseconds 1500 } catch { } }
Get-Process HallowVPN -ErrorAction SilentlyContinue | Stop-Process -Force
# the per-user VPN connection (no admin): hang up, then remove it
try { & rasdial.exe 'Hallow VPN' /disconnect | Out-Null } catch { }
try { if (Get-VpnConnection -Name 'Hallow VPN' -ErrorAction SilentlyContinue) { Remove-VpnConnection -Name 'Hallow VPN' -Force -ErrorAction SilentlyContinue } } catch { }
Start-Sleep -Milliseconds 600
Remove-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name 'HallowVPN' -ErrorAction SilentlyContinue
foreach ($dir in [Environment]::GetFolderPath('Programs'), [Environment]::GetFolderPath('Desktop')) { Remove-Item (Join-Path $dir 'Hallow VPN.lnk') -ErrorAction SilentlyContinue }
Remove-Item 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\HallowVPN' -Recurse -ErrorAction SilentlyContinue
Remove-Item (Join-Path $env:APPDATA 'HallowVPN') -Recurse -Force -ErrorAction SilentlyContinue   # settings, cache and the encrypted service credentials
Start-Process cmd.exe -ArgumentList "/c timeout /t 2 /nobreak >nul & rmdir /s /q `"$dest`"" -WindowStyle Hidden
$left = (netsh advfirewall firewall show rule name=all dir=out | Select-String -SimpleMatch 'Hallow VPN kill switch - ').Count
$msg = "Hallow VPN is uninstalled."
if ($left -gt 0) { $msg += "`n`nThe kill switch's firewall rules are still there ($left). Remove the rules named 'Hallow VPN kill switch - ...' in Windows Defender Firewall (advanced settings), or reinstall Hallow VPN and remove it there." }
$msg += "`n`nNordVPN's root certificate stays installed if you installed it (other VPN apps use it too)."
[System.Windows.Forms.MessageBox]::Show($msg, 'Hallow VPN') | Out-Null