# Uygulama durumu — 2026-09-06

## 2026-09-15 denetim düzeltmeleri

- Production adayları artık yalnız insan onaylı/TM/glossary statülerinden oluşabilir; `MACHINE_TRANSLATED` fail-closed reddedilir.
- Updater `.part` indirmeleri korur ve HTTP Range ile devam eder; full-DAT akışında rollback yedekleri en fazla iki dosya tutulur.
- State okuma ve ProcessGuard erişim hataları sessizce yok sayılmaz; manifestte `minimum_updater_version` zorunludur.
- Yeni semantic yayınlar schema v2 detached RSA-SHA256 imzası ister; tam DAT yayın betiği hukukî onay anahtarı olmadan engellenir.

## Doğrulanmış kaynak

- resmî İngilizce DAT: `1,894,213,416` bayt
- DAT SHA-256: `48deba5c621bb72492afd2687acffc49a0289054255c2136b7cb469ec1bdd967`
- katalog SHA-256: `bf5984ecb19add7c93911c73dba88ecdc1de221330027671c125da9212184922`
- yerelleştirme kaydı: `825,136`
- parse hatası: `0`

## Semantic yayın modeli

Geliştirici tarafında semantic doğrulama ve aday üretimi korunur; son kullanıcı
release'i imzalı `semantic_delta_patch` asset'i taşır. Updater manifest imzasını,
temel DAT kimliğini ve paket hash'ini doğrular; ardından değişiklikleri yerel
resmî DAT'a uygular. Geçmiş full-DAT manifestleri yalnız geçiş uyumluluğu için
okunabilir; yeni üretim betiği hukukî bayrak olmadan bunları yayımlamaz.

- güvenli Türkçe kayıt: `645,004`
- dokunulan DID/blok: `231,463`
- elle çevrilen kritik/genel düzeltme: `302`
- açık koruma kararı: `1,693`
- kritik inceleme: `0`
- genel inceleme/reddedilen aday: `0`

Koruma kararları; çevirisi oyunun komut veya dilbilgisi mekanizmasını bozacak anahtarlar, değişkenler, tek harflik cümle parçaları, biçim parçaları ve özel adlar içindir. Bunlar eksik çeviri olarak değil, kaynak biçimini koruma kararı olarak kayıtlıdır.

## Geçilen kapılar

| Kapı | Durum |
|---|---|
| Kaynak DAT ve katalog kimliği | PASS |
| Değişmiş kaynakta eski çeviriyi reddetme | PASS |
| Token, sayı, satır sonu ve işaretleme koruması | PASS |
| Kritik kayıtların yalnız insan onayıyla alınması | PASS |
| Kritik inceleme sayısının sıfır olması | PASS |
| Gerçek 1,9 GB DAT'a tam uygulama | PASS |
| Oluşan DAT'ın baştan sona yeniden okunması | PASS |
| Yanlış temel ve bozuk asset reddi | PASS |
| Yedekleme, atomik değiştirme ve geri alma | PASS |
| Steam ve bağımsız kurulum bulma | PASS |
| Public depo hijyen denetimi | PASS |

İlk tam uygulama kanıtı `645,004` kayıt, `231,463` DID, `825,136` yeniden okunan kayıt ve sıfır hata verdi. Nihai release paketi aynı tam round-trip kapısından ayrıca geçirilir.

## Testler

- updater davranış testi: `10`
- geliştirici güvenlik/diff/round-trip fixture testi: `29`
- updater manifest/yol sözleşmesi: `10`
- gerçek DAT tam uygulama ve yeniden okuma: `PASS`

## Otomasyon

- resmî SSG `Game.Version` değeri GitHub Actions ile günde bir kez izlenir;
- Steam ve bağımsız kurulum aynı resmî güncelleme sinyalini kullanır;
- gerçek DAT değişikliği güvenilir Windows kaynak makinesinde SHA-256 ve katalog özetiyle doğrulanır;
- yalnız yeni/değişen kaynaklar çeviri hattına girer;
- kritik inceleme sıfır değilse release manifesti updater tarafından reddedilir;
- kullanıcı aynı EXE ile yeni stable patch sürümünü görür ve uygular.

GitHub-hosted bir makine oyunun güncel proprietary DAT dosyasına kendiliğinden sahip değildir. Bu nedenle gerçek kaynak yenileme aşaması etiketli, güvenilir bir Windows runner gerektirir; yalnız resmî sürüm sinyali GitHub-hosted runner üzerinde çalışır. Bu sınır güvenlik nedeniyle gizlenmez veya atlanmaz.

## Public dağıtım sınırı

Kaynak Git deposuna tam DAT, RAR, ham İngilizce katalog, özel çeviri havuzu,
model ağırlığı veya oyuna ait native DLL konmaz. Yeni public release'lerde
yalnız imzalı semantic asset, setup ve manifest yayımlanır; full DAT legacy
varlığı hukukî onay olmadan üretilmez.
