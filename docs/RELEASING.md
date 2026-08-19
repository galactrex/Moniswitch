# Release process

Official Moniswitch releases are built and published by Galactrex.

## Gate the source

1. Start from a clean `main` branch.
2. Review the staged file list. Build folders, settings, logs, certificates,
   keys, archives, and local tool state must not be present.
3. Run the source privacy audit.
4. Search the staged content for addresses, usernames, machine names, local
   paths, device identifiers, credentials, and private project terminology.
5. Have a second reviewer inspect the exact staged tree without receiving the
   local settings values it is meant to catch.

## Verify the build

```powershell
dotnet build .\Moniswitch.csproj -c Release
dotnet run --project .\tests\Moniswitch.SmokeTests\Moniswitch.SmokeTests.csproj -c Release
.\tools\Build-Package.ps1
```

`Build-Package.ps1` creates the portable bundle, runs the release privacy gate,
and prints the ZIP's SHA-256 hash.

## Inspect the archive

- Confirm the archive includes `LICENSE`, `README.md`, `PRIVACY.md`, docs,
  generic integration templates, and the executable.
- Confirm it does not include `settings.json`, logs, keys, certificates, build
  directories, test output, screenshots, or account-specific paths.
- Launch the packaged executable on Windows and verify monitor discovery without
  changing a live route during release inspection.

## Publish

1. Commit and tag from the Galactrex maintainer identity.
2. Upload only the ZIP produced by the packaging script.
3. Publish the SHA-256 hash beside it.
4. State whether the executable is signed. Never let a missing signature become
   a surprise delivered by SmartScreen.
5. Keep the copyright and MIT license notice with the source and binary package.

No issue attachment, fork build, development folder, or convenient file found
on the desktop becomes an official release. Convenience is not a provenance
system.
