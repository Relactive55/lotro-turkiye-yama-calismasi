"""Fast local OPUS candidate generation with CTranslate2.

This runner keeps the same candidate contract as ``translation_pipeline.py``
while translating only unprotected text segments.  LOTRO format tokens,
escaped line breaks, inline tags and configured proper names never enter the
model and are reassembled byte-for-byte before validation.
"""

from __future__ import annotations

import argparse
import json
import re
import sys
from pathlib import Path

TOOL_DIR = Path(__file__).resolve().parents[1]
AUTOMATION_DIR = Path(__file__).resolve().parent
sys.path.insert(0, str(TOOL_DIR))
sys.path.insert(0, str(AUTOMATION_DIR))

try:
    import ctranslate2
    from transformers import MarianTokenizer
except Exception as exc:  # pragma: no cover - optional local runner
    raise RuntimeError(f"CTranslate2 dependencies unavailable: {exc}") from exc

from lotro_gpu_translate import protected_spans, protected_value, rebuild
from translation_pipeline import load_context, quality_problem


NUMBER_TOKEN_RE = re.compile(r"(?<![A-Za-z_])[-+]?(?:\d+(?:[.,]\d+)?)(?:\s*%)?")
MACRO_TOKEN_RE = re.compile(r"#-?\d+:\{[^{}\r\n]*\}")
# Stop at LOTRO's escaped control tokens (``\\n``, ``\\q``) while retaining
# punctuation attached to a lore name such as ``Nazgûl,`` or ``Dúath.``.
NONASCII_WORD_RE = re.compile(r"(?<!\S)[^\s\\]*[^\x00-\x7F][^\s\\]*")
SENTENCE_BOUNDARY_RE = re.compile(r"(?<=[.!?…])(\s+)")
MAX_MODEL_PLAIN_CHARS = 260
AUTO_PROPER_WORD_RE = re.compile(r"(?<![\w\\])[A-Z][A-Za-z\u00C0-\u00D6\u00D8-\u00F6\u00F8-\u00FF'\u2019-]{2,}(?![\w])")
COMMON_CAPITALIZED_WORDS = {
    "A", "An", "And", "After", "All", "At", "Before", "Behind", "Below",
    "But", "By", "Each", "Even", "Every", "For", "From", "He", "Her",
    "Here", "His", "How", "I", "If", "In", "Into", "It", "Less", "Like",
    "More", "My", "Near", "No", "Now", "Of", "On", "Once", "Or", "Our",
    "Over", "She", "Since", "Some", "Still", "That", "The", "Their", "Them",
    "Then", "There", "These", "They", "This", "Those", "Through", "To", "Under",
    "Upon", "We", "When", "Where", "Which", "While", "Why", "With", "Without",
    "You", "Your", "Alas", "Fall", "Nine", "North-kingdom", "Middle-earth",
    "Enemy", "Speak", "King", "Mountain", "Mountains", "Land", "World", "Road",
    "Gate", "Bridge", "Camp", "Inn", "Keep", "House", "Book", "Chapter", "Section",
    "One", "Two", "Three", "Four", "Five", "Six", "Seven", "Eight", "Ten",
    "Old", "New", "Last", "First", "East", "West", "North", "South", "Black", "Red",
    "Speech", "Free", "Peoples", "Captain", "Master", "Lady", "Lord", "Queen",
}


def _hard_split_plain(value: str) -> list[tuple[str, str]]:
    """Split an unusually long prose span at whitespace without losing it."""
    if len(value) <= MAX_MODEL_PLAIN_CHARS:
        return [("text", value)] if value else []
    pieces: list[tuple[str, str]] = []
    cursor = 0
    while len(value) - cursor > MAX_MODEL_PLAIN_CHARS:
        limit = min(len(value), cursor + MAX_MODEL_PLAIN_CHARS)
        cut = value.rfind(" ", cursor + 1, limit + 1)
        if cut <= cursor:
            cut = limit
        if cut > cursor:
            pieces.append(("text", value[cursor:cut]))
        ws_end = cut
        while ws_end < len(value) and value[ws_end].isspace():
            ws_end += 1
        if ws_end > cut:
            pieces.append(("token", value[cut:ws_end]))
        cursor = ws_end
    if cursor < len(value):
        pieces.append(("text", value[cursor:]))
    return pieces


