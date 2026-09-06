import argparse
import json
import os
import re
import threading
import time
from collections import Counter
from datetime import datetime
from pathlib import Path

os.environ.setdefault("HF_HUB_DISABLE_PROGRESS_BARS", "1")
os.environ.setdefault("HF_HUB_DISABLE_SYMLINKS_WARNING", "1")
os.environ.setdefault("HF_HUB_DISABLE_XET", "1")
os.environ.setdefault("TOKENIZERS_PARALLELISM", "false")

TOKEN_RE = re.compile(
    r"(%(?:\d+\$)?[sdif]|\{\d+(?:[^{}\r\n]*)?\}|\\[nrtq]"
    r"|<(?:[^<>\"']|\"[^\"]*\"|'[^']*')*>|\[[^\]\r\n]{1,64}\])"
)
NUMBER_RE = re.compile(r"(?<![\w])(?:%\s*)?(\d+(?:[.,]\d+)?)(?:\s*%)?")
WORD_RE = re.compile(r"[^\W_]+(?:['�\-][^\W_]+)*", re.UNICODE)
MODEL_MARKER_RE = re.compile(r"(?:ZXQ|XQ)\d{4}QXZ|</?think>", re.IGNORECASE)
ENGLISH_LEFTOVER_RE = re.compile(
    r"(?i)\b(the|and|you|your|with|from|into|must|cannot|available|unavailable|"
    r"an|of|to|for|on|at|as|its|been|being|this|that|these|those|which|what|"
    r"when|where|why|how|not|can|could|would|should|may|each|every|some|more|"
    r"less|than|only|also|make|reduce|received|given|use|used|default|damage|"
    r"character|recommended|click|level|choose|select|inventory|reward|quest|"
    r"current|item|items|following|remove|purchase|window|remaining|team|are|"
    r"was|were|has|have|hidden|spell|active|open|close|cancel|confirm|name|"
    r"description|price|quantity|equipment|achievement|fight|target|range|"
    r"health|armor|armour|critical|locked|unlocked|save|load|next|previous)\b"
)


def emit(message):
    print(message, flush=True)


def start_loading_heartbeat(label):
    stop = threading.Event()

    def pulse():
        while not stop.wait(10):
            emit(f"HEARTBEAT|{label}")

    threading.Thread(target=pulse, name="lotro-model-heartbeat", daemon=True).start()
    return stop


def normalized_name(text):
    return re.sub(r"\s+", " ", text.strip().rstrip(".,;:!?"))


def turkish_genitive(name):
    vowels = [char.casefold() for char in name if char.casefold() in "ae�io�u�"]
    vowel = vowels[-1] if vowels else "a"
    sound = "i" if vowel in "ei" else "�" if vowel in "��" else "u" if vowel in "ou" else "�"
    bridge = "n" if name[-1:].casefold() in "ae�io�u�" else ""
    return name + "'" + bridge + sound + "n"


def protected_value(original, kind):
    if kind == "NAME_POSSESSIVE":
        return turkish_genitive(original[:-2])
    return original


def find_protected_name_spans(text, protected_names):
    words = list(WORD_RE.finditer(text))
    spans = []
    index = 0
    while index < len(words):
        best = None
        for end_index in range(min(len(words) - 1, index + 7), index - 1, -1):
            candidate = text[words[index].start():words[end_index].end()]
            candidate_key = normalized_name(candidate)
            end = words[end_index].end()
            if candidate_key in protected_names:
                best = (words[index].start(), end, "NAME")
                index = end_index + 1
                break
            if candidate_key.endswith(("'s", "�s")) and candidate_key[:-2] in protected_names:
                best = (words[index].start(), end, "NAME_POSSESSIVE")
                index = end_index + 1
                break
        if best:
            spans.append(best)
        else:
            index += 1
    return spans


