# Mimari kararlar

Yayın modeli eksiksiz DAT'tır. Her `full_dat` paketi belirli bir temiz LOTRO
sürümüne uygulanmış bütün Türkçe çeviriyi taşır. Updater mevcut dosyanın
geçmişine bakarak katman seçmez; tam paketi indirir, hash'ini doğrular ve
oyun DAT'ını atomik olarak değiştirir. Semantic kataloglar geliştirici
doğrulaması için kullanılabilir, ancak son kullanıcı release'ine katman olarak
girmez.

## Roller

`LOTRÇEVİRİ.exe` geliştirici/çeviri/build aracıdır. **LOTR TÜRKÇE YAMA** ayrı bir son kullanıcı updater'ıdır; yalnız release kontrolü, indirme, doğrulama, backup, kurulum ve rollback yapar. Updater'a AI, TM, glossary, GPU veya model runtime eklenmez.

## Geliştirici akışı

Resmî launcher ile güncellenmiş temiz İngilizce DAT → read-only native extraction → catalog → `CatalogIdentity` fingerprint'leri → `CatalogDiff` (`UNCHANGED/NEW/MODIFIED/REMOVED/MOVED/AMBIGUOUS`) → approved/TM/glossary → yalnız unresolved `NEW/MODIFIED` için yerel MT → ordered token/tag/numeric validation → güncel İngilizce DAT üzerinde candidate build → round-trip/binary validation → insan incelemeli PR.

`DID:RecordIndex:GroupIndex:IndexInGroup` mevcut konum anahtarıdır; cross-version kimliği değildir. Structural, record, source ve context fingerprint'leri birlikte kullanılır. `EntryIdentity`, DID + record/structural fingerprint üzerinden komşu metin değişimlerinden bağımsızdır; `ContextFingerprint` yalnız yardımcı eşleştirme sinyalidir. `SourceDigest`, source + protected/numeric token signature + record/structural fingerprint'i çerçeveli SHA-256 ile bağlar. Text tek başına identity değildir; belirsiz eşleşmede otomatik carry-over yapılmaz.

## Son kullanıcı akışı

Sabit HTTPS GitHub stable release → manifest doğrulama → asset boyut/SHA-256 doğrulama → gerçek LOTRO dizini ve oyun/launcher kapalı kontrolü → backup → geçici candidate → güvenli replacement → post-install doğrulama → atomic `installed_patch.json`.

Kurulum mevcut DAT'ın clean baseline veya önceki patch olarak tanınmasını
gerektirmez. İndirilen tam asset boyut/hash ile doğrulanır; canlı DAT yedeklenir,
geçici aday dosyası hazırlanır ve ancak son hash doğrulamasından sonra atomik
değiştirilir. Launcher yeni bir oyun DAT'ı indirmiş olsa bile aynı akış uygulanır.
Başarısızlıkta canlı dosya ve `installed_patch.json` geri yüklenir.

## Semantic patch sözleşmesi

`semantic_delta_patch` JSON satırı raw English taşımaz. Her satır `entry_identity`, `dat_key`, DID/koordinatlar, `source_digest`, `token_signature`, `target`, `translation_status`, engine/version, classification ve `critical_ui` alanlarını taşır. `SemanticPatchBuilder` yalnız validation geçen `HUMAN_APPROVED`, `TM_REUSED`, `GLOSSARY` veya doğrulanmış `MACHINE_TRANSLATED` kayıtlarını deterministic sırada üretir. `SemanticPatchApplier` gerçek DAT yazıcısından bağımsız olarak source-digest, token ve critical-UI admission testini yapar; gerçek binary writer ayrı bir release kapısıdır.

## Yayın ayrımı

Release (`manifest.json` + tam DAT asset'i) ile **LOTR TÜRKÇE YAMA** kurulum
varlığı aynı stable release içinde doğrulanır. Tam DAT değiştiğinde installer
yeniden derlenmek zorunda değildir; manifest yeni asset ID/size/SHA-256 taşır.
Semantic paketler eski kurulumlarla uyumluluk için okunabilir olsa da yeni
release üretim betiği onları yayımlamaz.
