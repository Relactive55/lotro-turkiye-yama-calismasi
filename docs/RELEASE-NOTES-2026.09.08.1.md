# LOTRO Türkçe Yama 2026.09.08.1

Doğrulanmış yeni ana paket. Resmî kaynak sürümü: `3601.0066.7272.4024`.

- Önceki v9 manifestindeki yanlış aday katalog bilgisi yeni, tam doğrulanmış
  çıktıdan ölçülen bilgilerle değiştirildi. Aday DAT boyutu ve SHA-256 artık
  manifestte bulunur; küçük düzeltme paketleri için güvenilir temel sağlanır.
- DAT indeksleme ve yazma hızlandırıldı; disk hatası, iptal, yedek/geri alma ve
  kaynak dosya kimliği kontrolleri güçlendirildi. Uzun işler arayüz dışında
  çalışır ve aşama ilerlemesi gösterilir.
- Eksik kaynak kanıtı, yanlış koordinat veya aynı metne dayanarak başka satıra
  yönlendirme reddedilir.
- Kuyutorman köken eki ve ırk açıklamasındaki iki bağlam hatası düzeltildi.

Yeni kurulum aracı `1.1.0.0` gereklidir. Bu bir yeni kök olduğundan paketin
tamamı bir defa indirilir; sonraki doğrulanmış küçük düzeltmeler incremental
olarak dağıtılabilir. Sadece çeviri paketi, kurulum aracı ve manifest dağıtılır;
orijinal oyun DAT'ı veya ham katalog dağıtılmaz.

Doğrulama: 186 otomatik kontrol; gerçek kaynakta 643.828 hedef ve 825.136 kayıt
kontrol edildi. Orijinal DAT değiştirilmedi. Oyun içi test tamamlanmadı;
bildirilen tüm `string table error` olaylarının çözüldüğü iddia edilmez.
Her eski/yeni DAT sürümü için koşulsuz uyumluluk yoktur. Resmî sürüm algılama
mevcuttur; kullanıcı bilgisayarından bağımsız otomatik kaynak edinme ve yeni
metinleri çevirerek yayınlama henüz tamamlanmış değildir.