def protected_spans(text, protected_names):
    spans = [(match.start(), match.end(), "TOKEN") for match in TOKEN_RE.finditer(text)]
    structural = sorted(spans)
    cursor = 0
    for start, end, _ in structural + [(len(text), len(text), "END")]:
        if cursor < start:
            gap = text[cursor:start]
            for name_start, name_end, kind in find_protected_name_spans(gap, protected_names):
                spans.append((cursor + name_start, cursor + name_end, kind))
        cursor = max(cursor, end)
    spans.sort(key=lambda value: (value[0], value[1]))
    result = []
    last = 0
    for start, end, kind in spans:
        if start < last:
            continue
        result.append((start, end, kind))
        last = end
    return result


def split_protected(text, protected_names):
    slots = []
    translatable = []

    def add_plain(value):
        if not value:
            return
        leading = value[: len(value) - len(value.lstrip())]
        trailing = value[len(value.rstrip()):]
        core = value.strip()
        if leading:
            slots.append(("token", leading))
        if core:
            if re.search(r"[^\W\d_]", core, flags=re.UNICODE):
                slots.append(("text", len(translatable)))
                translatable.append(core)
            else:
                slots.append(("token", core))
        if trailing:
            slots.append(("token", trailing))

    cursor = 0
    for start, end, kind in protected_spans(text, protected_names):
        add_plain(text[cursor:start])
        slots.append(("token", protected_value(text[start:end], kind)))
        cursor = end
    add_plain(text[cursor:])
    return slots, translatable


def rebuild(slots, translated):
    return "".join(value if kind == "token" else translated[value] for kind, value in slots)


def protect_for_qwen(text, protected_names):
    replacements = []
    output = []
    cursor = 0
    for start, end, kind in protected_spans(text, protected_names):
        output.append(text[cursor:start])
        marker = f"ZXQ{len(replacements):04d}QXZ"
        output.append(marker)
        replacements.append((marker, protected_value(text[start:end], kind), kind))
        cursor = end
    output.append(text[cursor:])
    return "".join(output), replacements


def restore_qwen(text, replacements):
    for marker, _, _ in replacements:
        if text.count(marker) != 1:
            return text, "korunan LOTRO kodu veya ad� de�i�tirildi/kayboldu"
    for marker, original, _ in replacements:
        text = text.replace(marker, original)
    if MODEL_MARKER_RE.search(text):
        return text, "model koruma i�areti ��kt�da kald�"
    return text, ""


def clean_generated(text):
    if "</think>" in text:
        text = text.split("</think>", 1)[1]
    text = re.sub(
        r"(?i)^\s*(?:t�rk�e(?:\s+�eviri)?|�eviri|translation|turkish|i�te)\s*:\s*",
        "",
        text,
    )
    return text.strip().strip("\"")


def format_tokens(text):
    return Counter(match.group(0) for match in TOKEN_RE.finditer(text))


def number_values(text):
    return Counter(match.group(1).replace(",", ".") for match in NUMBER_RE.finditer(text))


def strip_names(text, names):
    result = text
    for name in sorted(names, key=len, reverse=True):
        if len(name) < 3:
            continue
        result = re.sub(re.escape(name), " ", result, flags=re.IGNORECASE)
    return result


def quality_problem(source, translation, protected_names):
    if not translation or not translation.strip():
        return "bo� �eviri"
    if MODEL_MARKER_RE.search(translation):
        return "model a��klamas� veya koruma i�areti s�zd�"
    if format_tokens(source) != format_tokens(translation):
        return "bi�im kodlar� de�i�ti, eksildi veya �o�ald�"
    if number_values(source) != number_values(translation):
        return "say�sal de�erler ya da y�zdeler de�i�ti"
    if translation.strip() == source.strip() and re.search(r"[A-Za-z]{3}", source):
        return "�ngilizce metin �evrilmeden kald�"
    if re.match(r"(?i)^\s*(translation|turkish|t�rk�e|�eviri|here is|i�te)\s*:", translation):
        return "model a��klamas� ekledi"
    source_words = len(re.findall(r"[A-Za-z�-z']+", source))
    target_words = len(re.findall(r"[A-Za-z�������������-z']+", translation))
    if source_words >= 8 and target_words < max(2, int(source_words * 0.35)):
        return "c�mle eksik veya a��r� k�salt�lm��"
    if len(source) >= 55 and len(translation) < int(len(source) * 0.30):
        return "uzun metnin �nemli b�l�m� kayboldu"
    if len(source) <= 48 and len(translation) > max(120, len(source) * 5):
        return "k�sa kaynak i�in ilgisiz derecede uzun ��kt�"
    if re.search(r"Ã|â|�|?", translation):
        return "bozuk metin kodlamas�"
    target_without_names = strip_names(translation, protected_names)
    if ENGLISH_LEFTOVER_RE.search(target_without_names):
        return "T�rk�e ��kt�da �ngilizce s�zc�k kald�"
    return ""


