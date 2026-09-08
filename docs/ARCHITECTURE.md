# Mimari kararlar

Semantic yayınlar kök + zincir katmanları olarak ilerler. Kök paket resmi temiz
DAT temelini taşır; sonraki `patch_mode=incremental` paketler yalnız değişen
kayıtları ve doğrulanmış predecessor kimliğini taşır. Updater, kurulu patch
durumuna göre zincirin eksik ucunu indirir ve her katmanda DAT/katalog,
token/format, aday ve rollback doğrulaması yapar.

## Roller

`LOTRÇEVİRİ.exe` geliştirici/çeviri/build aracıdır. `LOTRO_Turkce_Yama_Setup.exe` ayrı bir son kullanıcı updater'ıdır; yalnız release kontrolü, indirme, doğrulama, backup, kurulum ve rollback yapar. Updater'a AI, TM, glossary, GPU veya model runtime eklenmez.

## Geliştirici akışı

Resmî launcher ile güncellenmiş temiz İngilizce DAT → read-only native extraction → catalog → `CatalogIdentity` fingerprint'leri → `CatalogDiff` (`UNCHANGED/NEW/MODIFIED/REMOVED/MOVED/AMBIGUOUS`) → approved/TM/glossary → yalnız unresolved `NEW/MODIFIED` için yerel MT → ordered token/tag/numeric validation → güncel İngilizce DAT üzerinde candidate build → round-trip/binary validation → insan incelemeli PR.

`DID:RecordIndex:GroupIndex:IndexInGroup` mevcut konum anahtarıdır; cross-version kimliği değildir. Structural, record, source ve context fingerprint'leri birlikte kullanılır. `EntryIdentity`, DID + record/structural fingerprint üzerinden komşu metin değişimlerinden bağımsızdır; `ContextFingerprint` yalnız yardımcı eşleştirme sinyalidir. `SourceDigest`, source + protected/numeric token signature + record/structural fingerprint'i çerçeveli SHA-256 ile bağlar. Text tek başına identity değildir; belirsiz eşleşmede otomatik carry-over yapılmaz.

## Son kullanıcı akışı

Sabit HTTPS GitHub stable release → manifest doğrulama → asset boyut/SHA-256 doğrulama → gerçek LOTRO dizini ve oyun/launcher kapalı kontrolü → backup → geçici candidate → güvenli replacement → post-install doğrulama → atomic `installed_patch.json`.

İlk semantic kurulum yalnız manifest source SHA-256'sı ile eşleşen temiz baseline
üzerinde kabul edilir ve sonraki sürümler için doğrulanmış temiz kaynak yedeği
oluşturur. Resmî launcher daha sonra yamalı DAT'ı güncellerse updater; canlı DAT,
önceki temiz yedek ve yeni semantic paketten hem temiz hem Türkçe aday üretir.
Kaynak ve hedef katalog SHA-256 değerlerinin ikisi de eksiksiz eşleşmeden canlı
dosya değiştirilmez. Aynı sürümün yeniden çalıştırılması idempotent başarıdır.

## Semantic patch sözleşmesi

`semantic_delta_patch` JSON satırı raw English taşımaz. Her satır `entry_identity`, `dat_key`, DID/koordinatlar, `source_digest`, `token_signature`, `target`, `translation_status`, engine/version, classification ve `critical_ui` alanlarını taşır. `SemanticPatchBuilder` yalnız validation geçen `HUMAN_APPROVED`, `TM_REUSED`, `GLOSSARY` veya doğrulanmış `MACHINE_TRANSLATED` kayıtlarını deterministic sırada üretir. `SemanticPatchApplier` gerçek DAT yazıcısından bağımsız olarak source-digest, token ve critical-UI admission testini yapar; gerçek binary writer ayrı bir release kapısıdır.

## Yayın ayrımı

Patch release (`manifest.json` + yama asset'i) ile installer release (`LOTRO_Turkce_Yama_Setup.exe`) ayrı kanallardır. Patch değiştiğinde installer yeniden derlenmek zorunda değildir. Her patch immutable tag/release ID/asset ID/size/SHA-256 ile yayınlanır; otomatik merge/approve yoktur. Source acquisition cloud tarafı kanıtlanmadığı için varsayılan `CLIENT_ASSISTED` bundle akışıdır; yalnız güvenli normal içerik release'e girer, riskli satırlar İngilizce fallback olur.
