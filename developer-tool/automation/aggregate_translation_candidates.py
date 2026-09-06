"""Aggregate validated LOTRO translation candidates without touching a DAT.

The aggregate is the hand-off between translation review and the native DAT
builder.  Curated rows are normalized to the candidate schema, then every
record is keyed by the full source identity (entry, DAT key, and source
digest).  Duplicate records with the same target are collapsed; conflicting
records fail closed so a questionable string cannot reach a DAT build.
"""

from __future__ import annotations

import argparse
import csv
import json
from collections import Counter
from pathlib import Path
from typing import Iterable, Iterator


COMMON_FIELDS = (
    "critical_ui",
    "dat_key",
    "entry_identity",
    "quality_error",
    "source_digest",
    "target",
    "token_signature",
    "translation_engine",
    "translation_engine_version",
    "translation_status",
)


def _decode_pool_value(value: str) -> str:
    """Decode the reversible \"\\n\"/\"\\r\"/\"\\t\" TSV escaping used by the extractor."""
    result: list[str] = []
    index = 0
    while index < len(value or ""):
        char = value[index]
        if char == "\\" and index + 1 < len(value):
            escaped = value[index + 1]
            if escaped == "n":
                result.append("\n")
                index += 2
                continue
            if escaped == "r":
                result.append("\r")
                index += 2
                continue
            if escaped == "t":
                result.append("\t")
                index += 2
                continue
            if escaped == "\\":
                result.append("\\")
                index += 2
                continue
        result.append(char)
        index += 1
    return "".join(result)


def _candidate_key(row: dict) -> tuple[str, str, str]:
    return (
        str(row.get("entry_identity", "")),
        str(row.get("dat_key", "")),
        str(row.get("source_digest", "")),
    )


def _normalize_candidate(row: dict, origin: str) -> dict:
    result = {field: row.get(field, "") for field in COMMON_FIELDS}
    result["critical_ui"] = bool(result["critical_ui"])
    result["translation_status"] = str(result["translation_status"] or "UNTRANSLATED")
    result["translation_engine"] = str(result["translation_engine"] or origin)
    result["translation_engine_version"] = str(
        result["translation_engine_version"] or "unknown"
    )
    result["quality_error"] = str(result["quality_error"] or "")
    result["target"] = str(result["target"] or "")
    result["entry_identity"] = str(result["entry_identity"] or "")
    result["dat_key"] = str(result["dat_key"] or "")
    result["source_digest"] = str(result["source_digest"] or "")
    result["token_signature"] = str(result["token_signature"] or "")
    return result


def _read_jsonl(path: Path) -> Iterator[dict]:
    with path.open("r", encoding="utf-8-sig") as handle:
        for line_number, line in enumerate(handle, 1):
            if not line.strip():
                continue
            try:
                row = json.loads(line)
            except json.JSONDecodeError as exc:
                raise ValueError(f"invalid JSON at {path}:{line_number}: {exc}") from exc
            if not isinstance(row, dict):
                raise ValueError(f"candidate row is not an object at {path}:{line_number}")
            yield row


def _read_curated(path: Path) -> Iterator[dict]:
    with path.open("r", encoding="utf-8-sig", newline="") as handle:
        reader = csv.DictReader(handle, delimiter="\t")
        required = {"status", "key", "entry_identity", "source_digest", "token_signature", "critical_ui", "target"}
        missing = required.difference(reader.fieldnames or [])
        if missing:
            raise ValueError(f"curated pool is missing columns: {sorted(missing)}")
        for line_number, row in enumerate(reader, 2):
            status = str(row.get("status", "") or "")
            if status not in {"HUMAN_APPROVED", "TM_REUSED", "GLOSSARY"}:
                raise ValueError(f"unknown curated status at {path}:{line_number}: {status!r}")
            yield {
                "critical_ui": row.get("critical_ui", "0") == "1",
                "dat_key": row.get("key", ""),
                "entry_identity": row.get("entry_identity", ""),
                "quality_error": "",
                "source_digest": row.get("source_digest", ""),
                "target": _decode_pool_value(row.get("target", "") or ""),
                "token_signature": row.get("token_signature", ""),
                "translation_engine": "curated-pool",
                "translation_engine_version": status,
                "translation_status": status,
            }


def _validate_identity(row: dict, origin: str, ordinal: int) -> None:
    key = _candidate_key(row)
    if not all(key):
        raise ValueError(f"missing source identity in {origin} row {ordinal}: {key!r}")
    if not row["target"].strip():
        raise ValueError(f"empty target in {origin} row {ordinal}: {key!r}")
    if "\x00" in row["target"]:
        raise ValueError(f"NUL byte in target in {origin} row {ordinal}: {key!r}")
    if row["translation_status"] in {"UNTRANSLATED", "REVIEW_REQUIRED"}:
        raise ValueError(
            f"unapproved status in {origin} row {ordinal}: {row['translation_status']} {key!r}"
        )


