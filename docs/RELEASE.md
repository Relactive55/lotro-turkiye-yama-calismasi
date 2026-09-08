# Release modeli

## Zincirli küçük düzeltmeler

İlk kararlı semantic paket zincirin tam köküdür ve yaklaşık 440 MB olarak bir
kez indirilir. Sonraki düzeltmeler `patch_mode=incremental` ile yalnızca
değişen kayıtları içerir. Manifest; predecessor release/tag/asset kimliğini,
predecessor DAT ve katalog SHA-256 değerlerini ve zincir derinliğini taşır.
Updater önce bu kimlikleri doğrular, yalnız eksik katmanları indirir ve
katmanları kökten güncele sırayla uygular. Böylece mevcut kullanıcı için normal
düzeltme indirmesi KB/MB seviyesine iner; predecessor asset her release'e
yeniden yüklenmez.

Yeni bir kök semantic paket, oyun DAT temeli değiştiğinde veya zincir bakımı
gerektiğinde yayımlanır. Zincir 32 katmanla sınırlıdır; her katman bağımsız
SHA-256, katalog ve rollback doğrulamasından geçer.

İki ayrı kanal planlanır:

- Patch release: manifest ve doğrulanmış `semantic_delta_patch` asset'i.
- Installer release: `LOTRO_Turkce_Yama_Setup.exe` güncellemesi.

Patch release, installer'ın yeniden build edilmesini gerektirmemelidir. Her patch yeni immutable tag, release ID, asset ID, boyut ve SHA-256 alır. Draft/prerelease release'ler updater tarafından reddedilir.

Tam DAT asset'i public release'e konmaz; kullanıcı kendi resmî DAT'ını kullanır. Semantic patch manifesti kaynak DAT/katalog SHA-256'sını, güvenli/atlanan/kritik sayaçlarını ve üretici/sağlayıcı sürümünü taşır. Güncel 1,9 GB resmî DAT kopyasındaki tam uygulama ve yeniden okuma testi geçmeden semantic sürüm yayımlanmaz.

Sürüm kapıları: kaynak temel doğrulaması, deterministik paket üretimi, token/kalite koruması, yanlış-temel/tahrifat/geri alma testleri, DAT adayının tam yeniden okunması, kritik inceleme sayısının sıfır olması ve lisans incelemesi. Yayın işi hazır olmadan `contents: write` izni verilmez.
