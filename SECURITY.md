# Security policy

The updater is fail-closed. It uses a fixed HTTPS GitHub owner/repository, rejects draft and prerelease releases, validates the expected manifest and asset identity, streams downloads to a `.part` file, verifies size and SHA-256, checks the LOTRO directory and running processes, verifies a backup, and only then performs replacement.

Public distribution policy: end-user downloads are published only as assets of a
GitHub Release in this repository. README, documentation, release notes, Issues,
Pull Requests, repository metadata, and profile text must not link to ZIP, EXE,
MSI, archive, script, or other executable downloads hosted elsewhere. Shortener
and file-host redirects are prohibited. `scripts/audit-public-download-links.ps1`
and the required CI check enforce this rule for tracked repository content;
public GitHub surfaces are reviewed before each release.

Do not put secrets, game DAT files, raw localization dumps, model binaries, or user paths in issues, pull requests, or the repository.

Security reports should describe the affected component and reproduction steps without attaching proprietary game files.
