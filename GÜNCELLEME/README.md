# GÜNCELLEME — tek DAT

Bu klasörde yalnızca tek temiz İngilizce `client_local_English.dat` bulunur.

Oyun güncellemesi geldiğinde yeni temiz DAT'ı aynı dosyanın üzerine koyun.
`LOTRKaynakGonder.exe` ile dosyayı seçip **GitHub'a Gönder** düğmesine basın.
Önceki katalog bu klasördeki `catalog.jsonl.gz` durum dosyasından otomatik alınır;
dosya `.gitignore` ile yerel kalır ve GitHub'a gönderilmez. İkinci DAT seçmeniz
gerekmez.

Çevrilmiş DAT veya tam DAT yüklemeyin. İlk çalıştırmada yalnız temel durum
kaydedilir; sonraki güncellemelerde yeni/değişen satırlar için patch hazırlanır.
