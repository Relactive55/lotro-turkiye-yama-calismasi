# Zincirli semantic paketler

İlk kararlı semantic paket zincirin sabit köküdür. Sonraki düzeltmeler
`patch_mode=incremental` katmanları olarak yalnız değişen kayıtları taşır.

Bir incremental manifest şu predecessor kimliklerini içerir:

- `base_release_tag`, `base_release_id`
- `base_asset_name`, `base_asset_id`, `base_asset_size`, `base_asset_sha256`
- `base_candidate_dat_sha256`, `base_candidate_dat_size`
- `base_candidate_catalog_sha256`
- `base_patch_version` ve `chain_depth`

Updater en son stable release'i ve manifesti doğrular, predecessor tag'ini
GitHub API'den yükler ve bütün asset/katalog kimliklerini karşılaştırır. Döngü,
eksik alan, yanlış SHA-256, draft/prerelease veya 32 katmandan uzun zincir
fail-closed olarak reddedilir.

Kurulum akışı:

1. Kullanıcının `installed_patch.json` durumu gerçek DAT yolu, boyutu ve SHA-256
   ile doğrulanır, sonra zincirde bulunur. Kayıt tek başına yeterli değildir.
2. Kökten sonraki eksik katmanlar indirilir; mevcut predecessor asset yeniden
   indirilmez.
3. Her katman önceki Türkçe DAT'ın DAT ve katalog SHA-256'sını doğrular.
4. Yalnız katmanın kayıtları uygulanır; token/format, aday katalog ve rollback
   kontrolleri tamamlanmadan canlı DAT değiştirilmez.

Temiz kurulumda yaklaşık 440 MB'lık kök paket yalnızca bir kez indirilir.
Günlük UI düzeltmeleri küçük JSON katmanlarıdır. Zincir 32 katmana ulaştığında
veya resmi LOTRO DAT temeli değiştiğinde yeni bir kök paket yayımlanır.
# Arşiv notu

Bu belge geçmiş semantic/delta yayın sözleşmesini açıklar. Son kullanıcı
release'leri artık `full_dat` kullanır: güncel Türkçe DAT her sürümde baştan
indirilir ve oyun klasörüne yerleştirilir. Yeni yayınlarda bu zincir akışı
kullanılmaz; belge yalnız eski release'leri ve geliştirici doğrulama kodunu
anlamak için tutulur.
