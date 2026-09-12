# Release modeli

## Eksiksiz DAT yayınları

Her kararlı yayın, güncel oyun sürümüne uygulanmış tüm Türkçe çeviriyi içeren
tek bir `full_dat` varlığıdır. Güncelleme alan program bu dosyayı baştan
indirir, SHA-256 ile doğrular, mevcut `client_local_English.dat` dosyasını
yedekler ve tam dosyayı atomik olarak yerleştirir. Mevcut dosyanın daha önce
Türkçe yamalanmış olması veya launcher tarafından değiştirilmiş olması yayın
kurulumunu engellemez; yeni tam DAT eski katmanların üzerine uygulanmaz.

İki ayrı kanal planlanır:

- Patch release: manifest ve doğrulanmış `full_dat` asset'i.
- Installer release: **LOTR TÜRKÇE YAMA** kurulum aracının güncellemesi.

Patch release, installer'ın yeniden build edilmesini gerektirmemelidir. Her patch yeni immutable tag, release ID, asset ID, boyut ve SHA-256 alır. Draft/prerelease release'ler updater tarafından reddedilir.

Tam DAT asset'i public release'e konur; kullanıcı uygulaması yalnız bu eksiksiz
paketi kullanır. Manifest kaynak DAT kimliğini, tam aday DAT boyut/hash'ini,
güvenli/atlanan/kritik sayaçlarını ve üretici/sağlayıcı sürümünü taşır.
Güncel 1,9 GB resmî DAT kopyasındaki tam yazma ve yeniden okuma testi geçmeden
yayın yapılmaz. Eski semantic manifestleri okuyabilen uyumluluk kodu yeni
yayınlarda kullanılmaz.

Sürüm kapıları: kaynak temel doğrulaması, deterministik paket üretimi, token/kalite koruması, yanlış-temel/tahrifat/geri alma testleri, DAT adayının tam yeniden okunması, kritik inceleme sayısının sıfır olması ve lisans incelemesi. Yayın işi hazır olmadan `contents: write` izni verilmez.
