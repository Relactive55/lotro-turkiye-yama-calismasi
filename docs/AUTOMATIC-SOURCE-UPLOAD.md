# LOTRO kaynak güncellemesini otomatik gönderme

Oyun resmi LOTRO launcher ile güncellendikten sonra `tools/SourceUpdateSender`
programı çalıştırılır. Program seçilen `client_local_English.dat` dosyasını
salt okunur açar, önceki yerel katalog durumuyla karşılaştırır ve yalnızca
`NEW`/`MODIFIED` kayıtları küçük source bundle dosyalarına ayırır.

Ham DAT, kurulum EXE'si veya native DLL GitHub'a gönderilmez. İngilizce kaynak
metni yalnız private `Relactive55/lotro-turkiye-yama-kaynak` deposunda tutulur.
Program, GitHub CLI'nin mevcut oturumunu kullanır; parolayı veya token'ı dosyaya
yazmaz. Hedef depo private değilse gönderimi fail-closed durdurur.

## İlk kullanım

1. Private kaynak deposunda Actions secret olarak `TRANSLATION_REPO_TOKEN` tanımlı
   olmalıdır. Token yalnız `Relactive55/lotro-turkiye-yama-calismasi` üzerinde
   Contents: Read and write ve Pull requests: Read and write yetkilerine sahip
   fine-grained token olmalıdır.
2. Bilgisayarda GitHub CLI ile bir kez `gh auth login` yapılır.
3. İlk çalıştırmada `GÜNCELLEME/client_local_English.dat` içindeki tek temiz DAT
   seçilir. Program önceki katalog yoksa bu DAT'ı temel olarak kaydeder ve patch
   üretmez; durum dosyası proje kökündeki `GÜNCELLEME/catalog.jsonl.gz`
   içinde tutulur. Bu dosya `.gitignore` ile yerel kalır ve GitHub'a gönderilmez.
4. Sonraki LOTRO güncellemelerinde aynı dosyanın üzerine yeni temiz DAT'ı koyup
   yalnızca **GitHub'a Gönder** düğmesine basılır. Önceki katalog durum dosyasından
   otomatik alınır; ikinci DAT seçilmez.

Private Actions bundle'ı doğrular, çeviri deposundaki `translation_pipeline.py`
scriptini ücretsiz ve yerel Argos modeliyle çalıştırır ve İngilizce kaynak içermeyen
güvenli adayları public release deposunda inceleme Pull Request'i olarak açar.
PR yalnız güvenli hedef alanlarını taşır. Argos modeli
yalnız geçici runner alanına indirilir, boyut/SHA-256 ile doğrulanır ve depoya konmaz.
API anahtarı, Copilot hesabı veya son kullanıcı üyeliği gerekmez. Model çalışmazsa job
fail-closed durur; boş veya İngilizce aday çeviri deposuna aktarılmaz. Kritik
arayüz satırları ve kalite kontrolünden geçmeyen kayıtlar otomatik kabul edilmez.
DAT yazımı, round-trip doğrulaması ve release yayınlama kapıları korunur.

Programın varsayılan private deposu ve branch'i ayarlar dosyasından değiştirilebilir:

`%LOCALAPPDATA%\Relactive\LotroSourceSender\settings.json`

Program x86 olarak çalışmalıdır; aynı klasörde lisanslı `datexport.dll` bulunan
mevcut geliştirici aracı çıktısı ile birlikte kullanılmalıdır. Girdi DAT'ı hiçbir
zaman değiştirilmez.
