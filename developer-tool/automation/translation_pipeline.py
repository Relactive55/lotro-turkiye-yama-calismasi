"""Free, provider-swappable EN->TR pipeline for semantic patch candidates.

The script has no network API of its own. OPUS and Argos are optional local
providers; the default ``noop`` provider produces safe UNTRANSLATED rows.
TM, approved translations and glossary entries are resolved before a model
runs. The output intentionally omits English source text so it can be handed
to the semantic patch builder without creating a public catalog dump.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import os
import re
import shutil
import subprocess
import sys
import time
import urllib.error
import urllib.parse
import urllib.request
from pathlib import Path

TOKEN_RE = re.compile(
    r"(%(?:\d+\$)?[sdif]|\{\d+(?:[^{}\r\n]*)?\}|\\[nrtq]|"
    r"</?[^<>\"']+>|\[[^\]\r\n]{1,64}\])"
)
NUMBER_RE = re.compile(r"(?<![\w])[-+]?(?:\d+(?:[.,]\d+)?)(?![\w])")
MODEL_MARKER_RE = re.compile(r"ZXQ\d{4}QXZ|</?think>", re.IGNORECASE)
ENGLISH_LEFTOVER_RE = re.compile(
    r"(?i)\b(the|and|you|your|with|from|into|must|cannot|available|unavailable|"
    r"an|of|to|for|on|at|as|its|been|being|this|that|these|those|which|what|"
    r"when|where|why|how|not|can|could|would|should|may|each|every|some|more|"
    r"less|than|only|also|make|reduce|received|given|use|used|default|damage|"
    r"character|recommended|click|level|choose|select|inventory|reward|quest|"
    r"current|item|items|following|remove|purchase|window|remaining|team|are|"
    r"was|were|has|have|hidden|spell|active|open|close|cancel|confirm|name|"
    r"description|price|quantity|equipment|achievement|fight|target|range|"
    r"health|armor|armour|critical|locked|unlocked|save|load|next|previous|"
    r"alas|fall|nine|shire|enemy|speak|king|mountain|land|world|road|gate|"
    r"camp|inn|black|red|speech|keep|house|book|chapter|section|one|two|three|"
    r"four|five|six|seven|eight|ten|old|new|last|first|east|west|north|south)\b"
)
TAG_RE = re.compile(r"</?[^<>]+>")
TAG_NAME_RE = re.compile(r"^[A-Za-z][A-Za-z0-9_.:-]*")


def token_signature(text: str) -> str:
    tokens = [match.group(0) for match in TOKEN_RE.finditer(text or "")]
    numbers = [match.group(0).replace(",", ".") for match in NUMBER_RE.finditer(text or "")]
    canonical = "lotro-token-signature-v1|" + "|".join(tokens) + "|#|" + "|".join(numbers)
    return hashlib.sha256(canonical.encode("utf-8")).hexdigest()


def bracket_context_signature(text: str) -> tuple[tuple[str, bool, bool], ...]:
    """Keep inline [p]/[e]/[s] markers attached or separated as in source."""
    result = []
    for match in re.finditer(r"\[[^\]\r\n]{1,64}\]", text or ""):
        previous = text[match.start() - 1] if match.start() else ""
        following = text[match.end()] if match.end() < len(text) else ""
        result.append((match.group(0), bool(previous and (previous.isalnum() or previous == "_")), bool(following and (following.isalnum() or following == "_"))))
    return tuple(result)


def markup_problem(text: str) -> str:
    """Mirror the fail-closed XML-like tag stack guard used by the writer."""
    stack: list[str] = []
    for match in TAG_RE.finditer(text or ""):
        raw = match.group(0)
        inner = raw[1:-1].strip()
        if not inner:
            return "malformed markup"
        closing = inner.startswith("/")
        if closing:
            inner = inner[1:].strip()
        self_closing = inner.endswith("/")
        if self_closing:
            inner = inner[:-1].strip()
        name_match = TAG_NAME_RE.match(inner)
        if not name_match:
            return "malformed markup"
        name = name_match.group(0)
        if self_closing:
            continue
        if closing:
            if not stack or stack.pop().lower() != name.lower():
                return "markup nesting mismatch"
        else:
            stack.append(name)
    if stack:
        return "unclosed markup tag"
    return ""


def mask_tokens(text: str, protected_names: list[str]) -> tuple[str, list[tuple[str, str]]]:
    spans = [(match.start(), match.end(), match.group(0)) for match in TOKEN_RE.finditer(text)]
    for name in sorted({value.strip() for value in protected_names if value and value.strip()}, key=len, reverse=True):
        for match in re.finditer(re.escape(name), text, flags=re.IGNORECASE):
            spans.append((match.start(), match.end(), match.group(0)))
    spans.sort(key=lambda item: (item[0], -(item[1] - item[0])))
    selected: list[tuple[int, int, str]] = []
    cursor = -1
    for start, end, value in spans:
        if start < cursor:
            continue
        selected.append((start, end, value))
        cursor = end
    output: list[str] = []
    replacements: list[tuple[str, str]] = []
    cursor = 0
    for index, (start, end, value) in enumerate(selected):
        output.append(text[cursor:start])
        marker = f"ZXQ{index:04d}QXZ"
        output.append(marker)
        replacements.append((marker, value))
        cursor = end
    output.append(text[cursor:])
    return "".join(output), replacements


def restore_tokens(text: str, replacements: list[tuple[str, str]]) -> tuple[str, str]:
    result = text or ""
    for marker, original in replacements:
        if result.count(marker) != 1:
            return result, "protected token/name was changed or lost"
        result = result.replace(marker, original)
    if MODEL_MARKER_RE.search(result):
        return result, "model marker leaked into output"
    return result, ""


def quality_problem(source: str, target: str) -> str:
    if not target or not target.strip():
        return "empty target"
    if MODEL_MARKER_RE.search(target):
        return "model marker leaked"
    source_markup = markup_problem(source)
    if source_markup:
        return "source " + source_markup
    target_markup = markup_problem(target)
    if target_markup:
        return "translation " + target_markup
    if bracket_context_signature(source) != bracket_context_signature(target):
        return "protected bracket placement changed"
    if token_signature(source) != token_signature(target):
        return "token or numeric signature changed"
    if source.strip() == target.strip() and re.search(r"[A-Za-z]{3}", source):
        return "English source was returned unchanged"
    source_letters = sum(char.isalpha() for char in source)
    target_letters = sum(char.isalpha() for char in target)
    if source_letters >= 30 and target_letters < max(8, int(source_letters * 0.28)):
        return "target is implausibly short"
    if len(source) <= 48 and len(target) > max(120, len(source) * 5):
        return "target is implausibly long"
    if "�" in target or "Ãƒ" in target or "ï¿½" in target:
        return "encoding garbage"
    # A long candidate that still contains several ordinary English words is
    # usually a fragmented/copy-through draft rather than a Turkish result.
    # A small number of preserved LOTRO names is acceptable, so use a count
    # threshold and leave short labels to the normal checks above.
    if len(source) >= 80 and len(ENGLISH_LEFTOVER_RE.findall(target)) >= 2:
        return "likely English leftovers"
    return ""


class TranslationProvider:
    name = "NONE"
    version = "none"

    def translate(self, text: str) -> str | None:
        raise NotImplementedError

    def translate_many(self, texts: list[str]) -> list[str | None]:
        return [self.translate(text) for text in texts]


class NoopProvider(TranslationProvider):
    def translate(self, text: str) -> str | None:
        return None


class OpusProvider(TranslationProvider):
    name = "OPUS"

    def __init__(self, model_id: str, revision: str | None, cache_dir: str | None, offline: bool, beams: int = 3, max_new_tokens: int = 512, threads: int = 0, adaptive_max_tokens: bool = False) -> None:
        try:
            import torch
            from transformers import MarianMTModel, MarianTokenizer
        except Exception as exc:  # pragma: no cover - optional runner dependency
            raise RuntimeError(f"OPUS dependencies unavailable: {exc}") from exc
        effective_threads = max(1, min(int(threads) if threads > 0 else 4, os.cpu_count() or 4))
        torch.set_num_threads(effective_threads)
        try:
            torch.set_num_interop_threads(max(1, min(effective_threads, 4)))
        except RuntimeError:
            # A host embedding the provider may have already initialized the
            # inter-op pool; the intra-op setting above remains effective.
            pass
        kwargs = {"cache_dir": cache_dir}
        if revision:
            kwargs["revision"] = revision
        if offline:
            kwargs["local_files_only"] = True
        self.version = f"{model_id}@{revision or 'default'}"
        self._tokenizer = MarianTokenizer.from_pretrained(model_id, **kwargs)
        # HF config advertises float16 for this checkpoint, but float16 CPU
        # kernels are substantially slower. Float32 is numerically equivalent
        # for this model on the local CPU and preserves the beam-3 output.
        self._model = MarianMTModel.from_pretrained(model_id, **kwargs).eval().float()
        self._torch = torch
        self._beams = max(1, int(beams))
        self._max_new_tokens = max(16, int(max_new_tokens))
        self._adaptive_max_tokens = bool(adaptive_max_tokens)

    def translate(self, text: str) -> str:
        return self.translate_many([text])[0]

    def translate_many(self, texts: list[str]) -> list[str]:
        if not texts:
            return []
        encoded = self._tokenizer(texts, return_tensors="pt", padding=True, truncation=True, max_length=512)
        input_length = int(encoded["attention_mask"].sum(dim=1).max().item()) if "attention_mask" in encoded else 512
        generation_limit = self._max_new_tokens
        if self._adaptive_max_tokens:
            # Most LOTRO strings are short. Keep an ample proportional budget,
            # then retry only a genuinely truncated sequence at the full limit.
            generation_limit = min(self._max_new_tokens, max(64, input_length * 3 + 32))
        with self._torch.inference_mode():
            generated = self._model.generate(**encoded, num_beams=self._beams, max_new_tokens=generation_limit)
            decoded = [item.strip() for item in self._tokenizer.batch_decode(generated, skip_special_tokens=True)]
            if self._adaptive_max_tokens and generation_limit < self._max_new_tokens and self._tokenizer.eos_token_id is not None:
                retry = [index for index, item in enumerate(generated) if self._tokenizer.eos_token_id not in item.tolist()]
                if retry:
                    retry_texts = [texts[index] for index in retry]
                    retry_encoded = self._tokenizer(retry_texts, return_tensors="pt", padding=True, truncation=True, max_length=512)
                    retry_generated = self._model.generate(**retry_encoded, num_beams=self._beams, max_new_tokens=self._max_new_tokens)
                    for index, item in zip(retry, self._tokenizer.batch_decode(retry_generated, skip_special_tokens=True)):
                        decoded[index] = item.strip()
        return decoded


class ArgosProvider(TranslationProvider):
    name = "ARGOS"
    version = "argos-translate"

    def __init__(self) -> None:
        try:
            import argostranslate.translate as translate
        except Exception as exc:  # pragma: no cover - optional provider
            raise RuntimeError(f"Argos dependencies unavailable: {exc}") from exc
        self._translate = translate.translate

    def translate(self, text: str) -> str:
        return self._translate(text, "en", "tr")


class OpenAICompatibleProvider(TranslationProvider):
    """Translate through an explicitly configured OpenAI-compatible endpoint.

    The endpoint and key are supplied only by the private workflow.  The
    provider deliberately accepts HTTPS endpoints only and never writes the
    key or the English input to a repository file.
    """

    name = "OPENAI_COMPATIBLE"

    def __init__(self, model: str, endpoint: str, token_env: str, token_header: str = "Authorization", token_prefix: str = "Bearer") -> None:
        token = os.environ.get(token_env, "").strip()
        if not token:
            raise RuntimeError(f"translation API key is missing from {token_env}")
        parsed = urllib.parse.urlparse(endpoint)
        if parsed.scheme != "https" or not parsed.hostname:
            raise RuntimeError("translation API endpoint must use HTTPS")
        header = token_header.strip()
        if not header or any(char.isspace() for char in header):
            raise RuntimeError("translation API key header is invalid")
        self._token = token
        self._endpoint = endpoint
        self._model = model
        self._token_header = header
        self._token_prefix = token_prefix.strip()
        self.version = model

    @staticmethod
    def _decode_content(content: str, expected: int) -> list[str]:
        value = (content or "").strip()
        if value.startswith("```"):
            value = re.sub(r"^```(?:json)?\s*", "", value, flags=re.IGNORECASE)
            value = re.sub(r"\s*```$", "", value)
        decoded = json.loads(value)
        items = decoded.get("translations") if isinstance(decoded, dict) else None
        if not isinstance(items, list):
            raise RuntimeError("translation response has no translations array")
        result: list[str | None] = [None] * expected
        for item in items:
            if not isinstance(item, dict) or not isinstance(item.get("id"), int) or not isinstance(item.get("text"), str):
                raise RuntimeError("translation response item is malformed")
            index = item["id"]
            if index < 0 or index >= expected or result[index] is not None:
                raise RuntimeError("translation response ids are invalid or duplicated")
            result[index] = item["text"].strip()
        if any(item is None for item in result):
            raise RuntimeError("translation response is incomplete")
        return [str(item) for item in result]

    def translate(self, text: str) -> str:
        return self.translate_many([text])[0]

    def translate_many(self, texts: list[str]) -> list[str]:
        if not texts:
            return []
        request_body = {
            "model": self._model,
            "temperature": 0.1,
            "max_completion_tokens": min(8192, max(1024, sum(len(text) for text in texts) * 2)),
            "messages": [
                {
                    "role": "system",
                    "content": (
                        "You are a professional English-to-Turkish MMORPG localizer. "
                        "Translate naturally and consistently for a fantasy game UI. "
                        "Keep every ZXQddddQXZ marker byte-for-byte, in the same order. "
                        "Do not translate proper Tolkien names. Do not add explanations. "
                        "Return only JSON: {\"translations\":[{\"id\":0,\"text\":\"...\"}]} "
                        "with exactly one item for every supplied id."
                    ),
                },
                {
                    "role": "user",
                    "content": json.dumps(
                        {
                            "source_language": "English",
                            "target_language": "Turkish",
                            "items": [{"id": index, "text": text} for index, text in enumerate(texts)],
                        },
                        ensure_ascii=False,
                        separators=(",", ":"),
                    ),
                },
            ],
        }
        request_bytes = json.dumps(request_body, ensure_ascii=False).encode("utf-8")
        last_error: Exception | None = None
        for attempt in range(4):
            request = urllib.request.Request(
                self._endpoint,
                data=request_bytes,
                method="POST",
                headers={
                    "Accept": "application/json",
                    self._token_header: (self._token_prefix + " " + self._token).strip(),
                    "Content-Type": "application/json; charset=utf-8",
                    "User-Agent": "lotro-turkiye-yama-translation-pipeline",
                },
            )
            try:
                with urllib.request.urlopen(request, timeout=90) as response:
                    payload = json.loads(response.read().decode("utf-8"))
                content = payload["choices"][0]["message"]["content"]
                return self._decode_content(content, len(texts))
            except urllib.error.HTTPError as exc:
                last_error = exc
                if exc.code not in {429, 500, 502, 503, 504} or attempt == 3:
                    break
                retry_after = exc.headers.get("Retry-After", "")
                delay = int(retry_after) if retry_after.isdigit() else 2 ** attempt
                time.sleep(min(30, max(1, delay)))
            except (urllib.error.URLError, TimeoutError, KeyError, IndexError, TypeError, ValueError, RuntimeError) as exc:
                last_error = exc
                if attempt == 3:
                    break
                time.sleep(2 ** attempt)
        raise RuntimeError(f"OpenAI-compatible translation failed: {type(last_error).__name__}") from last_error


class GitHubModelsProvider(OpenAICompatibleProvider):
    """Compatibility shim that fails clearly after GitHub Models retirement."""

    name = "GITHUB_MODELS"

    def __init__(self, model: str, endpoint: str, token_env: str) -> None:
        raise RuntimeError(
            "GitHub Models inference API is retired; use copilot-cli or openai-compatible instead"
        )


class CopilotCliProvider(TranslationProvider):
    """GitHub-native translation through Copilot CLI in Actions.

    Copilot CLI is invoked in prompt mode with no repository instructions or
    built-in MCP servers.  It receives only the transient batch and must
    return the same protected-marker JSON contract as the HTTP provider.
    """

    name = "GITHUB_COPILOT"

    def __init__(self, command: str, model: str, token_env: str, timeout: int) -> None:
        token = os.environ.get(token_env, "").strip()
        if not token:
            raise RuntimeError(f"Copilot token is missing from {token_env}")
        executable = shutil.which(command)
        if not executable:
            raise RuntimeError("GitHub Copilot CLI is not installed on the runner")
        self._command = executable
        self._model = model or "auto"
        self._token_env = token_env
        self._timeout = max(30, int(timeout))
        self.version = f"copilot-cli/{self._model}"

    @staticmethod
    def _extract_json(response: str) -> str:
        value = (response or "").strip()
        start = value.find("{")
        end = value.rfind("}")
        if start < 0 or end < start:
            raise RuntimeError("Copilot CLI response has no JSON object")
        return value[start:end + 1]

    def translate(self, text: str) -> str:
        return self.translate_many([text])[0]

    def translate_many(self, texts: list[str]) -> list[str]:
        if not texts:
            return []
        request = {
            "source_language": "English",
            "target_language": "Turkish",
            "items": [{"id": index, "text": text} for index, text in enumerate(texts)],
        }
        prompt = (
            "Act only as an English-to-Turkish MMORPG localization service. "
            "Do not use tools, do not read files, do not explain anything. "
            "Keep every ZXQddddQXZ marker byte-for-byte and in the same order. "
            "Do not translate Tolkien or proper game names. Return ONLY one JSON object "
            "with exactly one translation for every item: "
            '{"translations":[{"id":0,"text":"..."}]}. Input: '
            + json.dumps(request, ensure_ascii=False, separators=(",", ":"))
        )
        environment = os.environ.copy()
        environment["COPILOT_MODEL"] = self._model
        command = [
            self._command,
            "--prompt=" + prompt,
            "--silent",
            "--model=" + self._model,
            "--no-auto-update",
            "--no-ask-user",
            "--no-custom-instructions",
            "--disable-builtin-mcps",
            "--no-remote",
            "--no-remote-export",
            "--no-experimental",
        ]
        try:
            result = subprocess.run(
                command,
                cwd=os.getcwd(),
                env=environment,
                stdin=subprocess.DEVNULL,
                capture_output=True,
                text=True,
                encoding="utf-8",
                errors="replace",
                timeout=self._timeout,
                check=False,
            )
        except (OSError, subprocess.TimeoutExpired) as exc:
            raise RuntimeError(f"Copilot CLI invocation failed: {type(exc).__name__}") from exc
        if result.returncode != 0:
            detail = " ".join((result.stderr or "").split())
            detail = re.sub(r"(?i)(?:bearer|token|authorization)[=: ]+\S+", "[redacted]", detail)
            detail = detail[:240]
            suffix = f": {detail}" if detail else ""
            raise RuntimeError(f"Copilot CLI returned exit code {result.returncode}{suffix}")
        return OpenAICompatibleProvider._decode_content(self._extract_json(result.stdout), len(texts))


def build_provider(name: str, args: argparse.Namespace) -> TranslationProvider:
    if name == "noop":
        return NoopProvider()
    if name == "opus":
        return OpusProvider(args.model, args.revision, args.cache, args.offline, args.beams, args.max_new_tokens, args.threads, args.adaptive_max_tokens)
    if name == "argos":
        return ArgosProvider()
    if name == "github-models":
        return GitHubModelsProvider(args.github_model, args.github_endpoint, args.github_token_env)
    if name == "openai-compatible":
        return OpenAICompatibleProvider(
            args.openai_model,
            args.openai_endpoint,
            args.openai_token_env,
            args.openai_token_header,
            args.openai_token_prefix,
        )
    if name == "copilot-cli":
        return CopilotCliProvider(args.copilot_command, args.copilot_model, args.copilot_token_env, args.copilot_timeout)
    raise ValueError(f"unknown provider: {name}")


def load_context(path: str | None) -> tuple[dict[str, str], dict[str, str], dict[str, str], list[str]]:
    if not path:
        return {}, {}, {}, []
    context = json.loads(Path(path).read_text(encoding="utf-8-sig"))
    approved = {str(key): str(value) for key, value in context.get("approved", {}).items() if str(key).strip() and str(value).strip()}
    tm = {str(key): str(value) for key, value in context.get("tm", {}).items() if str(key).strip() and str(value).strip()}
    glossary = {str(key): str(value) for key, value in context.get("glossary", {}).items() if str(key).strip() and str(value).strip()}
    names = [str(value) for value in context.get("protected_names", []) if str(value).strip()]
    return approved, tm, glossary, names


def process(row: dict, provider: TranslationProvider, approved: dict[str, str], tm: dict[str, str], glossary: dict[str, str], protected_names: list[str], generated: str | None = None, provider_error: str | None = None) -> dict:
    source = str(row.get("english", ""))
    exact_glossary = glossary.get(source)
    target = approved.get(source) or tm.get(source) or exact_glossary
    status = "HUMAN_APPROVED" if source in approved else ("TM_REUSED" if source in tm else ("GLOSSARY" if exact_glossary else None))
    engine = "approved" if status == "HUMAN_APPROVED" else ("tm" if status == "TM_REUSED" else ("glossary" if status == "GLOSSARY" else "NONE"))
    version = "curated" if status else "local"
    problem = ""
    if target is None and bool(row.get("critical_ui")):
        status = "REVIEW_REQUIRED"
        engine = "NONE"
        version = "none"
        problem = "critical UI requires human approval"
    elif provider_error:
        status = "UNTRANSLATED"
        engine = provider.name
        version = provider.version
        problem = f"TRANSLATION_PROVIDER_UNAVAILABLE: {provider_error}"
    elif target is None:
        masked, replacements = mask_tokens(source, protected_names)
        try:
            if generated is None:
                generated = provider.translate(masked)
            target, restore_problem = restore_tokens(generated or "", replacements)
            problem = restore_problem or quality_problem(source, target)
        except Exception as exc:  # good curated rows remain usable
            target = ""
            problem = f"TRANSLATION_PROVIDER_UNAVAILABLE: {type(exc).__name__}: {exc}"
        if not problem:
            status = "MACHINE_TRANSLATED"
            engine = provider.name
            version = provider.version
        else:
            status = "UNTRANSLATED"
            engine = provider.name
            version = provider.version
    else:
        problem = quality_problem(source, target)
        if problem:
            status = "REVIEW_REQUIRED"
            target = ""
    return {
        "entry_identity": row.get("entry_identity", ""),
        "dat_key": row.get("dat_key", ""),
        "source_digest": row.get("source_digest", ""),
        "token_signature": row.get("token_signature", ""),
        "target": target if status in {"HUMAN_APPROVED", "TM_REUSED", "GLOSSARY", "MACHINE_TRANSLATED"} else "",
        "translation_status": status or "UNTRANSLATED",
        "translation_engine": engine,
        "translation_engine_version": version,
        "critical_ui": bool(row.get("critical_ui")),
        "quality_error": problem,
    }


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--input", required=True, help="private/transient source JSONL")
    parser.add_argument("--output", required=True, help="candidate JSONL without English source")
    parser.add_argument("--context", help="private JSON with approved/tm/glossary/protected_names")
    parser.add_argument(
        "--provider",
        choices=("noop", "opus", "argos", "github-models", "openai-compatible", "copilot-cli"),
        default="noop",
    )
    parser.add_argument("--model", default="Helsinki-NLP/opus-mt-tc-big-en-tr")
    parser.add_argument("--revision")
    parser.add_argument("--cache")
    parser.add_argument("--offline", action="store_true")
    parser.add_argument("--batch-size", type=int, default=4)
    parser.add_argument("--beams", type=int, default=3)
    parser.add_argument("--max-new-tokens", type=int, default=512)
    parser.add_argument("--threads", type=int, default=0)
    parser.add_argument("--adaptive-max-tokens", action="store_true", help="experimental speed profile; retries only detected truncation")
    parser.add_argument("--length-bucket", action="store_true", help="experimental batching; may change beam tie-breaking")
    parser.add_argument("--github-model", default="openai/gpt-4.1")
    parser.add_argument("--github-endpoint", default="https://models.github.ai/inference/chat/completions")
    parser.add_argument("--github-token-env", default="GITHUB_TOKEN")
    parser.add_argument("--openai-model", default="")
    parser.add_argument("--openai-endpoint", default="")
    parser.add_argument("--openai-token-env", default="TRANSLATION_API_KEY")
    parser.add_argument("--openai-token-header", default="Authorization")
    parser.add_argument("--openai-token-prefix", default="Bearer")
    parser.add_argument("--copilot-command", default="copilot")
    parser.add_argument("--copilot-model", default="auto")
    parser.add_argument("--copilot-token-env", default="GITHUB_TOKEN")
    parser.add_argument("--copilot-timeout", type=int, default=180)
    args = parser.parse_args()
    approved, tm, glossary, protected_names = load_context(args.context)
    provider = build_provider(args.provider, args)
    input_text = Path(args.input).read_text(encoding="utf-8-sig")
    try:
        parsed_input = json.loads(input_text)
        rows = parsed_input if isinstance(parsed_input, list) else [parsed_input]
    except json.JSONDecodeError:
        rows = [json.loads(line) for line in input_text.splitlines() if line.strip()]
    batch_size = max(1, args.batch_size)
    generated_by_index: dict[int, str | None] = {}
    provider_errors_by_index: dict[int, str] = {}
    pending_indices: list[int] = []
    pending_texts: list[str] = []
    for index, row in enumerate(rows):
        source = str(row.get("english", ""))
        if (source and source not in approved and source not in tm and source not in glossary
                and not bool(row.get("critical_ui"))):
            masked, _ = mask_tokens(source, protected_names)
            pending_indices.append(index)
            pending_texts.append(masked)
    pending_items = list(zip(pending_indices, pending_texts))
    if args.length_bucket:
        pending_items.sort(key=lambda item: len(item[1]))
    # Identical masked sources have identical safe translations. Translate each
    # unique text once, then fan the result back to every catalog identity.
    grouped: dict[str, list[int]] = {}
    ordered_unique: list[tuple[str, list[int]]] = []
    for index, text in pending_items:
        if text not in grouped:
            grouped[text] = []
            ordered_unique.append((text, grouped[text]))
        grouped[text].append(index)
    for offset in range(0, len(ordered_unique), batch_size):
        chunk = ordered_unique[offset:offset + batch_size]
        chunk_texts = [item[0] for item in chunk]
        try:
            chunk_outputs = provider.translate_many(chunk_texts)
        except Exception as exc:
            chunk_outputs = [None for _ in chunk_texts]
            error_name = str(exc).strip() or type(exc).__name__
            error_name = re.sub(r"(?i)(?:bearer|token|authorization)[=: ]+\S+", "[redacted]", error_name)[:240]
            for _, indices in chunk:
                for index in indices:
                    provider_errors_by_index[index] = error_name
        for (_, indices), output in zip(chunk, chunk_outputs):
            for index in indices:
                generated_by_index[index] = output

    results = []
    for index, row in enumerate(rows):
        generated = generated_by_index.get(index)
        results.append(
            process(
                row,
                provider,
                approved,
                tm,
                glossary,
                protected_names,
                generated=generated,
                provider_error=provider_errors_by_index.get(index),
            )
        )
    Path(args.output).parent.mkdir(parents=True, exist_ok=True)
    Path(args.output).write_text("".join(json.dumps(result, ensure_ascii=False, sort_keys=True) + "\n" for result in results), encoding="utf-8")
    counts: dict[str, int] = {}
    for result in results:
        counts[result["translation_status"]] = counts.get(result["translation_status"], 0) + 1
    print("TRANSLATION_PIPELINE|provider=" + provider.name + "|counts=" + json.dumps(counts, sort_keys=True), file=sys.stderr)
    if provider_errors_by_index:
        print(
            "TRANSLATION_PROVIDER_ERRORS|count="
            + str(len(provider_errors_by_index))
            + "|types="
            + json.dumps(sorted(set(provider_errors_by_index.values())))
            ,
            file=sys.stderr,
        )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
