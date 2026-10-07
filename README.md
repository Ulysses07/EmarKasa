# Emar Kasa

Kanal kasalarını ve genel kasayı izlemek için sade kasa takip uygulaması. Cari, stok ve tedarikçi borç takibi ERP12'de kalır. .NET MAUI Windows uygulaması ve telefona uyumlu web arayüzü aynı ASP.NET Core API ve SQLite veritabanını kullanır. Üretim adresi [kasa.emarglobal.com](https://kasa.emarglobal.com/). Güncel canlı sürüm 4 Ekim 2026'da yayımlanan [2.4.1](docs/deploy/kasa-2.4.1.md)'dir. Bu kaynak ağacının sürümü [Directory.Build.props](Directory.Build.props) içindedir. Canlıda çalışan sürümü `/api/surum` ve yayın manifestinden ayrıca doğrulayın. Güncel kaynak kapsamı: [2.4.1 güvenlik ve geri yükleme düzeltmeleri](docs/deploy/kasa-2.4.1.md), [2.4 veri ve güvenlik değişiklikleri](docs/deploy/kasa-2.4.md), [2.3 PDF ekstre ve hesap hareketleri](docs/deploy/kasa-2.3.md), [2.2 aylık giderler ve kasa kontrolleri](docs/deploy/kasa-2.2.md).

## Temel özellikler

Genel kasa ve kanal kasaları, haftalık/aylık hareketler ve alıcı taslağı–editör onayı akışı öne çıkar. Alışta mal açıklaması, toplam tutar ve kanal dağılımı girilir. Firma adı yalnız açıklamadır; cari kartı oluşturulmaz. Editör ödemeyi açıklamayla düzeltebilir, iptal edebilir veya başka alışa taşıyabilir; isteğe bağlı belge ekleyebilir.

**Ekstre / Hareket Yükle** bölümünde kart ekstresi ve banka hesap hareketi PDF'leri okunur. Tüm tanınan satırlar seçimsiz gelir; editör tutar, yön ve kanal dağılımını kontrol edip önizlemeyle kaydeder. Faiz/komisyon sınıflandırması, benzer kayıt uyarısı, özel PDF geçmişi ve gerekçeli iptal vardır. Metin içeren şifresiz TL belgeleri desteklenir; banka düzenleri gerçek örneklerle ayrıca doğrulanmalıdır. Yerel PDF okuma için `pdfinfo` ve `pdftotext` gerekir; Docker görüntüsü araçları içerir.

Giderler seçilen dönem/kanalla Excel ve CSV olarak alınabilir; yazdırılabilir rapor tarayıcıdan PDF kaydedilebilir. Editör şifre değiştirebilir ve tek kullanımlık kurtarma kodu oluşturabilir. Sürüm bildirimi, günlük tutarlı yedek, elle yedek indirme ve ayrı dosyaya geri yükleme aracı eklendi. [Kapsam ve kurallar](docs/specs/2026-09-23-gelistirme.md), [2.0 dağıtımı](docs/deploy/kasa-2.0.md).

[Kişisel ekstre kuralları](docs/ekstre-kurallari.md): AI olmadan açıklamaya göre işlem türü ve kanal dağılımı önerisi, seçimi hatırlama, çelişki kontrolü ve kural yönetimi. Öneriler satırları kendiliğinden seçmez veya kaydetmez.

## Alış ve kanal eşleştirme

**Alışlar** ekranında alıcı alış taslağını girer, mal kalemlerini bir veya birden fazla kanala ayırır ve incelemeye gönderir. Editör dağılımı kontrol edip onaylar. Alıcı hesaplarını editör bu ekrandan oluşturur; alıcı yalnız kendi alışlarını görür, finansal raporlara ve ödeme düzenlemelerine erişemez.

