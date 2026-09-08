# Hızlı düzeltme akışı

## Yayın indirmesini de küçültme

Çalışma önbelleği terim aramayı hızlandırır; yayın asset'inin boyutunu tek başına
küçültmez. Bunun için ilk semantic paket zincirin sabit kökü, sonraki paketler
ise `patch_mode=incremental` katmanlarıdır. Her katman yalnız yeni/düzeltilen
kayıtları ve predecessor DAT/katalog kimliğini taşır. Updater mevcut
`installed_patch.json` durumuna göre zincirin eksik ucunu indirir; kök paket her
düzeltmede yeniden indirilmez. Temiz kurulumda kök bir kez alınır ve ardından
katmanlar sırayla uygulanır.

Terim seçerken 1,9 GB'lık resmî DAT yerine bakımcının bilgisayarında saklanan
katalog önbelleği kullanılabilir. Ancak değişen bir adayın katalog hash'i yalnız
kayıt önbelleğinden güvenle hesaplanamaz; gerçek DAT adayının oluşturulup tam
doğrulanması gerekir. Kaynak İngilizce metin içeren JSONL ve ikili
önbellekler yerel çalışma dosyalarıdır; GitHub deposuna veya release'e yüklenmez.

## Katmanlar

1. **Kaynak çapası:** `client_local_English.dat` yalnızca okunur. Kaynak DAT
   SHA-256'sı, boyutu, kaynak katalog özeti ve kayıt sayısı birlikte saklanır.
2. **Kayıt önbelleği:** Her kayıt için kararlı anahtar, konum, DID, kaynak metin,
   kayıt/yapı/kaynak/bağlam parmak izleri ve token imzası tutulur. JSONL gzip
   dosyası denetlenebilir yerel arşivdir. Metadata şeması v2, kaynak kimliğine
   ek olarak iki sıkıştırılmış dosyanın SHA-256 değerlerini, ikili biçim sürümünü
   ve `known_ui_fixes_sha256` içerik kimliğini taşır.
3. **Hızlı yan dosya:** Aynı kayıtların `LTCCACHE1` başlıklı ikili gzip yan dosyası
   vardır. Metadata'da adı ve SHA-256'sı doğrulanan yan dosya okunur; aynı isimde
   bir dosyanın varlığı yeterli değildir. Yan dosya eksikse doğrulanmış JSONL
   okunabilir; mevcut yan dosyanın hash'i yanlışsa işlem durur.
4. **Tam eşleşme dizini:** `Ranged`, `Damage`, `Duration`, `Cost`, `Cooldown`,
   `Focus`, `Main-hand`, `Off-hand`, `Fast`, `Induction`, `Melee` gibi kesin
   kaynak terimleri doğrudan kayıt anahtarlarına bağlanır. Böylece yeni bir
   düzeltmenin terim seçimi küçük bir karar dosyasıyla yapılabilir.
5. **Semantic delta:** Seçili kararlar doğrulanmış kaynak DAT ile eşleştirilir,
   token/biçim kuralları korunur ve yalnız değişen kayıtlar yeni semantic pakete
   yazılır. Gerçek aday DAT ayrıca oluşturulup yeniden okunur. Önbellek
   kayıtları hiçbir zaman DAT girdisi veya son kullanıcıya uygulanacak dosya
   değildir.

## Ne zaman yeniden tarama yapılır?

Önbellek şu çapalarından biri değiştiğinde geçersizdir:

- kaynak DAT SHA-256'sı veya boyutu,
- kaynak katalog SHA-256'sı,
- önbellek veya ikili kayıt biçimi sürümü,
- otomatik korumalı UI düzeltmelerinin içerik kimliği,
- JSONL/ikili dosyaların SHA-256'sı, kayıt sayısı, sırası veya temel katalog hash'i.

Bu değerler eşleşmiyorsa işlem fail-closed davranır ve yeni bir önbellek
oluşturulmasını ister. Eşleşiyorsa kaynak DAT'ı açmadan temel katalog kimliği
ve terim dizini kullanılabilir. Bu doğrulama değişen adayın hash'ini kanıtlamaz.

`KnownUiFixes.ContentRevisionSha256` kesin anahtar/kaynak/hedef tablosundan ve
karakter yuvası metinlerinden hesaplanır; yeni bir düzeltme eklendiğinde eski
önbellek otomatik olarak reddedilir. Düzeltme algoritmasının anlamı değişirse
bu özetteki alan sürümü de artırılır. UI kimliği taşımayan v1 önbellekler sessizce
yükseltilmez; güncel araçla yeniden oluşturulur.

