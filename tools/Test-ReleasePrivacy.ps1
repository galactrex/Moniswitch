param(
    [Parameter(Mandatory = $true)]
    [string]$BundlePath,
    [Parameter(Mandatory = $true)]
    [string]$SourceRoot,
    [string]$SettingsPath = (Join-Path $env:LOCALAPPDATA 'Moniswitch\settings.json')
)

$ErrorActionPreference = 'Stop'

$bundle = [IO.Path]::GetFullPath($BundlePath)
$source = [IO.Path]::GetFullPath($SourceRoot)
if (-not (Test-Path -LiteralPath $bundle -PathType Container)) {
    throw "Release bundle was not found: $bundle"
}
if (-not (Test-Path -LiteralPath $source -PathType Container)) {
    throw "Source root was not found: $source"
}

$errors = [Collections.Generic.List[string]]::new()
$bundleFiles = @(Get-ChildItem -LiteralPath $bundle -Recurse -File)
$forbiddenNames = @('settings.json')
$forbiddenExtensions = @(
    '.bak', '.backup', '.cer', '.crash', '.crt', '.db', '.der', '.dmp',
    '.dump', '.key', '.kdbx', '.log', '.old', '.orig', '.p12', '.pem',
    '.pfx', '.pid', '.ppk', '.pub', '.sqlite', '.sqlite3', '.tmp'
)

foreach ($file in $bundleFiles) {
    if ($file.Name -in $forbiddenNames) {
        $errors.Add("forbidden local-data file: $($file.FullName.Substring($bundle.Length + 1))")
    }
    if ($file.Extension.ToLowerInvariant() -in $forbiddenExtensions) {
        $errors.Add("forbidden credential or runtime file: $($file.FullName.Substring($bundle.Length + 1))")
    }
}