Ödeme, alışa bağlı tek bir giderdir. Mevcut gider de ikinci kez kasaya yazılmadan bağlanabilir. Kısmi ödemelerde kanal payları ödeme öncesinde gösterilir; son ödeme alışın kanal tutarlarını tam tamamlar. Taslak veya onay kasadan para düşürmez. Ödenmiş fakat onaylanmamış alışın gideri kasada kalır ve **dağılım bekliyor** uyarısıyla gösterilir.

Kanal düzeltmek için editör açıklama yazarak alışını taslağa iade eder, dağılımı düzenler ve yeniden onaylar. Bu işlem ödemeleri korur. Bağlı giderin tutarı/tarihi genel Giderler ekranından değiştirilemez; Alışlar içindeki ödeme düzeltme/iptal/taşıma kullanılır. Bir gider tek alışa bağlanabilir; tek gideri birkaç alışa bölme bu sürümün kapsamı değildir.

[Kullanım ve hesap kuralları](docs/specs/2026-09-19-alis-kanal-eslestirme.md).

## Proje yapısı

| Proje | Sorumluluk |
| --- | --- |
| `Kasa.App` | Windows masaüstü ve iOS MAUI arayüzü, platforma özel cihaz servisleri |
| `Kasa.App.Core` | Platformdan bağımsız ekran davranışları ve görünüm modelleri |
| `Kasa.ApiClient` | API sözleşmeleri, HTTP istemcisi ve oturum erişimi |
| `Kasa.Api` | Yetkilendirme, veri doğrulama, SQLite ve rapor uçları |
| `Kasa.Core` | Saf hesaplama kuralları, dönem ve taksit üretimi |
| `Kasa.Api/wwwroot` | Aktif, telefona uyumlu web arayüzü; ek paket/build gerektirmez |
| `Kasa.Api.Ui.Tests` | Tarayıcı para girişi, form ve rol kurallarının Node testleri |
| `*.Tests` | Core, API, API istemcisi ve görünüm modeli testleri |

