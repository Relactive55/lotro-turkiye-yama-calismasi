# LOTRO kaynak güncellemesini otomatik gönderme

Oyun resmi LOTRO launcher ile güncellendikten sonra `tools/SourceUpdateSender`
programı çalıştırılır. Program seçilen `client_local_English.dat` dosyasını
salt okunur açar, önceki yerel katalog durumuyla karşılaştırır ve yalnızca
`NEW`/`MODIFIED` kayıtları küçük source bundle dosyalarına ayırır.

Ham DAT, kurulum EXE'si veya native DLL GitHub'a gönderilmez. İngilizce kaynak
metni yalnız private `Relactive55/lotro-turkiye-yama-kaynak` deposunda tutulur.
Program, GitHub CLI'nin mevcut oturumunu kullanır; parolayı veya token'ı dosyaya
yazmaz. Hedef depo public ise gönderimi fail-closed durdurur.

## İlk kullanım

1. Private kaynak deposunda Actions secret olarak `PUBLIC_REPO_TOKEN` tanımlı
   olmalıdır. Token yalnız `Relactive55/lotro-turkiye-yama-calismasi` üzerinde
   Contents: Read and write ve Pull requests: Read and write yetkilerine sahip
   fine-grained token olmalıdır.
2. Bilgisayarda GitHub CLI ile bir kez `gh auth login` yapılır.
3. İlk çalıştırmada güncel DAT ve eski temiz DAT seçilir. Program `.lotro-source-
   state.jsonl.gz` dosyasını eski temiz DAT'ın yanında oluşturur.
4. Sonraki LOTRO güncellemelerinde yalnız yeni temiz DAT seçilip **GitHub'a
   Gönder** düğmesine basılır.

Private Actions bundle'ı doğrular, public projedeki `translation_pipeline.py`
scriptini GitHub Copilot CLI sağlayıcısıyla çalıştırır ve İngilizce kaynak
içermeyen güvenli adayları public projede Pull Request olarak açar. GitHub
Models inference API emekliye ayrıldığı için workflow artık Copilot CLI ve
`copilot-requests: write` yetkisini kullanır. Hesabın etkin Copilot planı yoksa
job fail-closed durur; boş veya İngilizce aday public depoya aktarılmaz. Kritik
arayüz satırları ve kalite kontrolünden geçmeyen kayıtlar otomatik kabul edilmez.
DAT yazımı, round-trip doğrulaması ve release yayınlama kapıları korunur.

Programın varsayılan private deposu ve branch'i ayarlar dosyasından değiştirilebilir:

`%LOCALAPPDATA%\Relactive\LotroSourceSender\settings.json`

Program x86 olarak çalışmalıdır; aynı klasörde lisanslı `datexport.dll` bulunan
mevcut geliştirici aracı çıktısı ile birlikte kullanılmalıdır. Girdi DAT'ı hiçbir
zaman değiştirilmez.
