"""Audit the aggregate candidate pool against private English source rows.

This is a read-only quality gate.  It joins by the complete catalog identity,
checks the source token signature, protected markup/brackets, numbers, and
the general translation guards used by the candidate validator.  No DAT or
game output is opened by this tool.
"""

from __future__ import annotations

import argparse
import csv
import json
import re
import unicodedata
from collections import Counter
from pathlib import Path
from typing import Iterator

from audit_curated_translations import catalog_token_signature, decode_pool_value, protected_format_problem
from translation_pipeline import quality_problem


ESCAPE_RE = re.compile(r"\\[A-Za-z]")
MACRO_RE = re.compile(r"#-?\d+:\{[^{}\r\n]*\}")
BRACKET_RE = re.compile(r"\[[^\]\r\n]{1,64}\]")
TECHNICAL_KEY_RE = re.compile(r"[A-Za-z0-9_.-]+(?:\\[A-Za-z0-9_.-]+)+")


def protected_escape_problem(source: str, target: str) -> str:
    """Check LOTRO escapes/macros not covered by the legacy signature."""
    source_escapes = ESCAPE_RE.findall(source or "")
    target_escapes = ESCAPE_RE.findall(target or "")
    if source_escapes != target_escapes:
        return "LOTRO escape stream differs"
    source_macros = MACRO_RE.findall(source or "")
    target_macros = MACRO_RE.findall(target or "")
    # Macro bodies may contain a localizable selector (for example ``the``)
    # and are therefore not compared byte-for-byte.  The wrapper index and
    # all bracket selectors must remain in the same order; malformed or
    # dropped wrappers still fail closed.
    source_structure = [(macro.split(":", 1)[0], BRACKET_RE.findall(macro)) for macro in source_macros]
    target_structure = [(macro.split(":", 1)[0], BRACKET_RE.findall(macro)) for macro in target_macros]
    if source_structure != target_structure:
        return "LOTRO macro structure differs"
    return ""


def source_key(row: dict) -> tuple[str, str, str]:
    return (
        str(row.get("entry_identity", "")),
        str(row.get("dat_key", row.get("key", ""))),
        str(row.get("source_digest", "")),
    )


def normalize_source(value: str) -> str:
    return unicodedata.normalize("NFKC", decode_pool_value(value or "")).replace("\r\n", "\n").replace("\r", "\n")


def read_tsv(path: Path) -> Iterator[dict]:
    with path.open("r", encoding="utf-8-sig", newline="") as handle:
        reader = csv.DictReader(handle, delimiter="\t")
        for row in reader:
            yield {
                "entry_identity": row.get("entry_identity", ""),
                "dat_key": row.get("key", row.get("dat_key", "")),
                "source_digest": row.get("source_digest", ""),
                "token_signature": row.get("token_signature", ""),
                "critical_ui": row.get("critical_ui", "0") == "1",
                "english": normalize_source(row.get("source", "")),
            }


def read_jsonl(path: Path) -> Iterator[dict]:
    with path.open("r", encoding="utf-8-sig") as handle:
        for line_number, line in enumerate(handle, 1):
            if not line.strip():
                continue
            row = json.loads(line)
            # JSONL repair/unresolved queues already carry the normalized
            # ``english`` value.  Decoding pool escapes a second time would
            # turn a literal LOTRO ``\\n`` token into a real newline and make
            # an otherwise correct stored token signature appear different.
            if "english" in row:
                row["english"] = unicodedata.normalize("NFKC", str(row.get("english", ""))).replace("\r\n", "\n").replace("\r", "\n")
            else:
                row["english"] = normalize_source(str(row.get("source", "")))
            yield row


