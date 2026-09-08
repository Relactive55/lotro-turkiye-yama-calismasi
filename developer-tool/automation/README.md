# Read-only catalog automation contract

The production GUI remains the developer tool. `ReadOnlyCatalogExtractor` calls the native x86 `datexport.dll` reader with `writable:false` and emits a catalog outside the repository. It never calls the native write/purge functions.

Each record contains DID, record/structural/source/context fingerprints, position metadata and the critical-UI flag. `CatalogDiff` matches these signals in order; position is only an auxiliary hint. Ties and unsafe matches become `AMBIGUOUS` with `ReviewRequired=true`, so no translation is carried over automatically.

The builder must copy the current clean English DAT to a temporary candidate for
the first/root semantic package and apply approved translations to that
candidate. A later correction layer may use the exact previous Turkish DAT as
its binary base only when `base_candidate_dat_sha256` and
`base_candidate_catalog_sha256` are recorded and verified. Native round-trip and
deterministic release checks remain explicit gates before publication.

## Reference translation provenance

The semantic generator can reuse a game-tested Turkish DAT only when its
original clean English DAT is supplied as the eighth argument:

```text
SemanticPatchGenerator current-clean.dat candidates.jsonl output.json patch-version verified-reference.dat private-review.jsonl manual-decisions.jsonl reference-source.dat
```

Use `-` for unused optional arguments. A reference DAT without its paired clean
source is rejected before generation. The paired file must be the clean source
from which that reference was built; when both builds share the same baseline,
it can be the current clean DAT. Keep these game files in the private work area.

Reference text is reused only when the current and reference source records have
the same key, source digest and token signature. If an official update changes
or removes that source identity, the old translation cannot acquire the new
digest or human approval. A separately validated current candidate or explicit
manual decision can still supply the translation; otherwise the source remains
English and missing critical records require review. The completion log reports
`reference_source_mismatch` to make skipped reference rows visible.

## Incremental correction package

Build the tool with `scripts/build.ps1 -Project SemanticGenerator` (`All` also
includes it), then run:

```text
SemanticPatchGenerator --incremental predecessor.dat predecessor-manifest.json corrections.jsonl new-output-directory patch-version
```

The predecessor must be the exact Turkish DAT from the last verified release.
Its manifest must include its actual `candidate_dat_sha256`,
`candidate_dat_size` and `candidate_catalog_sha256`, plus the original clean
baseline identities and GitHub release/asset identifiers. A legacy manifest
missing these candidate identities is rejected; do not infer them from an
unverified local DAT. The command verifies the supplied DAT and catalog against
that manifest before building any output. The manifest is trusted local release
metadata; the command does not authenticate or fetch a GitHub release.

`corrections.jsonl` uses the existing translation-candidate fields. Every row
must be `HUMAN_APPROVED` with `dat_key`, `source_digest`, `token_signature`, and
`target`. For an incremental package, the digest and token signature refer to
the text currently stored in the verified predecessor, which may already be
Turkish. They must not be copied from the original English root patch. Invalid,
missing, duplicate or protected keys stop generation. Unchanged targets are
omitted; only the selected corrections enter the patch. Game baseline changes
require a new full root package.

The output directory must be new. It contains the small semantic patch,
`manifest-template.json`, and `private-candidate.dat`. The shared installer
writer builds and fully verifies that private candidate to obtain real output
DAT and catalog hashes. It does not modify the input DAT or install into the
game. This verification still reads/copies a full DAT; incremental distribution
reduces the public download, not that release validation work.

The template preserves the original clean baseline, points to the predecessor
release/asset, increments chain depth, and requires the current updater version.
It deliberately has `release_id=0` and `asset_id=0`, so it is not installable.
After uploading only the semantic asset, bind these fields to the actual new
GitHub release and asset, validate the resulting manifest and release gates,
then publish the manifest. Never upload `private-candidate.dat`. This command
does not upload or publish anything. At depth 32, create a new full root package.

## Replace an unverifiable legacy root

```text
SemanticPatchGenerator --verified-root clean.dat full-semantic.json manifest.json manual-decisions-or-dash new-output-directory new-version official-game-version
```

The full input semantic asset must match its manifest SHA-256 and size. Its
source DAT and catalog must match the independently anchored clean baseline;
the game version must match too. This command cannot relabel an old baseline
as a newly detected version. It does not take the legacy candidate hash as
proof. It rebuilds a separate candidate, validates every target and the whole
DAT, verifies source preservation under a read lock, and measures the actual
result hashes. Optional source-bound reviewed decisions are applied before
the build. Existing directories are refused.

Outputs: semantic asset, `manifest-template.json`, `verification.json`, and
`private-candidate.dat`. The template has no release/asset IDs until uploaded.
`scripts/publish-verified-root.ps1` checks the private candidate and asset
against the evidence, requires successful GitHub validation for an exact
commit, uploads only semantic/EXE/manifest assets to a draft, checks GitHub's
asset SHA-256 values, and binds the real IDs. `-Publish` explicitly promotes
the verified draft; omission leaves it unpublished. It never uploads the
private DAT, raw catalog, or model cache and never overwrites public releases.

This command provides a verified replacement for a legacy root. It does not
automatically merge an existing incremental chain; all subsequent corrections
must be included in the full input/correction set when compacting a chain.
Independent acquisition of current official source data remains a separate
unfinished integration, not a capability of the version watcher.

New roots and incremental outputs use `.semantic.json.gz`. Updater 1.2.0.0
accepts both gzip and legacy JSON, checks the downloaded compressed hash/size
before decompression, and limits the decoded text to 512 Mi characters. The
semantic/source/target checks remain unchanged. An unpublished plain verified
root can be converted with `scripts/compress-verified-root.ps1`: it checks the
original asset anchor and identical decompressed SHA-256, validates the entire
compressed document with the new reader, and backs up the previous manifest
template. It does not rebuild or change the verified DAT. The publisher's
`-ValidateOnly` performs local checks without GitHub calls.
