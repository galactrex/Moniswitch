param(
    [string]$SourceRoot = (Join-Path $PSScriptRoot '..'),
    [string]$SettingsPath = (Join-Path $env:LOCALAPPDATA 'Moniswitch\settings.json')
)

$ErrorActionPreference = 'Stop'

$source = [IO.Path]::GetFullPath($SourceRoot)
if (-not (Test-Path -LiteralPath $source -PathType Container)) {
    throw "Source root was not found: $source"
}

$gitDirectory = Join-Path $source '.git'
if (-not (Test-Path -LiteralPath $gitDirectory -PathType Container)) {
    throw 'Initialize the Git repository and stage the intended source tree before running this audit.'
}

$relativePaths = @(& git -C $source diff --cached --name-only --diff-filter=ACMR)
if ($LASTEXITCODE -ne 0 -or $relativePaths.Count -eq 0) {
    throw 'No staged source files were found for the privacy audit.'
}

$errors = [Collections.Generic.List[string]]::new()
$forbiddenDirectories = @(
    '.idea', '.run', '.vs', 'artifacts', 'bin', 'coverage', 'dist', 'obj',
    'package', 'TestResults'
)
$forbiddenNames = @(
    '.env', 'known_hosts', 'settings.json', 'secrets.json'
)
$forbiddenExtensions = @(
    '.bak', '.backup', '.cer', '.crash', '.crt', '.db', '.der', '.dmp',
    '.dump', '.key', '.kdbx', '.log', '.old', '.orig', '.p12', '.pem',
    '.pfx', '.pid', '.ppk', '.pub', '.sqlite', '.sqlite3', '.tmp', '.zip'
)

$files = [Collections.Generic.List[IO.FileInfo]]::new()
foreach ($relativePath in $relativePaths) {
    $parts = $relativePath.Split(
        [char[]]@('\', '/'),
        [StringSplitOptions]::RemoveEmptyEntries)
    if ($parts | Where-Object { $_ -in $forbiddenDirectories }) {
        $errors.Add("forbidden generated or local directory: $relativePath")
    }

    $fullPath = [IO.Path]::GetFullPath((Join-Path $source $relativePath))
    if (-not $fullPath.StartsWith(
            $source + [IO.Path]::DirectorySeparatorChar,
            [StringComparison]::OrdinalIgnoreCase)) {
        $errors.Add("staged path escaped the source root: $relativePath")
        continue
    }
    if (-not (Test-Path -LiteralPath $fullPath -PathType Leaf)) {
        $errors.Add("staged file is missing: $relativePath")
        continue
    }

    $file = Get-Item -LiteralPath $fullPath
    if ($file.Name -in $forbiddenNames -or
        $file.Name.StartsWith('.env.', [StringComparison]::OrdinalIgnoreCase) -or
        $file.Name.EndsWith('.secrets.json', [StringComparison]::OrdinalIgnoreCase) -or
        $file.Name.StartsWith('id_rsa', [StringComparison]::OrdinalIgnoreCase) -or
        $file.Name.StartsWith('id_ed25519', [StringComparison]::OrdinalIgnoreCase)) {
        $errors.Add("forbidden local-data or credential file: $relativePath")
    }
    if ($file.Extension.ToLowerInvariant() -in $forbiddenExtensions) {
        $errors.Add("forbidden credential, archive, or runtime file: $relativePath")
    }
    $files.Add($file)
}

$localMarkers = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
function Add-LocalMarker([object]$Value) {
    if ($null -eq $Value) {
        return
    }

    $marker = ([string]$Value).Trim()
    $generic = @(
        'all', 'galactrex', 'linux-pc', 'moniswitch', 'untitled profile',
        'user', 'username', 'windows-pc'
    )
    if ($marker.Length -ge 4 -and $marker.ToLowerInvariant() -notin $generic) {
        [void]$localMarkers.Add($marker)
    }
}

Add-LocalMarker $env:USERNAME
Add-LocalMarker $env:COMPUTERNAME
Add-LocalMarker (& git config --global user.name 2>$null)
Add-LocalMarker (& git config --global user.email 2>$null)
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
        throw 'Local settings could not be parsed for the source privacy gate.'
    }
}

$latin1 = [Text.Encoding]::GetEncoding(28591)
$utf8Strict = New-Object Text.UTF8Encoding($false, $true)
$selfPath = [IO.Path]::GetFullPath($PSCommandPath)
$auditScriptPaths = @(
    $selfPath,
    [IO.Path]::GetFullPath((Join-Path $source 'tools\Test-ReleasePrivacy.ps1'))
)
$textPatterns = [ordered]@{
    'local Windows account path' = '(?i)[A-Z]:[\\/]+Users[\\/]+(?!WINDOWS_USER(?:[\\/]|$)|USERNAME(?:[\\/]|$))[^\\/\s]+'
    'local Linux account path' = '(?i)/home/(?!LINUX_USER(?:/|$)|USER(?:/|$))[^/\s]+'
    'UNC machine path' = '(?i)\\\\(?![.?]\\|WINDOWS_HOST\\|SERVER\\)[A-Za-z0-9._-]+\\[A-Za-z0-9$._-]+'
    'email address' = '(?i)\b[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}\b'
    'private key material' = '(?i)-----BEGIN (?:OPENSSH |RSA |EC |DSA )?PRIVATE KEY-----'
    'SSH public key material' = '(?im)^\s*(?:ssh-rsa|ssh-ed25519|ecdsa-sha2-[^\s]+)\s+[A-Za-z0-9+/]{40,}={0,3}'
    'certificate fingerprint' = '(?i)\b(?:SHA256:|v2:sha256:)[A-Za-z0-9+/]{32,}={0,3}\b'
    'GitHub token' = '(?i)\b(?:gh[pousr]_[A-Za-z0-9]{20,}|github_pat_[A-Za-z0-9_]{20,})\b'
    'cloud access key' = '\b(?:AKIA|ASIA)[A-Z0-9]{16}\b'
    'JWT or bearer token' = '(?i)\b(?:Bearer\s+[A-Za-z0-9._~+/-]{20,}|eyJ[A-Za-z0-9_-]{10,}\.[A-Za-z0-9_-]{10,}\.[A-Za-z0-9_-]{10,})\b'
    'credential in URL' = '(?i)\b[a-z][a-z0-9+.-]*://[^\s/@:]+:[^\s/@]+@'
}

