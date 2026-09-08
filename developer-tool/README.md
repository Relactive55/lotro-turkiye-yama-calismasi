# Geliştirici çeviri/build aracı

Bu dizin mevcut `LOTRÇEVİRİ.exe` aracının temiz kaynak kopyasıdır. Resmî launcher ile güncellenmiş temiz İngilizce DAT'ı read-only native reader ile kataloglar; `CatalogIdentity`, `CatalogDiff` ve `ProtectedFormat` çekirdekleriyle güvenli eşleştirme/doğrulama sağlar; onaylı çeviri, TM, glossary/protected names ve gerektiğinde yerel MT akışını kullanarak güncel İngilizce DAT üzerinde Türkçe aday üretir.

`DID:RecordIndex:GroupIndex:IndexInGroup` yalnızca konum anahtarıdır. Cross-version kimliği structural/record/source/context fingerprint'leriyle kurulur; position sadece yardımcı sinyaldir. `AMBIGUOUS` eşleşmeler otomatik taşınmaz.

Native `datexport.dll`, LOTRO DAT'ları, TM/catalog verileri ve model runtime'ları
bu çeviri çalışma kopyasına dahil edilmez. Native dosya yalnız
lisans/redistribution onayı bulunan yerel build ortamında sağlanmalıdır.
