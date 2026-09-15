# LOTR TÜRKÇE YAMA

## Dağıtım güvenliği politikası (2026-09-15)

Yeni public release'ler varsayılan olarak kullanıcının kurulu resmî DAT'ı
üzerinde uygulanan imzalı `semantic_delta_patch` paketleridir. Tam proprietary
DAT dağıtımı varsayılan release akışında kapatılmıştır; legacy full-DAT yayını
yalnız hukukî onay ve `-AllowFullDatDistribution` ile açılabilir.

Updater indirmeyi `.part` dosyasında saklar, HTTP Range ile devam ettirir,
minimum updater sürümünü fail-closed zorunlu tutar ve schema v2 manifestlerini
detached RSA-SHA256 imzası olmadan kabul etmez.

LOTRO Türkiye Yama Çalışması'nın son kullanıcı uygulaması ve güvenli güncelleme akışı.

Yeni yayınlar imzalı `semantic_delta_patch` paketini taşır. Updater bu paketi
kullanıcının kurulu resmî DAT'ı üzerinde yerel olarak uygular; tam DAT yeniden
dağıtımı yalnız hukukî onayla açılan legacy kanaldır.
Güncellemede program imzalı semantic katmanı doğrular ve güncel resmî DAT'a
uygular; tam DAT'ı yeniden dağıtmaz.

## Güncel programı indir

Tam katalog taramasıyla arayüz, yetenek/eşya ve istatistik düzeltmeleri: [2026.09.12.1 notları](docs/RELEASE-NOTES-2026.09.12.1.md).
Bu paket için kurulum aracı `1.4.0.0` gerekir.

