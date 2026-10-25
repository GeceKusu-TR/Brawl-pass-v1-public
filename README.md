# Brawl pass v1

Bu depo BSL V41 özel sunucu çalışmasının temizlenmiş kaynak paylaşımıdır.

## Güvenlik uyarısı

Üretim hesapları, oyuncu kayıtları, oturum belirteçleri, parola kayıtları,
loglar, Redis/MySQL yedekleri, APK dosyaları ve sunucuya özel kimlik bilgileri
bu paylaşımın dışında bırakılmıştır. Yeni kurulumda kendi veritabanı ve kendi
gizli değerlerinizi kullanın; örnek parolaları üretimde kullanmayın.

## Kurulum özeti

1. .NET 8 SDK kurun.
2. Kalıcı oyuncu verisini `SaveBase` dışında, erişimi sınırlı bir dizinde
   oluşturun; mevcut hesap dosyalarını depoya göndermeyin.
3. MySQL ve Redis'i localhost'a bağlayın. Redis'i internete açmayın.
4. Admin bind adresi ve yönetici sırrını ortam değişkenlerinden sağlayın.
5. `dotnet build BSL.v41.sln -c Release` çalıştırın.
6. Release DLL'ini systemd veya PM2 benzeri bir process manager ile başlatın.
7. İlk çalıştırmadan önce yedek alma ve geri yükleme testini yapın.

## Bilinen durumlar ve sorunlar

- Giriş, hesap kimliği, pass token doğrulaması ve hesap verisi kalıcılığı
  ayrı akışlardır; istemciden gelen account ID tek başına yetki kabul edilmemeli.
- `OwnHomeDataMessage` kaynak bakiyesi, kupa ve Brawl Pass durumunun aynı hesap
  kaydından üretildiğini doğrulamalıdır.
- `EndClientTurnMessage` bozuk veya kısmi paketlerde istisna atmadan reddedilmeli;
  komut listesi her mesaj ve oturum için yeniden oluşturulmalıdır.
- Kutu ve Brawl Pass ödülleri idempotent olmalı; claim kaydı ile kaynak güncellemesi
  atomik yapılmalıdır. Yeniden gönderilen istemci komutu ikinci ödülü vermemeli.
- Büyük kutu, mega kutu ve savaş kutusu teslimatlarının veri yapıları birbirine
  karıştırılmamalıdır. Çoklu ödül ekranı gerçek V41 cihazında doğrulanmadan
  üretimde açılmamalıdır.
- Takım, kulüp, profil, sohbet, liderlik tablosu ve savaş geçmişi mesajlarının
  her biri istemcinin beklediği yanıt alanlarıyla birlikte test edilmelidir.
- Savaş eşleştirmesi bu çalışma ağacında bakım modunda tutulabilir; bakım yanıtı
  kasıtlı bir durumdur ve hata gibi loglanmamalıdır.
- V41.150 native istemci çökmesi, yalnızca sunucu logu ile çözülmüş sayılmaz.
  Gerçek cihazda paket sırası, payload boyutu, PID ve Android crash log birlikte
  incelenmelidir.

## Test ilkeleri

- Gerçek oyuncu verisini sıfırlamayın.
- Token, parola, e-posta veya kişisel verileri loglamayın.
- Protokol alanlarını örnek veya tahminle değiştirmeyin; izole encoder testleri
  ve gerçek cihaz doğrulaması kullanın.
- Başarısız cihaz testini başarılı olarak raporlamayın.
- Üretime almadan önce ayrı test hesabı ve veritabanı yedeği kullanın.

## Mevcut çalışma hakkında

Çalışma sırasında görülen başlıca hatalar; ödül komutlarının tekrar işlenmesi,
ödül kaydının restart sonrasında korunmaması, Brawl Pass ödül ekranının beklemede
kalması, bazı `OwnHomeData` yenilemelerinin native istemciyi çökertmesi ve
liderlik tablosu payload'ının cihazda çökme şüphesi oluşturmasıydı. Bu nedenle
kaynak paylaşımı, hassas canlı veri olmadan ve bu sınırlamalar açıkça belirtilerek
hazırlanmıştır.