def split_plain_core(core: str) -> list[tuple[str, str]]:
    """Keep long paragraphs within the local model's reliable context window."""
    if len(core) <= MAX_MODEL_PLAIN_CHARS:
        return [("text", core)] if core else []
    pieces: list[tuple[str, str]] = []
    cursor = 0
    # Sentence-level chunks retain much more context than word-by-word
    # translation while avoiding <unk>/copy-through failures on lore pages.
    for match in SENTENCE_BOUNDARY_RE.finditer(core):
        pieces.extend(_hard_split_plain(core[cursor : match.start()]))
        pieces.append(("token", match.group(0)))
        cursor = match.end()
    pieces.extend(_hard_split_plain(core[cursor:]))
    return pieces


def split_protected_numeric(text: str, protected_names: list[str], auto_protect_proper: bool = False) -> tuple[list[tuple[str, object]], list[str]]:
    """Split format tokens, configured names and game values from model text.

    Numeric values are protected as whole spans (including a leading sign and
    percent sign) so a fast quantized pass cannot silently alter a game value.
    """
    base_spans = list(protected_spans(text, protected_names))
    # LOTRO plural/placeholder macros such as ``#1:{the[!n]}`` are not
    # ordinary prose.  Keep the complete macro intact instead of allowing a
    # quantized model to reorder its number or bracket marker.
    spans: list[tuple[int, int, str]] = []
    occupied: list[tuple[int, int]] = []
    for match in MACRO_TOKEN_RE.finditer(text or ""):
        spans.append((match.start(), match.end(), "MACRO"))
        occupied.append((match.start(), match.end()))
    for start, end, kind in base_spans:
        # Keep sentence punctuation attached to a protected proper name.  If
        # the period is left in the following prose span, Marian can emit an
        # ``<unk>`` for a fragment beginning with ``.`` and the whole record
        # would be discarded even though the translation is otherwise sound.
        if (kind.startswith("NAME") or kind == "AUTO_NAME") and end < len(text) and text[end] in ".,!?;:":
            end += 1
        if any(left < end and start < right for left, right in occupied):
            continue
        spans.append((start, end, kind))
        occupied.append((start, end))
    if auto_protect_proper:
        # Game-specific proper names are far more numerous than the small
        # static lore list. Preserve capitalized words occurring inside a
        # sentence so a missing Marian vocabulary item cannot become ``<unk>``.
        # This is opt-in because preserving every capitalized game noun can
        # reduce fluency in long prose; such drafts remain review-only.
        for match in AUTO_PROPER_WORD_RE.finditer(text or ""):
            raw = match.group(0)
            if raw in COMMON_CAPITALIZED_WORDS:
                continue
            prefix = (text[: match.start()] or "").rstrip()
            if not prefix or prefix.endswith((".", "!", "?", "\\n")):
                continue
            start, end = match.start(), match.end()
            if any(left < end and start < right for left, right in occupied):
                continue
            spans.append((start, end, "AUTO_NAME"))
            occupied.append((start, end))
    # Names with diacritics can become ``<unk>`` in the converted Marian
    # vocabulary.  Protect those words so the original lore spelling survives
    # the fast pass (plain ASCII names remain model-translatable).
    for match in NONASCII_WORD_RE.finditer(text or ""):
        if any(start < match.end() and match.start() < end for start, end in occupied):
            continue
        spans.append((match.start(), match.end(), "NAME"))
        occupied.append((match.start(), match.end()))
    occupied = [(start, end) for start, end, _ in spans]
    for match in NUMBER_TOKEN_RE.finditer(text or ""):
        if any(start < match.end() and match.start() < end for start, end in occupied):
            continue
        spans.append((match.start(), match.end(), "NUMBER"))
    spans.sort(key=lambda value: (value[0], value[1]))
    slots: list[tuple[str, object]] = []
    translatable: list[str] = []

    def add_plain(value: str) -> None:
        if not value:
            return
        leading = value[: len(value) - len(value.lstrip())]
        trailing = value[len(value.rstrip()) :]
        core = value.strip()
        if leading:
            slots.append(("token", leading))
        if core:
            for part_kind, part in split_plain_core(core):
                if part_kind == "text" and re.search(r"[^\W\d_]", part, flags=re.UNICODE):
                    slots.append(("text", len(translatable)))
                    translatable.append(part)
                else:
                    slots.append(("token", part))
        if trailing:
            slots.append(("token", trailing))

    cursor = 0
    for start, end, kind in spans:
        add_plain(text[cursor:start])
        raw = text[start:end]
        slots.append(("token", protected_value(raw, kind) if kind != "NUMBER" else raw))
        cursor = end
    add_plain(text[cursor:])
    return slots, translatable


