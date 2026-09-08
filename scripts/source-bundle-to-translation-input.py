"""Turn private source bundles into the private JSONL input expected by the
translation pipeline.

The output contains the English source and must stay on the private runner.
The translation pipeline deliberately removes that field from its candidate
output before anything is copied to the public repository.
"""

from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path


HEX64 = set("0123456789abcdefABCDEF")


def require_digest(value: object, name: str) -> str:
    text = str(value or "")
    if len(text) != 64 or any(char not in HEX64 for char in text):
        raise ValueError(f"{name} must be a SHA-256 digest")
    return text.lower()


def load_bundle(path: Path) -> dict:
    try:
        value = json.loads(path.read_text(encoding="utf-8-sig"))
    except (OSError, json.JSONDecodeError) as exc:
        raise ValueError(f"invalid source bundle {path}: {exc}") from exc
    if not isinstance(value, dict):
        raise ValueError(f"source bundle is not an object: {path}")
    if value.get("schema_version") != 1 or value.get("bundle_kind") != "client_assisted_source_update":
        raise ValueError(f"source bundle schema is invalid: {path}")
    require_digest(value.get("source_dat_sha256"), "source_dat_sha256")
    require_digest(value.get("source_catalog_sha256"), "source_catalog_sha256")
    if int(value.get("source_dat_size") or 0) < 1:
        raise ValueError(f"source DAT size is invalid: {path}")
    records = value.get("records")
    if not isinstance(records, list) or len(records) > 100000:
        raise ValueError(f"source records are invalid: {path}")
    return value


def validate_record(record: object, path: Path) -> dict:
    if not isinstance(record, dict):
        raise ValueError(f"source record is not an object: {path}")
    identity = str(record.get("entry_identity") or "")
    dat_key = str(record.get("dat_key") or "")
    source = record.get("source")
    if not identity or not dat_key or not isinstance(source, str) or not source.strip():
        raise ValueError(f"source record has no private English source: {path}")
    if len(source) > 16000:
        raise ValueError(f"source record is too long: {path}")
    try:
        did = int(record["did"])
        record_index = int(record["record_index"])
        group_index = int(record["group_index"])
        index_in_group = int(record["index_in_group"])
    except (KeyError, TypeError, ValueError) as exc:
        raise ValueError(f"source record coordinates are invalid: {path}") from exc
    if did < 0 or record_index < 0 or group_index < -1 or index_in_group < 0:
        raise ValueError(f"source record coordinates are invalid: {path}")
    expected_key = f"{did:08X}:{record_index}:{group_index}:{index_in_group}"
    if dat_key != expected_key:
        raise ValueError(f"source record DAT key does not match coordinates: {path}")
    if str(record.get("classification") or "") not in {"NEW", "MODIFIED"}:
        raise ValueError(f"source record classification is invalid: {path}")
    source_digest = require_digest(record.get("source_digest"), "source_digest")
    token_signature = require_digest(record.get("token_signature"), "token_signature")
    return {
        "entry_identity": identity,
        "dat_key": dat_key,
        "did": did,
        "record_index": record_index,
        "group_index": group_index,
        "index_in_group": index_in_group,
        "source_digest": source_digest,
        "token_signature": token_signature,
        "classification": str(record["classification"]),
        "critical_ui": bool(record.get("critical_ui")),
        "english": source,
    }


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--bundle", action="append", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--metadata", type=Path, required=True)
    args = parser.parse_args()

    seen: set[tuple[str, str, str]] = set()
    rows: list[dict] = []
    dat_identities: set[tuple[str, int]] = set()
    for bundle_path in args.bundle:
        bundle = load_bundle(bundle_path)
        dat_identity = (require_digest(bundle["source_dat_sha256"], "source_dat_sha256"), int(bundle["source_dat_size"]))
        dat_identities.add(dat_identity)
        for raw in bundle["records"]:
            row = validate_record(raw, bundle_path)
            key = (row["entry_identity"], row["dat_key"], row["source_digest"])
            if key in seen:
                raise ValueError(f"duplicate source identity across bundles: {key[0]}")
            seen.add(key)
            rows.append(row)
    if len(dat_identities) != 1:
        raise ValueError("source bundles do not share one DAT identity")

    args.output.parent.mkdir(parents=True, exist_ok=True)
    with args.output.open("w", encoding="utf-8", newline="\n") as handle:
        for row in rows:
            handle.write(json.dumps(row, ensure_ascii=False, sort_keys=True) + "\n")

    dat_sha, dat_size = next(iter(dat_identities))
    catalog_sha = hashlib.sha256("\n".join(sorted(key[0] + "|" + key[1] + "|" + key[2] for key in seen)).encode("utf-8")).hexdigest()
    metadata = {
        "schema": "lotro-private-translation-input-v1",
        "source_dat_sha256": dat_sha,
        "source_dat_size": dat_size,
        "source_bundle_count": len(args.bundle),
        "record_count": len(rows),
        "identity_fingerprint": catalog_sha,
        "public_output_contains_english": False,
    }
    args.metadata.parent.mkdir(parents=True, exist_ok=True)
    args.metadata.write_text(json.dumps(metadata, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print("SOURCE_INPUT|" + json.dumps(metadata, ensure_ascii=False, sort_keys=True))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
