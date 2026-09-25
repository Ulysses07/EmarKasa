# Emar Kasa

Kanal kasalarını ve genel kasayı izlemek için sade kasa takip uygulaması. Cari, stok ve tedarikçi borç takibi ERP12'de kalır. .NET MAUI Windows uygulaması ve telefona uyumlu web arayüzü aynı ASP.NET Core API ve SQLite veritabanını kullanır. Üretim adresi [kasa.emarglobal.com](https://kasa.emarglobal.com/). 23 Eylül 2026 itibarıyla 2.3.0 yayında; önceki kayıtlar ve kasa raporları korunmuştur. Güncel kapsam: [2.3 PDF ekstre ve hesap hareketleri](docs/deploy/kasa-2.3.md), [2.2 aylık giderler ve kasa kontrolleri](docs/deploy/kasa-2.2.md).

## Temel özellikler

Genel kasa ve kanal kasaları, haftalık/aylık hareketler ve alıcı taslağı–editör onayı akışı öne çıkar. Alışta mal açıklaması, toplam tutar ve kanal dağılımı girilir. Firma adı yalnız açıklamadır; cari kartı oluşturulmaz. Editör ödemeyi açıklamayla düzeltebilir, iptal edebilir veya başka alışa taşıyabilir; isteğe bağlı belge ekleyebilir.

**Ekstre / Hareket Yükle** bölümünde kart ekstresi ve banka hesap hareketi PDF'leri okunur. Tüm tanınan satırlar seçimsiz gelir; editör tutar, yön ve kanal dağılımını kontrol edip önizlemeyle kaydeder. Faiz/komisyon sınıflandırması, benzer kayıt uyarısı, özel PDF geçmişi ve gerekçeli iptal vardır. Metin içeren şifresiz TL belgeleri desteklenir; banka düzenleri gerçek örneklerle ayrıca doğrulanmalıdır. Yerel PDF okuma için `pdfinfo` ve `pdftotext` gerekir; Docker görüntüsü araçları içerir.

Giderler seçilen dönem/kanalla Excel ve CSV olarak alınabilir; yazdırılabilir rapor tarayıcıdan PDF kaydedilebilir. Editör şifre değiştirebilir ve tek kullanımlık kurtarma kodu oluşturabilir. Sürüm bildirimi, günlük tutarlı yedek, elle yedek indirme ve ayrı dosyaya geri yükleme aracı eklendi. [Kapsam ve kurallar](docs/specs/2026-09-23-gelistirme.md), [2.0 dağıtımı](docs/deploy/kasa-2.0.md).

## Alış ve kanal eşleştirme

**Alışlar** ekranında alıcı alış taslağını girer, mal kalemlerini bir veya birden fazla kanala ayırır ve incelemeye gönderir. Editör dağılımı kontrol edip onaylar. Alıcı hesaplarını editör bu ekrandan oluşturur; alıcı yalnız kendi alışlarını görür, finansal raporlara ve ödeme düzenlemelerine erişemez.

Ödeme, alışa bağlı tek bir giderdir. Mevcut gider de ikinci kez kasaya yazılmadan bağlanabilir. Kısmi ödemelerde kanal payları ödeme öncesinde gösterilir; son ödeme alışın kanal tutarlarını tam tamamlar. Taslak veya onay kasadan para düşürmez. Ödenmiş fakat onaylanmamış alışın gideri kasada kalır ve **dağılım bekliyor** uyarısıyla gösterilir.

Kanal düzeltmek için editör açıklama yazarak alışını taslağa iade eder, dağılımı düzenler ve yeniden onaylar. Bu işlem ödemeleri korur. Bağlı giderin tutarı/tarihi genel Giderler ekranından değiştirilemez; Alışlar içindeki ödeme düzeltme/iptal/taşıma kullanılır. Bir gider tek alışa bağlanabilir; tek gideri birkaç alışa bölme bu sürümün kapsamı değildir.

[Kullanım ve hesap kuralları](docs/specs/2026-09-19-alis-kanal-eslestirme.md).

## Proje yapısı

