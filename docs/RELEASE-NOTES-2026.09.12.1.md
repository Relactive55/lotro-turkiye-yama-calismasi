# LOTR TÜRKÇE YAMA 2026.09.12.1

Bu sürüm dağıtım modelini eksiksiz Türkçe DAT'a geçirir.

- Güncelleme paketi `full_dat` türündedir; semantic/delta yama asset'i
  yayımlanmaz.
- Program tam DAT'ı geçici dosyada indirir, boyut ve SHA-256 doğrulamasından
  sonra `client_local_English.dat` dosyasını yedekleyip atomik olarak değiştirir.
- Mevcut DAT'ın eski Türkçe yama, launcher tarafından yenilenmiş oyun dosyası
  veya tanınmayan bir sürüm olması kurulumu engellemez.
- İptal ve hata durumunda canlı DAT ile `installed_patch.json` korunur veya
  yedekten geri yüklenir.

Bu release'in tam DAT asset'i yalnız GitHub Releases üzerinden indirilir;
kaynak Git deposuna eklenmez.
