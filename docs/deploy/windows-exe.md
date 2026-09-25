# Emar Kasa — Windows Paketsiz `.exe` Derleme ve Dağıtım Kılavuzu

## Amaç

Emar Kasa Windows sürümünü Microsoft Store veya MSIX paketi olmadan doğrudan
`.exe` olarak derleyip iç kullanım ortaklarına dağıtmak.

---

## Ön Koşullar

| Gereksinim | Komut |
|---|---|
| .NET 10 SDK | `dotnet --version` → `10.x.x` |
| MAUI workload | `dotnet workload install maui` |
| Windows 10 (19041+) veya Windows 11 | — |

---

## Derleme Komutu

Repo kökünden çalıştırılır:

```powershell
dotnet publish Kasa.App/Kasa.App.csproj -c Release -f net10.0-windows10.0.19041.0 -p:WindowsPackageType=None
```

> `WindowsPackageType=None` projede zaten varsayılan olarak ayarlı; komut
> satırından tekrar vermek gerekli değildir, ancak açıklık için eklendi.

Başarılı çıktının son satırı:

```
Kasa.App -> ...\bin\Release\net10.0-windows10.0.19041.0\win-x64\publish\
```

---

## Çıktı Konumu

```
Kasa.App\bin\Release\net10.0-windows10.0.19041.0\win-x64\publish\
```

Ana yürütülebilir dosya: `Kasa.App.exe`

---

## Dağıtım

1. Yukarıdaki `publish\` klasörünün **tamamını** zip'le (yalnız `.exe` değil;
   bağımlı DLL'ler, `runtimeconfig.json`, `deps.json` vb. gereklidir).
2. Zip'i ortaklara ilet.
3. Kullanıcı zip'i bir klasöre açar ve `Kasa.App.exe`'ye çift tıklar.

> **SmartScreen uyarısı:** Uygulama imzasız olduğundan Windows ilk
> çalıştırmada "Windows bilgisayarınızı korudu" uyarısı gösterebilir.
> "Daha fazla bilgi" → "Yine de çalıştır" adımlarını izle.

---

## İmza (Opsiyonel)

İmzasız dağıtım iç kullanım için yeterlidir. İleride SmartScreen uyarısını
kaldırmak gerekirse bir **Authenticode kod imzalama sertifikası** (EV veya
OV, yetkili CA'dan) edinilebilir ve şu komutla uygulanabilir:

```powershell
signtool sign /fd SHA256 /tr http://timestamp.digicert.com /td SHA256 `
  /f sertifika.pfx /p <parola> Kasa.App.exe
```

---

## API Adresi

Varsayılan API adresi `https://kasa.emarglobal.com/` olup `Kasa.ApiClient/ApiAdresi.cs` içinde tanımlıdır.

**`MauiProgram.cs`** içinde:

```csharp
BaseAddress = ApiAdresi.Coz(Environment.GetEnvironmentVariable("KASA_API_URL")),
```

`KASA_API_URL` boşsa varsayılan adres kullanılır. Başka bir ortama bağlanmak için
uygulamayı başlatmadan önce bu değişkeni ayarlayın; çalışan süreç yeniden
başlatılmadan yeni değer okunmaz. HTTPS adresleri ve yalnız yerel döngü
adreslerindeki HTTP kabul edilir. Örneğin yerel geliştirmede
`$env:KASA_API_URL = 'http://localhost:5232/'` kullanılabilir. Dağıtım öncesinde
varsayılan adresin DNS ve HTTPS erişimini doğrulayın.

---

## Hızlı Başvuru

| Alan | Değer |
|---|---|
| TFM | `net10.0-windows10.0.19041.0` |
| RID | `win-x64` (publish varsayılanı) |
| ApplicationId | `com.royalmezat.kasa` |
| ApplicationTitle | `Emar Kasa` |
| Paket tipi | Paketsiz (`WindowsPackageType=None`) |
| API sunucusu | `https://kasa.emarglobal.com/` |
