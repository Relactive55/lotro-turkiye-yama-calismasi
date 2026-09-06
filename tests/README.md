# Tests

Tests use synthetic catalog bytes, a fake release transport and temporary fake LOTRO directories. They never use the user's production LOTRO installation or copy a real DAT into this repository.

Run the updater/developer harness with `scripts/test-updater.ps1 -DotnetPath <dotnet.exe>`. It builds into a system temporary directory and removes that directory in a `finally` block. The current harness reports 10 updater behavior checks and 29 developer safety/diff/round-trip fixture checks.

The reflection contract script (`run-updater-contracts.ps1`) is run against a separately built updater assembly and currently covers 10 endpoint/manifest/path contracts. The complete release matrix remains documented in `docs/TEST-PLAN.md`.
