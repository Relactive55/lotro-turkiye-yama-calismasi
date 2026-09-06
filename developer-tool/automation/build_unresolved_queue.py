"""List source rows that still have no accepted translation candidate."""

from __future__ import annotations

import argparse
import csv
import json
from collections import Counter
from pathlib import Path

from audit_curated_translations import decode_pool_value


def key(row: dict) -> tuple[str, str, str]:
    return (
        str(row.get("entry_identity", "")),
        str(row.get("dat_key", row.get("key", ""))),
        str(row.get("source_digest", "")),
    )


def load_accepted(path: Path) -> set[tuple[str, str, str]]:
    accepted = set()
    with path.open("r", encoding="utf-8-sig") as handle:
        for line in handle:
            if line.strip():
                row = json.loads(line)
                item_key = key(row)
                if not all(item_key):
                    raise ValueError(f"accepted candidate has incomplete key: {item_key!r}")
                accepted.add(item_key)
    return accepted


def emit_row(output, row: dict, accepted: set[tuple[str, str, str]], seen: set[tuple[str, str, str]], counts: Counter) -> None:
    item_key = key(row)
    if not all(item_key) or item_key in accepted or item_key in seen:
        return
    seen.add(item_key)
    critical = bool(row.get("critical_ui"))
    counts["critical_ui" if critical else "noncritical"] += 1
    output.write(json.dumps({
        "critical_ui": critical,
        "dat_key": item_key[1],
        "entry_identity": item_key[0],
        "english": row.get("english", ""),
        "source_digest": item_key[2],
        "token_signature": row.get("token_signature", ""),
        "review_status": row.get("status", "UNTRANSLATED"),
    }, ensure_ascii=False, sort_keys=True) + "\n")


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--accepted", required=True, type=Path)
    parser.add_argument("--review-queue", required=True, type=Path)
    parser.add_argument("--repair-input", required=True, type=Path)
    parser.add_argument("--output", required=True, type=Path)
    parser.add_argument("--summary", required=True, type=Path)
    args = parser.parse_args()

    accepted = load_accepted(args.accepted)
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.summary.parent.mkdir(parents=True, exist_ok=True)
    seen: set[tuple[str, str, str]] = set()
    counts = Counter()
    with args.output.open("w", encoding="utf-8", newline="\n") as output:
        with args.review_queue.open("r", encoding="utf-8-sig", newline="") as handle:
            for row in csv.DictReader(handle, delimiter="\t"):
                row["entry_identity"] = row.get("entry_identity", "")
                row["dat_key"] = row.get("key", "")
                row["english"] = decode_pool_value(row.get("source", "") or "").replace("\r\n", "\n").replace("\r", "\n")
                row["critical_ui"] = row.get("critical_ui", "0") == "1"
                emit_row(output, row, accepted, seen, counts)
        with args.repair_input.open("r", encoding="utf-8-sig") as handle:
            for line in handle:
                if line.strip():
                    emit_row(output, json.loads(line), accepted, seen, counts)
    summary = {
        "schema": "lotro-unresolved-review-queue-v1",
        "accepted_candidates": len(accepted),
        "unresolved_rows": len(seen),
        "critical_ui_rows": counts["critical_ui"],
        "noncritical_rows": counts["noncritical"],
        "output": str(args.output),
        "dat_writes": 0,
    }
    args.summary.write_text(json.dumps(summary, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print("UNRESOLVED_QUEUE|" + json.dumps(summary, ensure_ascii=False, sort_keys=True))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