Kaynak çapalı önbellek doğrulaması için `CatalogCache.OpenVerified(recordsPath, metadataPath,
expectedSourceDatSha256, expectedSourceDatSize, expectedSourceCatalogSha256)`
kullanılır. Beklenen kaynak değerleri önceden doğrulanmış kaynak çapası veya
paket sözleşmesinden gelmelidir. Açılışta sıkıştırılmış dosyalar hash'lenir ve
aynı açık dosya üzerinden kayıtlar okunur; kaynak kimliği, biçim, kayıt sayısı,
benzersiz anahtar/sıra ve hesaplanan temel katalog hash'i doğrulanır. Sonuç
doğrulanmış temel kimliği taşır. `ComputeCandidateHash(null)` veya boş sözlük
bu temel hash'i dosyaları yeniden açmadan döndürür. **Boş olmayan bütün karar
kümeleri reddedilir:** değişmeyen metin gibi görünen kararlar da buna dahildir.

Eski iki parametreli `CatalogCache.ComputeCandidateHash(recordsPath, decisions)`
yerel araç uyumluluğu için korunur. Bu yol her çağrıda dosyaları doğrular;
ayrı bir beklenen kaynak çapası alamadığı için güvenilir yerel metadata
gerektirir. Bu giriş de boş olmayan kararları reddeder.

## Kayıt önbelleğinin sınırı

Düz tabloyu tarayan fallback ayrıştırıcı, bazı kayıtların kimliğini komşu
metnin UTF-16 baytları üzerinden oluşturabilir. Bir satırın çevrilmesi, metni
aynı kalan başka bir satırın kayıt parmak izini değiştirebilir. Yalnız kaynak
ve bağlam hash'lerini güncelleyip kayıt/yapı parmak izlerini sabit tutan eski
önbellek hesabı bu durumda yanlış aday hash'i üretir. Önbellekteki kaynak metin
de normalize edildiği için görünürde eşit bir hedef bile aynı ham baytları
garanti etmez.

V2 önbellek bu bayt bağımlılıklarını veya özgün DID payload'larını içermez.
Bu nedenle değiştirilmiş bir katalog hash'i tahmin etmez. Desteklenen yol,
`ManagedSemanticDatPatcher` ile gerçek adayı oluşturmak; tüm katalog, hedef
metinler, DAT yapısı ve dosya kimliğini doğrulamak; yayın kimliklerini bu sonuçtan
almaktır. Gelecekte yalnız değişen DID'lerin payload'ını yeniden oluşturup
ayrıştıran bir hızlandırma ayrıca doğrulanabilir; mevcut kayıt önbelleği bunun
yerine geçmez.

## Bakımcı akışı

### Saniyeden kısa hedefli ön kontrol

Tek bir ekran görüntüsündeki hatayı kontrol etmek için her seferinde tam
katalog çıkarılmaz. `scripts/test-reviewed-decisions.ps1 -SourceDat <temiz.dat>
-Decisions <kararlar>` derlenmiş üreticiyi kullanarak yalnız kararlardaki
tablolara bakar; oyun dosyasına yazmaz. Örnek kaynak-özetli kararlar
`developer-tool/automation/reviewed/screenshot-2026-09-08.decisions` dosyasındadır.
Dosya satır başına JSON biçimindedir ve tam üreticinin mevcut
`manual-decisions` argümanıyla kullanılabilir. Kaynak/token özeti olmayan eski
elle kararlar reddedilir; kaynağı tekrar incelemeden yeni özetle damgalanmaz.

Bu ön kontrol yayın izni değildir. Düzeltme onaylanınca gerçek özel aday
oluşturulur ve tam denetim yapılır. Incremental üretici için kararlar önceki
Türkçe DAT'ın metnine bağlanmalıdır; temiz İngilizce kaynağa bağlı bu örnek
dosya doğrudan incremental girdi olarak kullanılamaz.

1. Doğrulanmış temiz DAT'ı yerel kaynak olarak seç.
2. İlk sürümde katalog kayıt önbelleğini ve ikili yan dosyayı oluştur; metadata
   içindeki kaynak çapalarını sakla; ham kaynak metinleri yerel çalışma
   dizininde tut.
3. Çeviri kararlarını kesin anahtarlarla seç; semantic üreticisiyle doğrulanmış
   kaynak veya predecessor DAT üzerinden paketi ve gerçek adayı oluştur.
4. Gerçek adayın tamamını yeniden doğrula; katalog hash'ini, DAT SHA-256'sını
   ve boyutunu bu sonuçtan al. Eski bir önbellek tahminini yayın kimliği olarak
   kullanma.
5. Bilinen korumalı UI satırları `KnownUiFixes` ile fail-closed uygulanır; kayıt
   sayısı veya token imzası değişirse paket reddedilir.
6. Release'e doğrulanmış semantic paket, manifest ve kurulum EXE'si yüklenir.
   DAT ve kaynak metin içeren katalog önbellekleri release'e konmaz.

Önbellek oluşturma kaynak DAT hash'i ve katalog taraması gerektirir. Terim
aramaları önbelleği kullanır; değişen adayların gerçek DAT doğrulaması devam
eder. Kaynak çapası, önbellek biçimi veya otomatik UI içeriği değiştiğinde
önbellek yeniden oluşturulur. Bozuk
bir ikili yan dosya, doğrulanmış güncel JSONL'den `MaterializeFastRecords` ile
yerel olarak yeniden üretilebilir.
