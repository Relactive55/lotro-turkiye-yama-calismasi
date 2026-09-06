"""Audit human-approved and rule-backed translations without touching a DAT.

The curated pool is treated as protected input: valid targets are preserved
byte-for-byte by downstream candidate generation, while invalid targets are
copied to a private review queue. This audit never replaces a human/rule
translation automatically.
"""

from __future__ import annotations

import argparse
import csv
import hashlib
import json
import re
import unicodedata
from collections import Counter
from contextlib import nullcontext
from pathlib import Path

try:
    from translation_pipeline import bracket_context_signature, quality_problem, token_signature
except ImportError:  # direct invocation from the automation directory
    from developer_tool.automation.translation_pipeline import bracket_context_signature, quality_problem, token_signature


CURATED_STATUSES = {"HUMAN_APPROVED", "TM_REUSED", "GLOSSARY"}
CATALOG_TOKEN_RE = re.compile(
    r"%(?:\d+\$)?[sdif]|\{\d+(?:[^}]*)?\}|\\[nrt]|</?[^<>]+>|(?<![\w])\[[^\]\r\n]{1,64}\]"
)
# .NET's source regex excludes Unicode letters and underscore, but deliberately
# does not exclude a preceding digit; that makes ``1-0`` parse as ``1``, ``-0``.
CATALOG_NUMBER_RE = re.compile(r"(?<![^\W\d_])(?<!_)[-+]?(?:\d+(?:[.,]\d+)?)(?![^\W\d_]|_)")


def _catalog_tokens(text: str) -> tuple[list[str], list[str], bool]:
    """Mirror ProtectedFormat.GetTokenSignature/Validate for audit purposes."""
    tokens = [match.group(0) for match in CATALOG_TOKEN_RE.finditer(text or "")]
    spans = [(match.start(), match.end()) for match in CATALOG_TOKEN_RE.finditer(text or "")]
    stack: list[str] = []
    valid = True
    for token in tokens:
        if not token.startswith("<"):
            continue
        inner = token[1:-1].strip()
        if not inner:
            valid = False
            break
        closing = inner.startswith("/")
        if closing:
            inner = inner[1:].strip()
        self_closing = inner.endswith("/")
        if self_closing:
            inner = inner[:-1].strip()
        tag = re.match(r"^[A-Za-z][A-Za-z0-9_.:-]*", inner)
        if not tag:
            valid = False
            break
        name = tag.group(0)
        if self_closing:
            continue
        if closing:
            if not stack or stack.pop().lower() != name.lower():
                valid = False
                break
        else:
            stack.append(name)
    if stack:
        valid = False
    numbers: list[str] = []
    for match in CATALOG_NUMBER_RE.finditer(text or ""):
        if any(start < match.end() and match.start() < end for start, end in spans):
            continue
        numbers.append(match.group(0).replace(",", "."))
    return tokens, numbers, valid


def catalog_token_signature(text: str) -> str:
    tokens, numbers, valid = _catalog_tokens(text)
    canonical = "lotro-token-signature-v1|" + ("valid|" if valid else "invalid|") + "tokens="
    canonical += "".join(f"{len(token)}:{token}|" for token in tokens)
    canonical += "numbers=" + "".join(f"{len(number)}:{number}|" for number in numbers)
    return hashlib.sha256(canonical.encode("utf-8")).hexdigest()


def decode_pool_value(value: str) -> str:
    """Decode the pool extractor's reversible TSV escaping."""
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


def protected_format_problem(source: str, target: str) -> str:
    source_tokens, source_numbers, source_valid = _catalog_tokens(source)
    target_tokens, target_numbers, target_valid = _catalog_tokens(target)
    if not source_valid:
        return "source protected format invalid"
    if not target_valid:
        return "translation protected format invalid"
    if source_tokens != target_tokens:
        return "ordered protected token stream differs"
    if source_numbers != target_numbers:
        return "numeric/game value stream differs"
    if bracket_context_signature(source) != bracket_context_signature(target):
        return "protected bracket placement changed"
    return ""


