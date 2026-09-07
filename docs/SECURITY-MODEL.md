# Updater güvenlik modeli

- Endpoint sabittir: `Relactive55/lotro-turkiye-yama-calismasi` ve HTTPS.
- `raw main`, kullanıcı URL'si, config/CLI endpoint override ve HTTP downgrade yoktur; redirect hedefi yeniden doğrulanır.
- Yalnız stable (`draft=false`, `prerelease=false`) release kabul edilir.
- Manifest release ID/tag ve beklenen asset ID/name ile bağlanır.
- İndirme streaming `.part` dosyasına yapılır; Content-Length, boyut ve SHA-256 uyuşmazsa hedef DAT'a dokunulmaz.
- Güvenli dosya adı, disk alanı, iptal ve ilerleme kontrolleri uygulanır.
- LOTRO root, DAT varlığı, launcher/client process durumu ve backup hash'i kurulumdan önce kontrol edilir.
- Replacement sonrası hash veya state yazımı başarısızsa backup rollback uygulanır.
- Network hatası mevcut kurulumu değiştirmez.
- Semantic patch entry'si source digest veya token signature eşleşmiyorsa satır
  atlanır; mevcut English metin korunur. `AMBIGUOUS`, değişmiş critical UI,
  quality/token hatası ve binary writer doğrulama hatası otomatik release'i
  bloke eder veya satırı English fallback'e düşürür.
- Client-assisted source bundle opt-in'dir; kullanıcı dosyası kendiliğinden
  internete yüklenmez ve bundle tam DAT/raw catalog içermez.
- Translation pipeline `noop`/OPUS/Argos provider sözleşmesine sahiptir; paid
  API, model binary'si veya özel VPS zorunluluğu yoktur. Model revision/size/
  SHA-256 doğrulanmadan otomatik release'e bağlanmaz.
