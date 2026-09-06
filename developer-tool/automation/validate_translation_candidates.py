"""Independent validation for machine or repair candidates.

This is deliberately separate from generation: a candidate must pass the
protected-format guard twice before it can be handed to a semantic patch
review. The script never writes a DAT.
"""

from __future__ import annotations

import argparse
import json
import unicodedata
from collections import Counter
from pathlib import Path

from audit_curated_translations import catalog_token_signature, protected_format_problem
from translation_pipeline import quality_problem


def source_record_key(row: dict) -> tuple[str, str, str]:
    """Identify one catalog string, not merely its containing entry.

    An entry identity can contain several text fields.  The data key and
    immutable source digest are therefore part of the fail-closed join between
    a private English source row and a candidate.
    """
    return (
        str(row.get("entry_identity", "")),
        str(row.get("dat_key", "")),
        str(row.get("source_digest", "")),
    )


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--input", required=True)
    parser.add_argument("--source", required=True, help="private JSONL carrying the English source for validation")
    parser.add_argument("--output", required=True)
    parser.add_argument("--summary", required=True)
    args = parser.parse_args()
    input_path = Path(args.input)
    source_path = Path(args.source)
    output_path = Path(args.output)
    summary_path = Path(args.summary)
    output_path.parent.mkdir(parents=True, exist_ok=True)
    summary_path.parent.mkdir(parents=True, exist_ok=True)
    accepted = 0
    rejected = 0
    reasons = Counter()
    rows = 0
    source_by_record: dict[tuple[str, str, str], dict] = {}
    with source_path.open("r", encoding="utf-8-sig") as source_file:
        for line in source_file:
            if line.strip():
                source_row = json.loads(line)
                key = source_record_key(source_row)
                if not all(key):
                    raise ValueError("source row is missing entry_identity, dat_key, or source_digest")
                if key in source_by_record:
                    raise ValueError("duplicate source record identity")
                source_by_record[key] = source_row
    with input_path.open("r", encoding="utf-8-sig") as source_file, output_path.open("w", encoding="utf-8") as target_file:
        for line in source_file:
            if not line.strip():
                continue
            rows += 1
            row = json.loads(line)
            source_row = source_by_record.get(source_record_key(row), {})
            source = unicodedata.normalize("NFKC", str(source_row.get("english", ""))).replace("\r\n", "\n").replace("\r", "\n")
            target = str(row.get("target", ""))
            reason = ""
            if not source_row:
                reason = "source record missing"
            elif row.get("translation_status") != "MACHINE_TRANSLATED":
                reason = "candidate is not machine translated"
            elif bool(row.get("critical_ui")):
                reason = "critical UI candidate is not admitted"
            elif row.get("token_signature") and catalog_token_signature(source) != str(row.get("token_signature")):
                reason = "source token signature mismatch"
            else:
                reason = protected_format_problem(source, target) or quality_problem(source, target)
            if reason:
                rejected += 1
                reasons[reason] += 1
                continue
            accepted += 1
            target_file.write(json.dumps({
                key: value for key, value in row.items()
                if key not in {"english", "previous_target", "curated_status", "curated_quality_error"}
            }, ensure_ascii=False, sort_keys=True) + "\n")
    summary = {
        "schema": "lotro-candidate-validation-v1",
        "input": str(input_path),
        "rows": rows,
        "accepted": accepted,
        "rejected": rejected,
        "rejection_reasons": dict(sorted(reasons.items())),
        "safe_output": str(output_path),
        "dat_writes": 0,
    }
    summary_path.write_text(json.dumps(summary, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print("CANDIDATE_VALIDATION|" + json.dumps(summary, ensure_ascii=False, sort_keys=True))
    return 0 if rejected == 0 else 2


if __name__ == "__main__":
    raise SystemExit(main())
