# ORJİNAL DAT

Bu klasörde yalnızca resmi launcher'dan alınmış temiz İngilizce
`client_local_English.dat` tutulur. Türkçe yama uygulanmış DAT kullanılmaz.

Oyun güncellemesi geldiğinde yeni temiz DAT'ı proje kökündeki `GÜNCELLEME`
klasörüne koyun; buradaki ilk temiz temel DAT'ı silmeyin. İlk gönderimde
`LOTRKaynakGonder.exe` ile iki temiz dosyayı seçip **GitHub'a Gönder** düğmesine
basın. Program sonraki karşılaştırmalar için son gönderilen kataloğu yerel
durum dosyasında saklar; sonraki güncellemelerde yalnızca GÜNCELLEME dosyasını
yenisiyle değiştirmeniz yeterlidir.

Dosya adları aynı olabilir; klasörler farklıdır:

```text
ORJİNAL DAT/client_local_English.dat   (eski temiz İngilizce temel)
GÜNCELLEME/client_local_English.dat    (yeni temiz İngilizce güncelleme)
```

İki dosyanın da temiz olması gerekir: araç eski ve yeni katalogları
karşılaştırarak yalnızca yeni/değişen satırları çıkarır.

Çevrilmiş DAT, tam DAT veya kişisel dosya bu klasöre konulmaz ve GitHub'a
yüklenmez. Ayrıntılı sıra için [`docs/GUNCELLEME-REHBERI.md`](../docs/GUNCELLEME-REHBERI.md)
dosyasına bakın.
