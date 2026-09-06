# LOTRO_Turkce_Yama_Setup

Bu, `LOTRÇEVİRİ.exe` geliştirici aracından ayrı son kullanıcı updater'ıdır. AI/translation/TM/glossary/GPU/Qwen/OPUS bileşeni taşımaz.

Akış:

1. Sabit HTTPS `Relactive/lotro-turkiye-yama-calismasi` stable release kontrolü.
2. `manifest.json` ve beklenen patch asset kimliği, boyutu ve SHA-256 doğrulaması.
3. 1.9 GB sınıfı full DAT için streaming `.part` indirme, disk alanı kontrolü, iptal ve ilerleme bildirimi.
4. LOTRO DAT + launcher/client marker doğrulaması ve çalışan süreç kontrolü.
5. Doğrulanmış backup, geçici candidate, güvenli replacement ve post-install hash kontrolü.
6. Atomic `installed_patch.json`; hata durumunda DAT/state rollback.
7. İlk temiz kurulumda sonraki sürümler için doğrulanmış kaynak yedeğini otomatik
   saklama; kullanıcıdan DAT seçmesini veya taşımasını istememe.
8. Resmî oyun güncellemesi Türkçe DAT üzerine gelirse güncellenmiş DAT, önceki
   temiz yedek ve yeni semantic patch'i otomatik birleştirme; temiz ve Türkçe
   katalog kimlikleri bütünüyle doğrulanmazsa hiçbir oyun dosyasını değiştirmeme.

Updater manifestteki `asset_kind=semantic_delta_patch` sözleşmesini indirip
doğrular (kaynak DAT/katalog kimliği, asset boyutu/SHA-256, şema ve token
kuralları). Yönetilen DAT yazıcısı sentetik testlerin yanında güncel 1,9 GB
resmî DAT kopyasında tam uygulama ve yeniden okuma kapısından geçirilir. Canlı
DAT doğrudan düzenlenmez; doğrulanmış temiz kaynak yedeğinden ayrı aday üretilir
ve yalnız bütün kontroller geçerse güvenli değiştirme yapılır. `full_dat` yolu
yalnız uyumluluk kodudur ve son dağıtım modeli değildir. Windows kod imzalama
ayrıca yapılmadıkça SmartScreen uyarısı görülebilir.
