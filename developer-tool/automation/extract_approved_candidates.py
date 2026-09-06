"""Extract non-machine candidates that the generator itself marked safe."""

from __future__ import annotations

import argparse
import json
from collections import Counter
from pathlib import Path


ALLOWED = {"TM_REUSED", "HUMAN_APPROVED", "GLOSSARY"}


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--input", required=True, type=Path)
    parser.add_argument("--output", required=True, type=Path)
    parser.add_argument("--summary", required=True, type=Path)
    args = parser.parse_args()
    counts = Counter()
    selected = 0
    with args.input.open("r", encoding="utf-8-sig") as input_file, args.output.open("w", encoding="utf-8", newline="\n") as output_file:
        for line in input_file:
            if not line.strip():
                continue
            row = json.loads(line)
            status = str(row.get("translation_status", ""))
            counts[status] += 1
            if status not in ALLOWED or str(row.get("quality_error", "") or ""):
                continue
            output_file.write(json.dumps(row, ensure_ascii=False, sort_keys=True) + "\n")
            selected += 1
    summary = {
        "schema": "lotro-approved-candidate-extract-v1",
        "input": str(args.input),
        "input_status_counts": dict(sorted(counts.items())),
        "selected": selected,
        "output": str(args.output),
        "dat_writes": 0,
    }
    args.summary.parent.mkdir(parents=True, exist_ok=True)
    args.summary.write_text(json.dumps(summary, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print("APPROVED_EXTRACT|" + json.dumps(summary, ensure_ascii=False, sort_keys=True))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
