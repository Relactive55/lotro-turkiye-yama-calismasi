# Source update katkısı

Current status: source-bundle generation is implemented as a read-only
library/test capability. The user-facing Setup export button, public upload
flow and retention policy are still release gates.

Yeni source bundle göndermek isteyen oyuncu için hedef akış:

1. Resmi LOTRO launcher ile güncelle.
2. Kullanıcıya görünen export akışı açıldığında **Güncelleme Verisini Hazırla**
   seçeneğini açıkça onayla.
3. Read-only export `source-bundle.schema.json` uyumlu küçük dosya hazırlar.
4. `scripts/validate-source-bundle.ps1` yerel doğrulamasından geçir.
5. **GitHub'da Güncelleme Bildir** ile normal issue/PR sayfasını aç.
6. Kullanıcı GitHub token'ını Setup'a vermez; dosyayı kendi hesabıyla web
   akışında ekler.

Actions bundle'ı kör kabul etmez: source DAT SHA-256, catalog fingerprint,
duplicate identity, classification ve token metadata kontrol edilir. Tek bir
bundle production release'i doğrudan yazamaz; doğrulanmış katkı önce candidate
pipeline'a girer.

Bundle kişisel dosya, tam DAT, RAR, model veya log içermemelidir. Ham source
metninin public PR'da görünmesi telif açısından uygun değilse bundle yalnız
digest/identity metadata taşımalı ve çeviri işi güvenli, geçici bir çalışma
alanında yapılmalıdır.
