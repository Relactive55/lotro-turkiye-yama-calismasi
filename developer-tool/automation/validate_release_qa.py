"""Fail-closed release QA summary gate.

The private translation pipeline can pass its aggregate summary to this tool
without exposing English source text.  A production hand-off is accepted only
when every unresolved/review/machine/conflict counter is zero.
"""

from __future__ import annotations

import argparse
import json
from pathlib import Path


ZERO_KEYS = (
    "machine_only_records",
    "untranslated_english_records",
    "mixed_language_records",
    "terminology_violations",
    "duplicate_conflicting_targets",
    "critical_review_required",
    "review_required",
    "conflict_count",
)


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--summary", required=True, type=Path)
    parser.add_argument("--output", required=True, type=Path)
    args = parser.parse_args()
    summary = json.loads(args.summary.read_text(encoding="utf-8-sig"))
    status_counts = summary.get("status_counts", {}) or {}
    reasons = summary.get("rejection_reasons", {}) or {}
    normalized = {
        "schema": "lotro-release-qa-v1",
        "status": summary.get("status", "unknown"),
        "total_records": int(summary.get("rows", summary.get("unique_records", 0)) or 0),
        "translated_records": int(summary.get("accepted", summary.get("unique_records", 0)) or 0),
        "protected_records": int(summary.get("protected_records", 0) or 0),
        "machine_only_records": int(status_counts.get("MACHINE_TRANSLATED", 0) or 0),
        "untranslated_english_records": int(status_counts.get("UNTRANSLATED", 0) or 0),
        "mixed_language_records": int(summary.get("mixed_language_records", 0) or 0),
        "terminology_violations": int(summary.get("terminology_violations", 0) or 0),
        "duplicate_conflicting_targets": int(summary.get("conflict_count", 0) or 0),
        "conflict_count": int(summary.get("conflict_count", 0) or 0),
        "critical_ui_reviewed": int(summary.get("critical_ui_reviewed", 0) or 0),
        "critical_review_required": int(
            summary.get("critical_review_required", status_counts.get("REVIEW_REQUIRED", 0)) or 0
        ),
        "review_required": int(status_counts.get("REVIEW_REQUIRED", 0) or 0),
        "rejection_reasons": reasons,
        "source_summary": str(args.summary),
    }
    failures = [key for key in ZERO_KEYS if normalized[key] != 0]
    if normalized["status"] not in {"ok", "pass", "VERIFIED_ROOT"}:
        failures.append("status")
    normalized["status"] = "PASS" if not failures else "FAIL"
    normalized["failures"] = failures
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(normalized, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print("RELEASE_QA|" + json.dumps(normalized, ensure_ascii=False, sort_keys=True))
    return 0 if not failures else 2


if __name__ == "__main__":
    raise SystemExit(main())