$textExtensions = @(
    '.config', '.conf', '.cs', '.csproj', '.example', '.ini', '.json', '.md',
    '.ps1', '.service', '.sh', '.svg', '.txt', '.xml', '.yaml', '.yml'
)
$textNames = @('.editorconfig', '.gitattributes', '.gitignore', 'LICENSE')
$excludedSourceDirectories = @(
    '.git', '.idea', '.run', '.vs', 'artifacts', 'bin', 'coverage', 'dist',
    'obj', 'package', 'TestResults'
)
$auditScriptPaths = @(
    [IO.Path]::GetFullPath($PSCommandPath),
    [IO.Path]::GetFullPath((Join-Path $source 'tools\Test-SourcePrivacy.ps1'))
)
$sourceFiles = @(Get-ChildItem -LiteralPath $source -Recurse -File | Where-Object {
    $relative = $_.FullName.Substring($source.Length).TrimStart([char[]]@('\', '/'))
    $relativeParts = $relative.Split(
        [char[]]@('\', '/'),
        [StringSplitOptions]::RemoveEmptyEntries)
    -not ($relativeParts | Where-Object { $_ -in $excludedSourceDirectories }) -and
    ($_.Extension.ToLowerInvariant() -in $textExtensions -or $_.Name -in $textNames) -and
    $_.FullName -notin $auditScriptPaths
})
$textFiles = @($bundleFiles | Where-Object {
    $_.Extension.ToLowerInvariant() -in $textExtensions
}) + $sourceFiles

$privateAddress = '(?<![0-9])(?:10(?:\.[0-9]{1,3}){3}|172\.(?:1[6-9]|2[0-9]|3[01])(?:\.[0-9]{1,3}){2}|192\.168(?:\.[0-9]{1,3}){2})(?![0-9])'
$windowsUserPath = '(?i)[A-Z]:[\\/]+Users[\\/]+(?!WINDOWS_USER(?:[\\/]|$)|USERNAME(?:[\\/]|$))[^\\/\s]+'
$linuxUserPath = '(?i)/home/(?!LINUX_USER(?:/|$)|USER(?:/|$))[^/\s]+'
$privateKeyBlock = '(?i)-----BEGIN (?:OPENSSH |RSA |EC |DSA )?PRIVATE KEY-----'

foreach ($file in $textFiles | Sort-Object FullName -Unique) {
    try {
        $content = [IO.File]::ReadAllText($file.FullName)
    }
    catch {
        $errors.Add("unreadable release text file: $($file.Name)")
        continue
    }

    $scope = if ($file.FullName.StartsWith($bundle, [StringComparison]::OrdinalIgnoreCase)) {
        "bundle/$($file.FullName.Substring($bundle.Length + 1))"
    }
    else {
        "source/$($file.FullName.Substring($source.Length + 1))"
    }

    if ($content -match $privateAddress) {
        $errors.Add("private network address in $scope")
    }
    if ($content -match $windowsUserPath -or $content -match $linuxUserPath) {
        $errors.Add("local account path in $scope")
    }
    if ($content -match $privateKeyBlock) {
        $errors.Add("private key material in $scope")
    }
}

$localMarkers = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
function Add-LocalMarker([object]$Value) {
    if ($null -eq $Value) {
        return
    }

    $text = ([string]$Value).Trim()
    $generic = @('all', 'linux-pc', 'windows-pc', 'untitled profile', 'user', 'username')
    if ($text.Length -ge 4 -and $text.ToLowerInvariant() -notin $generic) {
        [void]$localMarkers.Add($text)
    }
}

Add-LocalMarker $env:USERNAME
Add-LocalMarker $env:COMPUTERNAME
if (Test-Path -LiteralPath $SettingsPath -PathType Leaf) {
    try {
        $settings = Get-Content -LiteralPath $SettingsPath -Raw | ConvertFrom-Json
        Add-LocalMarker $settings.QuickToggleMonitorId
        Add-LocalMarker $settings.InputSharing.WindowsScreenName
        Add-LocalMarker $settings.InputSharing.LinuxScreenName
        Add-LocalMarker $settings.InputSharing.DeskflowExecutablePath
        Add-LocalMarker $settings.LanCanvas.LinuxHost
        Add-LocalMarker $settings.LanCanvas.LinuxUser
        Add-LocalMarker $settings.LanCanvas.SshKeyPath
        Add-LocalMarker $settings.LanCanvas.MoonlightExecutablePath
        foreach ($profile in @($settings.Profiles)) {
            Add-LocalMarker $profile.Id
            Add-LocalMarker $profile.Name
            foreach ($monitorId in @($profile.Assignments.PSObject.Properties.Name)) {
                Add-LocalMarker $monitorId
            }
        }
    }
    catch {
        throw 'Local settings could not be parsed for the privacy gate.'
    }
}

$payloads = [Collections.Generic.List[object]]::new()
$latin1 = [Text.Encoding]::GetEncoding(28591)
foreach ($file in $bundleFiles) {
    $bytes = [IO.File]::ReadAllBytes($file.FullName)
    $payloads.Add([pscustomobject]@{
        Scope = "bundle/$($file.FullName.Substring($bundle.Length + 1))"
        Content = $latin1.GetString($bytes)
    })
}
foreach ($file in $sourceFiles) {
    $bytes = [IO.File]::ReadAllBytes($file.FullName)
    $payloads.Add([pscustomobject]@{
        Scope = "source/$($file.FullName.Substring($source.Length + 1))"
        Content = $latin1.GetString($bytes)
    })
}

foreach ($marker in $localMarkers) {
    $utf8 = $latin1.GetString([Text.Encoding]::UTF8.GetBytes($marker))
    $utf16 = $latin1.GetString([Text.Encoding]::Unicode.GetBytes($marker))
    foreach ($entry in $payloads) {
        if ($entry.Content.IndexOf($utf8, [StringComparison]::Ordinal) -ge 0 -or
            $entry.Content.IndexOf($utf16, [StringComparison]::Ordinal) -ge 0) {
            $errors.Add("identifier from local settings in $($entry.Scope)")
        }
    }
}

if ($errors.Count -gt 0) {
    $safeErrors = $errors | Sort-Object -Unique | ForEach-Object { " - $_" }
    throw "Release privacy gate failed:`n$($safeErrors -join "`n")"
}

[pscustomobject]@{
    FilesScanned = $bundleFiles.Count
    LocalMarkersChecked = $localMarkers.Count
    Result = 'PASS'
}
