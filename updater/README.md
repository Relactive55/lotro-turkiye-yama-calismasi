# LOTR TÜRKÇE YAMA

Bu programın kullanıcıya görünen adı **LOTR TÜRKÇE YAMA**'dır. Teknik yayın varlığı adı, mevcut kurulumlarla uyumluluğu korumak için sabit tutulur.

Yayın deposu son kullanıcıların giriş yapmadan güncelleme alabilmesi için public'tir;
ham kaynak ve çeviri çalışma deposu ayrı tutulur ve private kalır.

Zincirli semantic yayınlarda ilk kök paket bir kez alınır. `patch_mode=incremental`
manifestleri predecessor release ve DAT/katalog SHA-256 kimliklerini doğrular;
kurulum aracı yalnız eksik küçük düzeltme katmanlarını indirip sırayla uygular.
Bu nedenle mevcut kullanıcı sonraki düzeltmelerde 440 MB'lık kök paketi yeniden
indirmez. Temiz kurulumda kök ve ardından gerekli katmanlar otomatik çözülür.
DAT okuma/yeniden oluşturma arayüzü kilitlemez: kurulum ayrı iş parçacığında
çalışır, aşama mesajı ve hareketli ilerleme çubuğu gösterilir; **İptal** düğmesi
güvenli iptal başlatır. Dosya yolu, boyutu ve SHA-256 birlikte doğrulanır;
kullanıcı aynı boyutta orijinal DAT kopyaladıysa eski kurulum kaydına güvenilmez.
Pencere kapatılırsa devam eden iptal/geri alma bitmeden süreç kapatılmaz.

Bu, `LOTRÇEVİRİ.exe` geliştirici aracından ayrı son kullanıcı updater'ıdır. AI/translation/TM/glossary/GPU/Qwen/OPUS bileşeni taşımaz.

Akış:

1. Sabit HTTPS `Relactive55/lotro-turkiye-yama-calismasi` stable release kontrolü.
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
9. Kaynak veya sonuç kimliği uyuşmuyorsa işlem güvenli biçimde durur; paket
   üretimindeki bir hata otomatik olarak bozuk orijinal DAT sayılmaz.

Updater manifestteki `asset_kind=semantic_delta_patch` sözleşmesini indirip
doğrular (kaynak DAT/katalog kimliği, asset boyutu/SHA-256, şema ve token
kuralları). Yönetilen DAT yazıcısı sentetik testlerin yanında güncel 1,9 GB
resmî DAT kopyasında tam uygulama ve yeniden okuma kapısından geçirilir. Canlı
DAT doğrudan düzenlenmez; doğrulanmış temiz kaynak yedeğinden ayrı aday üretilir
ve yalnız bütün kontroller geçerse güvenli değiştirme yapılır. `full_dat` yolu
yalnız uyumluluk kodudur ve son dağıtım modeli değildir. Windows kod imzalama
ayrıca yapılmadıkça SmartScreen uyarısı görülebilir.
