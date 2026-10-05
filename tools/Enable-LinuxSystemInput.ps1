param([switch]$StageOnly, [switch]$OpenInstaller)
$ErrorActionPreference = 'Stop'
if ($OpenInstaller) {
    Start-Process powershell.exe -ArgumentList @('-NoProfile', '-ExecutionPolicy', 'Bypass',
        '-File', ('"' + $PSCommandPath + '"')) -WindowStyle Normal
    return
}
$workspace = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$settings = Get-Content -LiteralPath (Join-Path $env:LOCALAPPDATA 'Moniswitch/settings.json') -Raw | ConvertFrom-Json
$connection = $settings.LanCanvas
$identity = [Environment]::ExpandEnvironmentVariables($connection.SshKeyPath)
$destination = "$($connection.LinuxUser)@$($connection.LinuxHost)"
$sshOptions = @('-i', $identity, '-o', 'BatchMode=yes', '-o', 'ConnectTimeout=5')
& ssh @sshOptions $destination 'mkdir -p .local/state/Moniswitch/system-input'
if ($LASTEXITCODE -ne 0) { throw 'Could not reach the Linux receiver.' }
$files = @('enable-system-input.sh', 'moniswitch-waynergy-boot', 'test-system-input.py') | ForEach-Object {
    Join-Path $workspace "integration/waynergy/$_"
}
& scp @sshOptions -q @files "${destination}:.local/state/Moniswitch/system-input/"
if ($LASTEXITCODE -ne 0) { throw 'Could not stage the system-input upgrade.' }
& ssh @sshOptions $destination 'sh -n .local/state/Moniswitch/system-input/enable-system-input.sh && sh -n .local/state/Moniswitch/system-input/moniswitch-waynergy-boot && python3 .local/state/Moniswitch/system-input/test-system-input.py .local/state/Moniswitch/system-input/moniswitch-waynergy-boot'
if ($LASTEXITCODE -ne 0) { throw 'Linux shell validation failed.' }
if ($StageOnly) { Write-Host 'Upgrade staged and shell syntax checked. Live services unchanged.'; return }
Write-Host 'Enter your Linux admin password here when sudo asks. Do not send it in chat.'
& ssh @sshOptions -t $destination 'sudo sh .local/state/Moniswitch/system-input/enable-system-input.sh'
if ($LASTEXITCODE -ne 0) { throw 'System-input upgrade did not complete.' }
Read-Host 'System input enabled. Press Enter to close'