def load_sources(tsv_paths: list[Path], jsonl_paths: list[Path]) -> tuple[dict[tuple[str, str, str], dict], int]:
    sources: dict[tuple[str, str, str], dict] = {}
    duplicates = 0
    for path, iterator in [(path, read_tsv(path)) for path in tsv_paths] + [(path, read_jsonl(path)) for path in jsonl_paths]:
        for row in iterator:
            key = source_key(row)
            if not all(key):
                raise ValueError(f"source identity is incomplete in {path}: {key!r}")
            previous = sources.get(key)
            if previous is None:
                sources[key] = row
            elif previous.get("english") != row.get("english"):
                raise ValueError(f"conflicting English source for {key!r} ({path})")
            else:
                duplicates += 1
    return sources, duplicates


def audit_candidate(row: dict, source: dict | None) -> str:
    if source is None:
        return "source record missing"
    english = source.get("english", "")
    target = str(row.get("target", "") or "")
    if not english.strip():
        return "empty source"
    if not target.strip():
        return "empty target"
    if row.get("translation_status") == "MACHINE_TRANSLATED" and bool(row.get("critical_ui")):
        return "critical UI machine candidate"
    if row.get("translation_status") not in {"MACHINE_TRANSLATED", "TM_REUSED", "HUMAN_APPROVED", "GLOSSARY"}:
        return "unapproved translation status"
    if row.get("translation_status") == "GLOSSARY" and target == english and TECHNICAL_KEY_RE.fullmatch(english):
        # Resource identifiers are deliberately preserved, not translated.
        return ""
    stored_signature = str(row.get("token_signature", "") or "")
    if stored_signature and catalog_token_signature(english) != stored_signature:
        return "source token signature mismatch"
    return protected_format_problem(english, target) or protected_escape_problem(english, target) or quality_problem(english, target)


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--input", required=True, type=Path)
    parser.add_argument("--source-tsv", action="append", type=Path, default=[])
    parser.add_argument("--source-jsonl", action="append", type=Path, default=[])
    parser.add_argument("--safe-output", required=True, type=Path)
    parser.add_argument("--review-output", required=True, type=Path)
    parser.add_argument("--summary", required=True, type=Path)
    args = parser.parse_args()

    sources, source_duplicates = load_sources(args.source_tsv, args.source_jsonl)
    args.safe_output.parent.mkdir(parents=True, exist_ok=True)
    args.review_output.parent.mkdir(parents=True, exist_ok=True)
    args.summary.parent.mkdir(parents=True, exist_ok=True)
    counts = Counter()
    reasons = Counter()
    rows = accepted = rejected = 0
    with args.input.open("r", encoding="utf-8-sig") as input_file, args.safe_output.open("w", encoding="utf-8", newline="\n") as safe_file, args.review_output.open("w", encoding="utf-8", newline="\n") as review_file:
        for line_number, line in enumerate(input_file, 1):
            if not line.strip():
                continue
            row = json.loads(line)
            rows += 1
            status = str(row.get("translation_status", ""))
            counts[status] += 1
            source_row = sources.get(source_key(row))
            problem = audit_candidate(row, source_row)
            if problem:
                rejected += 1
                reasons[problem] += 1
                # Keep the private English source on the retry queue.  This
                # lets the next protected-model pass repair only the failed
                # rows without reconstructing another source join.
                review_file.write(json.dumps({**row, "english": (source_row or {}).get("english", ""), "audit_reason": problem}, ensure_ascii=False, sort_keys=True) + "\n")
            else:
                accepted += 1
                safe_file.write(json.dumps(row, ensure_ascii=False, sort_keys=True) + "\n")
    summary = {
        "schema": "lotro-aggregate-quality-audit-v1",
        "input": str(args.input),
        "source_rows": len(sources),
        "duplicate_source_rows": source_duplicates,
        "rows": rows,
        "accepted": accepted,
        "rejected": rejected,
        "status_counts": dict(sorted(counts.items())),
        "rejection_reasons": dict(sorted(reasons.items())),
        "safe_output": str(args.safe_output),
        "review_output": str(args.review_output),
        "dat_writes": 0,
    }
    args.summary.write_text(json.dumps(summary, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print("AGGREGATE_AUDIT|" + json.dumps(summary, ensure_ascii=False, sort_keys=True))
    return 0 if rejected == 0 else 2


if __name__ == "__main__":
    raise SystemExit(main())
