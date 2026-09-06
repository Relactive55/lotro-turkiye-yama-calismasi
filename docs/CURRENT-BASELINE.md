# Current clean English baseline

This is a read-only verification of the newly supplied official launcher DAT on
2026-09-05. The binary itself is outside this repository.

## Verified identity

- file: `ORJİNAL DAT/client_local_English.dat`
- size: `1,894,213,416` bytes
- SHA-256: `48DEBA5C621BB72492AFD2687ACFFC49A0289054255C2136B7CB469EC1BDD967`
- modified time: `2026-09-05 14:08:28.609 +03:00`
- modified UTC: `2026-09-05T11:08:28.6093943Z`
- native open: `rc=0`, x86, read-only
- block size: `256`
- `vnumDatFile`: `112`
- `vnumGameData`: `50528256`
- `datFileId`: `2`
- DAT stamp: `926CD8E3-2984-4CA9-9C6B-6DF2C8EB6BC3 : 1100`
- first iteration GUID: `B2CD2E86-48F9-4D42-90A7-6C2CC2B45894`
- logical catalog SHA-256: `bf5984ecb19add7c93911c73dba88ecdc1de221330027671c125da9212184922`

Bağımsız salt-okunur kontrol olarak Steam LOTRO kurulumundaki aynı adlı DAT da
aynı boyut, SHA-256 ve değiştirilme zamanını verdi. Bu eşleşme, `ORJİNAL DAT`
dosyasının güncel resmi istemci kopyası olduğunu destekler; Steam dosyası yine
de bu depoya kopyalanmadı.

## Classification

`CURRENT_DAT_CLASSIFICATION=CLEAN_ENGLISH` and
`CURRENT_DAT_CONFIDENCE=HIGH`.

Evidence was combined rather than inferred from one heuristic:

- `281,422` localization DIDs, `825,136` records, zero parse errors and zero
  failed DIDs;
- `520,291` English-signal rows and `709,572` rows passing the broad English
  word-shape check, versus `7,212` Turkish-signal rows;
- raw exact matches against the active TM target set: `38,063`, but only `6`
  Turkish-signal rows (the rest are punctuation, English names or other
  structural targets); approved target traces: `6`, of which `4` are short,
  ambiguous labels;
- critical UI range contains `9,918` records across `92` DIDs, and both the
  critical samples and fixed-seed random samples are English;
- known translated outputs have different hashes: `CIKTI` is
  `6560E85E5E577BE1078C135074F1445FFBA7919515A9E8B98AD3942A233440B0` and
  `ÇEVRİLMİŞ DAT` is
  `6C3751F2D6094265E1BB82015D192EC8ACF9CBE43FAA992B207EB2C5DB3E201F`.

## Transaction rule

The old historical `0E84…` reference is not a current source of truth. The
previous approved binary is not available for a byte-for-byte diff, so the
transaction is recorded as `DIFF_PENDING_OLD_FILE_MISSING`; no old baseline was
overwritten and no DAT was added to the repository. The new clean identity is
safe to use as the local canonical baseline for catalog/diff work, but it is not
itself a public release.
