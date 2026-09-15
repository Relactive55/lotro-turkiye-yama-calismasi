# Teknik denetim düzeltme durumu — 2026-09-15

Bu belge, teknik denetim raporundaki uygulanabilir maddelerin bu revizyondaki
karşılığını ve dışarıdan sağlanması gereken bağımlılıkları ayırır.

## Tamamlanan düzeltmeler

- Üretim adaylarında `MACHINE_TRANSLATED` fail-closed reddedilir; yalnız insan
  onayı, TM veya glossary statüleri yayınlanabilir.
- Updater indirmeleri sonsuz ağ zaman aşımıyla `.part` dosyasını korur ve HTTP
  Range ile devam eder. Yarım paket kimliği yanındaki metadata ile doğrulanır.
- Rollback yedekleri en fazla iki dosyaya budanır; state dosyası kaynak yedeği
  ve SHA-256'sını kaydeder. Geri alma hash'i yedekten değil, işlem öncesi canlı
  dosyadan alınır.
- State ve süreç erişim hataları artık sessizce yok sayılmaz; manifestte
  `minimum_updater_version` zorunludur ve desteklenmeyen sürüm fail-closed durur.
- Public yayın varsayılanı imzalı semantic patch'tir. Schema v2 manifesti,
  detached RSA-SHA256 `manifest.sig` olmadan kabul edilmez.
- Yayın betiği tam proprietary DAT'ı varsayılan olarak engeller; yalnız açık
  hukukî onay için `-AllowFullDatDistribution` ile legacy yol açılır.
- Release QA özeti için `developer-tool/automation/validate_release_qa.py`
  eklendi ve GitHub iş akışına fail-closed kapı bağlandı.
- Windows Authenticode için `scripts/sign-updater.ps1` eklendi.
- README, updater, mimari ve release belgeleri semantic yayın modeline göre
  güncellendi.

## Dış bağımlılık / kullanıcı kararı gereken maddeler

- Schema v2 için RSA özel anahtarı güvenli/offline ortamda üretilip yayın
  ortamına gizli değişken veya dosya olarak sağlanmalıdır. Özel anahtar depoya
  konmaz; mevcut gömülü public anahtar, bu anahtar sağlandığında eşleştirilmelidir.
- Authenticode imzası için güvenilir bir Windows kod imzalama sertifikası ve
  zaman damgası erişimi gerekir.
- LOTRO'nun proprietary DAT'ını public dağıtma izni hukukî olarak doğrulanmadan
  yeni full-DAT release yayımlanmaz. Tarihî release'ler otomatik silinmedi;
  silme/değiştirme ayrı ve açık bir yayın kararı gerektirir.
- Resmî launcher sonrası temiz DAT edinimi hâlâ yetkili Windows kaynak makinesi
  ve erişim gerektirir; otomasyon bu kaynağı kendiliğinden icat etmez.
- Font dosyalarındaki eksik Türkçe gliflerin kalıcı düzeltilmesi, oyunun özel
  font varlıklarına erişim ve lisans/asset değişikliği gerektirir; updater bunu
  güvenli biçimde varsayamaz.

## Doğrulama

Derleme ve davranış testleri `scripts/build.ps1` ve `scripts/test-updater.ps1`
ile; yayın sözleşmesi `tests/run-updater-contracts.ps1` ile çalıştırılmalıdır.
Yeni semantic release, imza anahtarı ve Authenticode sertifikası sağlanmadan
`-Publish` ile başlatılmamalıdır.
