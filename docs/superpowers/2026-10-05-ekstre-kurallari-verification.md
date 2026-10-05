# Kişisel ekstre kuralları — uygulama ve doğrulama

Plan: [Ekstre sınıflandırma kuralları](plans/2026-10-05-ekstre-kurallari.md)

Kullanım: [Kişisel ekstre kuralları](../ekstre-kurallari.md)

## Uygulanan kapsam

- Yapay zekâ, dış servis ve yeni paket olmadan açıklama, belge türü, banka ve yön koşullarıyla kalıcı kurallar.
- Türkçe harf/büyük-küçük harf/noktalama sadeleştirmesi ve sözcük sınırıyla eşleştirme; farklı hedeflerde çelişki gösterimi.
- Mevcut işlem türü ve genel kasa/eşit kanal dağılımı için öneriler. Yeni kategori raporu veya OCR eklenmedi.
- Web, telefona uyumlu tam web görünümü ve Windows'ta yönetim, öneri uygulama ve “Bu seçimi hatırla”.
- Toplu uygulamada elle değiştirilen satırları koruma; mali kayıt için mevcut seçim, önizleme ve onay.
- Boş başlangıç tablosu, sürümlü düzenleme, tekrar istek koruması, tam SQLite yedeği ve gerçek geri yükleme testi.
- Eski sunucu veya geçici kural bağlantı hatasında elle PDF işleme akışının kullanılabilmesi.

## Doğrulama sonuçları

Son işlev düzeltmesinden sonra aşağıdaki kontroller geçti. .NET testleri yerelde Debug yapılandırmasında çalıştı; GitHub CI ayrıca Release kontrollerini yürütür.

| Kontrol | Sonuç |
| --- | --- |
| `Kasa.Api.Tests` tamamı | 1.336 / 1.336 |
| `Kasa.App.Core.Tests` tamamı | 1.258 / 1.258 |
| `Kasa.ApiClient.Tests` tamamı | 208 / 208 |
| `Kasa.Sozlesme.Tests` tamamı | 60 / 60 |
| `Kasa.Core.Tests` tamamı | 133 / 133 |
| Tüm Node arayüz testleri | 272 / 272 |
| Özelliğin Playwright kontrolleri | 3 görünüm + 2 kurulum testi, 5 / 5 |
| Web ESLint / Prettier | Geçti |
| Windows .NET 10 derlemesi | 0 hata, 0 uyarı |
| MAUI kaynak/satır denetimi | Geçti; 545 kaynak başvurusu, 0 tanımsız kaynak, taban artırılmadı |
| C# boşluk biçimi | Geçti |

Tarayıcı kontrolleri 1.280 px masaüstü ve 360/375 px telefon genişliklerini kapsar. Kural oluşturma/silme gerçek API ile doğrulandı; PDF'den okunmuş belge ve öneriler test verisiyle sağlandı. Düzenleme API davranışı ayrıca HTTP testleriyle doğrulandı. Satır uygulaması mali kayıt isteği göndermedi. Erişilebilirlik ve yatay taşma kontrolleri geçti. Bu, bütün eski tarayıcı senaryolarının veya Linux ekran görüntüsü karşılaştırmalarının yerelde yeniden çalıştırıldığı anlamına gelmez.

Temel komutlar:

```text
dotnet test Kasa.Api.Tests/Kasa.Api.Tests.csproj --no-build --no-restore
dotnet test Kasa.App.Core.Tests/Kasa.App.Core.Tests.csproj --no-restore
dotnet test Kasa.ApiClient.Tests/Kasa.ApiClient.Tests.csproj --no-restore
dotnet test Kasa.Sozlesme.Tests/Kasa.Sozlesme.Tests.csproj --no-restore
dotnet test Kasa.Core.Tests/Kasa.Core.Tests.csproj --no-restore
node --test Kasa.Api.Ui.Tests/*.test.mjs
node node_modules/playwright/cli.js test --grep 'kişisel kural formu'
dotnet build Kasa.App/Kasa.App.csproj --no-restore -f net10.0-windows10.0.19041.0
bash .github/scripts/maui-lint.sh
dotnet format whitespace --folder . --exclude '**/bin/' '**/obj/' --verify-no-changes
```

Playwright komutu `Kasa.Ui.E2E` klasöründen çalıştırıldı.

## Bağımsız inceleme ve düzeltme

`a5118874f4ed9e9d33f79c23987808c85f1bb279` sürümü `dd4f1de` tabanına, plana ve tasarıma karşı bir kez bağımsız incelendi. Kritik bulgu yoktu. Tek P2 bulgu, pasif veya silinmiş kanala bağlı mevcut kuralın kapatılamaması ve Windows formunun bu seçili kanalları kaybetmesiydi. Küçük bulgu ertelenmedi.

Sorun önce iki API ve iki Windows form modeli testinde yeniden üretildi: kapatma isteği 200 yerine 400 döndü; seçili kanal kimliği formdan kayboldu. Düzeltme sonrası dört durum geçti. Mevcut kural eski hedeflerini koruyarak kapatılabiliyor. Yeni kural, hedef değiştirme veya yeniden etkinleştirme geçerli aktif kanal gerektiriyor. Windows formu kullanılamayan seçili hedefi açık etiketle koruyor. Yukarıdaki bütün işlev testleri bu düzeltmeden sonra geçti.

İlk kaynak denetimi dört C# dosyasında biçim ve Windows formunda üç uzun satır buldu. Bu bulgular yalnız boşluk/satır düzeni değişiklikleriyle giderildi. Son C# biçim ve MAUI kaynak denetimleri geçti; denetim tabanı gevşetilmedi.

## Doğrulama sınırları ve kararlar

- **Windows canlı kullanım:** Bağımsız incelemeci canlı pencere etkileşimi, odak ve yerleşimi değerlendirmedi. Derleme ve ortak form davranışı testlerini kabul ettik; canlı kullanım doğrulaması iddiası yok. Beklenmeyen bir odak, yerleşim veya olay davranışı gerçek Windows kullanımında ek düzeltme gerektirebilir.
- **Gerçek banka PDF doğruluğu:** Temsili kullanıcı PDF'leri verilmediği için tüm bankalar veya kişisel sınıflandırma doğruluğu hakkında yüzde belirlenmedi. Elle seçim/önizleme korundu. Desteklenmeyen bir PDF düzeni veya fazla geniş kişisel ifade için PDF okuyucu/kural düzeltmesi gerekebilir.

[PR #54](https://github.com/Ulysses07/EmarKasa/pull/54), `release/2.x` dalına açıldı ve sohbete bağlandı. Bu rapor yerel doğrulamayı kaydeder; GitHub CI sonuçları PR üzerinde ayrıca görülebilir. Sürüm değişikliği, birleştirme ve dağıtım bu çalışma kapsamında yapılmadı.
