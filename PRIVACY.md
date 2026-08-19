# Privacy

Moniswitch keeps its configuration on the Windows PC that runs it. It does not
include telemetry, analytics, an updater, or a cloud account.

## Data stored locally

`%LocalAppData%\Moniswitch\settings.json` can contain:

- saved route names and monitor identifiers;
- local screen names and executable paths;
- the LAN address and SSH username configured for LAN Canvas;
- the path to an SSH private key. The key itself is not copied into settings.

Deskflow creates its TLS identity under `%LocalAppData%\Moniswitch\deskflow`.
That identity and its private key stay outside every Moniswitch release bundle.

## Network activity

Moniswitch has no cloud service. Network activity occurs only after the user
configures local features:

- Input Link uses Deskflow on the local network.
- LAN Canvas uses SSH and Moonlight to reach the computer selected by the user.

## Release boundary

Only `package\Moniswitch-win-x64.zip` is intended for distribution. The build
script creates that archive from a fixed set of application files and runs a
privacy gate before writing the ZIP. Local settings, logs, certificates, keys,
build artifacts, and machine-specific paths are rejected.

Names such as `Work computer` belong only to the local settings file of the
person who created them. They are not application defaults and are never added
to the release archive.

## Repository and issue boundary

The official repository contains source, generic templates, tests, and public
documentation. It must not contain local settings, build output, logs,
screenshots with incidental metadata, private network addresses, usernames,
machine names, home paths, device identifiers, SSH material, certificates, or
account credentials.

Issue forms ask for sanitized facts. Never attach the complete settings
directory or raw diagnostic output. Replace identifying values with explicit
placeholders and include only the lines needed to reproduce the problem.

## Privacy view

Private details are hidden by default. **Private / Hidden** masks device
identifiers and replaces saved route names with numbered labels. Monitor model
names always remain visible so each physical display can be identified. Input
Link and LAN Canvas also mask connection fields until **Show details** is
pressed. This changes presentation only; it does not delete the local setup.
