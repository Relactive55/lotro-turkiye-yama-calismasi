# Source acquisition ve istemci destekli güncelleme

## Karar

`SOURCE_ACQUISITION_MODE = CLIENT_ASSISTED`

`CLOUD_OFFICIAL_SOURCE_FETCH = UNVERIFIED`

GitHub Actions'ın yeni İngilizce metni çevirebilmesi için önce yeni source
catalog gerekir. Referans LOTROKoniecDev projesinin güncel dokümantasyonu da
oyun sürümlerinin elle kaydedildiğini söylüyor; launcher/DAT tarafında güvenilir
ve ücretsiz, herkese açık bir resmi tam source indirme sözleşmesi bu projede
kanıtlanmadı. Bu yüzden kararsız reverse-engineered endpoint production'a
otomatik bağlanmaz.

## İki güvenli seviye

### 1. CLOUD_OFFICIAL (gelecek kapısı)

Ancak aşağıdakiler birden fazla kontrollü denemeyle kanıtlanırsa açılabilir:

- resmi Standing Stone kaynağından HTTPS ve kimlik doğrulaması,
- değişmez source/DAT kimliği ve beklenen boyut/SHA-256,
- rate limit ve lisans koşulları,
- aynı source'un yeniden üretilebilir export'u,
- yeni source'un tam DAT olarak depoya yazılmasını gerektirmeyen akış.

Şimdilik bu şartlar sağlanmış sayılmıyor.

### 2. CLIENT_ASSISTED (mevcut tasarım)

Kullanıcı resmi launcher ile LOTRO'yu günceller. Setup, DAT'ı yalnız read-only
okuyarak metadata, catalog fingerprint, `NEW/MODIFIED` satır kimlikleri,
`source_digest` ve gerekli token bilgilerini içeren küçük bir bundle hazırlar.

Bakımcı girdi sözleşmesi sabittir:

- klasör: proje kökündeki `GÜNCELLEME`
- dosya: `client_local_English.dat`
- `GÜNCELLEME` girdisi varsa geliştirici aracı bunu `ORJİNAL DAT` kaynağından
  önce seçer;
- girdi salt okunur kaynak kabul edilir; doğrudan değiştirilmez ve GitHub'a
  yüklenmez.

Bundle:

- opt-in olmadan yüklenmez,
- kişisel dosya veya tam 1,9 GB DAT içermez,
- şema, SHA-256, duplicate ve catalog tutarlılığı kontrolünden geçer,
- yalnız `NEW`/`MODIFIED` kayıtları taşır,
- `scripts/validate-source-bundle.ps1` ile fail-closed doğrulanır.

Mevcut uygulama `developer-tool/src/LotroTrGemini/SourceBundle.cs` içindedir.
Çıkartılmış catalog ve diff üzerinden deterministik JSON bundle üretir; kendi
başına yükleme yapmaz, DAT'ı değiştirmez ve ham İngilizceyi yalnızca çağıran
kod açıkça seçerse ekler. Kullanıcıya gösterilen opt-in dışa aktarma düğmesi,
incelenmiş gönderim akışı ve public bundle saklama politikası release işidir.

Kullanıcıya dosya gönderimi veya GitHub token'ı istemek yerine normal web/PR
akışı gösterilir. Public repo'da ham bundle kalıcı tutulmadan önce lisans ve
kişisel veri kontrolü gerekir.

## Bilinçli sınır

Yeni source kimse tarafından sağlanmadıkça yeni İngilizce metni çevirmek teknik
olarak mümkün değildir. Bu durumda mevcut `source_digest` eşleşen çeviriler
kullanılabilir; değişen veya belirsiz satırlar İngilizce fallback olur.
