# Test plan

Automated tests use synthetic bytes, a fake release transport and temporary fake LOTRO directories. Never replace or modify a live installation for testing. Opt-in release verification may read an explicitly selected original DAT and write only a new private candidate, as described below; proprietary fixtures are not committed.

## Network matrix

Success, connection refused, timeout, 404/500, malformed or empty manifest, missing/duplicate/wrong asset ID/name, truncated download, Content-Length/size/hash mismatch, connection drop, redirect loop, HTTPS-to-HTTP downgrade, non-allowlisted redirect, draft and prerelease release.

## Install matrix

Valid root, wrong root, game/launcher running, insufficient disk, backup failure, download failure, hash failure, replacement failure, post-install failure, state failure, rollback, already-current, newer patch, downgrade, malformed state and missing state.

## Developer matrix

Synthetic catalog ordered-token, tag-stack, placeholder-index, numeric-value, duplicate/order, `MOVED`, `AMBIGUOUS` and round-trip-loss tests. Run two builds with identical inputs for byte determinism; if DAT metadata timestamps prevent byte determinism, measure semantic/catalog determinism instead.

## Current automated coverage

The checked-in harness covers stable/draft release selection, manifest identity, HTTPS policy helpers, safe asset names, streaming fixture verification, valid/invalid LOTRO roots, baseline mismatch fail-closed, backup/install/state, rollback on state failure, semantic patch manifest validation, semantic install fail-closed, official-update-over-translated-DAT recovery, same-release idempotence, ordered protected formats, source-digest move/change behavior, deterministic semantic serialization, matching-digest apply, stale English fallback, critical-UI exclusion, chained predecessor resolution/download trimming and all six diff classifications. It uses synthetic bytes and temporary directories only. A real DAT writer round-trip, A→B→C→D human-less simulation and provider benchmark remain release gates, not claims that production application is complete.

The semantic writer must also preserve each official localization subfile's raw
or compressed representation. A managed-parser round trip is insufficient:
changing that storage representation can produce a parseable DAT which the
LOTRO client refuses to load.

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
# Gerçek DAT üzerinde isteğe bağlı çevrim testi

`tests/RealDatVerification` projesi güncel kurulum yazıcısını kullanır. Kullanım:

```text
RealDatVerification.exe source.dat manifest.json semantic.json NEW-private-candidate.dat
```

Kaynak ve paket hash'leri, bütün çeviri hedefleri, DAT yapısı ve gerçek sonuç
kataloğu doğrulanır. Kaynağın SHA-256, boyut ve değişiklik zamanı sonunda tekrar
kontrol edilir. Çıktı mutlaka yeni, ayrı bir yerel dosyadır; canlı DAT değiştirilmez.
`--inspect-result` yalnız geliştirici teşhisidir: manifest uyuşmazlığında özel adayı
karşılaştırma için tutar ama başarı bildirmez. Bu kip yayın doğrulaması yerine geçmez.
Bu test oyun içi görsel/dil kalitesi testinin yerine geçmez.
