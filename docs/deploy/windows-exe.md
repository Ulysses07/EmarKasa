# Emar Kasa — Windows kurulum ve güncelleme yayını

Windows MAUI masaüstü uygulaması Windows 10/11 x64 için yayımlanır. Paket .NET ve Windows App SDK çalışma zamanlarını içerir. ARM64 veya x86 paketi bu akışta üretilmez.

## İlk kurulum

1. GitHub Actions içinde başarılı **Windows desktop release** veya **Kasa CI** koşusunu aç.
2. **Artifacts** altından `kasa-windows-<ürün sürümü>-win-x64` dosyasını indir ve aç.
3. İçindeki `*-Setup.exe` kurulum dosyasını çalıştır.
4. Uygulamayı **Emar Kasa** Başlat menüsü veya masaüstü kısayoluyla aç.

Kurulum kullanıcıya özeldir; varsayılan konum `%LocalAppData%\EmarKasa` olur. Kısayol, güncelleme sonrasında da aynı kurulumdaki güncel uygulamayı açar. Kurulum klasöründeki dosyaları tek tek taşıma.

**Daha önce ZIP kullanıyorsan bir kez Setup ile kur.** Eski ZIP klasöründen çalışan uygulama uygulama içi güncelleme yapamaz. Alternatif dağıtım olarak `EmarKasa-<ürün sürümü>-win-x64.zip` korunur: tüm klasörü çıkarıp `Kasa.App.exe` açılır; yenileme elle yeni ZIP ile yapılır.

Marka geçişinde uygulama kimliği `com.royalmezat.kasa` yerine `com.emar.kasa` oldu. Windows güvenli depolama konumu bu kimliğe bağlı olduğundan **yeniden giriş gerekebilir**. Önceki kimliğin yerel giriş bilgileri otomatik taşınmaz veya silinmez. Finans kayıtları API sunucusunda kaldığı için exe/kurulum değiştirmek sunucudaki kayıtları değiştirmez.

## Uygulama içi güncelleme

