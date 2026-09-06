# LOTRO Türkiye Yama Çalışması

The Lord of the Rings Online için ücretsiz Türkçe yerelleştirme ve güvenli güncelleme projesidir. Son kullanıcı tam oyun DAT'ı indirmez; GitHub Releases sayfasındaki `LOTRO_Turkce_Yama_Setup.exe` aracını indirir ve **Yama Yap** düğmesine basar. Araç Steam ve bağımsız LOTRO kurulumlarını bulabilir, güncel semantic paketi indirir ve kullanıcının kendi resmî DAT dosyasına uygular.

## Son kullanıcı akışı

1. GitHub Releases sayfasından kurulum aracını indirin.
2. LOTRO ve launcher kapalıyken aracı çalıştırın.
3. **Yama Yap** düğmesine basın.
4. Araç kaynak DAT ve katalog kimliğini, paket boyutunu ve SHA-256 özetini doğrular; temiz kaynak yedeği almadan canlı dosyayı değiştirmez.
5. Yeni bir Türkçe paket yayımlandığında aynı kurulum aracı bunu otomatik gösterir.

Kod imzalama sertifikası eklenmediği sürece Windows SmartScreen uyarısı görülebilir.

## Doğrulanmış temel ve çeviri paketi

- resmî İngilizce DAT boyutu: `1,894,213,416` bayt
- kaynak DAT SHA-256: `48deba5c621bb72492afd2687acffc49a0289054255c2136b7cb469ec1bdd967`
- kaynak katalog SHA-256: `bf5984ecb19add7c93911c73dba88ecdc1de221330027671c125da9212184922`
- katalog kaydı: `825,136`
- güvenle uygulanan Türkçe kayıt: `645,004`
- dokunulan yerelleştirme bloğu: `231,463`
- kritik inceleme: `0`
- genel inceleme/reddedilen aday: `0`

Komut anahtarları, değişkenler, biçim parçaları ve özel adlar çeviri sayılmaz; bozulmamaları için açık koruma kararlarıyla kaynak biçiminde tutulur. Semantic paketin güncel 1,9 GB resmî DAT kopyasına tam uygulanması ve oluşan `825,136` kaydın yeniden okunması başarıyla sınanmıştır.

Tam DAT, RAR, ham katalog, özel çeviri havuzu, model dosyası veya oyuna ait native DLL bu depoda tutulmaz.

## Otomatik güncelleme

`Watch official LOTRO version` işi Standing Stone Games'in resmî `Game.Version` değerini altı saatte bir denetler ve yeni sürüm için tek takip kaydı açar. Steam zorunlu değildir. Sürüm sinyali tek başına yama yayımlamaz; güvenilir Windows kaynak makinesinde güncel DAT özeti değişimi doğrulanır, yalnız yeni/değişen İngilizce kayıtlar çeviri hattına alınır ve bütün kalite kapıları geçerse yeni release hazırlanır.

Bir kullanıcı oyun içinde sorun bildirdiğinde ilgili kayıt düzeltilerek yeni semantic paket yayımlanabilir. Kurulum aracının kodu değişmediyse kullanıcı yeni EXE indirmek zorunda kalmaz; mevcut araç yeni patch sürümünü görür.

## Depo yapısı

- `updater/`: son kullanıcı kurulum ve güncelleme aracı
- `developer-tool/`: katalog, diff, çeviri ve paket üretim kaynağı
- `schemas/`: manifest, semantic patch ve kurulum durumu sözleşmeleri
- `scripts/`: build, doğrulama ve resmî sürüm algılama araçları
- `tests/`: gerçek oyun dosyası içermeyen güvenlik ve davranış testleri
- `docs/`: mimari, otomasyon, güvenlik ve release belgeleri

Yerel build için `scripts/build.ps1 -Project All -DotnetPath <dotnet.exe>`, test için `scripts/test-updater.ps1 -DotnetPath <dotnet.exe>` kullanılabilir.
