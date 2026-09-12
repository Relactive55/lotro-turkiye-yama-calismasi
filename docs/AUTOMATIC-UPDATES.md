# Otomatik LOTRO güncelleme algılama

## Neyi otomatik algılıyoruz?

GitHub Actions günde bir kez (09:17 UTC / 12:17 Türkiye saati) Standing Stone Games'in resmi GLS hizmetinden
launcher yapılandırma adresini, ardından aynı resmi sunucudan `Game.Version`
değerini okur. Her yeni sürüm için yalnız bir GitHub issue açılır. Sunucu yanıtı
alınamaz, beklenmeyen bir alan adına yönlenir veya sonuç belirsiz olursa işlem
fail-closed biter ve sahte güncelleme sinyali üretmez.

Steam ve bağımsız kurulum resmi LOTRO launcher ile aynı güncel oyun içeriğine
getirildiği için Steam istemcisi zorunlu değildir. `Game.Version` yine yalnız
"resmi oyun paketi değişmiş olabilir" sinyalidir; hangi DAT'ın veya hangi
İngilizce metnin değiştiğini tek başına kanıtlamaz.

## Güvenli uçtan uca akış

1. `lotro-update-watch.yml` yeni resmi SSG oyun sürümünü kaydeder.
2. Güvenilir Windows kaynak makinesi resmi LOTRO istemcisini günceller.
3. Geliştirici aracı güncel resmi DAT'tan katalog çıkarır ve önceki katalogla
   `source_digest` üzerinden karşılaştırır.
4. Yeni/değişen İngilizce satırlar çeviri adayına girer. Kaynağı değişen eski
   Türkçe satırlar otomatik yayımdan çıkar ve İngilizce fallback olarak kalır.
5. Biçim belirteçleri, token sırası, kritik arayüz, katalog bütünlüğü ve DAT
   round-trip kapıları geçmeden release oluşmaz.
6. İnsan onayından sonra manifest, eksiksiz Türkçe DAT ve gerekiyorsa ayrı setup
   sürümü GitHub Release'e konur. Semantic/delta asset son kullanıcıya
   yayımlanmaz.
7. Kullanıcının setup uygulaması stable release'i kontrol eder; tam DAT boyutu
   ve SHA-256 doğrulandıktan sonra mevcut oyun DAT'ını backup/rollback
   korumasıyla tamamen değiştirir.

## Kaynak ve otomatik çeviri

Resmi oyun sürümü değişikliği yalnız tetikleyicidir. GitHub'ın yeni İngilizce
LOTRO metinlerine erişebilmesi için güncellenmiş resmi oyun kurulumu gerekir.
Kaynak DAT resmi patch hizmetinden geçici runner alanına yalnız işleme amacıyla
alınabiliyorsa GitHub-hosted Windows runner kullanılabilir; bu güvenilir ve
sürdürülebilir biçimde kanıtlanamazsa kaynak çıkarma işi etiketlenmiş bir
self-hosted Windows runner'da yapılır. Çeviri önce mevcut onaylı havuz/TM/sözlük,
sonra GitHub Actions içindeki ücretsiz yerel Argos sağlayıcısıyla hazırlanır.
İstenirse geliştirici tarafından ayrıca Copilot CLI veya OpenAI-uyumlu uç nokta
yapılandırılabilir; bunlar varsayılan değildir. GitHub Models inference API emekliye
ayrıldığı için kullanılmaz. Token ve biçim testini geçmeyen sonuç yayımlanmaz.
GitHub depolarında ham İngilizce katalog, DAT, RAR, native oyun DLL'i, model
veya yerel çalışma çıktısı herkese açık yayımlanmaz.

## Benzer LOTRO yaklaşımı

`koniecdev/LotroKoniecDev` projesi de oyun DAT'ından İngilizce metni çıkarır,
onaylı çeviri dosyasını indirir, `source_digest` ile değişen İngilizceye ait eski
çevirileri geçersiz sayar ve kullanıcının DAT'ına yerinde uygular. Ancak proje
dokümantasyonunda oyun sürümlerinin hâlen yönetici tarafından elle kaydedildiği,
otomatik sürüm algılamanın yol haritasında olduğu belirtilir. Buradaki resmî SSG
`Game.Version` izleyicisi erken uyarı katmanını ekler; gerçek DAT SHA-256 değişimi
güvenilir Windows kaynak makinesinde ayrıca doğrulanmadan sürüm yayımlanmaz.
