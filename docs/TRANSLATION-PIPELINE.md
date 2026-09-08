# Ücretsiz çeviri pipeline'ı

`developer-tool/automation/translation_pipeline.py` provider-swappable bir aday
üreticisidir. Private Actions akışı varsayılan olarak ücretsiz Argos modelini geçici
runner alanında çalıştırır; kullanıcı veya API anahtarı gerekmez. Copilot/OpenAI-uyumlu
sağlayıcılar yalnızca ayrıca yapılandırılırsa kullanılabilir; yerel `noop`, OPUS ve Argos
sağlayıcıları anahtar olmadan çalışabilir.

## Öncelik sırası

1. insan onaylı exact kayıt,
2. `approved_updates`,
3. exact Translation Memory,
4. glossary,
5. protected Tolkien/LOTRO adları,
6. seçili local provider (OPUS veya Argos),
7. token/number/markup doğrulaması ve kalite guard.

Model çalışmazsa iyi mevcut kayıtlar korunur; yeni riskli kayıtlar
`UNTRANSLATED`/`REVIEW_REQUIRED` olarak kalır. Bozuk veya token kaybetmiş çıktı
semantic patch'e alınmaz.

## Provider sözleşmesi

Script şu provider'ları destekler:

- `noop`: bağımlılıksız güvenli fallback ve CI sözleşme testi,
- `copilot-cli`: private GitHub Actions içinde `GITHUB_TOKEN` ve
  `copilot-requests: write` izniyle GitHub Copilot CLI üzerinden bulut çevirisi,
- `openai-compatible`: private workflow secret'ı ile açıkça yapılandırılmış
  HTTPS OpenAI-uyumlu uç nokta,
- `opus`: `Helsinki-NLP/opus-mt-tc-big-en-tr` için lazy local Transformers
  sağlayıcısı,
- `argos`: yerel Argos Translate sağlayıcısı.

Provider yalnız `translate(text)` davranışıyla değiştirilebilir. Dynamic
argument, markup ve protected isimler modelden önce `ZXQ####QXZ` maskeleriyle
korunur; geri yükleme başarısızsa kayıt reddedilir. Inline köşeli işaretlerin
(`[...]`) kaynağa göre bağlı/ayrı konumu da korunur; bu konum değişirse kayıt
semantic patch'e alınmaz.
Maskelenmiş kaynak metinleri aynıysa model yalnızca bir kez çalıştırılır ve
sonuç ilgili tüm catalog kimliklerine güvenli biçimde dağıtılır.
Copilot veya OpenAI-uyumlu servis kimlik bilgisi hiçbir dosyaya veya loga
yazılmaz; İngilizce kaynak yalnız private iş akışında istek sırasında geçici
olarak gönderilir ve aday çıktısından atılır. GitHub Models inference API
emekliye ayrıldığı için `github-models` seçeneği bilinçli olarak hata verir.
Kota veya servis hatasında iyi mevcut kayıtlar korunur, yeni kayıtlar güvenli
biçimde `UNTRANSLATED` kalır; release zorlanmaz.

## İnsan ve kural kayıtları

`audit_curated_translations.py`, `HUMAN_APPROVED`, `TM_REUSED` ve `GLOSSARY`
kayıtlarının tamamını DAT'a dokunmadan kontrol eder. Geçerli kayıtlar
`curated-safe-pool.tsv` içine aynen alınır; bozuk token, sayı, encoding veya
prompt-artığı taşıyan kayıtlar `curated-review-queue.tsv` ve ayrı bir OPUS
onarım kuyruğuna ayrılır. Onarım çıktısı ikinci bağımsız doğrulamadan geçmeden
aday havuzuna alınmaz.

## Model güvenliği ve runtime

`model-lock.example.json` alanları model kimliği, revision, boyut ve SHA-256
kilidi için şablondur; gerçek revision/boyut/hash bu hostta henüz bağımsız
olarak doğrulanmadığı için `verified=false` bırakılmıştır. Model binary'si
depoya commit edilmez.

GitHub-hosted runner RAM/CPU/download/install süresi gerçek model ile ölçülmeden
OPUS'u otomatik release job'ına bağlamak yoktur. İlk CI sözleşmesi `noop` ile
çalışabilir; provider benchmark'ı ayrı, elle başlatılan bir kapıdır.

Yerel CPU'da kalite profili beam=3 ve tam 512 token bütçesidir. Python 3.11
üzerinde float32 CPU yolu, önceki float16 CPU yoluna göre ölçülen yaklaşık
`2–3x` üretim hızını sağlarken örnek karşılaştırmada `8/8` çıktıyı aynen korudu.
Beam=2/1, agresif token bütçesi ve CTranslate2 denemeleri kalite kapısından
geçmediği için varsayılan profile alınmadı; deneysel hız seçenekleri otomatik
DAT uygulamasına bağlı değildir.

## Çıktı

Çıktı JSONL satırları `entry_identity`, `source_digest`, `token_signature`,
`target`, `translation_status`, provider ve kalite sonucunu taşır. Ham İngilizce
source çıktı dosyasına yazılmaz; semantic patch builder da aynı nedenle public
patch'e source metni koymaz.
