# LOTR TÜRKÇE YAMA 2026.09.08.2

DAT yazma düzeltmesi; kurulum aracı **1.3.0.0** gereklidir.

- Modern DAT kayıtlarının 8 baytlık başlığı artık doğru okunup yazılıyor.
  Önceki 4 baytlık okuma, içerik sonundaki dört baytı dışarıda bırakıyordu.
  Fotoğraftaki `250047CF` NPC tablosunda bu baytlar parametre bilgisiydi.
- Modern parçalı kayıtlar kendi ek-blok tablosuyla okunuyor; taşma, kesilme ve
  çakışma reddediliyor. Modern dosyalarda eski zincir yazma yolu engellendi.
- 643.828 çeviri, aynı doğrulanmış temiz DAT'tan çıkarılan eski ve doğru okuma
  sonuçlarıyla tek tek karşılaştırıldı. Anahtar, kaynak özeti, korunan simgeler
  veya kayıt kimliği uyuşmayan çeviriler otomatik taşınmaz.
- Yeni tam kök paket **67.511.953 bayt**. Bu dosya bir defa indirilir;
  sonraki uygun küçük düzeltmeler en fazla 32 katmanlı incremental zincirle
  dağıtılabilir. Kaynak değiştiğinde veya zincir dolduğunda yeni kök gerekir.

Kaynak oyun sürümü: `3601.0066.7272.4024`.
Yeni aday DAT: `1.939.169.648` bayt,
SHA-256 `fb775e755b520bda6100382f6377c906d7b477194a3d5b3be2b02a7635215d02`.
Kaynak DAT değiştirilmedi. 231.578 kaydın fiziksel yazılması bu makinedeki
ölçümde yaklaşık 10 saniye sürdü; kopyalama, katalog ve bütünlük kontrolleri
bu süreye dahil değildir.

Doğrulama: 216 otomatik kontrol; gerçek kaynakta tam oluşturma ve yeniden
okuma; NPC tablosunun başı/sonu için okuyucudan bağımsız fiziksel bayt testi.
Aynı fiziksel test önceki `.1` adayını reddetti, yeni adayı kabul etti.
Önceki `.1` taslağı bu nedenle kullanılmamalı ve yayımlanmamalıdır.
Eski yazıcıyla üretilmiş bir aday, hash bilgileri olsa bile yeni incremental
pakete temel yapılamaz; temiz kaynaktan yeni kök gereklidir.

Oyun içi test henüz yapılmadı. Bütün ekran metinlerinin doğru çevrildiği veya
tüm string-table hatalarının giderildiği iddia edilmez. Her eski/yeni DAT için
koşulsuz uyumluluk yoktur: eşleşen kaynak veya kanıtlanabilen kurtarma gerekir.
Uygun çevirisi olmayan yeni metin kaynak dilinde korunur.

Resmî sürüm izleyicisi çalışır; kullanıcı bilgisayarından bağımsız güncel
oyun kaynağı edinme, yeni çevirileri inceleme ve yayınlama hattı henüz tamamlanmadı.
Bu sürüm o otomasyonu tamamlanmış gibi sunmaz. Son kullanıcı paketine yalnız
çeviri paketi, kurulum aracı ve manifest alınır; özel DAT ve ham katalog alınmaz.