[**LOTR TÜRKÇE YAMA GitHub Releases sayfasını aç (Windows)**](https://github.com/Relactive55/lotro-turkiye-yama-calismasi/releases/latest)

Açılan sayfadaki **LOTR TÜRKÇE YAMA** kurulum varlığını indirin. Son kullanıcı
dağıtımında proje dışında barındırılan ZIP, EXE veya başka bir çalıştırılabilir
dosya kullanılmaz.

Program imzalı manifesti ve semantic paketi geçici dosyada indirir; imza, boyut
ve SHA-256 doğrulaması tamamlanmadan canlı DAT'ı değiştirmez. Hata durumunda
canlı dosya değiştirilmez veya değişiklik başladıysa doğrulanmış yedekten geri
dönülür.

The Lord of the Rings Online için ücretsiz Türkçe yerelleştirme ve güvenli güncelleme projesidir. Son kullanıcı GitHub Releases sayfasındaki **LOTR TÜRKÇE YAMA** kurulum aracını indirir ve **Yama Yap** düğmesine basar. Araç Steam ve bağımsız LOTRO kurulumlarını bulur, güncel tam Türkçe DAT'ı indirir ve oyun klasörüne güvenli biçimde yerleştirir.

> Kullanıcıya görünen ürün adı **LOTR TÜRKÇE YAMA**'dır. GitHub varlığının teknik dosya adı, mevcut kurulumları bozmamak için uyumluluk amacıyla sabit tutulur.

> Kaynak çıkarma ve çeviri deposu private tutulur; bu release deposu son kullanıcıların GitHub hesabı olmadan güncelleme alabilmesi için public bırakılmıştır.

## Son kullanıcı akışı

1. GitHub Releases sayfasından kurulum aracını indirin.
2. LOTRO ve launcher kapalıyken aracı çalıştırın.
3. **Yama Yap** düğmesine basın.
4. Araç manifest imzasını, paket boyutunu ve SHA-256 özetini doğrular; yedek
   almadan canlı dosyayı değiştirmez.
5. Yeni bir Türkçe paket yayımlandığında aynı kurulum aracı bunu otomatik gösterir.
6. Resmî launcher DAT'ı güncellerse yine yalnız **Yama Yap** düğmesine basılır.
   Araç yeni semantic paketi güncel resmî DAT üzerinde doğrular ve uygular.

Kod imzalama sertifikası eklenmediği sürece Windows SmartScreen uyarısı görülebilir.

## 2026.09.08.2 temelinin doğrulama referansı

- resmî İngilizce DAT boyutu: `1,894,213,416` bayt
- kaynak DAT SHA-256: `48deba5c621bb72492afd2687acffc49a0289054255c2136b7cb469ec1bdd967`
- kaynak katalog SHA-256: `bf5984ecb19add7c93911c73dba88ecdc1de221330027671c125da9212184922`
- katalog kaydı: `825,136`
- doğrulanan Türkçe kayıt: `643,828`
- dokunulan yerelleştirme bloğu: `231,578`
- kritik inceleme: `0`
- genel inceleme/reddedilen aday: `0`
- yeni kök sürümü: `2026.09.08.2`, kurulum aracı: `1.3.0.0`
- doğrulanmış Türkçe DAT boyutu: `1,939,169,648` bayt
- doğrulanmış Türkçe DAT SHA-256: `fb775e755b520bda6100382f6377c906d7b477194a3d5b3be2b02a7635215d02`
- sıkıştırılmış ana indirme: `67,511,953` bayt

Komut anahtarları, değişkenler, biçim parçaları ve özel adlar çeviri sayılmaz; bozulmamaları için açık koruma kararlarıyla kaynak biçiminde tutulur. Semantic paketin güncel 1,9 GB resmî DAT kopyasına tam uygulanması ve oluşan `825,136` kaydın yeniden okunması başarıyla sınanmıştır.

Tam DAT kaynak Git deposunda tutulmaz; yeni public release'lerde yalnız semantic
paket yayımlanır. RAR, ham katalog, özel çeviri havuzu, model dosyası ve oyuna
ait native DLL bu depoda tutulmaz.

## Hızlı düzeltme akışı

Bakımcılar için katalog önbelleği, terim seçimini DAT'ı yeniden açmadan hızlandırır.
Değişmiş bir adayın sonuç hash'i kayıt önbelleğinden tahmin edilmez: bazı tabloların
kayıt kimliği komşu metnin baytlarına bağlıdır. Yayın kimlikleri gerçek adayın tam
yazma/yeniden okuma sonucundan alınır. Ayrıntılı akış ve fail-closed
kuralları [`docs/FAST-CORRECTION-WORKFLOW.md`](docs/FAST-CORRECTION-WORKFLOW.md)
belgesinde, metadata sözleşmesi ise [`schemas/catalog-cache.schema.json`](schemas/catalog-cache.schema.json)
dosyasındadır.

## Otomatik güncelleme

Modern DAT başlığı düzeltmesi ve önceki taslağın neden geçersiz kılındığı:
[2026.09.08.2 sürüm notları](docs/RELEASE-NOTES-2026.09.08.2.md).

8 Eylül denetimi, v9'un önbellekten hesaplanmış sonuç katalog kimliğinde hata
buldu. Gerçek adayın bütün çeviri hedefleri doğrulandı; mevcut v9 manifestiyle
eşleşme testi ise geçmedi. Bu bulgu ve yerel düzeltmeler, yukarıdaki önceki sürüm
doğrulama sonuçlarıyla karıştırılmamalıdır. Ayrıntılar:
[güvenlik ve hız denetimi](docs/AUDIT-2026-09-08.md).

`Watch official LOTRO version` işi Standing Stone Games'in resmî `Game.Version` değerini günde bir kez, Türkiye saatiyle yaklaşık 12:17'de denetler ve yeni sürüm için tek takip kaydı açar. Steam zorunlu değildir. Sürüm sinyali tek başına yama yayımlamaz; güvenilir Windows kaynak makinesinde güncel DAT özeti değişimi doğrulanır, yalnız yeni/değişen İngilizce kayıtlar çeviri hattına alınır ve bütün kalite kapıları geçerse yeni release hazırlanır.

Bakımcı, resmî launcher güncellemesine denk geldiğinde temiz
`client_local_English.dat` dosyasını proje kökündeki `GÜNCELLEME` klasörüne
koyar ve mevcut dosyanın yerine geçirir. Geliştirici aracı tek dosyayı yalnız
kaynak olarak okur; önceki katalogu aynı klasördeki
`GÜNCELLEME/catalog.jsonl.gz` yerel durum kaydından alır. Durum dosyası `.gitignore`
ile yerel tutulur; tam DAT veya katalog GitHub'a yüklenmez.

Bir kullanıcı oyun içinde sorun bildirdiğinde ilgili kayıt düzeltilir ve yeni
semantic aday üretilir. Kurulum aracının kodu değişmediyse kullanıcı yeni EXE
indirmek zorunda kalmaz; mevcut araç yeni imzalı paketi görür.

## Depo yapısı

- `updater/`: son kullanıcı kurulum ve güncelleme aracı
- `developer-tool/`: katalog, diff, çeviri ve paket üretim kaynağı
- `schemas/`: manifest, semantic patch ve kurulum durumu sözleşmeleri
- `scripts/`: build, doğrulama ve resmî sürüm algılama araçları
- `tests/`: gerçek oyun dosyası içermeyen güvenlik ve davranış testleri
- `docs/`: mimari, otomasyon, güvenlik ve release belgeleri

Yerel build için `scripts/build.ps1 -Project All -DotnetPath <dotnet.exe>`, test için `scripts/test-updater.ps1 -DotnetPath <dotnet.exe>` kullanılabilir.
## Oyun güncellemesi sonrası otomatik çeviri

Oyun resmi launcher ile güncellendiğinde sürüm izleyicisi GitHub'da sinyal
oluşturur. Güncel temiz `client_local_English.dat` dosyasını seçmek için
`LOTRKaynakGonder.exe` yardımcı programı kullanılabilir. Program DAT'ı salt
okunur tarar, yeni/değişen metinleri private kaynak deposuna gönderir; GitHub
Actions otomatik çeviri adayı üretip çeviri deposunda incelemeli Pull Request açar.
Ham DAT hiçbir depoda herkese açık yayımlanmaz. Kurulum ve ilk kullanım ayrıntıları için
[otomatik kaynak gönderme rehberine](docs/AUTOMATIC-SOURCE-UPLOAD.md) bakın.
Güncelleme geldiğinde izlenecek kısa sıra için [güncelleme rehberine](docs/GUNCELLEME-REHBERI.md) bakın.
