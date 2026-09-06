# Translation coverage against the new clean DAT — 2026-09-05

> Bu belge 5 Eylül tarihli ara denetimin tarihsel kaydıdır. Güncel ve doğrulanmış sonuçlar için `IMPLEMENTATION-STATUS.md` dosyasına bakın.

This report is a dry run. It reads `ORJİNAL DAT/client_local_English.dat` and
the local translation stores, applies the safety order to temporary rows, and
does not write a DAT, TM file, release asset or installation state.

## Active stores

The developer tool uses `Freedom/Data/tm.tsv` when it exists. Its exact loaded
count is `458,627`. The older root-level `Freedom/tm.tsv` contains `394,769`
entries but is not merged because the Data store is non-empty. The active
`Freedom/Data/approved_updates.tsv` loads `3,375` valid Key+Source+Target rules.
The protected-name list has `48` unique values and the glossary has `47` unique
entries.

The complete curated pool contains `420,386` human/rule-backed rows. The
independent curated audit accepted `420,247` rows and quarantined `139` rows
with protected-bracket/token problems. The quarantined rows were never used as
DAT input; their private OPUS repair pass produced `138` machine outputs, of
which `127` passed the second guard. The remaining `12` outputs stay in review.

## Dry-run result on the clean baseline

| Measurement | Result |
|---|---:|
| localization DIDs | 281,422 |
| extracted rows | 825,136 |
| critical UI rows | 9,918 |
| English-signal rows before translation | 520,291 |
| approved exact Key+Source hits | 3,234 |
| exact TM hits after approved rules | 417,017 |
| exact glossary hits after approved/TM rules | 136 |
| unresolved English-signal rows after curated stores | 100,250 |
| parse errors | 0 |
| DAT writes | 0 |

The counts are row applications, so one store entry can apply to multiple DAT
rows. Human-approved rules are applied first; they are never overwritten by TM
or machine translation. `NEW`/`MODIFIED` rows without a safe result remain
English and are marked for translation/review rather than receiving stale text.

## Existing DAT decision

The `ÇEVRİLMİŞ DAT` and `CIKTI` files were both classified `MIXED`: they retain
English-signal rows and are older catalog identities than the new clean file.
They are preservation evidence only; neither is copied over the clean baseline.
Existing Turkish text can be carried only when the multi-signal identity and
`source_digest` match. A changed source is `SOURCE_CHANGED`/`NEEDS_TRANSLATION`.

## Translation gate

The provider abstraction and token/number/markup guards are present. The
LOTRO-root runtime is now validated at
`LOTRO-root/.venv` (`Python 3.11.9`), outside the
public source tree; the translation contract reports `TRANSLATION_CONTRACT_PASS`.
Dependencies are local CPU
packages only (`torch 2.14.0+cpu`, `transformers 5.16.1`, `sentencepiece`),
and the OPUS model is kept outside the repository under the D: LOTRO work area.

The first bounded OPUS run used 128 non-critical unresolved rows. It produced
126 `MACHINE_TRANSLATED` candidates and rejected 2 unchanged-English outputs;
all 128 passed independent identity/token checks, and 0 critical-UI rows were
included. The candidate file is
`ÇEVİRİ HAVUZU/2026-09-05/semantic-candidates-128.jsonl`; its model revision and
SHA-256 are recorded in `work/translation-model-cache/model-lock.json`.
The curated repair file is
`ÇEVİRİ HAVUZU/2026-09-05/curated-repair-safe-127.jsonl`; it also contains
candidates only. No candidate was written to a DAT or promoted to a public
release.

The quality-profile replay of the same 128-row input (float32 CPU, beam=3,
full token budget) produced `125` machine rows and failed closed on `3`; the
strict independent guard admitted `122` candidates. This replay is recorded
separately as `semantic-candidates-128-f32.jsonl` and does not replace the
earlier review file.

The full unresolved review queue remains 100,250 rows. Critical UI records are
marked `REVIEW_REQUIRED`; non-critical rows without a validated result remain
English. A full Turkish DAT or public release is not claimed by this report.
