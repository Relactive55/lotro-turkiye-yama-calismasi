# Release modeli

## Yeni güvenlik kuralı

Public release akışının varsayılanı imzalı semantic pakettir; kurulum aracı
kullanıcının resmî DAT'ı üzerinde yerel olarak çalışır. Tam DAT yeniden
dağıtımı hukukî onay olmadan yayın betiği tarafından engellenir.

## Semantic patch yayınları

Her kararlı yayın, güncel oyun sürümüne ait imzalı bir `semantic_delta_patch`
varlığıdır. Updater manifest imzasını ve SHA-256'yı doğrular, kullanıcının resmî
`client_local_English.dat` dosyasını yedekler ve semantic değişiklikleri atomik
olarak uygular. Temel DAT kimliği uyuşmuyorsa yayın fail-closed durur.

İki ayrı kanal planlanır:

- Patch release: manifest, detached `manifest.sig` ve doğrulanmış semantic asset.
- Installer release: **LOTR TÜRKÇE YAMA** kurulum aracının güncellemesi.

Patch release, installer'ın yeniden build edilmesini gerektirmemelidir. Her patch yeni immutable tag, release ID, asset ID, boyut ve SHA-256 alır. Draft/prerelease release'ler updater tarafından reddedilir.

Semantic asset public release'e konur; kullanıcı uygulaması yalnız imzası
doğrulanmış bu paketi kullanır. Manifest kaynak DAT kimliğini, asset boyut/hash'ini,
güvenli/atlanan/kritik sayaçlarını ve üretici/sağlayıcı sürümünü taşır. Eski
full-DAT manifestleri geçiş uyumluluğu için okunabilir; yeni üretim betiği
`-AllowFullDatDistribution` olmadan bunları yayımlamaz.

Sürüm kapıları: kaynak temel doğrulaması, deterministik paket üretimi, token/kalite koruması, yanlış-temel/tahrifat/geri alma testleri, DAT adayının tam yeniden okunması, kritik inceleme sayısının sıfır olması ve lisans incelemesi. Yayın işi hazır olmadan `contents: write` izni verilmez.
