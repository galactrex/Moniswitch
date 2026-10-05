$ErrorActionPreference = 'Stop'
$settings = Get-Content -LiteralPath (Join-Path $env:LOCALAPPDATA 'Moniswitch/settings.json') -Raw | ConvertFrom-Json
$connection = $settings.LanCanvas
$identity = [Environment]::ExpandEnvironmentVariables($connection.SshKeyPath)
$destination = "$($connection.LinuxUser)@$($connection.LinuxHost)"
Write-Host 'Enter your Linux administrator password only in this terminal when sudo asks.'
Write-Host 'After Ready appears, switch to Linux, move the mouse, tap Shift, and return to Windows.'
& ssh -t -i $identity -o BatchMode=yes -o ConnectTimeout=5 $destination `
    'sh -c ''sudo python3 .local/state/Moniswitch/input-diagnostic/diagnose-input.py | tee .local/state/Moniswitch/input-diagnostic/result.txt'''
if ($LASTEXITCODE -ne 0) { Write-Host 'The Linux diagnostic could not finish.' }
Read-Host 'Press Enter to close'
