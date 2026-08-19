param(
    [ValidateRange(1, 300)]
    [int]$Seconds = 10
)

$app = Get-Process -Name Moniswitch -ErrorAction SilentlyContinue |
    Select-Object -First 1

if (-not $app) {
    throw 'Moniswitch is not running.'
}

$children = Get-CimInstance Win32_Process |
    Where-Object { $_.ParentProcessId -eq $app.Id }

$processIds = @($app.Id) + @($children.ProcessId)
$before = @{}

foreach ($processId in $processIds) {
    $process = Get-Process -Id $processId -ErrorAction SilentlyContinue
    if ($process) {
        $before[$processId] = $process.CPU
    }
}

Start-Sleep -Seconds $Seconds

foreach ($processId in $processIds) {
    $process = Get-Process -Id $processId -ErrorAction SilentlyContinue
    if (-not $process -or -not $before.ContainsKey($processId)) {
        continue
    }

    [pscustomobject]@{
        Process = $process.ProcessName
        Pid = $process.Id
        WorkingMB = [math]::Round($process.WorkingSet64 / 1MB, 1)
        PrivateMB = [math]::Round($process.PrivateMemorySize64 / 1MB, 1)
        CpuMs = [math]::Round(($process.CPU - $before[$processId]) * 1000, 1)
        SampleSeconds = $Seconds
    }
}
