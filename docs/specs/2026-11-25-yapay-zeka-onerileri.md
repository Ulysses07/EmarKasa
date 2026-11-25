# Yapay zekâ önerileri

Tarih: 2026-11-25 · Dal: `ozellik/yapay-zeka` · Kaynak: "KASA DÜZENLEMELER" madde 7

İstek: "Hesap hareketleri ve kart ekstrelerini ayırmak ve düzenlemek için bir yapay zekâ entegre edelim, ucuz ama iyi bir model lazım."
Kararlar (25.11.2026): satırlar **maskeli** gönderilir (IBAN, kart no, kişi adları gizli); yalnız hafızanın tanımadığı satırlar, **öneri** olarak;
önce örneklerle ölçüm; aylık harcama sınırı; model Claude Haiku 5.5.

Bu belge `docs/ekstre-kurallari.md`'deki "yapay zekâ kullanmaz" ve `docs/deploy/kasa-2.3.md`'deki "belge yapay zekâya gönderilmez" kararlarını şöyle
değiştirir: **belge (PDF/Excel) yine gönderilmez.** Özellik kapalı gelir; sunucuya anahtar girilip açılırsa kullanıcı düğmeye bastığında yalnız aşağıdaki
maskeli metin gönderilir.

## Ne yapar

Kişisel kuralın ve işlem hafızasının tanımadığı, bekleyen TL satırları için tür (gelir, gider, kart harcaması, iade, karta ödeme, "kasa hareketi değil"),
kanal dağılımı, kısa kategori ve düzenlenmiş açıklama önerir. Öneri satırı doldurur; **satırı seçmez, "hazır" saymaz, toplu işaretlemeye girmez, kaydetmez.**
Kullanıcı satırı tek tek işaretler, önizler ve onaylar. Kaydedilen satır hafızaya geçer; aynı hareket bir daha yapay zekâya sorulmaz.

- Öncelik: kişisel kural → hafıza → yapay zekâ → okuyucunun önerisi. Elle değiştirilen satır ezilmez.
- Yönü okunamayan satırda tür doldurulmaz, öneri yalnız yazı olarak görünür. Belgeden okunan yönle çelişen öneri atılır.
- "Kasa hareketi değil" (virman, vadeli hesap, bloke) yalnız yazıdır; yok sayma kararını kullanıcı verir.
- Karta ödemede kart, iadede kaynak harcama, çok kanallı özel tutar önerilmez.
- ERP12 belgesinde kullanılmaz (eşleşme kuralı kesindir, satırlar ad ağırlıklıdır).

## Maskeleme

İzin listesi: yalnız koddaki sözlükte bulunan sözcükler olduğu gibi gider (bankacılık terimleri, sektör ve gider sözcükleri, vergi/fatura, ay adları, herkesçe
bilinen büyük markalar). **Listede olmayan her sözcük** (kişi adı, soyadı, firma adı, şehir, adres, kullanıcı adı) `[1]`, `[2]` gibi numaraya dönüşür; aynı
sözcük aynı istekte aynı numaradır. Rakam içeren her sözcük (IBAN, kart, hesap, telefon, TC/vergi no, referans, tarih) `#` olur. Adıyla kişi adı karışabilen
markalar yalnız ikili geçtiğinde açıktır ("ARAS KARGO" açık, "ARAS YILMAZ" kapalı). Sözlükte yaygın kişi adı ve soyadı bulunmadığı testle denetlenir.

| Belgedeki satır | Gönderilen |
|---|---|
| `03.11.2026 FAST AHMET YILMAZ KASIM KİRA TR12 0006 … −5.000,00` | `CIKIS · 1.000–10.000 · FAST [1] [2] KASIM KIRA #` |
| `SHELL PETROL 4521 KADIKÖY` (kart) | `CIKIS · 1.000–10.000 · SHELL PETROL # [3]` |

Tarih, kesin tutar, bakiye, banka, hesap adı, kart adı, dosya adı, belge ve kullanıcı bilgisi **gönderilmez**; tutar yalnız aralıktır. Kanal adı da aynı
maskeden geçer. Geçmiş kararlardan en çok 60 örnek (aynı maskeyle) eklenir. Gönderilecek metin gönderilmeden önce ekranda görülebilir.
Açıklamadaki yazı izin listesinden geçtiği için belgeye yazılmış bir talimat modele ulaşamaz. Yanıttaki yer tutucular sunucuda gerçek sözcüğe çevrilir.

