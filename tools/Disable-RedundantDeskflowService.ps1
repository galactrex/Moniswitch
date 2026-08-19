#Requires -RunAsAdministrator

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = [Security.Principal.WindowsPrincipal]::new($identity)
$isAdministrator = $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)

if (-not $isAdministrator) {
    $arguments = @(
        '-NoProfile'
        '-ExecutionPolicy'
        'Bypass'
        '-File'
        ('"' + $PSCommandPath + '"')
    )
    $process = Start-Process powershell.exe -Verb RunAs -ArgumentList $arguments -Wait -PassThru
    exit $process.ExitCode
}

$service = Get-Service -Name Deskflow -ErrorAction SilentlyContinue
if ($null -eq $service) {
    Write-Host "Deskflow service is not installed. Nothing to change."
    exit 0
}

if ($service.Status -ne 'Stopped') {
    Stop-Service -Name Deskflow -Force
}

Set-Service -Name Deskflow -StartupType Disabled
Write-Host "Deskflow service disabled. Moniswitch starts deskflow-core directly when input sharing is enabled."