| Proje | Sorumluluk |
| --- | --- |
| `Kasa.App` | Windows MAUI arayüzü ve cihaz servisleri |
| `Kasa.App.Core` | Platformdan bağımsız ekran davranışları ve görünüm modelleri |
| `Kasa.ApiClient` | API sözleşmeleri, HTTP istemcisi ve oturum erişimi |
| `Kasa.Api` | Yetkilendirme, veri doğrulama, SQLite ve rapor uçları |
| `Kasa.Core` | Saf hesaplama kuralları, dönem ve taksit üretimi |
| `Kasa.Api/wwwroot` | Aktif, telefona uyumlu web arayüzü; ek paket/build gerektirmez |
| `Kasa.Api.Ui.Tests` | Tarayıcı para girişi, form ve rol kurallarının Node testleri |
| `*.Tests` | Core, API, API istemcisi ve görünüm modeli testleri |
| `web` | Emekli React istemcisi; güncel ürünün dağıtımına dahil değil |

Tüm aktif projeler .NET 10 kullanır. `Kasa.App` şu anda yalnız `net10.0-windows10.0.19041.0` hedefini derler. Android/iOS/MacCatalyst dosyaları depoda bulunsa da bu platformlar etkin derleme hedefleri değildir.

## Yerel geliştirme

API ve dört test projesi için .NET 10 SDK yeterlidir. Windows istemcisi ayrıca Windows üzerinde MAUI iş yüklerini ve Windows SDK'yı gerektirir.

Depo kökünde API'yi başlatın:

```powershell
dotnet restore Kasa.Api/Kasa.Api.csproj
dotnet run --project Kasa.Api/Kasa.Api.csproj --launch-profile http
```

`http` profili API'yi `http://localhost:5232` adresinde, `Development` ortamıyla başlatır. `GET /health` sağlık kontrolüdür. Yerel geliştirme giriş bilgileri `editor` / `degistir-beni`; bunlar yalnız `appsettings.Development.json` içindedir. Yerel veritabanı bağlantısı `Data Source=kasa.db` olarak tanımlıdır.

Windows istemcisini ayrı bir PowerShell oturumunda yerel API'ye yönlendirip çalıştırın:

```powershell
$env:KASA_API_URL = 'http://localhost:5232/'
dotnet workload restore Kasa.App/Kasa.App.csproj
dotnet build Kasa.App/Kasa.App.csproj --configuration Debug --framework net10.0-windows10.0.19041.0 -t:Run
```

`KASA_API_URL` istemci süreci başlarken okunur. HTTPS adresleri ve yalnız yerel döngü adreslerindeki HTTP (`localhost`, `127.0.0.1`, `::1`) kabul edilir. Değer boşsa üretim adresi `https://kasa.emarglobal.com/` kullanılır; yerel çalışırken değişkeni açıkça ayarlayın. Adreste kullanıcı bilgisi, sorgu veya fragment bulunamaz. Visual Studio'dan çalıştırırken değişkenin o sürece de iletildiğinden emin olun.

## Derleme ve test

MAUI yüklemeden, Windows veya Linux'ta dört test projesini ayrı ayrı çalıştırabilirsiniz:

```powershell
dotnet test Kasa.Core.Tests/Kasa.Core.Tests.csproj --configuration Release
dotnet test Kasa.Api.Tests/Kasa.Api.Tests.csproj --configuration Release
dotnet test Kasa.ApiClient.Tests/Kasa.ApiClient.Tests.csproj --configuration Release
dotnet test Kasa.App.Core.Tests/Kasa.App.Core.Tests.csproj --configuration Release
```

Bu komutlar gereken NuGet paketlerini de geri yükler. API testleri ayrı test yapılandırması ve bellek içi SQLite kullanır. Tüm çözümü Linux'ta derlemek Windows MAUI hedefini de yüklemeye çalışacağından platformdan bağımsız kontroller için yukarıdaki projeleri kullanın.

Windows istemcisinin Release derlemesi:

```powershell
dotnet workload restore Kasa.App/Kasa.App.csproj
dotnet build Kasa.App/Kasa.App.csproj --configuration Release --framework net10.0-windows10.0.19041.0
```

