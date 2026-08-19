# Security

## Report a vulnerability privately

Do not open a public issue for a vulnerability, credential exposure, or a flaw
that could allow unintended network or computer access.

Use GitHub's **Report a vulnerability** flow when it is available. If the button
is unavailable, open a minimal issue asking Galactrex to establish a private
reporting channel. Do not include exploit details, credentials, addresses,
screenshots, logs, or identifying metadata in that issue.

Include privately:

- the affected Moniswitch version;
- the affected feature and operating systems;
- the smallest reproducible sequence;
- the practical impact;
- a sanitized proof of concept if one is required.

Do not test against a computer, network, or account you do not own or have
explicit permission to assess.

## Scope

Moniswitch's network-capable features are Input Link and LAN Canvas. Display
routing itself uses the local Windows monitor API and DDC/CI connection.

Third-party projects such as Deskflow, Waynergy, Sunshine, Moonlight, OpenSSH,
and the operating systems retain their own security policies. Report a flaw to
the project that owns it; include Moniswitch only when its integration creates
the vulnerability.

## Release integrity

Official release archives are published by Galactrex through this repository.
Each release should include a SHA-256 hash. Do not trust executables attached to
issues, forks, mirrors, or messages claiming to be a faster download.
