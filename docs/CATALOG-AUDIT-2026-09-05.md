# Read-only catalog audit — 2026-09-05

All three production DATs were opened with the x86 native reader in
read-only mode. No DAT was copied into this repository and no source file was
modified. Language counts are conservative heuristics over the extracted
localization value; they are evidence for classification, not a claim that a
short label can be translated without context.

## File and native identity

| Metric | Clean English (`ORJİNAL DAT`) | `CIKTI` | `ÇEVRİLMİŞ DAT` |
|---|---:|---:|---:|
| DAT size (bytes) | 1,894,213,416 | 1,898,408,612 | 1,898,408,612 |
| SHA-256 | `48DEBA5C621BB72492AFD2687ACFFC49A0289054255C2136B7CB469EC1BDD967` | `6560E85E5E577BE1078C135074F1445FFBA7919515A9E8B98AD3942A233440B0` | `6C3751F2D6094265E1BB82015D192EC8ACF9CBE43FAA992B207EB2C5DB3E201F` |
| modified time (+03:00) | 2026-09-05 14:08:28.609 | 2026-09-01 22:13:18.523 | 2026-07-29 15:43:33.713 |
| logical catalog SHA-256 | `bf5984ecb19add7c93911c73dba88ecdc1de221330027671c125da9212184922` | `fbca536c71d8530b9a925d65119cd7d83f66c57ddbb51d42e58d123522e805b0` | `10a7b29e71a0041f2bb79bb8be8878e3876ec549c9fc72488b2dbe55beb42472` |
| block size | 256 | 256 | 256 |
| `vnumDatFile` | 112 | 112 | 112 |
| `vnumGameData` | 50,528,256 | 50,528,256 | 50,528,256 |
| `datFileId` | 2 | 2 | 2 |
| native parse errors | 0 | 0 | 0 |

The DAT stamp is `926CD8E3-2984-4CA9-9C6B-6DF2C8EB6BC3 : 1100` and the first
iteration GUID is `B2CD2E86-48F9-4D42-90A7-6C2CC2B45894` for all three files.
Native metadata is therefore not sufficient as a patch identity; the binary
SHA-256 and logical catalog identity remain mandatory.

## Extraction and language signals

| Metric | Clean English | `CIKTI` | `ÇEVRİLMİŞ DAT` |
|---|---:|---:|---:|
| localization DIDs | 281,422 | 281,410 | 281,228 |
| extracted records | 825,136 | 825,100 | 824,702 |
| structured payloads | 0 | 0 | 0 |
| anchored/flat fallback payloads | 279,657 | 279,646 | 279,480 |
| empty payloads | 1,765 | 1,764 | 1,748 |
| failed DIDs | 0 | 0 | 0 |
| critical UI records (`0x250001A0`–`0x250001FF`) | 9,918 | 9,907 | 9,907 |
| critical UI DIDs | 92 | 92 | 92 |
| Turkish-signal rows | 7,212 | 422,825 | 670,029 |
| English-signal rows | 520,291 | 97,504 | 6,880 |
| broad English word-shape hits | 709,572 | 708,561 | 713,748 |

The two translated outputs are therefore `MIXED`, not complete Turkish
baselines: `CIKTI` retains a large English portion and `ÇEVRİLMİŞ DAT` retains
6,880 English-signal rows. Neither is used as the clean source baseline.

## Clean-baseline classification signals

- The clean file has zero parse errors and zero failed DIDs.
- Active `Freedom/Data` TM target traces are `38,063`, but only `6` of those
  rows also pass the Turkish heuristic. Approved target traces are `6`, of
  which `4` are short ambiguous labels such as common two-letter words.
- Critical samples and a fixed-seed random sample contain English source text;
  no bulk Turkish block was observed.
- The clean SHA-256 is distinct from both known translated outputs and matches
  the newly supplied launcher baseline identity.

Conclusion: `CURRENT_DAT_CLASSIFICATION=CLEAN_ENGLISH` with `HIGH` confidence;
the translated files are evidence for preservation candidates only. A real
cross-version diff must still be generated from structural, record, source and
context fingerprints. The historical approved binary referenced by the old
`0E84…` note is no longer available, so that binary diff remains explicitly
pending rather than being guessed from positional rows.