Emekli React istemcisi (`web/`) dağıtımın parçası olmadığı için depodan kaldırıldı; son hâli [621b370](https://github.com/Ulysses07/EmarKasa/tree/621b370f0968829a2fbef9495155876e0661e9db/web) commit'indedir.

Tüm aktif projeler .NET 10 kullanır. `Kasa.App` şu anda yalnız `net10.0-windows10.0.19041.0` hedefini derler. Android/iOS/MacCatalyst dosyaları depoda bulunsa da bu platformlar etkin derleme hedefleri değildir.

## Yerel geliştirme

API ve beş test projesi için .NET 10 SDK yeterlidir. Windows istemcisi ayrıca Windows üzerinde MAUI iş yüklerini ve Windows SDK'yı gerektirir.

Depo kökünde API'yi başlatın:

```powershell
$env:Kasa__JwtKey = [Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(48))
$env:Kasa__EditorKullanici = 'editor'
$env:Kasa__EditorSifre = Read-Host 'Yerel editör parolası'
dotnet restore Kasa.Api/Kasa.Api.csproj
dotnet run --project Kasa.Api/Kasa.Api.csproj --launch-profile http
```

`http` profili API'yi `http://localhost:5232` adresinde, `Development` ortamıyla başlatır. `GET /health` sağlık kontrolüdür. Kullanıcı adı `editor`, parola yukarıda seçtiğiniz değerdir. JWT anahtarı ve parola depoda tutulmaz; API bu değerler olmadan açılmaz. Ortam değişkenlerini API'yi başlattığınız aynı PowerShell oturumunda ayarlayın. Yerel veritabanı bağlantısı `Data Source=kasa.db` olarak tanımlıdır; önceden oluşturulmuş bir veritabanında ortam parolasını değiştirmek kayıtlı editör parolasını değiştirmez.

Windows istemcisini ayrı bir PowerShell oturumunda yerel API'ye yönlendirip çalıştırın:

```powershell
$env:KASA_API_URL = 'http://localhost:5232/'
dotnet workload restore Kasa.App/Kasa.App.csproj
dotnet build Kasa.App/Kasa.App.csproj --configuration Debug --framework net10.0-windows10.0.19041.0 -t:Run
```

`KASA_API_URL` istemci süreci başlarken okunur. HTTPS adresleri ve yalnız yerel döngü adreslerindeki HTTP (`localhost`, `127.0.0.1`, `::1`) kabul edilir. Değer boşsa üretim adresi `https://kasa.emarglobal.com/` kullanılır; yerel çalışırken değişkeni açıkça ayarlayın. Adreste kullanıcı bilgisi, sorgu veya fragment bulunamaz. Visual Studio'dan çalıştırırken değişkenin o sürece de iletildiğinden emin olun.

## Derleme ve test

MAUI yüklemeden, Windows veya Linux'ta beş test projesini ayrı ayrı çalıştırabilirsiniz:

```powershell
dotnet test Kasa.Core.Tests/Kasa.Core.Tests.csproj --configuration Release
dotnet test Kasa.Api.Tests/Kasa.Api.Tests.csproj --configuration Release
dotnet test Kasa.ApiClient.Tests/Kasa.ApiClient.Tests.csproj --configuration Release
dotnet test Kasa.App.Core.Tests/Kasa.App.Core.Tests.csproj --configuration Release
dotnet test Kasa.Sozlesme.Tests/Kasa.Sozlesme.Tests.csproj --configuration Release
```

Bu komutlar gereken NuGet paketlerini de geri yükler. API testleri ayrı test yapılandırması ve bellek içi SQLite kullanır. Tüm çözümü Linux'ta derlemek Windows MAUI hedefini de yüklemeye çalışacağından platformdan bağımsız kontroller için yukarıdaki projeleri kullanın.

## Windows ve iOS için ayrı paketler

Aynı uygulama Windows'ta masaüstü ZIP, bulut macOS runner'ında iOS IPA olarak derlenir. Her ikisi aynı API'ye bağlanır;
iOS bir çevrimdışı veritabanı veya masaüstüyle ayrıca eşitlenen ikinci veri kaynağı oluşturmaz.

- [Windows paketleme rehberi](docs/deploy/windows-exe.md): tam çalışma zamanı içeren ZIP, aynı pakete açılış testi.
- [iOS / TestFlight rehberi](docs/deploy/ios-yayin.md): kendi Mac'in gerekmez; GitHub macOS runner'ı derler ve imzalar.
- [İki platformun yayın akışı](docs/deploy/windows-ios-yayin.md): ortak sürüm, ayrı paketler ve doğrulama kapıları.

Windows ve iOS yayınları Actions'tan elle başlatılır. Kod CI'ı canlıya dağıtım veya Apple yüklemesi yapmaz.
iOS imzalama ve TestFlight için Apple sertifika/profil/API anahtarlarının ayrıca yapılandırılması gerekir.

Windows istemcisinin Release derlemesi:

```powershell
dotnet workload restore Kasa.App/Kasa.App.csproj
dotnet build Kasa.App/Kasa.App.csproj --configuration Release --framework net10.0-windows10.0.19041.0
```

[CI iş akışı](.github/workflows/ci.yml), push ve pull request olaylarında beş test projesini Ubuntu üzerinde, Windows uygulaması derlemesini ayrı Windows işi olarak çalıştırır. İşler .NET 10 SDK'yı seçer. CI paket yayımlamaz ve canlıya dağıtım yapmaz. [iOS kontrolü](.github/workflows/ios-validation.yml) ayrıca bulut macOS üzerinde imzasız Release cihaz derlemesini sınar; cihazda çalışma ve TestFlight doğrulaması ayrı adımlardır.

## Üretim yapılandırması

Üretimde aşağıdaki değerleri ortam değişkenleri veya dağıtım sisteminin gizli değer deposu üzerinden sağlayın:

| Ortam değişkeni | Gereklilik |
| --- | --- |
| `ASPNETCORE_ENVIRONMENT` | `Production` |
| `Kasa__JwtKey` | En az 32 UTF-8 baytlık, rastgele ve geliştirmeden farklı JWT imzalama anahtarı |
| `Kasa__EditorKullanici` | Üretime ait editör kullanıcı adı |
| `Kasa__EditorSifre` | Üretime ait özel parola; geliştirme parolası kabul edilmez. Yalnız ilk kurulumda (editör şifresi uygulamada hiç değiştirilmediyse) ve `Kasa__EditorSifreSifirla` ile geçerlidir |
| `Kasa__EditorSifreSifirla` | İsteğe bağlı; varsayılan `false` (compose'da `KASA_EDITOR_SIFRE_SIFIRLA`). `true` iken açılışta editör şifresi `Kasa__EditorSifre`'ye sıfırlanır (12–1024 karakter olmalı, yoksa API başlamaz); kurtarma kodu ve bütün editör oturumları düşer, geri yüklemenin kilitlediği editör girişi açılır. Aynı ortam şifresiyle yalnız bir kez uygulanır: bayrak açık unutulsa da editörün sonradan değiştirdiği şifre ezilmez. Yalnız sıfırlama için açın, sonra kaldırın ([deploy/README.md](deploy/README.md) "Editör şifresini sıfırlama") |
| `ConnectionStrings__Kasa` | Kalıcı diskteki SQLite bağlantısı; örnek: `Data Source=/data/kasa.db` |
| `GuvenlikGunlugu__Yol` | İsteğe bağlı. Veritabanı dışındaki güvenlik günlüğünün yolu; varsayılan yedek dizininde (`Yedek__Dizin`, compose'da `/yedekler`) `guvenlik-gunlugu.jsonl`. Veri dizininden (`/data`) ayrı kalmalıdır; silinmez. Geri yüklemede yedekten sonraki şifre ve alıcı kararları buradan yeniden uygulanır ([runbook](docs/deploy/operasyon-runbook.md) "Geri yüklemeden sonra") |
| `GuvenlikGunlugu__Etkin` | Üretimde zorunlu `true`; `false` verilirse API açılmaz. Geliştirmede varsayılan `false`. Günlük yazılamazsa kimlik/izin değişikliği kaydedilmez; yedek geri yüklenirken günlük yoksa veya yedekten sonra başlamışsa editör girişi kilitlenir ve alıcılar pasifleştirilir |
| `Kasa__GuvenilirVekiller` | `X-Forwarded-For` kabul edilen vekil adresleri (IP/CIDR; `;` veya `,` ile ayrılır). Boşsa loopback ve Docker'ın varsayılan havuzları; yalnız yerel Nginx'in bağlandığı kurulumda yeterlidir. Ortak Docker ağında (Caddy şablonu) yalnız ters vekil konteynerinin adresine daraltılmalıdır; bkz. [deploy/README.md](deploy/README.md) "Güvenilen vekiller" |

Eksik/geçersiz kimlik ayarlarında API başlamaz. Gerçek anahtar ve parolaları depoya yazmayın. Veritabanında editör şifresi varken `.env`'deki `KASA_EDITOR_SIFRE`'yi değiştirmek girişi değiştirmez; ortam şifresi yalnız ilk kurulumda ve sıfırlama bayrağıyla (`Kasa__EditorSifreSifirla`) geçerlidir. Bir geri yükleme yedekten sonraki bir şifre değişikliği bulursa editör girişini kilitler (ne yedekteki ne ortamdaki şifre geçer); kilidi yalnız sıfırlama açar ([runbook](docs/deploy/operasyon-runbook.md) "Geri yüklemeden sonra"). Docker Compose dosyası `KASA_JWT_KEY`, `KASA_EDITOR_KULLANICI`, `KASA_EDITOR_SIFRE` ve `KASA_EDITOR_SIFRE_SIFIRLA` değerlerini ilgili ASP.NET Core ayarlarına eşler. API dış erişimini HTTPS üzerinden sunun; ters vekil arkasındaki dahili HTTP bağlantısı istemci adresinden ayrıdır.

Nginx Compose şablonu `/data` ve `/yedekler` host dizinlerini `deploy/.env` içindeki zorunlu `KASA_DATA_DIR` ve `KASA_BACKUP_DIR` değişkenlerinden alır; Caddy temel şablonuna kalıcı `/yedekler` bağlaması için [Caddy yedek ek dosyası](deploy/docker-compose.caddy-backup.yml) eklenmelidir. Eksik veya boş değişkende Compose durur. Bağlamalar `create_host_path: false` ile yazılıdır; yol yoksa Docker boş dizin açmaz, `up` hata verir. `deploy/kasa-data` 2.0 öncesinden korunmuş eski veritabanıdır ve bağlanmamalıdır. Etkin dizin son yayın manifestinin `dataDirectory` alanında ve çalışan konteynerin `/data` bağlama kaynağındadır. Güncellemeden önce değeri `.env`'e yazın, `docker compose -f docker-compose.nginx.yml config` ile doğrulayın, ardından `up` çalıştırın. Adımlar [deploy/README.md](deploy/README.md) "Güncelleme" bölümündedir.

Bu sürümde oturum damgası doğrulaması eklendiği için önceki sürümün açık oturumları bir kez yeniden giriş gerektirir. Bundan sonra ilgili giriş bilgileri değiştirildiğinde eski oturumlar da geçersizleşir.

## Veritabanı başlangıcı ve geçiş

API açılışında `KasaVeritabaniBaslatici.Baslat(db)` çalışır. Yeni veritabanını hazırlar; mevcut desteklenen şemayı kayıtları koruyarak günceller ve kanal bağlantılarını taşır. Eski hareketlerde adı bulunan, kanal listesinde bulunmayan kanalları pasif kayıt olarak korur. Aynı dönem ve kanala ait eski yinelenen gelirlerin bütün satırları korunur; bu gruplar salt okunurdur ve düzenleme isteği açıklayıcı 409 yanıtı alır. Satırlar birleştirilmez veya silinmez. Yinelenen kanal adları, tanınmayan özel şema veya geçersiz ilişki gibi desteklenmeyen belirsizliklerde geçiş geri alınır ve başlangıç durdurulur.

Canlı veritabanını güncellemeden önce tutarlı bir yedek alın ve geri yüklemeyi doğrulayın. Çalışan SQLite veritabanında yalnız ana `.db` dosyasını kopyalamak yerine SQLite yedekleme yöntemini kullanın veya uygulamayı durdurarak kopyalayın. `docs/deploy` belgelerindeki ve arşivlenmiş eski planlardaki (`docs/plans`) veritabanını silip yeniden oluşturma talimatları mevcut veriye uygulanmamalıdır; güncel giriş noktası başlatıcıdır.

23 Eylül 2026 canlı geçişinde eski satırlar, beş migration, bütün geçmiş kasa/kanal raporları ve yedekten geri yükleme sunucu içinde doğrulandı. Eski veri dizini değiştirilmedi; yeni sürüm ayrı veri dizininde çalışır ve diğer veri dosyaları da korunur. O günkü kontrol: Core 79, API 146, ApiClient 59, App.Core 75 ve web 33 olmak üzere **392 test** geçti; Windows Release derlemesi 0 hata ve 0 uyarıyla tamamlandı. Bu sayı güncel dalın test sonucu değildir. Yayın ve geri dönüş adımları [2.0 dağıtım kılavuzundadır](docs/deploy/kasa-2.0.md).

## Sonraki adımlar

[Ürün yol haritası](docs/roadmap.md) güncel sade kasa kapsamını listeler. Mevcut kart/kredi hesaplama kuralları korunur. Yeni cari, stok, banka hesabı ve vade modülleri eklenmez; ERP12 entegrasyonu yapılmamıştır. Tarihsel kararlar için `docs/specs` korunur. Ajan uygulama planları (`docs/plans`) depodan kaldırıldı; son hâli [621b370](https://github.com/Ulysses07/EmarKasa/tree/621b370f0968829a2fbef9495155876e0661e9db/docs/plans) commit'indedir.
