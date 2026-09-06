# Security policy

The updater is fail-closed. It uses a fixed HTTPS GitHub owner/repository, rejects draft and prerelease releases, validates the expected manifest and asset identity, streams downloads to a `.part` file, verifies size and SHA-256, checks the LOTRO directory and running processes, verifies a backup, and only then performs replacement.

Do not put secrets, game DAT files, raw localization dumps, model binaries, or user paths in issues, pull requests, or the repository.

Security reports should describe the affected component and reproduction steps without attaching proprietary game files.
