# LOTR TÜRKÇE YAMA — kısa güncelleme sırası

## Oyun güncellemesi geldiğinde

1. LOTRO'yu resmi launcher ile güncelleyin ve oyunu/launcher'ı kapatın.
2. Temiz `client_local_English.dat` dosyasını proje kökündeki
   `GÜNCELLEME` klasörüne koyun. Önceki temiz dosya `ORJİNAL DAT` içinde kalsın.
   Aynı dosya adı sorun değildir; klasör yolları farklıdır:

   ```text
   lotro-turkiye-yama-calismasi/
   ├─ ORJİNAL DAT/client_local_English.dat   (eski temiz İngilizce temel)
   └─ GÜNCELLEME/client_local_English.dat    (yeni temiz İngilizce güncelleme)
   ```

   İki dosya da Türkçe yama yapılmamış olmalıdır; aynı dosya adını farklı
   klasörlerde kullanmak normaldir. Bu iki dosyayı aynı klasöre koymayın.
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