def classify_default(text):
    value = text.strip()
    if len(value) <= 42 and not re.search(r"[.!?]", value):
        return "ARAYUZ"
    if re.match(r"(?i)^(Defeat|Collect|Talk to|Find|Use|Bring|Return to)\b", value):
        return "GOREV_HEDEF"
    if re.search(r"(?i)\b(damage|morale|armou?r|cooldown|rating|resistance)\b|%", value):
        return "MEKANIK_BUFF_ACIKLAMA"
    if len(value) >= 150 or "\n" in value or "\\n" in value:
        return "DIYALOG_HIKAYE"
    return "GENEL_OYUN_METNI"


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--input", required=True)
    parser.add_argument("--output", required=True)
    parser.add_argument("--cache", required=True)
    parser.add_argument("--context", required=True)
    parser.add_argument("--live-log")
    parser.add_argument("--batch", type=int, default=32)
    parser.add_argument("--qwen-model", choices=("8b-q4", "4b-q6"), default="8b-q4")
    args = parser.parse_args()

    with open(args.context, "r", encoding="utf-8-sig") as source:
        context = json.load(source)
    protected_names = {
        normalized_name(str(name))
        for name in context.get("protected_names", [])
        if str(name).strip()
    }
    glossary = {
        str(source): str(target)
        for source, target in context.get("glossary", {}).items()
        if str(source).strip() and str(target).strip()
    }
    glossary_casefold = {source.casefold(): target for source, target in glossary.items()}
    ambiguous_terms = {"will", "power", "fate", "might", "use", "account", "character"}

    def relevant_glossary(source):
        lowered = source.casefold()
        relevant = []
        for english, turkish in sorted(glossary.items(), key=lambda pair: len(pair[0]), reverse=True):
            folded = english.casefold()
            if " " in english:
                applies = folded in lowered
            elif folded in ambiguous_terms:
                applies = source.strip().casefold() == folded
            else:
                applies = re.search(r"(?i)(?<!\w)" + re.escape(english) + r"(?!\w)", source) is not None
            if applies:
                relevant.append((english, turkish))
            if len(relevant) >= 30:
                break
        return relevant

    def normalize_terms(source, translation):
        exact = glossary_casefold.get(source.strip().casefold())
        if exact:
            return exact
        if re.search(r"(?i)\bdamage\b", source):
            translation = re.sub(r"(?iu)(?<!\w)zarar(?!\w)", "hasar", translation)
        if re.search(r"(?i)\barmou?r\b", source):
            translation = re.sub(r"(?iu)(?<!\w)(?:z�rh|z�rh�|z�rha|z�rh�n|z�rhtan|z�rhla)(?!\w)", lambda m: m.group(0), translation)
        if re.search(r"(?i)\bcooldown\b", source):
            translation = re.sub(r"(?iu)\bso�uma s�resi\b", "bekleme s�resi", translation)
        return translation

    import torch
    if os.name == "nt":
        os.add_dll_directory(os.path.join(os.path.dirname(torch.__file__), "lib"))
        package_bin = os.path.abspath(os.path.join(os.path.dirname(torch.__file__), "..", "bin"))
        if os.path.isdir(package_bin):
            os.add_dll_directory(package_bin)
    from huggingface_hub import hf_hub_download
    from llama_cpp import Llama
    from transformers import MarianMTModel, MarianTokenizer

    if not torch.cuda.is_available():
        emit("ERROR|NVIDIA CUDA kullan�lam�yor; ekran kart� s�r�c�s�n� kontrol edin.")
        return 2
    device_name = torch.cuda.get_device_name(0)
    total_vram = torch.cuda.get_device_properties(0).total_memory / (1024 ** 3)
    emit(f"DEVICE|{device_name}|{total_vram:.1f} GB")

    emit("STATUS|H�zl� �ngilizce-T�rk�e OPUS modeli y�kleniyor�")
    loading = start_loading_heartbeat("OPUS_INDIRME_YUKLEME")
    try:
        opus_id = "Helsinki-NLP/opus-mt-tc-big-en-tr"
        opus_cache = os.path.join(args.cache, "opus_en_tr_cache")
        try:
            opus_tokenizer = MarianTokenizer.from_pretrained(
                opus_id, cache_dir=opus_cache, local_files_only=True
            )
            opus_model = MarianMTModel.from_pretrained(
                opus_id, cache_dir=opus_cache, dtype=torch.float16, local_files_only=True
            ).to("cuda").eval()
        except Exception:
            emit("STATUS|OPUS modeli ilk kullan�m i�in indiriliyor; bu i�lem yaln�z bir kez yap�l�r�")
            opus_tokenizer = MarianTokenizer.from_pretrained(
                opus_id, cache_dir=opus_cache, local_files_only=False
            )
            opus_model = MarianMTModel.from_pretrained(
                opus_id, cache_dir=opus_cache, dtype=torch.float16, local_files_only=False
            ).to("cuda").eval()
    finally:
        loading.set()
    emit("STATUS|OPUS haz�r; Qwen yaln�z kalite reddi alan sat�rlar� d�zeltecek.")

    qwen = None

    def ensure_qwen():
        nonlocal qwen
        if qwen is not None:
            return qwen
        emit("STATUS|��pheli sat�rlar i�in Qwen3 kalite d�zelticisi y�kleniyor�")
        loading_qwen = start_loading_heartbeat("QWEN_INDIRME_YUKLEME")
        try:
            if args.qwen_model == "4b-q6":
                repo = "Qwen/Qwen3-4B-GGUF"
                filename = "Qwen3-4B-Q6_K.gguf"
                model_cache = os.path.join(args.cache, "qwen3_4b_model_cache")
            else:
                repo = "Qwen/Qwen3-8B-GGUF"
                filename = "Qwen3-8B-Q4_K_M.gguf"
                model_cache = os.path.join(args.cache, "qwen3_8b_model_cache")
            try:
                model_path = hf_hub_download(
                    repo_id=repo, filename=filename, cache_dir=model_cache, local_files_only=True
                )
            except Exception:
                emit("STATUS|Qwen3 modeli ilk kullan�m i�in indiriliyor; bu i�lem yaln�z bir kez yap�l�r�")
                model_path = hf_hub_download(
                    repo_id=repo, filename=filename, cache_dir=model_cache, local_files_only=False
                )
            qwen = Llama(
                model_path=model_path,
                n_gpu_layers=-1,
                n_ctx=4096,
                n_batch=64,
                n_threads=max(2, (os.cpu_count() or 4) // 2),
                flash_attn=True,
                verbose=False,
            )
        finally:
            loading_qwen.set()
        emit("STATUS|Qwen3 kalite d�zelticisi haz�r.")
        return qwen

    category_guidance = {
        "ARAYUZ": "Use concise, immediately understandable and consistent Turkish UI wording.",
        "GOREV_HEDEF": "Use natural LOTRO quest language and concise Turkish imperatives.",
        "DIYALOG_HIKAYE": "Preserve Tolkien tone, emotion and intent in fluent natural Turkish. Never translate proper names.",
        "MEKANIK_BUFF_ACIKLAMA": "Translate mechanics precisely. Preserve values, abbreviations and every protected token.",
        "GENEL_OYUN_METNI": "Use polished, idiomatic Turkish suitable for a professional MMORPG localization.",
    }
    base_prompt = (
        "/no_think\nYou are the senior Turkish localization editor for The Lord of the Rings Online. "
        "Translate the English text into polished, natural and unambiguous Turkish. Preserve Tolkien lore, "
        "tone and all proper names. Never translate character, NPC, monster, item, skill, place or title names "
        "that are protected by ZXQ0000QXZ placeholders. Preserve every placeholder, number, percentage, "
        "abbreviation, sentence and meaning. Never invent, censor, summarize, merge or explain. "
        "Use correct Turkish suffixes, possessives and natural word order. Return only the Turkish translation."
    )

    def qwen_translate(source, category):
        protected_source, replacements = protect_for_qwen(source, protected_names)
        terms = relevant_glossary(source)
        terminology = ""
        if terms:
            terminology = "\nMandatory LOTRO terminology: " + "; ".join(
                f"{english} => {turkish}" for english, turkish in terms
            )
        last = ""
        problem = ""
        for attempt in range(2):
            correction = "" if not problem else f"\nPrevious output failed validation: {problem}. Correct it completely."
            messages = [
                {
                    "role": "system",
                    "content": (
                        base_prompt
                        + "\nCategory: "
                        + category
                        + ". "
                        + category_guidance.get(category, category_guidance["GENEL_OYUN_METNI"])
                        + terminology
                        + correction
                    ),
                },
                {"role": "user", "content": "/no_think\n" + protected_source},
            ]
            response = ensure_qwen().create_chat_completion(
                messages=messages,
                temperature=0.05,
                top_p=0.95,
                top_k=20,
                repeat_penalty=1.05,
                max_tokens=min(1024, max(48, int(len(source) * 1.6))),
                seed=attempt,
                stream=True,
            )
            chunks = []
            heartbeat = time.time()
            for chunk in response:
                delta = chunk.get("choices", [{}])[0].get("delta", {}).get("content", "")
                if delta:
                    chunks.append(delta)
                if time.time() - heartbeat >= 1.5:
                    emit("HEARTBEAT|MODEL_YANITI")
                    heartbeat = time.time()
            generated = normalize_terms(source, clean_generated("".join(chunks)))
            restored, restore_problem = restore_qwen(generated, replacements)
            problem = restore_problem or quality_problem(source, restored, protected_names)
            last = restored
            if not problem:
                return restored, ""
        return last, problem

    def opus_translate(segments):
        if not segments:
            return []
        encoded = opus_tokenizer(
            segments, return_tensors="pt", padding=True, truncation=True, max_length=512
        ).to("cuda")
        with torch.inference_mode():
            generated = opus_model.generate(
                **encoded,
                num_beams=3,
                max_new_tokens=512,
                renormalize_logits=True,
            )
        return [
            clean_generated(value)
            for value in opus_tokenizer.batch_decode(generated, skip_special_tokens=True)
        ]

    def translate_batch(units):
        slots_by_index = {}
        ranges = {}
        all_segments = []
        results = [None] * len(units)
        for index, unit in enumerate(units):
            source = unit["english"]
            exact = glossary_casefold.get(source.strip().casefold())
            if exact:
                results[index] = (exact, "")
                continue
            if len(source) > 1400:
                results[index] = qwen_translate(source, unit["category"])
                continue
            slots, segments = split_protected(source, protected_names)
            first = len(all_segments)
            all_segments.extend(segments)
            ranges[index] = (first, len(all_segments))
            slots_by_index[index] = slots

        drafts = []
        for start in range(0, len(all_segments), 64):
            drafts.extend(opus_translate(all_segments[start:start + 64]))

        for index, unit in enumerate(units):
            if results[index] is not None:
                continue
            first, last = ranges[index]
            translated_parts = []
            for source_segment, draft in zip(
                [slot for slot in all_segments[first:last]], drafts[first:last]
            ):
                translated_parts.append(normalize_terms(source_segment, draft))
            translation = normalize_terms(
                unit["english"], rebuild(slots_by_index[index], translated_parts)
            )
            problem = quality_problem(unit["english"], translation, protected_names)
            if problem:
                emit(f"QUALITY_FALLBACK|{unit['items'][0].get('key', '')}|{problem}")
                try:
                    translation, problem = qwen_translate(unit["english"], unit["category"])
                except Exception as exc:
                    problem = f"Qwen kalite d�zeltmesi ba�ar�s�z: {type(exc).__name__}: {exc}"
            results[index] = (translation, problem)
        return results

    items = []
    with open(args.input, "r", encoding="utf-8-sig") as source:
        for line in source:
            if line.strip():
                row = json.loads(line)
                row["category"] = row.get("category") or classify_default(row.get("english", ""))
                items.append(row)

    units = []
    unit_by_source = {}
    for item in items:
        token = (item["english"], item["category"])
        unit = unit_by_source.get(token)
        if unit is None:
            unit = {"english": token[0], "category": token[1], "items": []}
            unit_by_source[token] = unit
            units.append(unit)
        unit["items"].append(item)

    batches = []
    by_category = {}
    for unit in units:
        by_category.setdefault(unit["category"], []).append(unit)
    for category_units in by_category.values():
        pending = []
        pending_chars = 0
        for unit in category_units:
            length = len(unit["english"])
            limit = min(max(1, args.batch), 8 if length > 120 else 32)
            char_limit = 5000 if length > 120 else 3000
            if length > 1400 or len(pending) >= limit or (pending and pending_chars + length > char_limit):
                if pending:
                    batches.append(pending)
                    pending = []
                    pending_chars = 0
            if length > 1400:
                batches.append([unit])
            else:
                pending.append(unit)
                pending_chars += length
        if pending:
            batches.append(pending)
    emit(f"DEDUP|{len(items)}|{len(units)}|{len(batches)}")

    output_path = Path(args.output)
    output_path.parent.mkdir(parents=True, exist_ok=True)
    live_target = None
    if args.live_log:
        live_path = Path(args.live_log)
        live_path.parent.mkdir(parents=True, exist_ok=True)
        new_file = not live_path.exists() or live_path.stat().st_size == 0
        live_target = live_path.open("a", encoding="utf-8-sig" if new_file else "utf-8", buffering=1)
        if new_file:
            live_target.write("Zaman\tYontem\tAnahtar\tKategori\tDurum\tIngilizce\tTurkce\tAciklama\n")

    def live_field(value):
        return str(value or "").replace("\\", "\\\\").replace("\t", "\\t").replace("\r", "\\r").replace("\n", "\\n")

    completed = 0
    started = time.time()
    try:
        with output_path.open("w", encoding="utf-8", buffering=1) as target:
            for batch_number, batch_units in enumerate(batches, 1):
                emit(
                    f"ITEM_START|{batch_units[0]['items'][0].get('key', '')}|"
                    f"{completed + 1}|{len(items)}|paket {batch_number}/{len(batches)}"
                )
                try:
                    results = translate_batch(batch_units)
                except Exception as exc:
                    emit(f"BATCH_RETRY|{len(batch_units)}|{type(exc).__name__}: {exc}")
                    results = []
                    for unit in batch_units:
                        try:
                            results.append(qwen_translate(unit["english"], unit["category"]))
                        except Exception as unit_exc:
                            results.append(("", f"GPU i��i hatas�: {type(unit_exc).__name__}: {unit_exc}"))
                for unit, (translation, problem) in zip(batch_units, results):
                    for item in unit["items"]:
                        target.write(
                            json.dumps(
                                {
                                    "key": item["key"],
                                    "translation": translation,
                                    "worker_quality": problem,
                                },
                                ensure_ascii=False,
                            )
                            + "\n"
                        )
                        if live_target is not None:
                            live_target.write(
                                "\t".join(
                                    live_field(value)
                                    for value in (
                                        datetime.now().strftime("%Y-%m-%d %H:%M:%S.%f")[:-3],
                                        "OTOMATIK_GPU",
                                        item["key"],
                                        unit["category"],
                                        "REDDEDILDI" if problem else "URETILDI",
                                        item["english"],
                                        translation,
                                        problem,
                                    )
                                )
                                + "\n"
                            )
                        completed += 1
                    target.flush()
                    if live_target is not None:
                        live_target.flush()
                elapsed = max(time.time() - started, 0.001)
                rate = completed / elapsed
                remaining = int((len(items) - completed) / rate) if rate else 0
                emit(f"PROGRESS|{completed}|{len(items)}|{rate:.1f}|{remaining}")
    finally:
        if live_target is not None:
            live_target.close()
    emit(f"DONE|{completed}|{time.time() - started:.1f}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
