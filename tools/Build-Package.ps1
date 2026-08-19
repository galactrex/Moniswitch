param(
    [string]$DotnetPath = 'dotnet',
    [string]$Runtime = 'win-x64'
)

$workspace = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$packageRoot = [IO.Path]::GetFullPath((Join-Path $workspace 'package'))
$bundle = [IO.Path]::GetFullPath((Join-Path $packageRoot "Moniswitch-$Runtime"))
$zip = "$bundle.zip"
$publish = [IO.Path]::GetFullPath((Join-Path $workspace "obj\package-publish-$Runtime"))

foreach ($path in @($packageRoot, $bundle, $publish)) {
    if (-not $path.StartsWith($workspace + [IO.Path]::DirectorySeparatorChar)) {
        throw "Package path escaped the workspace: $path"
    }
}

foreach ($path in @($bundle, $publish)) {
    if (Test-Path -LiteralPath $path) {
        Remove-Item -LiteralPath $path -Recurse -Force
    }
}
if (Test-Path -LiteralPath $zip) {
    Remove-Item -LiteralPath $zip -Force
}

New-Item -ItemType Directory -Path $publish, $bundle -Force | Out-Null

& $DotnetPath publish (Join-Path $workspace 'Moniswitch.csproj') `
    -c Release `
    -r $Runtime `
    --self-contained false `
    -p:PublishSingleFile=true `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    -o $publish
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE"
}

$executable = Join-Path $publish 'Moniswitch.exe'
if (-not (Test-Path -LiteralPath $executable)) {
    throw 'Published Moniswitch.exe was not found.'
}

Copy-Item -LiteralPath $executable -Destination $bundle
Copy-Item -LiteralPath (Join-Path $workspace 'README.md') -Destination $bundle
Copy-Item -LiteralPath (Join-Path $workspace 'LICENSE') -Destination $bundle
Copy-Item -LiteralPath (Join-Path $workspace 'PRIVACY.md') -Destination $bundle
Copy-Item -LiteralPath (Join-Path $workspace 'SECURITY.md') -Destination $bundle
Copy-Item -LiteralPath (Join-Path $workspace 'CONTRIBUTING.md') -Destination $bundle
Copy-Item -LiteralPath (Join-Path $workspace 'docs') -Destination $bundle -Recurse
Copy-Item -LiteralPath (Join-Path $workspace 'integration') -Destination $bundle -Recurse

$assetOutput = Join-Path $bundle 'assets'
$toolOutput = Join-Path $bundle 'tools'
New-Item -ItemType Directory -Path $assetOutput, $toolOutput -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $workspace 'assets\moniswitch-mark.svg') -Destination $assetOutput
Copy-Item -LiteralPath (Join-Path $workspace 'tools\Disable-RedundantDeskflowService.ps1') -Destination $toolOutput
Copy-Item -LiteralPath (Join-Path $workspace 'tools\Measure-Idle.ps1') -Destination $toolOutput

$privacyResult = & (Join-Path $workspace 'tools\Test-ReleasePrivacy.ps1') `
    -BundlePath $bundle `
    -SourceRoot $workspace
if ($LASTEXITCODE -ne 0) {
    throw "release privacy gate failed with exit code $LASTEXITCODE"
}

Compress-Archive -Path (Join-Path $bundle '*') -DestinationPath $zip -CompressionLevel Optimal

$hash = Get-FileHash -LiteralPath $zip -Algorithm SHA256
[pscustomobject]@{
    Bundle = $bundle
    Zip = $zip
    ExecutableBytes = (Get-Item -LiteralPath (Join-Path $bundle 'Moniswitch.exe')).Length
    ZipBytes = (Get-Item -LiteralPath $zip).Length
    SHA256 = $hash.Hash
    PrivacyGate = $privacyResult.Result
}
