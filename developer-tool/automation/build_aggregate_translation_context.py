"""Extend a private translation context with audited aggregate candidates.

Only exact, unambiguous English-source -> Turkish-target pairs are added to
the TM map.  Existing approved/TM/glossary entries always win, and source
records that cannot be joined or have conflicting targets are skipped.  The
tool never opens or writes a DAT.
"""

from __future__ import annotations

import argparse
import csv
import json
from pathlib import Path

from audit_curated_translations import decode_pool_value


def key(row: dict) -> tuple[str, str, str]:
    return (
        str(row.get("entry_identity", "")),
        str(row.get("dat_key", row.get("key", ""))),
        str(row.get("source_digest", "")),
    )


def normalized_source(value: str) -> str:
    return decode_pool_value(value or "").replace("\r\n", "\n").replace("\r", "\n")


def load_sources(tsv_paths: list[Path], jsonl_paths: list[Path]) -> tuple[dict[tuple[str, str, str], str], int]:
    sources: dict[tuple[str, str, str], str] = {}
    conflicts = 0
    for path in tsv_paths:
        with path.open("r", encoding="utf-8-sig", newline="") as handle:
            for row in csv.DictReader(handle, delimiter="\t"):
                item_key = key(row)
                english = normalized_source(row.get("source", ""))
                if not all(item_key) or not english:
                    continue
                previous = sources.get(item_key)
                if previous is not None and previous != english:
                    conflicts += 1
                    continue
                sources[item_key] = english
    for path in jsonl_paths:
        with path.open("r", encoding="utf-8-sig") as handle:
            for line in handle:
                if not line.strip():
                    continue
                row = json.loads(line)
                item_key = key(row)
                # Repair JSONL rows already contain a once-decoded English
                # value; decode pool escapes only for raw ``source`` fields.
                raw_english = str(row.get("english", "")) if "english" in row else str(row.get("source", ""))
                english = (
                    raw_english.replace("\r\n", "\n").replace("\r", "\n")
                    if "english" in row
                    else normalized_source(raw_english)
                )
                if not all(item_key) or not english:
                    continue
                previous = sources.get(item_key)
                if previous is not None and previous != english:
                    conflicts += 1
                    continue
                sources[item_key] = english
    return sources, conflicts


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--base-context", required=True, type=Path)
    parser.add_argument("--aggregate", required=True, type=Path)
    parser.add_argument("--source-tsv", action="append", type=Path, default=[])
    parser.add_argument("--source-jsonl", action="append", type=Path, default=[])
    parser.add_argument("--output", required=True, type=Path)
    parser.add_argument("--summary", required=True, type=Path)
    args = parser.parse_args()

    base = json.loads(args.base_context.read_text(encoding="utf-8-sig"))
    sources, source_conflicts = load_sources(args.source_tsv, args.source_jsonl)
    targets: dict[str, str] = {}
    ambiguous: set[str] = set()
    aggregate_rows = missing_sources = 0
    with args.aggregate.open("r", encoding="utf-8-sig") as handle:
        for line in handle:
            if not line.strip():
                continue
            aggregate_rows += 1
            row = json.loads(line)
            english = sources.get(key(row))
            if not english:
                missing_sources += 1
                continue
            target = str(row.get("target", "") or "").strip()
            if not target:
                continue
            previous = targets.get(english)
            if previous is None and english not in ambiguous:
                targets[english] = target
            elif previous != target:
                targets.pop(english, None)
                ambiguous.add(english)

    approved = dict(base.get("approved", {}) or {})
    tm = dict(base.get("tm", {}) or {})
    glossary = dict(base.get("glossary", {}) or {})
    added = 0
    for english, target in targets.items():
        if english in approved or english in tm or english in glossary or english in ambiguous:
            continue
        tm[english] = target
        added += 1

    context = {
        "approved": approved,
        "tm": tm,
        "glossary": glossary,
        "protected_names": list(base.get("protected_names", []) or []),
    }
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.summary.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(context, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    summary = {
        "schema": "lotro-aggregate-translation-context-v1",
        "aggregate_rows": aggregate_rows,
        "source_rows": len(sources),
        "source_conflicts": source_conflicts,
        "missing_sources": missing_sources,
        "unambiguous_targets": len(targets),
        "ambiguous_sources": len(ambiguous),
        "base_approved": len(approved),
        "base_tm": len(base.get("tm", {}) or {}),
        "base_glossary": len(glossary),
        "added_tm": added,
        "output_tm": len(tm),
        "output": str(args.output),
        "dat_writes": 0,
    }
    args.summary.write_text(json.dumps(summary, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print("AGGREGATE_CONTEXT|" + json.dumps(summary, ensure_ascii=False, sort_keys=True))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