def main() -> int:
    global MAX_MODEL_PLAIN_CHARS
    parser = argparse.ArgumentParser()
    parser.add_argument("--input", required=True, help="private source JSONL")
    parser.add_argument("--output", required=True, help="candidate JSONL")
    parser.add_argument("--context", help="private JSON with approved/tm/glossary/protected_names")
    parser.add_argument("--model", required=True, help="local Marian tokenizer directory")
    parser.add_argument("--ct2-model", required=True, help="local CTranslate2 model directory")
    parser.add_argument("--batch-size", type=int, default=64)
    parser.add_argument("--beams", type=int, default=3)
    parser.add_argument("--max-decoding-length", type=int, default=512)
    parser.add_argument("--inter-threads", type=int, default=1)
    parser.add_argument("--intra-threads", type=int, default=0)
    parser.add_argument("--compute-type", default="default")
    parser.add_argument("--length-bucket", action="store_true", help="group similar source lengths to avoid padding waste")
    parser.add_argument("--auto-protect-proper", action="store_true", help="opt-in proper-name fallback; keep its drafts for review")
    parser.add_argument("--max-plain-chars", type=int, default=MAX_MODEL_PLAIN_CHARS, help="maximum unprotected prose chunk size")
    args = parser.parse_args()

    MAX_MODEL_PLAIN_CHARS = max(64, int(args.max_plain_chars))

    approved, tm, glossary, protected_names = load_context(args.context)
    tokenizer = MarianTokenizer.from_pretrained(args.model, local_files_only=True)
    translator = ctranslate2.Translator(
        args.ct2_model,
        device="cpu",
        compute_type=args.compute_type,
        inter_threads=max(1, args.inter_threads),
        intra_threads=max(0, args.intra_threads),
    )

    input_path = Path(args.input)
    rows = [
        json.loads(line)
        for line in input_path.read_text(encoding="utf-8-sig").splitlines()
        if line.strip()
    ]

    # Each source is rebuilt from the same protected slots.  Identical plain
    # segments are deduplicated, which is important for repeated LOTRO UI
    # fragments spread across many DAT records.
    layouts: list[tuple[dict, list[str]]] = []
    segment_index: dict[str, int] = {}
    unique_segments: list[str] = []
    for row in rows:
        source = str(row.get("english", ""))
        # Context matches (and critical UI rows) do not need model inference.
        # Skipping their protected-span layout keeps large aggregate contexts
        # fast while preserving the exact approved/TM/glossary target below.
        if bool(row.get("critical_ui")) or approved.get(source) is not None or tm.get(source) is not None or glossary.get(source) is not None:
            layouts.append(([], []))
            continue
        slots, segments = split_protected_numeric(source, protected_names, args.auto_protect_proper)
        indices: list[int] = []
        for segment in segments:
            index = segment_index.get(segment)
            if index is None:
                index = len(unique_segments)
                segment_index[segment] = index
                unique_segments.append(segment)
            indices.append(index)
        layouts.append((slots, indices))

    print(f"CT2_DEDUP|rows={len(rows)}|segments={len(unique_segments)}", file=sys.stderr, flush=True)
    segment_outputs: list[str] = [""] * len(unique_segments)
    batch_size = max(1, args.batch_size)
    encoded_all = [tokenizer.convert_ids_to_tokens(tokenizer.encode(text)) for text in unique_segments]
    order = list(range(len(unique_segments)))
    if args.length_bucket:
        order.sort(key=lambda index: len(encoded_all[index]))
    for offset in range(0, len(order), batch_size):
        batch_indices = order[offset : offset + batch_size]
        chunk = [unique_segments[index] for index in batch_indices]
        encoded = [encoded_all[index] for index in batch_indices]
        # Short strings almost never need the full 512-token generation
        # budget.  Long strings retain the full cap, so this only removes
        # wasted decoding work and never truncates below a safe floor.
        max_input_tokens = max((len(tokens) for tokens in encoded), default=0)
        adaptive_limit = max(64, max_input_tokens * 3 + 32)
        decoding_length = min(max(16, args.max_decoding_length), adaptive_limit)
        try:
            generated = translator.translate_batch(
                encoded,
                beam_size=max(1, args.beams),
                max_decoding_length=decoding_length,
            )
            for segment_index, result in zip(batch_indices, generated):
                segment_outputs[segment_index] = tokenizer.convert_tokens_to_string(result.hypotheses[0]).strip()
        except Exception as exc:
            print(f"CT2_BATCH_ERROR|offset={offset}|size={len(chunk)}|{type(exc).__name__}: {exc}", file=sys.stderr, flush=True)
        if offset == 0 or (offset + len(chunk)) % (batch_size * 10) == 0 or offset + len(chunk) == len(unique_segments):
            print(f"CT2_PROGRESS|completed={offset + len(chunk)}|total={len(unique_segments)}|max_decode={decoding_length}", file=sys.stderr, flush=True)

    results: list[dict] = []
    for row, (slots, indices) in zip(rows, layouts):
        source = str(row.get("english", ""))
        critical = bool(row.get("critical_ui"))
        exact = approved.get(source) or tm.get(source) or glossary.get(source)
        if critical:
            target = ""
            status = "REVIEW_REQUIRED"
            engine = "NONE"
            version = "none"
            problem = "critical UI requires human approval"
        elif exact is not None:
            target = str(exact)
            status = "TM_REUSED" if source in tm else ("HUMAN_APPROVED" if source in approved else "GLOSSARY")
            engine = "tm" if source in tm else ("approved" if source in approved else "glossary")
            version = "curated"
            problem = quality_problem(source, target)
            if problem:
                target = ""
                status = "REVIEW_REQUIRED"
        else:
            drafts = [segment_outputs[index] for index in indices]
            target = rebuild(slots, drafts)
            problem = "unknown model token in output" if "<unk>" in target else quality_problem(source, target)
            if problem:
                status = "UNTRANSLATED"
                engine = "OPUS-CT2"
                version = f"beams={max(1, args.beams)};compute={args.compute_type}"
                target = ""
            else:
                status = "MACHINE_TRANSLATED"
                engine = "OPUS-CT2"
                version = f"beams={max(1, args.beams)};compute={args.compute_type}"
        results.append(
            {
                "entry_identity": row.get("entry_identity", ""),
                "dat_key": row.get("dat_key", ""),
                "source_digest": row.get("source_digest", ""),
                "token_signature": row.get("token_signature", ""),
                "target": target,
                "translation_status": status,
                "translation_engine": engine,
                "translation_engine_version": version,
                "critical_ui": critical,
                "quality_error": problem,
            }
        )

    output_path = Path(args.output)
    output_path.parent.mkdir(parents=True, exist_ok=True)
    output_path.write_text(
        "".join(json.dumps(result, ensure_ascii=False, sort_keys=True) + "\n" for result in results),
        encoding="utf-8",
    )
    counts: dict[str, int] = {}
    for result in results:
        counts[result["translation_status"]] = counts.get(result["translation_status"], 0) + 1
    print("CT2_TRANSLATION|" + json.dumps(counts, ensure_ascii=False, sort_keys=True), file=sys.stderr, flush=True)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
