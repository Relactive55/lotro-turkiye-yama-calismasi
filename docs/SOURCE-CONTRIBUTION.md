# Source update katkısı

Current status: source-bundle generation and the read-only Windows sender are
implemented. Source bundles and translation context are retained only in the
private source repository; candidate changes still pass validation and review
before a release.

Yeni source bundle göndermek isteyen oyuncu için hedef akış:

1. Resmi LOTRO launcher ile güncelle.
2. `LOTRKaynakGonder.exe` ile yalnız yeni temiz
   `client_local_English.dat` dosyasını seç.
3. Program önceki katalogu yerel durum dosyasından alır; ilk çalıştırmada
   yalnız temel durumu kaydeder, sonraki çalıştırmalarda read-only olarak sadece
   `NEW`/`MODIFIED` satırlardan küçük bundle üretip GitHub CLI oturumuyla özel
   kaynak deposuna gönderir.
4. Private Actions bundle'ı doğrular, ücretsiz yerel Argos çevirisini üretir ve
   İngilizce içermeyen adayları public release deposunda inceleme PR'ı olarak açar.
5. Kaynak DAT, ham katalog ve token hiçbir aşamada herkese açık depoya veya
   release asset'ine gönderilmez.

Actions bundle'ı kör kabul etmez: source DAT SHA-256, catalog fingerprint,
duplicate identity, classification ve token metadata kontrol edilir. Tek bir
bundle production release'i doğrudan yazamaz; doğrulanmış katkı önce candidate
pipeline'a girer.

Bundle kişisel dosya, tam DAT, RAR, model veya log içermemelidir. Ham source
metni yalnız private Actions çalışma alanında kullanılır; çeviri PR'ı yalnız
doğrulama sonucu ve İngilizce içermeyen aday alanlarını taşır.
