# LOTR TÜRKÇE YAMA — kısa güncelleme sırası

## Oyun güncellemesi geldiğinde

1. LOTRO'yu resmi launcher ile güncelleyin ve oyunu/launcher'ı kapatın.
2. Temiz `client_local_English.dat` dosyasını proje kökündeki
   `GÜNCELLEME` klasörüne koyun ve önceki dosyanın yerine geçir:

   ```text
   lotro-turkiye-yama-calismasi/
   └─ GÜNCELLEME/
      ├─ client_local_English.dat            (tek temiz İngilizce DAT)
      └─ catalog.jsonl.gz                    (otomatik yerel durum; GitHub'a gitmez)
   ```

   Dosya Türkçe yama yapılmamış olmalıdır.
3. `LOTRKaynakGonder.exe` programında bu tek DAT'ı seçip **GitHub'a Gönder**
   düğmesine basın. Çevrilmiş DAT veya tam DAT yüklemeyin.
4. GitHub Actions yeni/değişen satırları ücretsiz Argos ile çevirir, kontrol eder
   ve inceleme PR'ı açar. Onaydan sonra kaynaklar, DAT/semantic paket ve gerekiyorsa
   EXE güncellenir; testler geçince GitHub Release yayımlanır.

İlk gönderimde program seçilen DAT'ın katalog özetini proje kökündeki
`GÜNCELLEME/catalog.jsonl.gz` dosyasında saklar. Bu dosya `.gitignore` ile yerel
kalır ve GitHub'a gönderilmez. Bir sonraki oyun güncellemesinde
yalnızca `GÜNCELLEME/client_local_English.dat` dosyasını yenisiyle değiştirin;
durum dosyası eski karşılaştırma noktası olarak kalır. İlk çalıştırma yalnızca
temel durum oluşturur; sonraki güncelleme yeni/değişen satırlar için patch üretir.
Durum dosyası kaybolursa program yeni DAT'ı yeniden temel kabul eder.

## Temizlik kuralı

- Güncel `GÜNCELLEME`, kaynak kodu, `*.cs`, `*.csproj`, script,
  belge, manifest ve release dosyaları korunur.
- Yalnızca kesin geçici olduğu doğrulanan kopya, log, önbellek ve derleme
  çıktıları Geri Dönüşüm Kutusu'na taşınır. Emin olunmayan dosya silinmez.
- C çalışma klasöründen yalnız LOTRO projesi için gerekli dosyalar projeye alınır;
  diğerleri doğrulanmadan çöpe gönderilmez.
