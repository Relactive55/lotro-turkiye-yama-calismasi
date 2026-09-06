# Release modeli

İki ayrı kanal planlanır:

- Patch release: manifest ve doğrulanmış `semantic_delta_patch` asset'i.
- Installer release: `LOTRO_Turkce_Yama_Setup.exe` güncellemesi.

Patch release, installer'ın yeniden build edilmesini gerektirmemelidir. Her patch yeni immutable tag, release ID, asset ID, boyut ve SHA-256 alır. Draft/prerelease release'ler updater tarafından reddedilir.

Tam DAT asset'i public release'e konmaz; kullanıcı kendi resmî DAT'ını kullanır. Semantic patch manifesti kaynak DAT/katalog SHA-256'sını, güvenli/atlanan/kritik sayaçlarını ve üretici/sağlayıcı sürümünü taşır. Güncel 1,9 GB resmî DAT kopyasındaki tam uygulama ve yeniden okuma testi geçmeden semantic sürüm yayımlanmaz.

Sürüm kapıları: kaynak temel doğrulaması, deterministik paket üretimi, token/kalite koruması, yanlış-temel/tahrifat/geri alma testleri, DAT adayının tam yeniden okunması, kritik inceleme sayısının sıfır olması ve lisans incelemesi. Yayın işi hazır olmadan `contents: write` izni verilmez.
