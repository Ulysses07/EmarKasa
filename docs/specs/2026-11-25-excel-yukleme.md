# Excel ile hareket yükleme

Tarih: 2026-11-25 · Dal: `ozellik/excel-yukleme` · Kaynak: "KASA DÜZENLEMELER" madde 1 ve 2

İstek: "Örnek excel dosyası hatalı ise yüklerken bilgi versin, düzeltmeden işleme almasın." ve "PDF ve Excel ile yüklenen işlemleri ayırt
etmede daha detaylı baksın."
Karar (25.11.2026): hem bankadan inen Excel hem bizim örnek dosya yüklenir; hatalı dosya düzeltilmeden işleme alınmaz.

## Akış

Excel, PDF ile aynı ekrandan ve aynı uçtan (`POST /api/ekstre-aktar/yukle`) yüklenir; satırlar aynı Bekleyen/Kapanan ekranında görünür.
Hafıza, kişisel kural, önizleme, benzer kayıt uyarısı, yok sayma, eşleştirme ve iptal aynen çalışır.

- Biçimler: `.xlsx` ve `.xls`. Dosya türü uzantıdan değil içerikten anlaşılır. "`.xls`" adlı web sayfası (HTML) kabul edilmez; Excel'de
  açıp `.xlsx` olarak kaydetmesi söylenir. Parola korumalı dosya reddedilir.
- Sınırlar: 10 MB, açılmış hâli 100 MB, 5.000 dolu satır, 60 sütun, 1.500 hareket.
- Dosya belge deposunda saklanır; indirme ve "yeniden oku" PDF'teki gibi çalışır. Veritabanı değişikliği yoktur.
- Örnek dosya: `GET /api/ekstre-aktar/sablon` (Editör). "Örnek Excel'i indir" düğmesi.

## Tablonun bulunması

Her sayfanın ilk 60 satırında başlık aranır: bir **tarih**, bir **açıklama** ve tutar (**Giriş** ve **Çıkış** ya da **Tutar**) sütunu.
Başlık adları Türkçe harf, büyük/küçük harf, noktalama ve parantezden bağımsız karşılaştırılır.

| Sütun | Tanınan başlıklar (özet) |
|---|---|
| Tarih | Tarih, İşlem tarihi, Hareket tarihi, Harcama tarihi (yoksa Valör) |
| Açıklama | Açıklama, İşlem açıklaması, Detay, İşyeri… (İşlem türü varsa başına eklenir) |
| Giriş | Giriş, Alacak, Yatan, Gelen |
| Çıkış | Çıkış, Borç, Çekilen, Giden, Harcama |
| Tutar | Tutar, İşlem tutarı, Meblağ |
| Yön | B/A, Borç/Alacak, Yön |
| Bakiye | Bakiye, Kalan bakiye, İşlem sonrası bakiye |
| Para birimi | Para birimi, Döviz, Döviz cinsi |

Başlık bulunamazsa, birden çok sayfada hareket tablosu varsa ya da aynı sütun iki kez varsa dosya reddedilir; ileti ne bulunduğunu söyler.
Okunan belgede hangi sütunun ne olarak okunduğu belge uyarısında yazar ("Sütunlar: Tarih A, Açıklama C, Çıkış D, Giriş E").

## Hatalı dosya işleme alınmaz

Hareket satırlarında tek bir hata bile varsa **hiçbir şey kaydedilmez** ve hatalar Excel satır numarasıyla birlikte döner (422, en çok 50 satır):

- tarih boş, okunamıyor ya da 2000–2100 dışında,
- tutar okunamıyor, sıfır, kuruştan küçük hane içeriyor ya da binlik/ondalık ayırıcısı belirsiz (`12,500`),
- Giriş ve Çıkış ikisi birden dolu ya da ikisi de boş,
- açıklama boş,
- hücrede Excel hatası (`#REF!`),
- örnek dosyanın "ÖRNEK" satırları silinmemiş,
- Giriş/Çıkış sütunları bakiyeyle çelişiyor (bağların çoğunda bakiye ters yönde değişiyor).

Tarihi olmayan toplam, devir ve bakiye satırları hareket değildir; "Atlanan satırlar"da görünür.

## Giriş mi çıkış mı

1. Ayrı Giriş/Çıkış (Borç/Alacak) sütunu ya da Yön sütunu: kesin (`Sutun`).
2. Tek Tutar sütununda eksi varsa: bankada eksi çıkış, artı giriş (`Isaret`). Kartta eksi/artının anlamı ödeme, iade ve faiz
   satırlarından çıkarılır; çıkarılamazsa satır belirsiz kalır.
3. Hepsi artıysa: bakiye sütunundan (`Bakiye`), yoksa açıklamadaki tek anlamlı sözcükten (`Sozcuk`); o da yoksa belirsiz kalır.
4. Bakiye sütunu varsa her satır komşusuyla doğrulanır. Çelişen satırın yönü belirsiz bırakılır ve uyarı yazılır; tutmayan bakiyeler belge
   uyarısında Excel satır numarasıyla listelenir (eksik ya da fazladan satır olabilir).

Kart ekstresinde tür önerisi (harcama, ödeme, iade, indirim, taksit) PDF okuyucusuyla aynı kuraldır (`KartKarari`).

## Örnek dosya

"Hareketler" sayfası: `Tarih | Açıklama | Giriş (TL) | Çıkış (TL) | Bakiye (isteğe bağlı)`; iki örnek satır ("ÖRNEK: …"), tarih ve tutar
hücrelerinde Excel doğrulaması. "Nasıl doldurulur" sayfası kuralları anlatır. Kart ekstresinde Çıkış harcama, Giriş ödeme/iadedir.

## Bağımlılık

`ExcelDataReader` 3.9.0 (MIT, 16.06.2026; nuget.org'da en son kararlı sürüm, bilinen açık yok). `.xlsx`, `.xlsb` ve `.xls` okur. Okuma süreç
içindedir; zip açılımı, satır ve sütun sınırları önceden uygulanır.

## Kapsam dışı

CSV, HTML biçimli sahte `.xls`, döviz hareketi kaydı (satır görünür, kaydedilmez), Excel'de kanal/tür sütunu (hafıza ve toplu uygula var).

## Testler

Örnek dosyanın kendisi (silinmemiş örnekler reddedilir, doldurulunca okunur), başlık eşleme, iki sayfa, sütun çakışması, tarih ve tutar
biçimleri, hata listesi, yön kuralları, bakiye doğrulama ve çelişki, özet satırları, kart, döviz, `.xls`, HTML, parola, bozuk zip, zip bombası,
indirme türü, yeniden okuma, yetki; istemcide dosya seçimi, çok satırlı hata kutusu, şablon indirme.
