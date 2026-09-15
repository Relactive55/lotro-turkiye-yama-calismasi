# LOTR TÜRKÇE YAMA

## Güncel dağıtım politikası

Yeni yayınlarda imzalı semantic paket ve kullanıcının resmî DAT'ı temel alınır.
Full-DAT varlıkları yalnız geriye dönük uyumluluk içindir ve yayın betiğinde
açık hukukî onay anahtarı olmadan üretilemez. İndirme `.part` dosyasını korur,
HTTP Range ile devam eder ve manifest schema v2 imzasını doğrulamadan kuruluma
başlamaz.

Bu programın kullanıcıya görünen adı **LOTR TÜRKÇE YAMA**'dır. Teknik yayın varlığı adı, mevcut kurulumlarla uyumluluğu korumak için sabit tutulur.

Yayın deposu son kullanıcıların giriş yapmadan güncelleme alabilmesi için public'tir;
ham kaynak ve çeviri çalışma deposu ayrı tutulur ve private kalır.

Yeni yayınlar imzalı semantic paketlerdir. Kurulum aracı manifest imzasını ve
paket hash'ini doğrular, ardından semantic değişiklikleri kullanıcının kurulu
resmî DAT'ı üzerinde uygular. Tam DAT akışı yalnız hukukî onayla açılan legacy
kanaldır; launcher'ın yeni DAT'ı ile eski Türkçe DAT bu kanalda birleştirilmez.
DAT okuma/yeniden oluşturma arayüzü kilitlemez: kurulum ayrı iş parçacığında
çalışır, aşama mesajı ve hareketli ilerleme çubuğu gösterilir; **İptal** düğmesi
güvenli iptal başlatır. Dosya yolu, boyutu ve SHA-256 birlikte doğrulanır;
kullanıcı aynı boyutta orijinal DAT kopyaladıysa eski kurulum kaydına güvenilmez.
Pencere kapatılırsa devam eden iptal/geri alma bitmeden süreç kapatılmaz.

Bu, `LOTRÇEVİRİ.exe` geliştirici aracından ayrı son kullanıcı updater'ıdır. AI/translation/TM/glossary/GPU/Qwen/OPUS bileşeni taşımaz.

Akış:

1. Sabit HTTPS `Relactive55/lotro-turkiye-yama-calismasi` stable release kontrolü.
2. `manifest.json` ve beklenen patch asset kimliği, boyutu ve SHA-256 doğrulaması.
3. Büyük paketler için streaming `.part` indirme, HTTP Range ile devam, disk alanı kontrolü, iptal ve ilerleme bildirimi.
4. LOTRO DAT + launcher/client marker doğrulaması ve çalışan süreç kontrolü.
5. Doğrulanmış backup, geçici candidate, güvenli replacement ve post-install hash kontrolü.
6. Atomic `installed_patch.json`; hata durumunda DAT/state rollback.
7. İlk temiz kurulumda sonraki sürümler için doğrulanmış kaynak yedeğini otomatik
   saklama; kullanıcıdan DAT seçmesini veya taşımasını istememe.
8. Resmî oyun güncellemesi sonrası yeni semantic paket güncel DAT üzerinde
   doğrulanır ve uygulanır; uyumsuz temel fail-closed reddedilir.
9. Kaynak veya sonuç kimliği uyuşmuyorsa işlem güvenli biçimde durur; paket
   üretimindeki bir hata otomatik olarak bozuk orijinal DAT sayılmaz.

**Tekrar Kontrol Et** bellekteki eski release'i kullanmaz; GitHub stable release
sorgusunu yeniden çalıştırır. Hash tabanlı geçici paket klasörleri sınırlı
retention ile tutulur; yarım `.part` dosyaları resume için korunur.

Updater manifestteki `semantic_delta_patch` sözleşmesini indirir ve doğrular
(detached RSA-SHA256 imzası, asset boyutu/SHA-256, kaynak oyun kimliği ve şema).
Paket geçici `.part` dosyasına alınır, doğrulama tamamlanmadan canlı dosyaya
dokunulmaz; başarısız değişimde doğrulanmış yedek ve kurulum durumu geri yüklenir.
`full_dat` yalnız geçmiş yayınlarla uyumluluk için okunabilir. Windows kod
imzalama ayrıca yapılmadıkça SmartScreen uyarısı görülebilir.
