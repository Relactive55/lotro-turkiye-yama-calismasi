# LotroKoniecDev inceleme sonucu

İncelenen referans: <https://github.com/koniecdev/LotroKoniecDev>

Bu çalışma kopyasına referans projenin kodu veya `datexport.dll` binary'si
kopyalanmadı. Yalnızca doğrulanabilir fikirler, kendi DAT kimlik modelimize
uyarlanarak uygulanıyor.

## Alınan fikirler

- Patcher ile TMS arasında sürümlü bir çeviri dosyası sözleşmesi.
- Her satırda İngilizce kaynağı bağlayan `source_digest`.
- Yazım anında kaynak kontrolü: kaynak değişmişse eski Türkçe yazılmaz,
  satır İngilizce bırakılır.
- Dinamik argümanların maskeleme/geri yükleme ve sıralama bilgisi.
- Oyun güncellemesi sonrası değişen satırları `NeedsReview`/review-required
  olarak dışarıda bırakma.
- Export, patch, backup, oyun çalışıyor kontrolü ve açık hata kodları.

## Bilinçli olarak alınmayanlar

- Referans projenin TMS, PostgreSQL, OAuth, VPS veya özel sunucu mimarisi.
- `datexport.dll` ve diğer native binary'ler; yeniden dağıtım izni bu depoda
  doğrulanmış değildir.
- Referansın yedi alanlı `file_id||gossip_id||...` sözleşmesi birebir
  kopyalanmadı. Bizim DAT extractor kimliklerimiz DID + record/group/index,
  structural/context fingerprint ve `entry_identity` alanlarını birlikte
  kullanıyor.

## Bizim eşleme

| Referans kavramı | Türkçe proje karşılığı |
|---|---|
| `file_id` | `CatalogRecord.Did` |
| `gossip_id` | DAT satırının `dat_key`/koordinat bilgisi |
| `translated_text` | semantic patch `target` |
| `args_order`, `args_id` | protected token signature + DAT satır koordinatları |
| `approved` | `translation_status` (`HUMAN_APPROVED`, `TM_REUSED`, `GLOSSARY`, `MACHINE_TRANSLATED`) |
| `source_digest` | source + token/numeric signature + record/structural fingerprint SHA-256 |

`ContextFingerprint` yalnız belirsiz eşleşmeleri çözmek için yardımcı sinyaldir;
source digest ve stable identity komşu satır değiştiğinde gereksiz yere
değişmez. `AMBIGUOUS` eşleşme hiçbir zaman otomatik carry-over alamaz.

## Sonuç

`LOTROKONIECDEV_REFERENCE_ANALYZED = YES`.

Referans proje kendi README'sinde oyun sürümlerinin şu an elle kaydedildiğini
ve otomatik forum izleyicisinin yol haritasında olduğunu bildiriyor. Bu nedenle
cloud üzerinden yeni resmi DAT/source edinimi bu çalışma için kanıtlanmış bir
özellik olarak işaretlenmedi; istemci destekli source bundle fallback'i esas
alınan tasarımdır.