[CI iş akışı](.github/workflows/ci.yml), push ve pull request olaylarında dört test projesini Ubuntu üzerinde, Windows uygulaması derlemesini ayrı Windows işi olarak çalıştırır. İşler .NET 10 SDK'yı seçer. CI paket yayımlamaz ve canlıya dağıtım yapmaz; emekli web istemcisi zorunlu CI kapsamına dahil değildir.

## Üretim yapılandırması

Üretimde aşağıdaki değerleri ortam değişkenleri veya dağıtım sisteminin gizli değer deposu üzerinden sağlayın:

| Ortam değişkeni | Gereklilik |
| --- | --- |
| `ASPNETCORE_ENVIRONMENT` | `Production` |
| `Kasa__JwtKey` | En az 32 UTF-8 baytlık, rastgele ve geliştirmeden farklı JWT imzalama anahtarı |
| `Kasa__EditorKullanici` | Üretime ait editör kullanıcı adı |
| `Kasa__EditorSifre` | Üretime ait özel parola; geliştirme parolası kabul edilmez |
| `ConnectionStrings__Kasa` | Kalıcı diskteki SQLite bağlantısı; örnek: `Data Source=/data/kasa.db` |

Eksik/geçersiz kimlik ayarlarında API başlamaz. Gerçek anahtar ve parolaları depoya yazmayın. Docker Compose dosyası `KASA_JWT_KEY`, `KASA_EDITOR_KULLANICI` ve `KASA_EDITOR_SIFRE` değerlerini ilgili ASP.NET Core ayarlarına eşler. API dış erişimini HTTPS üzerinden sunun; ters vekil arkasındaki dahili HTTP bağlantısı istemci adresinden ayrıdır.

Bu sürümde oturum damgası doğrulaması eklendiği için önceki sürümün açık oturumları bir kez yeniden giriş gerektirir. Bundan sonra ilgili giriş bilgileri değiştirildiğinde eski oturumlar da geçersizleşir.

## Veritabanı başlangıcı ve geçiş

API açılışında `KasaDatabaseInitializer.Initialize(db)` çalışır. Yeni veritabanını hazırlar; mevcut desteklenen şemayı kayıtları koruyarak günceller ve kanal bağlantılarını taşır. Eski hareketlerde adı bulunan, kanal listesinde bulunmayan kanalları pasif kayıt olarak korur. Aynı dönem ve kanala ait eski yinelenen gelirlerin bütün satırları korunur; bu gruplar salt okunurdur ve düzenleme isteği açıklayıcı 409 yanıtı alır. Satırlar birleştirilmez veya silinmez. Yinelenen kanal adları, tanınmayan özel şema veya geçersiz ilişki gibi desteklenmeyen belirsizliklerde geçiş geri alınır ve başlangıç durdurulur.

Canlı veritabanını güncellemeden önce tutarlı bir yedek alın ve geri yüklemeyi doğrulayın. Çalışan SQLite veritabanında yalnız ana `.db` dosyasını kopyalamak yerine SQLite yedekleme yöntemini kullanın veya uygulamayı durdurarak kopyalayın. `docs/deploy` ve eski planlardaki veritabanını silip yeniden oluşturma talimatları mevcut veriye uygulanmamalıdır; güncel giriş noktası başlatıcıdır.

23 Eylül 2026 canlı geçişinde eski satırlar, beş migration, bütün geçmiş kasa/kanal raporları ve yedekten geri yükleme sunucu içinde doğrulandı. Eski veri dizini değiştirilmedi; yeni sürüm ayrı veri dizininde çalışır ve diğer veri dosyaları da korunur. Son kontrol: Core 79, API 146, ApiClient 59, App.Core 75 ve web 33 olmak üzere **392 test** geçti; Windows Release derlemesi 0 hata ve 0 uyarıyla tamamlandı. Yayın ve geri dönüş adımları [2.0 dağıtım kılavuzundadır](docs/deploy/kasa-2.0.md).

## Sonraki adımlar

[Ürün yol haritası](docs/roadmap.md) güncel sade kasa kapsamını listeler. Mevcut kart/kredi hesaplama kuralları korunur. Yeni cari, stok, banka hesabı ve vade modülleri eklenmez; ERP12 entegrasyonu yapılmamıştır. Tarihsel kararlar için `docs/specs` ve `docs/plans` korunur.
