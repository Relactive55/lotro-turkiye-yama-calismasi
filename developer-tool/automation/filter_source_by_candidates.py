"""Create a retry source list by removing already accepted candidate keys."""

from __future__ import annotations

import argparse
import json
from pathlib import Path


def key(row: dict) -> tuple[str, str, str]:
    return (
        str(row.get("entry_identity", "")),
        str(row.get("dat_key", row.get("key", ""))),
        str(row.get("source_digest", "")),
    )


def read_rows(path: Path):
    with path.open("r", encoding="utf-8-sig") as handle:
        for line in handle:
            if line.strip():
                yield json.loads(line)


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--source", required=True, type=Path)
    parser.add_argument("--accepted", action="append", required=True, type=Path)
    parser.add_argument("--output", required=True, type=Path)
    parser.add_argument("--summary", required=True, type=Path)
    args = parser.parse_args()

    accepted: set[tuple[str, str, str]] = set()
    for path in args.accepted:
        for row in read_rows(path):
            item_key = key(row)
            if not all(item_key):
                raise ValueError(f"accepted candidate has incomplete key: {path}: {item_key!r}")
            accepted.add(item_key)

    args.output.parent.mkdir(parents=True, exist_ok=True)
    total = kept = removed = 0
    with args.output.open("w", encoding="utf-8", newline="\n") as output:
        for row in read_rows(args.source):
            total += 1
            item_key = key(row)
            if item_key in accepted:
                removed += 1
                continue
            kept += 1
            output.write(json.dumps(row, ensure_ascii=False, sort_keys=True) + "\n")
    summary = {
        "schema": "lotro-source-retry-filter-v1",
        "source": str(args.source),
        "accepted_inputs": [str(path) for path in args.accepted],
        "total_rows": total,
        "removed_rows": removed,
        "remaining_rows": kept,
        "output": str(args.output),
        "dat_writes": 0,
    }
    args.summary.parent.mkdir(parents=True, exist_ok=True)
    args.summary.write_text(json.dumps(summary, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print("SOURCE_RETRY_FILTER|" + json.dumps(summary, ensure_ascii=False, sort_keys=True))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
