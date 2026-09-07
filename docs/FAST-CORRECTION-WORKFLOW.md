# Hızlı düzeltme akışı

Tek tek arayüz düzeltmelerinde 1,9 GB'lık resmî DAT'ı tekrar tekrar okumak
gerekmemeli. Hızlı akış, kaynak DAT'ı yalnızca doğrulanmış bir kaynak özeti
oluşturmak için bir kez okur; sonraki aday üretimleri GitHub'da saklanan katalog
önbelleğinden çalışır.

## Katmanlar

1. **Kaynak çapası:** `client_local_English.dat` yalnızca okunur. Kaynak DAT
   SHA-256'sı, boyutu, kaynak katalog özeti ve kayıt sayısı birlikte saklanır.
2. **Kayıt önbelleği:** Her kayıt için kararlı anahtar, konum, DID, kaynak metin,
   kayıt/yapı/kaynak/bağlam parmak izleri ve token imzası tutulur. JSONL gzip
   dosyası denetlenebilir arşivdir.
3. **Hızlı yan dosya:** Aynı kayıtların `LTCCACHE1` başlıklı ikili gzip yan dosyası
   vardır. Hash hesabında JSON ayrıştırma yerine sıralı ikili okuma kullanılır.
4. **Tam eşleşme dizini:** `Ranged`, `Damage`, `Duration`, `Cost`, `Cooldown`,
   `Focus`, `Main-hand`, `Off-hand`, `Fast`, `Induction`, `Melee` gibi kesin
   kaynak terimleri doğrudan kayıt anahtarlarına bağlanır. Böylece yeni bir
   düzeltme için tam tarama değil, küçük bir karar dosyası yeterlidir.
5. **Semantic delta:** Seçili kararlar önbellekten okunur, token/biçim kuralları
   korunur ve yalnız değişen kayıtlar yeni semantic pakete yazılır. Önbellek
   kayıtları hiçbir zaman DAT girdisi veya son kullanıcıya uygulanacak dosya
   değildir.

## Ne zaman yeniden tarama yapılır?

Önbellek ancak şu çapalarından biri değiştiğinde geçersizdir:

- kaynak DAT SHA-256'sı veya boyutu,
- kaynak katalog SHA-256'sı,
- önbellek şema sürümü,
- otomatik korumalı UI düzeltmelerinin sürümü.

Bu değerler eşleşmiyorsa işlem fail-closed davranır ve yeni bir önbellek
oluşturulmasını ister. Eşleşiyorsa aynı kaynak DAT'ı açmadan hash, terim kararı
ve semantic paket üretimi yapılabilir.

## Bakımcı akışı

1. Doğrulanmış temiz DAT'ı yerel kaynak olarak seç.
2. İlk sürümde katalog kayıt önbelleğini ve ikili yan dosyayı oluştur; metadata
   içindeki kaynak çapalarını sakla.
3. Çeviri kararlarını kesin anahtarlarla üret ve semantic delta'yı önbellekten
   oluştur.
4. Aday katalog hash'ini yine önbellekten hesapla; paket başlığındaki hash ile
   karşılaştır.
5. Bilinen korumalı UI satırları `KnownUiFixes` ile fail-closed uygulanır; kayıt
   sayısı veya token imzası değişirse paket reddedilir.
6. Release'e semantic paket, kurulum EXE'si ve önbellek metadata/kayıt varlıkları
   yüklenir. Büyük DAT dosyası hiçbir zaman release'e konmaz.

İlk önbellek oluşturma, kaynak DAT için tek uzun okumadır. Sonraki küçük terim
düzeltmeleri saniyeler içinde tamamlanır; tam DAT taraması yalnızca resmî oyun
 güncellemesi gerçekten yeni bir kaynak çapası ürettiğinde yapılır.
