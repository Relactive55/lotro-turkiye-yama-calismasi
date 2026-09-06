# Read-only catalog automation contract

The production GUI remains the developer tool. `ReadOnlyCatalogExtractor` calls the native x86 `datexport.dll` reader with `writable:false` and emits a catalog outside the repository. It never calls the native write/purge functions.

Each record contains DID, record/structural/source/context fingerprints, position metadata and the critical-UI flag. `CatalogDiff` matches these signals in order; position is only an auxiliary hint. Ties and unsafe matches become `AMBIGUOUS` with `ReviewRequired=true`, so no translation is carried over automatically.

The builder must copy the current clean English DAT to a temporary candidate and apply approved translations to that candidate. The previous Turkish DAT may provide translation reference rows but never the binary base. Native round-trip and deterministic release checks remain explicit gates before publication.
