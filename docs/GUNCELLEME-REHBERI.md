# LOTR TÜRKÇE YAMA — kısa güncelleme sırası

## Oyun güncellemesi geldiğinde

1. LOTRO'yu resmi launcher ile güncelleyin ve oyunu/launcher'ı kapatın.
2. Temiz `client_local_English.dat` dosyasını proje kökündeki
   `GÜNCELLEME` klasörüne koyun. Önceki temiz dosya `ORJİNAL DAT` içinde kalsın.
3. `LOTRKaynakGonder.exe` programında yeni ve önceki temiz DAT'ı seçip
   **GitHub'a Gönder** düğmesine basın. Çevrilmiş DAT veya tam DAT yüklemeyin.
4. GitHub Actions yeni/değişen satırları ücretsiz Argos ile çevirir, kontrol eder
   ve inceleme PR'ı açar. Onaydan sonra kaynaklar, DAT/semantic paket ve gerekiyorsa
   EXE güncellenir; testler geçince GitHub Release yayımlanır.

## Temizlik kuralı

- `ORJİNAL DAT`, güncel `GÜNCELLEME`, kaynak kodu, `*.cs`, `*.csproj`, script,
  belge, manifest ve release dosyaları korunur.
- Yalnızca kesin geçici olduğu doğrulanan kopya, log, önbellek ve derleme
  çıktıları Geri Dönüşüm Kutusu'na taşınır. Emin olunmayan dosya silinmez.
- C çalışma klasöründen yalnız LOTRO projesi için gerekli dosyalar projeye alınır;
  diğerleri doğrulanmadan çöpe gönderilmez.

