"""Keep only non-critical source rows for automatic translation retries."""

from __future__ import annotations

import argparse
import json
from pathlib import Path


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--input", required=True, type=Path)
    parser.add_argument("--output", required=True, type=Path)
    parser.add_argument("--summary", required=True, type=Path)
    args = parser.parse_args()
    total = kept = 0
    args.output.parent.mkdir(parents=True, exist_ok=True)
    with args.input.open("r", encoding="utf-8-sig") as input_file, args.output.open("w", encoding="utf-8", newline="\n") as output_file:
        for line in input_file:
            if not line.strip():
                continue
            row = json.loads(line)
            total += 1
            if bool(row.get("critical_ui")):
                continue
            output_file.write(json.dumps(row, ensure_ascii=False, sort_keys=True) + "\n")
            kept += 1
    summary = {"schema": "lotro-noncritical-filter-v1", "input_rows": total, "kept_rows": kept, "output": str(args.output), "dat_writes": 0}
    args.summary.parent.mkdir(parents=True, exist_ok=True)
    args.summary.write_text(json.dumps(summary, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print("NONCRITICAL_FILTER|" + json.dumps(summary, ensure_ascii=False, sort_keys=True))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