## Anthropic tarafı

API'ye gönderilen veri izinsiz model eğitiminde kullanılmaz; saklama için platform belgesi varsayılan saklama olmadığını, ticari saklama sayfası ise en çok
30 gün içinde silindiğini yazar (işaretlenen içerikte daha uzun): [API ve veri saklama](https://platform.claude.com/docs/en/manage-claude/api-and-data-retention),
[saklama süresi](https://privacy.claude.com/en/articles/7996866-how-long-do-you-store-my-organization-s-data). Fiyat (25.11.2026,
[resmî sayfa](https://platform.claude.com/docs/en/about-claude/pricing)): Haiku 5.5 girdi 0,10 $, çıktı 0,50 $ / milyon jeton (100.000 jetona kadar istek).
60 satırlık bir soru yaklaşık 5.000 girdi + 4.000 çıktı jetonu, **0,0025 $** tutar.

## Harcama sınırı

- Aylık sınır `YapayZeka:AylikSinirUsd` (varsayılan **2 $**). Her çağrıdan önce en yüksek olası maliyet ayrılır; ayın harcaması + ayrılan sınırı aşacaksa
  çağrı yapılmaz. Yanıt gelince gerçek maliyet yazılır; bağlantı kesilip sonuç bilinmiyorsa ayrılan tutar harcanmış sayılır.
- Aynı anda tek çağrı; bir çağrıda en çok 60 farklı satır. Aynı istek kimliği ikinci kez ücret doğurmaz.
- Anthropic Console'da ayrıca harcama sınırı konmalıdır.

## Ölçüm

**Ölç** düğmesi geçmiş kararların yarısını gizleyip (örnek verilmeden) sorar, kullanıcının kararıyla karşılaştırır: tür, dağılım ve yüksek güvenli tür
doğruluğu ile maliyet. En az 10 farklı karar gerekir. Ayrıca gerçek kullanımda önerisi kaydedilmiş satırların kaçının aynı kaydedildiği gösterilir.

## Açma / kapama

Sunucuda `deploy/.env`: `KASA_YAPAY_ZEKA_ETKIN=true`, `KASA_YAPAY_ZEKA_ANAHTAR=…`, `KASA_YAPAY_ZEKA_AYLIK_SINIR_USD=2`. Anahtar yalnız sunucuda durur; istemciye,
günlüğe ve veritabanına (yedeğine) yazılmaz. Adımlar: [docs/deploy/yapay-zeka.md](../deploy/yapay-zeka.md). Kapalıyken hiçbir dış bağlantı kurulmaz.

## Uçlar ve veri

Editör: `GET /api/ekstre-aktar/yapay-zeka` (durum, ay harcaması, doğruluk, son ölçüm), `GET /api/ekstre-aktar/{id}/yapay-zeka` (gönderilecek maskeli metin),
`POST /api/ekstre-aktar/{id}/yapay-zeka` (sor), `POST /api/ekstre-aktar/yapay-zeka/olcum`. Öneri mevcut `oneriler` yanıtında `YapayZekaOnerisi` durumuyla gelir.
Tablolar `YapayZekaCagrilar` (maliyet, jeton, durum; metin saklanmaz) ve `YapayZekaOneriler` (migration `20261125000400_YapayZeka`). Belge yeniden okununca
önerileri silinir. SDK: resmî `Anthropic` NuGet paketi.

## Kapsam dışı

Kendiliğinden (düğmesiz) çağrı, kendiliğinden kayıt, PDF/Excel'in gönderilmesi, OCR, ERP12 satırları, sohbet, web arayüzü.

## Testler

Maske (IBAN, kart, ad, marka, yer tutucu, geri çevirme, ad listesi), kapalıyken çağrı yok, sınır ve ayırma, tekrar istek, yanıt doğrulama (yön, tür, kanal, satır),
yarım/ret/hata yanıtları, öncelik, yeniden okuma, doğruluk ve ölçüm, SDK isteği (model, şema, başlık; sahte HTTP), istemci doldurma ve ekran.
Gerçek API çağrısı testte yapılmaz.
