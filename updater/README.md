# LOTR TÜRKÇE YAMA

Bu programın kullanıcıya görünen adı **LOTR TÜRKÇE YAMA**'dır. Teknik yayın varlığı adı, mevcut kurulumlarla uyumluluğu korumak için sabit tutulur.

Yayın deposu son kullanıcıların giriş yapmadan güncelleme alabilmesi için public'tir;
ham kaynak ve çeviri çalışma deposu ayrı tutulur ve private kalır.

Her yayın eksiksiz, güncel Türkçe `client_local_English.dat` dosyasıdır.
Kurulum aracı semantic/delta katmanı dağıtmaz ve mevcut DAT'ın üzerine çeviri
uygulamaya çalışmaz: yeni sürüm bulunduğunda tam DAT'ı indirir, doğrular ve
oyun klasöründeki dosyayla atomik olarak değiştirir. Böylece launcher'ın yeni
resmî DAT'ı, eski bir Türkçe DAT veya eksik kurulum durumu aynı güvenli akışta
ele alınır.
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
8. Resmî oyun güncellemesi Türkçe DAT'ın üzerine gelse bile yeni tam DAT indirilir;
   eski dosyayla semantic birleştirme yapılmaz.
9. Kaynak veya sonuç kimliği uyuşmuyorsa işlem güvenli biçimde durur; paket
   üretimindeki bir hata otomatik olarak bozuk orijinal DAT sayılmaz.

Updater manifestteki `asset_kind=full_dat` sözleşmesini indirir ve doğrular
(asset boyutu/SHA-256, kaynak oyun kimliği ve şema). Tam DAT geçici `.part`
dosyasına alınır, hash doğrulaması tamamlanmadan canlı dosyaya dokunulmaz;
başarısız değişimde doğrulanmış yedek ve kurulum durumu geri yüklenir.
`semantic_delta_patch` geçmiş yayınlarla uyumluluk için kodda okunabilir olsa da
son kullanıcı akışı ve yayın betiği yalnız `full_dat` kabul eder. Windows kod
imzalama ayrıca yapılmadıkça SmartScreen uyarısı görülebilir.