function Test-AllowedIpv4([string]$Address) {
    $octets = $Address.Split('.') | ForEach-Object { [int]$_ }
    if ($octets | Where-Object { $_ -lt 0 -or $_ -gt 255 }) {
        return $true
    }

    return $octets[0] -eq 127 -or
        ($octets[0] -eq 192 -and $octets[1] -eq 0 -and $octets[2] -eq 2) -or
        ($octets[0] -eq 198 -and $octets[1] -eq 51 -and $octets[2] -eq 100) -or
        ($octets[0] -eq 203 -and $octets[1] -eq 0 -and $octets[2] -eq 113)
}

foreach ($file in $files) {
    $relativePath = $file.FullName.Substring($source.Length + 1)
    $bytes = [IO.File]::ReadAllBytes($file.FullName)
    $binaryText = $latin1.GetString($bytes)

    foreach ($marker in $localMarkers) {
        $utf8Marker = $latin1.GetString([Text.Encoding]::UTF8.GetBytes($marker))
        $utf16Marker = $latin1.GetString([Text.Encoding]::Unicode.GetBytes($marker))
        if ($binaryText.IndexOf($utf8Marker, [StringComparison]::OrdinalIgnoreCase) -ge 0 -or
            $binaryText.IndexOf($utf16Marker, [StringComparison]::OrdinalIgnoreCase) -ge 0) {
            $errors.Add("local identifier in staged file: $relativePath")
        }
    }

    if ($file.FullName -in $auditScriptPaths -or $bytes.Length -gt 5MB -or $bytes -contains 0) {
        continue
    }

    try {
        $content = $utf8Strict.GetString($bytes)
    }
    catch {
        $errors.Add("staged text is not valid UTF-8: $relativePath")
        continue
    }

    foreach ($entry in $textPatterns.GetEnumerator()) {
        if ($content -match $entry.Value) {
            $errors.Add("$($entry.Key) in staged file: $relativePath")
        }
    }

    $ipv4Matches = [regex]::Matches(
        $content,
        '(?<![0-9])(?:[0-9]{1,3}\.){3}[0-9]{1,3}(?![0-9])')
    foreach ($match in $ipv4Matches) {
        if (-not (Test-AllowedIpv4 $match.Value)) {
            $errors.Add("network address in staged file: $relativePath")
        }
    }

    if ($content -match '(?i)(?<![0-9a-f:])(?:fe80|f[cd][0-9a-f]{2}|2[0-9a-f]{3}):(?:[0-9a-f]{0,4}:){1,6}[0-9a-f]{1,4}(?![0-9a-f:])') {
        $errors.Add("IPv6 address in staged file: $relativePath")
    }
}

$repoName = (& git -C $source config --local user.name 2>$null)
$repoEmail = (& git -C $source config --local user.email 2>$null)
if ($repoName -cne 'Galactrex') {
    $errors.Add('repository-local Git author name is not Galactrex')
}
if ($repoEmail -notmatch '(?i)^[^@\s]+@(?:users\.noreply\.github\.com|galactrex\.invalid)$') {
    $errors.Add('repository-local Git author email is not privacy-safe')
}

$commitCount = [int](& git -C $source rev-list --all --count)
$historyIdentities = if ($commitCount -gt 0) {
    @(& git -C $source log --format='%an%x09%ae%x09%cn%x09%ce')
}
else {
    @()
}
foreach ($identity in $historyIdentities) {
    $fields = $identity.Split("`t")
    if ($fields.Count -ne 4 -or
        $fields[0] -cne 'Galactrex' -or
        $fields[2] -cne 'Galactrex' -or
        $fields[1] -notmatch '(?i)^[^@\s]+@(?:users\.noreply\.github\.com|galactrex\.invalid)$' -or
        $fields[3] -notmatch '(?i)^[^@\s]+@(?:users\.noreply\.github\.com|galactrex\.invalid)$') {
        $errors.Add('Git history contains a non-Galactrex or identifying author/committer')
    }
}

$remoteUrls = @(& git -C $source remote -v 2>$null)
foreach ($remote in $remoteUrls) {
    if ($remote -match '(?i)https?://[^\s/@:]+:[^\s/@]+@') {
        $errors.Add('Git remote contains embedded credentials')
    }
}

if ($errors.Count -gt 0) {
    $safeErrors = $errors | Sort-Object -Unique | ForEach-Object { " - $_" }
    throw "Source privacy gate failed:`n$($safeErrors -join "`n")"
}

[pscustomobject]@{
    FilesScanned = $files.Count
    LocalMarkersChecked = $localMarkers.Count
    GitAuthor = 'Galactrex'
    Result = 'PASS'
}
