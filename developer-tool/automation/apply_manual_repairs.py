"""Materialize reviewed target text for selected private source rows."""

from __future__ import annotations

import argparse
import json
import re
from pathlib import Path


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--input", required=True, type=Path)
    parser.add_argument("--targets", required=True, type=Path)
    parser.add_argument("--output", required=True, type=Path)
    parser.add_argument("--summary", required=True, type=Path)
    args = parser.parse_args()

    targets = json.loads(args.targets.read_text(encoding="utf-8-sig"))
    selected = 0
    missing = []
    with args.input.open("r", encoding="utf-8-sig") as input_file, args.output.open("w", encoding="utf-8", newline="\n") as output_file:
        seen = set()
        for line in input_file:
            if not line.strip():
                continue
            source = json.loads(line)
            dat_key = str(source.get("dat_key", ""))
            target = targets.get(dat_key)
            if target is None:
                missing.append(dat_key)
                continue
            if dat_key in seen:
                raise ValueError(f"duplicate manual repair key: {dat_key}")
            seen.add(dat_key)
            is_technical_key = target == str(source.get("english", "")) and bool(re.fullmatch(r"[A-Za-z0-9_.-]+(?:\\[A-Za-z0-9_.-]+)+", target))
            output_file.write(json.dumps({
                "critical_ui": bool(source.get("critical_ui")),
                "dat_key": dat_key,
                "entry_identity": source.get("entry_identity", ""),
                "quality_error": "",
                "source_digest": source.get("source_digest", ""),
                "target": target,
                "token_signature": source.get("token_signature", ""),
                "translation_engine": "technical-key" if is_technical_key else "codex-assistant",
                "translation_engine_version": "2026-09-06-technical-preserve" if is_technical_key else "2026-09-06-manual-structural",
                "translation_status": "GLOSSARY" if is_technical_key else "MACHINE_TRANSLATED",
            }, ensure_ascii=False, sort_keys=True) + "\n")
            selected += 1
    summary = {
        "schema": "lotro-manual-repair-candidate-v1",
        "input": str(args.input),
        "target_map": str(args.targets),
        "selected": selected,
        "unmatched_input_rows": len(missing),
        "unmatched_keys": missing,
        "output": str(args.output),
        "dat_writes": 0,
    }
    args.summary.parent.mkdir(parents=True, exist_ok=True)
    args.summary.write_text(json.dumps(summary, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print("MANUAL_REPAIR|" + json.dumps(summary, ensure_ascii=False, sort_keys=True))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
