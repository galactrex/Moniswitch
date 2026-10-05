$ErrorActionPreference = 'Stop'
$settingsFile = Join-Path $env:LOCALAPPDATA 'Moniswitch/settings.json'
$settings = Get-Content -LiteralPath $settingsFile -Raw | ConvertFrom-Json
$connection = $settings.LanCanvas
$identity = [Environment]::ExpandEnvironmentVariables($connection.SshKeyPath)
$destination = "$($connection.LinuxUser)@$($connection.LinuxHost)"
Write-Host 'Enter your Linux administrator password in this window when sudo asks.'
Write-Host 'The password is sent to sudo through SSH, not to Moniswitch or chat.'
& ssh -t -i $identity -o BatchMode=yes -o ConnectTimeout=5 $destination `
    'sudo sh .local/state/Moniswitch/repair/repair-installed-uinput.sh'
if ($LASTEXITCODE -ne 0) { throw 'The Linux repair did not complete.' }
Write-Host 'Repair complete. Tell Codex so it can verify the receiver before restarting sharing.'
Read-Host 'Press Enter to close'
