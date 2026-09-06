"""Emit exact context matches as reusable translation candidates.

This is the fast path for a review queue: no model is invoked when an
approved, glossary, or unambiguous TM translation already exists for the
English source.  It never writes a DAT.
"""

from __future__ import annotations

import argparse
import json
from pathlib import Path

from translation_pipeline import quality_problem


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--input", required=True, type=Path)
    parser.add_argument("--context", required=True, type=Path)
    parser.add_argument("--output", required=True, type=Path)
    parser.add_argument("--summary", required=True, type=Path)
    args = parser.parse_args()

    context = json.loads(args.context.read_text(encoding="utf-8-sig"))
    approved = dict(context.get("approved", {}) or {})
    tm = dict(context.get("tm", {}) or {})
    glossary = dict(context.get("glossary", {}) or {})
    rows = matches = 0
    args.output.parent.mkdir(parents=True, exist_ok=True)
    with args.input.open("r", encoding="utf-8-sig") as source, args.output.open("w", encoding="utf-8", newline="\n") as target:
        for line in source:
            if not line.strip():
                continue
            rows += 1
            row = json.loads(line)
            if bool(row.get("critical_ui")):
                continue
            english = str(row.get("english", ""))
            if english in approved:
                value, status, engine = approved[english], "HUMAN_APPROVED", "approved"
            elif english in tm:
                value, status, engine = tm[english], "TM_REUSED", "tm"
            elif english in glossary:
                value, status, engine = glossary[english], "GLOSSARY", "glossary"
            else:
                continue
            value = str(value or "")
            problem = quality_problem(english, value)
            if problem:
                continue
            target.write(json.dumps({
                "entry_identity": row.get("entry_identity", ""),
                "dat_key": row.get("dat_key", ""),
                "source_digest": row.get("source_digest", ""),
                "token_signature": row.get("token_signature", ""),
                "target": value,
                "translation_status": status,
                "translation_engine": engine,
                "translation_engine_version": "context-v4",
                "critical_ui": False,
                "quality_error": "",
            }, ensure_ascii=False, sort_keys=True) + "\n")
            matches += 1
    summary = {
        "schema": "lotro-context-candidate-extract-v1",
        "input_rows": rows,
        "matches": matches,
        "output": str(args.output),
        "dat_writes": 0,
    }
    args.summary.parent.mkdir(parents=True, exist_ok=True)
    args.summary.write_text(json.dumps(summary, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print("CONTEXT_CANDIDATES|" + json.dumps(summary, ensure_ascii=False, sort_keys=True))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
