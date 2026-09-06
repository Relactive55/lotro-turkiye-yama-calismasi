"""Build a private, quality-filtered context from the curated TSV pool."""

from __future__ import annotations

import argparse
import csv
import json
from collections import Counter
from pathlib import Path

from audit_aggregate_candidates import protected_escape_problem
from audit_curated_translations import decode_pool_value, protected_format_problem
from translation_pipeline import quality_problem


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--curated", required=True, type=Path)
    parser.add_argument("--base-context", required=True, type=Path)
    parser.add_argument("--output", required=True, type=Path)
    parser.add_argument("--summary", required=True, type=Path)
    args = parser.parse_args()

    base = json.loads(args.base_context.read_text(encoding="utf-8-sig"))
    approved: dict[str, str] = {}
    tm: dict[str, str] = {}
    glossary: dict[str, str] = {}
    invalid = Counter()
    conflicts = 0
    rows = 0
    accepted = 0
    priority = {"TM_REUSED": 1, "GLOSSARY": 2, "HUMAN_APPROVED": 3}
    chosen: dict[str, tuple[int, str, str]] = {}
    with args.curated.open("r", encoding="utf-8-sig", newline="") as handle:
        reader = csv.DictReader(handle, delimiter="\t")
        for row in reader:
            rows += 1
            status = str(row.get("status", "") or "")
            if status not in priority:
                invalid["unknown status"] += 1
                continue
            source = decode_pool_value(row.get("source", "") or "").replace("\r\n", "\n").replace("\r", "\n")
            target = decode_pool_value(row.get("target", "") or "")
            problem = protected_format_problem(source, target) or protected_escape_problem(source, target) or quality_problem(source, target)
            if problem:
                invalid[problem] += 1
                continue
            if not source.strip() or not target.strip():
                invalid["empty source/target"] += 1
                continue
            old = chosen.get(source)
            rank = priority[status]
            if old is not None and old[1] != target:
                # A conflicting source text is not safe to use as context;
                # keep the higher-trust row only when its status is stronger.
                if old[0] == rank:
                    conflicts += 1
                    chosen.pop(source, None)
                    continue
                if old[0] > rank:
                    continue
            chosen[source] = (rank, target, status)
            accepted += 1

    for source, (_, target, status) in chosen.items():
        if status == "HUMAN_APPROVED":
            approved[source] = target
        elif status == "GLOSSARY":
            glossary[source] = target
        else:
            tm[source] = target
    context = {
        "approved": approved,
        "tm": tm,
        "glossary": glossary,
        "protected_names": base.get("protected_names", []),
    }
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.summary.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(context, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    summary = {
        "schema": "lotro-full-translation-context-v1",
        "input_rows": rows,
        "quality_valid_rows": accepted,
        "unique_sources": len(chosen),
        "approved_sources": len(approved),
        "tm_sources": len(tm),
        "glossary_sources": len(glossary),
        "conflicting_sources": conflicts,
        "excluded_rows": dict(sorted(invalid.items())),
        "output": str(args.output),
        "dat_writes": 0,
    }
    args.summary.write_text(json.dumps(summary, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print("FULL_CONTEXT|" + json.dumps(summary, ensure_ascii=False, sort_keys=True))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