def _repair_priority(origin: str) -> int:
    """Rank targeted retries above older bulk candidates, fail closed otherwise."""
    name = Path(origin).name
    if "manual-escape-repairs" in name or "manual-hard-repairs" in name:
        return 50
    if "ct2-escape-repair-f32-b3" in name:
        return 40
    if "ct2-escape-repair-int8-b2" in name:
        return 30
    if "ct2-structural-repair" in name:
        return 20
    # The later segmented retry was generated specifically from the quality
    # review queue (beam-2, current model context).  Prefer it over an older
    # bulk result when both candidates share the same source identity.  It has
    # already passed the same token/macro/quality audit, so this is a targeted
    # replacement rather than an unrestricted last-write-wins rule.
    if "ct2-bulk-eleventh-segment" in name:
        return 15
    return 0


def _iter_sources(curated: Path | None, candidates: Iterable[Path]) -> Iterator[tuple[str, Iterator[dict]]]:
    if curated:
        yield str(curated), _read_curated(curated)
    for path in candidates:
        yield str(path), _read_jsonl(path)


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--curated", type=Path, help="validated curated-safe-pool.tsv")
    parser.add_argument("--candidate", action="append", type=Path, default=[], help="validated candidate JSONL (repeatable)")
    parser.add_argument("--output", required=True, type=Path)
    parser.add_argument("--summary", required=True, type=Path)
    args = parser.parse_args()

    records: dict[tuple[str, str, str], dict] = {}
    origins: dict[tuple[str, str, str], str] = {}
    counts = Counter()
    duplicate_count = 0
    replacement_count = 0
    conflicts: list[dict] = []
    try:
        for origin, rows in _iter_sources(args.curated, args.candidate):
            origin_count = 0
            for ordinal, raw in enumerate(rows, 1):
                origin_count += 1
                row = _normalize_candidate(raw, origin)
                _validate_identity(row, origin, ordinal)
                key = _candidate_key(row)
                previous = records.get(key)
                if previous is None:
                    records[key] = row
                    origins[key] = origin
                    continue
                if previous["target"] == row["target"] and previous["token_signature"] == row["token_signature"]:
                    duplicate_count += 1
                    continue
                incoming_priority = _repair_priority(origin)
                existing_priority = _repair_priority(origins[key])
                if incoming_priority > existing_priority:
                    records[key] = row
                    origins[key] = origin
                    replacement_count += 1
                    continue
                if existing_priority > incoming_priority:
                    replacement_count += 1
                    continue
                conflicts.append({
                    "key": key,
                    "existing_origin": origins[key],
                    "incoming_origin": origin,
                    "existing_target": previous["target"],
                    "incoming_target": row["target"],
                })
            counts[origin] = origin_count
    except (OSError, ValueError) as exc:
        summary = {
            "schema": "lotro-translation-aggregate-v1",
            "status": "error",
            "error": str(exc),
            "dat_writes": 0,
        }
        args.summary.parent.mkdir(parents=True, exist_ok=True)
        args.summary.write_text(json.dumps(summary, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
        print("TRANSLATION_AGGREGATE|" + json.dumps(summary, ensure_ascii=False, sort_keys=True))
        return 2

    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.summary.parent.mkdir(parents=True, exist_ok=True)
    if conflicts:
        summary = {
            "schema": "lotro-translation-aggregate-v1",
            "status": "conflict",
            "input_counts": dict(counts),
            "unique_records": len(records),
            "duplicate_records": duplicate_count,
            "replacement_records": replacement_count,
            "conflict_count": len(conflicts),
            "conflicts": conflicts[:100],
            "dat_writes": 0,
        }
        args.summary.write_text(json.dumps(summary, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
        print("TRANSLATION_AGGREGATE|" + json.dumps(summary, ensure_ascii=False, sort_keys=True))
        return 2

    with args.output.open("w", encoding="utf-8", newline="\n") as handle:
        for key in sorted(records):
            handle.write(json.dumps(records[key], ensure_ascii=False, sort_keys=True) + "\n")

    status_counts = Counter(row["translation_status"] for row in records.values())
    engine_counts = Counter(row["translation_engine"] for row in records.values())
    summary = {
        "schema": "lotro-translation-aggregate-v1",
        "status": "ok",
        "input_counts": dict(counts),
        "unique_records": len(records),
        "duplicate_records": duplicate_count,
        "replacement_records": replacement_count,
        "conflict_count": 0,
        "status_counts": dict(sorted(status_counts.items())),
        "engine_counts": dict(sorted(engine_counts.items())),
        "output": str(args.output),
        "dat_writes": 0,
    }
    args.summary.write_text(json.dumps(summary, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print("TRANSLATION_AGGREGATE|" + json.dumps(summary, ensure_ascii=False, sort_keys=True))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