def audit_row(row: dict[str, str]) -> str:
    source = unicodedata.normalize("NFKC", decode_pool_value(row.get("source", "") or "")).replace("\r\n", "\n").replace("\r", "\n")
    target = decode_pool_value(row.get("target", "") or "")
    stored_signature = row.get("token_signature", "") or ""
    if not source.strip():
        return "empty source"
    if not target.strip():
        return "empty curated target"
    if row.get("status", "") not in CURATED_STATUSES:
        return "unknown curated status"
    if stored_signature and catalog_token_signature(source) != stored_signature:
        return "stored source token signature mismatch"
    problem = protected_format_problem(source, target) or quality_problem(source, target)
    if problem:
        return problem
    if "\x00" in target:
        return "NUL byte in curated target"
    return ""


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--input", required=True, help="private curated-pool.tsv")
    parser.add_argument("--review-output", required=True, help="private invalid-row TSV")
    parser.add_argument("--summary-output", required=True, help="private JSON summary")
    parser.add_argument("--safe-output", help="private TSV containing only validated curated rows")
    parser.add_argument("--repair-input", help="private JSONL input for model repair candidates")
    args = parser.parse_args()

    input_path = Path(args.input)
    review_path = Path(args.review_output)
    summary_path = Path(args.summary_output)
    safe_path = Path(args.safe_output) if args.safe_output else None
    repair_path = Path(args.repair_input) if args.repair_input else None
    review_path.parent.mkdir(parents=True, exist_ok=True)
    summary_path.parent.mkdir(parents=True, exist_ok=True)
    if safe_path:
        safe_path.parent.mkdir(parents=True, exist_ok=True)
    if repair_path:
        repair_path.parent.mkdir(parents=True, exist_ok=True)
    counts = Counter()
    reasons = Counter()
    total = valid = invalid = 0
    safe_context = safe_path.open("w", encoding="utf-8", newline="") if safe_path else nullcontext(None)
    repair_context = repair_path.open("w", encoding="utf-8") if repair_path else nullcontext(None)
    with input_path.open("r", encoding="utf-8-sig", newline="") as source_file, review_path.open("w", encoding="utf-8", newline="") as review_file, safe_context as safe_file, repair_context as repair_file:
        reader = csv.DictReader(source_file, delimiter="\t")
        original_fields = list(reader.fieldnames or [])
        fieldnames = original_fields + ["audit_reason"]
        writer = csv.DictWriter(review_file, fieldnames=fieldnames, delimiter="\t", lineterminator="\n")
        writer.writeheader()
        safe_writer = None
        if safe_path:
            safe_writer = csv.DictWriter(safe_file, fieldnames=original_fields, delimiter="\t", lineterminator="\n")
            safe_writer.writeheader()
        for row in reader:
            total += 1
            status = row.get("status", "") or ""
            counts[status] += 1
            reason = audit_row(row)
            if reason:
                invalid += 1
                reasons[reason] += 1
                row["audit_reason"] = reason
                writer.writerow(row)
                if repair_path:
                    source = unicodedata.normalize("NFKC", decode_pool_value(row.get("source", "") or "")).replace("\r\n", "\n").replace("\r", "\n")
                    repair_file.write(json.dumps({
                        "english": source,
                        "entry_identity": row.get("entry_identity", ""),
                        "dat_key": row.get("key", ""),
                        "source_digest": row.get("source_digest", ""),
                        "token_signature": row.get("token_signature", ""),
                        "critical_ui": row.get("critical_ui", "0") == "1",
                        "curated_status": row.get("status", ""),
                        "curated_quality_error": reason,
                        "previous_target": decode_pool_value(row.get("target", "") or ""),
                    }, ensure_ascii=False, sort_keys=True) + "\n")
            else:
                valid += 1
                if safe_writer:
                    safe_writer.writerow({key: row.get(key, "") for key in original_fields})
    summary = {
        "schema": "lotro-curated-quality-audit-v1",
        "input": str(input_path),
        "total_rows": total,
        "valid_rows": valid,
        "invalid_rows": invalid,
        "status_counts": dict(sorted(counts.items())),
        "invalid_reason_counts": dict(sorted(reasons.items())),
        "review_queue": str(review_path),
        "safe_pool": str(safe_path) if safe_path else None,
        "repair_input": str(repair_path) if repair_path else None,
        "dat_writes": 0,
    }
    summary_path.write_text(json.dumps(summary, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print("CURATED_AUDIT|" + json.dumps(summary, ensure_ascii=False, sort_keys=True))
    return 0 if invalid == 0 else 2


if __name__ == "__main__":
    raise SystemExit(main())
