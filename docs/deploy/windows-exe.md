# Emar Kasa — Windows masaüstü ZIP yayını

Windows MAUI masaüstü uygulaması bağımsız ürün olarak korunur.
macOS üzerinde iOS hedefinin eklenmesi Windows'un hedefini veya verisini değiştirmez.

## GitHub'dan paket üret

[Windows desktop release](../../.github/workflows/release-windows.yml) yalnız manuel başlatılır:

1. Workflow dosyası deponun varsayılan `release/2.x` dalına alındıktan sonra GitHub **Actions**
   sayfasında **Windows desktop release → Run workflow** aç.
2. Derlenecek dalı seç. Varsayılan seçenekler yalnız indirilebilir artifact üretir.
3. İş bitince **Artifacts** altından `kasa-windows-<ürün sürümü>-win-x64` paketini indir.
4. İçindeki `EmarKasa-<ürün sürümü>-win-x64.zip` dosyasını tamamen bir klasöre aç.
5. Bu klasördeki **Kasa.App.exe** dosyasını çalıştır. Yanındaki DLL/kaynak dosyalarını taşıma veya silme.

Paket .NET ve Windows App SDK çalışma zamanlarını içerir. Hedef Windows 10/11 x64'tür;
Windows ARM64 veya x86 için ayrı paket üretilmez.
`KasaSurumu` [Directory.Build.props](../../Directory.Build.props) üzerinden okunur.
Ürün sürümü otomatik artırılmaz.

Yayın işi .NET SDK `10.0.401` / workload set `10.0.401.1` kullanır;
ortak istemci testlerini çalıştırır, **dağıtılan aynı publish klasöründeki** uygulamanın açılışını sınar,
ardından ZIP ve SHA-256 üretir.
Açılış testi yalnız `127.0.0.1:9` adresine bağlanır; gerçek sunucuya mali istek göndermez.
Bu duman testi gerçek kullanıcı oturumu ve tüm ekranların kullanım testi değildir.

### İsteğe bağlı GitHub taslak sürümü

`create_draft_release` varsayılan olarak kapalıdır.
Açılırsa yeni ve benzersiz bir `release_tag` gir: örneğin ürün sürümüne uygun `vX.Y.Z-windows`.
Workflow yalnız **taslak** oluşturur; herkese açık sürümü otomatik yayımlamaz.
Mevcut etiket [GitHub referans API](https://docs.github.com/en/rest/git/refs#list-matching-references) ile denetlenir ve reddedilir; mevcut release/etiket güncellenmez. Bu adım için yalnız taslak işine `contents: write` verilir.

[GitHub manuel workflow rehberi](https://docs.github.com/en/actions/how-tos/manage-workflow-runs/manually-run-a-workflow)
dosyanın varsayılan dalda bulunması gerektiğini açıklar.

## Paket bütünlüğü ve güncelleme

Artifact içindeki `SHA256SUMS.txt` ZIP özetini içerir. PowerShell'de indirdiğin ZIP için:

```powershell
Get-FileHash -LiteralPath .\EmarKasa-<ürün-sürümü>-win-x64.zip -Algorithm SHA256
```

Dosya adını gerçek indirilen ZIP'e göre seç ve özetin aynı olduğunu kontrol et.
ZIP içindeki `build.json` sürüm, kaynak commit ve açılış testinin sonucunu kaydeder.
Yenilemede uygulamayı kapat, yeni ZIP'i yeni klasöre aç ve yeni `Kasa.App.exe` ile başlat.
Finans kayıtları API sunucusunda tutulur; yerel exe klasörüne veri taşıma/migration yapılmaz.

EXE bu kişisel dağıtım akışında Authenticode ile imzalanmaz.
Windows internetten indirilen imzasız pakette SmartScreen uyarısı gösterebilir;
paketin kendi GitHub yayının ve doğrulanmış checksum'ıyla eşleştiğini kontrol et.
Kod imzalama sertifikası edinmek bu akışın şartı değildir.

## Yerel Windows derlemesi

.NET 10 SDK ve MAUI Windows workload kurulu Windows makinede depo kökünden:

```powershell
dotnet workload install maui-windows --version 10.0.401.1
dotnet publish Kasa.App/Kasa.App.csproj -c Release -f net10.0-windows10.0.19041.0 `
    -r win-x64 -p:RuntimeIdentifierOverride=win-x64 -p:WindowsPackageType=None `
    -p:SelfContained=true -p:WindowsAppSDKSelfContained=true -p:PublishSingleFile=false
```

Çıktı `Kasa.App/bin/Release/net10.0-windows10.0.19041.0/win-x64/publish/` altındadır.
Yalnız EXE değil **klasörün tamamı** dağıtılır. GitHub akışı bunu
[windows-release.ps1](../../.github/scripts/windows-release.ps1) ile paketler.

.NET 10 için taşınabilir `win-x64` RID kullanılır; `win10-x64` kullanılmaz.
[Microsoft paketsiz MAUI yayını](https://learn.microsoft.com/en-us/dotnet/maui/windows/deployment/publish-unpackaged-cli?view=net-maui-10.0)
hedef proje, RID ve Windows App SDK dağıtım özelliklerini açıklar.

## API adresi

Varsayılan adres [ApiAdresi.cs](../../Kasa.ApiClient/ApiAdresi.cs) içinde
`https://kasa.emarglobal.com/` olarak tanımlıdır.
Başka ortam için uygulama açılmadan önce `KASA_API_URL` ayarlanır; çalışan uygulama yeniden başlatılmalıdır.
HTTPS veya yalnız yerel döngü HTTP kabul edilir:

```powershell
$env:KASA_API_URL = 'http://localhost:5232/'
```

Paket kabulünde temiz bir Windows makinede açılış, HTTPS bağlantısı, giriş,
PDF seçimi ve finans önizleme/kayıt akışını dene.
iOS'un imzalama/TestFlight adımları [ayrı iOS rehberindedir](ios-yayin.md).

PR ve push üzerinde mevcut **Kasa CI / Build and smoke-test Windows MAUI app** işi aynı `.github/workflows/windows-validation.yml` paketleme akışını çağırır. Başarılı CI koşusunun artifact’ı da tam dağıtım ZIP’idir. Elle yayın akışı bu ortak işi tekrar kullanır; CI taslak sürüm oluşturamaz. Gizli başlatılan pencere, görünürlüğe bağlı `Process.MainWindowHandle` yerine süreç kimliği ve WinUI pencere sınıfıyla bulunur.
