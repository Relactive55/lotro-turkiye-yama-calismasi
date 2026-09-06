# Test plan

Real LOTRO installations and real DAT files must never be used as test fixtures. Use synthetic bytes, a fake release transport and temporary fake LOTRO directories.

## Network matrix

Success, connection refused, timeout, 404/500, malformed or empty manifest, missing/duplicate/wrong asset ID/name, truncated download, Content-Length/size/hash mismatch, connection drop, redirect loop, HTTPS-to-HTTP downgrade, non-allowlisted redirect, draft and prerelease release.

## Install matrix

Valid root, wrong root, game/launcher running, insufficient disk, backup failure, download failure, hash failure, replacement failure, post-install failure, state failure, rollback, already-current, newer patch, downgrade, malformed state and missing state.

## Developer matrix

Synthetic catalog ordered-token, tag-stack, placeholder-index, numeric-value, duplicate/order, `MOVED`, `AMBIGUOUS` and round-trip-loss tests. Run two builds with identical inputs for byte determinism; if DAT metadata timestamps prevent byte determinism, measure semantic/catalog determinism instead.

## Current automated coverage

The checked-in harness covers stable/draft release selection, manifest identity, HTTPS policy helpers, safe asset names, streaming fixture verification, valid/invalid LOTRO roots, baseline mismatch fail-closed, backup/install/state, rollback on state failure, semantic patch manifest validation, semantic install fail-closed, official-update-over-translated-DAT recovery, same-release idempotence, ordered protected formats, source-digest move/change behavior, deterministic semantic serialization, matching-digest apply, stale English fallback, critical-UI exclusion and all six diff classifications. It uses synthetic bytes and temporary directories only. A real DAT writer round-trip, A→B→C→D human-less simulation and provider benchmark remain release gates, not claims that production application is complete.

## 2026-09-05 read-only baseline evidence

The newly supplied clean DAT was opened with the x86 native reader without write
access. Its SHA-256, native metadata, logical catalog hash, zero parse errors,
critical UI count and fixed-seed random localization sample were recorded in
`CURRENT-BASELINE.md` and `CATALOG-AUDIT-2026-09-05.md`. The two older translated
outputs were inspected the same way and remain `MIXED`; they are not test
fixtures for a live install.

The translation coverage run applies approved → TM → glossary to transient
rows only. It is a coverage measurement, not a DAT build. The provider contract
must be rerun after a validated Python 3.11 runtime is available; missing
runtime is a deliberate fail-closed result.