Setup ile kurulu Windows uygulaması başlangıçta yalnız kararlı [Emar Kasa GitHub yayınlarındaki](https://github.com/Ulysses07/EmarKasa/releases) yeni sürüm bilgisini kontrol eder; paketi indirmez veya kurmaz. **Uygulama güncellemeleri** görünümü açık ekranın üzerinde açılır ve alttaki formu korur. Her sayfadan kontrol ve indirme yapılabilir. İndirme ve **Kur ve yeniden başlat** kullanıcı tarafından başlatılır; indirilmiş paket sonraki açılışta kendiliğinden uygulanmaz.

Güncelleme kurulumu uygulamayı kapatıp yeniden başlattığı için yalnız **Haftalık rapor** veya **Giriş** ekranından başlatılır. Başka bir ekranda çalışıyorsan önce formunu kaydet, **Haftalık rapor** ekranına dön ve güncelleme görünümünü yeniden açarak kurulumu onayla. Devam eden işlemler tamamlanmadan kurulum başlamaz. Açık form varken kontrol ve indirme yapabilirsin; bu işlemler formu kapatmaz.

Yayın hazırlayan kişi aynı GitHub sürümüne aşağıdaki üretim dosyalarını birlikte eklemeli:

| Dosya | Kullanım |
| --- | --- |
| `*-Setup.exe` | İlk kurulum |
| `EmarKasa-<sürüm>-full.nupkg` | Kurulu uygulamanın indireceği tam güncelleme paketi |
| `releases.win.json` | Paket sürümü, adı, boyutu ve özetlerini içeren güncelleme feed'i |
| `EmarKasa-<sürüm>-win-x64.zip` | Elle kullanılan alternatif dağıtım |
| `build.json`, `SHA256SUMS.txt` | Kaynak commit, doğrulama sonuçları ve dosya özetleri |

CLI paket adında platform/kanal eki kullanabilir; artifact içindeki gerçek `*-full.nupkg` dosya adını koru. Feed dosyasında yazan adla yüklenen paket adı aynı olmalı.

**Taslak GitHub sürümleri ve prerelease sürümleri bu istemcinin güncelleme kanalında görünmez.** Dosyalar kontrol edildikten sonra taslak GitHub arayüzünde ayrıca kararlı sürüm olarak yayımlanmalıdır. Workflow bu yayımı otomatik yapmaz. Yeni sürüm için `KasaSurumu` elle yükseltilir; kurulu sürümden daha yüksek olmalıdır.

Velopack paket kimliği `EmarKasa`, uygulama kimliği `com.emar.kasa` ve bildirim/kısayol AUMID'si `EmarKasa.Masaustu` ilk kurulumdan sonra sabit kalır. Kimlik değişikliği sonraki sürümde ayrı kurulum veya giriş depolaması oluşturabilir.

## GitHub'dan paket üret

[Windows desktop release](../../.github/workflows/release-windows.yml) yalnız manuel başlatılır:

1. Workflow varsayılan `release/2.x` dalına alındıktan sonra **Actions → Windows desktop release → Run workflow** aç.
2. Derlenecek dalı seç. Varsayılan seçenekler yalnız indirilebilir artifact üretir.
3. İsteğe bağlı `create_draft_release` seçilirse yeni ve benzersiz `release_tag` gir; örneğin ürün sürümüne uygun `vX.Y.Z-windows`.
4. Koşu başarılı olduktan sonra artifact'ı indir. Taslak seçildiyse aynı dosyalar yeni GitHub taslağına yüklenir.

Mevcut etiket reddedilir; mevcut release veya etikete dosya eklenmez. Yalnız taslak oluşturan işte `contents: write` yetkisi bulunur. Üretim sürümü [Directory.Build.props](../../Directory.Build.props) içindeki `KasaSurumu` üzerinden okunur ve workflow tarafından artırılmaz.

[GitHub manuel workflow rehberi](https://docs.github.com/en/actions/how-tos/manage-workflow-runs/manually-run-a-workflow) varsayılan dal koşulunu açıklar. Velopack'in [dağıtım rehberi](https://docs.velopack.io/distributing/overview) kurulum, paket ve feed dosyalarını açıklar.

## CI neyi doğrular?

PR/push üzerindeki **Kasa CI / Build and smoke-test Windows MAUI app** ve manuel yayın aynı [windows-validation.yml](../../.github/workflows/windows-validation.yml) işini çağırır. .NET SDK `10.0.401`, workload set `10.0.401.1` ve Velopack CLI `1.2.161` sabitlenmiştir; CLI yalnız runner'ın geçici tool klasörüne kurulur.

1. Ortak istemci testleri ve yayın dosya sınırı/manifest/feed kontrolleri çalışır.
2. Tek self-contained publish üretilir; aynı uygulamanın gerçek WinUI penceresi açılışta ve 15 saniye sonra kontrol edilir.
3. Bu çıktıdan ZIP, gerçek Setup, full paket ve feed üretilir. Velopack giriş kancasının kontrolü atlanmaz.
4. **Dağıtılacak Setup** sessizce runner'ın geçici klasörüne kurulur; kurulu manifest ve gerçek WinUI açılışı sınanır.
5. Aynı binary ile yalnız paket metadata sürümü bir sonraki yamaya artırılmış yerel test paketi üretilir. Şu anki ürün için bu `2.4.1 → 2.4.2` olur. Kurulu uygulama gerçekten kontrol eder, indirir, uygular ve yeniden başlar; yeni process sürüm raporu, kurulu manifest ve güncellenmiş WinUI açılışı doğrulanır.
6. Yalnız üretim Setup/full paket/feed/ZIP ve özet dosyaları artifact'a yüklenir. **Sentetik 2.4.2 paketi yüklenmez; ürün veya assembly sürümü değişmez.**

Kurulum/güncelleme testi yalnız `GITHUB_ACTIONS=true` ve `RUNNER_ENVIRONMENT=github-hosted` ortamında çalışabilir. Geçici yollar mutlak kökleri ve reparse/junction içermemeleriyle kontrol edilir; temizlik yalnız bu koşunun kurulum klasörünü ve oradan çalışan süreçleri hedefler. Yerel Windows kullanıcısına kurulum yapmaz.

Açılış testleri `KASA_API_URL=http://127.0.0.1:9/` kullanır; gerçek API'ye finans isteği göndermez. Bu kontrol kullanıcı girişini, tüm ekranları veya yayımlanmış GitHub feed'inden gerçek ağ indirmesini kanıtlamaz. Yeni değişikliğin bulut koşusu geçmeden “kurulum/güncelleme çalıştı” sonucu çıkarılmaz.

## Bütünlük ve kabul

`SHA256SUMS.txt` artifact'taki üretim dosyalarının özetlerini içerir. Örneğin indirdiğin gerçek Setup dosyası için:

```powershell
Get-FileHash -LiteralPath .\EmarKasa-win-Setup.exe -Algorithm SHA256
```

Gerçek dosya adını kullan ve aynı isimli satırdaki SHA-256 ile karşılaştır. Artifact kökündeki `build.json` kaynak commit ve kurulum/güncelleme doğrulama sonuçlarını kaydeder. ZIP içindeki `build.json`, ZIP oluşturulmadan önceki publish açılış sonucunu içerir.

EXE ve Setup bu kişisel dağıtımda Authenticode ile imzalanmaz. Windows internetten indirilen imzasız pakette SmartScreen uyarısı gösterebilir. Kendi GitHub artifact/yayının ve checksum eşleşmesini kontrol et. Bu akış ayrıca kod imzalama sertifikası gerektirmez.

İlk kullanım kabulü için temiz Windows üzerinde Setup, açılış, HTTPS bağlantısı, giriş, PDF seçimi ve finans önizleme/kayıt akışını dene. İlk gerçek yüksek sürüm yayımlandığında kurulu uygulamadan yayımlanmış GitHub feed'i üzerinden indirme/yeniden başlatmayı da dene.

## Yerel Windows publish

.NET 10 SDK ve MAUI Windows workload kurulu Windows makinede depo kökünden:

```powershell
dotnet workload install maui-windows --version 10.0.401.1
dotnet publish Kasa.App/Kasa.App.csproj -c Release -f net10.0-windows10.0.19041.0 `
    -r win-x64 -p:RuntimeIdentifierOverride=win-x64 -p:WindowsPackageType=None `
    -p:SelfContained=true -p:WindowsAppSDKSelfContained=true -p:PublishSingleFile=false
```

Çıktı `Kasa.App/bin/Release/net10.0-windows10.0.19041.0/win-x64/publish/` altındadır. Yalnız EXE değil klasörün tamamı gerekir. Taşınabilir `win-x64` RID kullanılır. [Microsoft paketsiz MAUI yayını](https://learn.microsoft.com/en-us/dotnet/maui/windows/deployment/publish-unpackaged-cli?view=net-maui-10.0) dağıtım özelliklerini açıklar. [windows-release.ps1](../../.github/scripts/windows-release.ps1) kurulum testi içerdiği için yerel makinede çalıştırılmaz.

Velopack [Windows kurulum rehberi](https://docs.velopack.io/packaging/operating-systems/windows) ve [CLI referansı](https://docs.velopack.io/reference/cli/content/vpk-windows) paketlemeyi açıklar.

## API adresi

Varsayılan API [ApiAdresi.cs](../../Kasa.ApiClient/ApiAdresi.cs) içinde `https://kasa.emarglobal.com/` olarak tanımlıdır. Başka ortam için `KASA_API_URL` uygulama açılmadan önce ayarlanır; çalışan uygulama yeniden başlatılmalıdır. HTTPS veya yalnız yerel döngü HTTP kabul edilir:

```powershell
$env:KASA_API_URL = 'http://localhost:5232/'
```

iOS imzalama/TestFlight adımları [iOS rehberindedir](ios-yayin.md).
