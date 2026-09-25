# Kredi kartları ve krediler — kullanıcı kurallarıyla uygulama planı

Durum: 23 Eylül 2026'da uygulandı ve kasa.emarglobal.com üzerinde 2.1.0 olarak yayına alındı. Kart/kredi takibi, web ve Windows ekranları, telefon/masaüstü Web Push ve uygulama içi bildirim merkezi eklendi. 480 otomatik test geçti; sunucu üzerindeki kopyada ve son yayın verisinde eski kayıtlar/rapor tutarları karşılaştırıldı, yedek geri açıldı. Cihazlara gerçek bildirim teslimi kullanıcı izin verdikten sonra test düğmesiyle sınanmalıdır. ERP12 kapsamı genişletilmedi. İşletim ve kurulum ayrıntıları: [Kasa 2.1](../deploy/kasa-2.1.md).

## Amaç ve kesinleşen kurallar

Amaç; hangi kartın/kredinin ne kadar ödemesi kaldığını, sıradaki ödeme tarihini ve bu ödemenin hangi kanala ait olduğunu genel kasa ile birlikte görmek.

Kullanıcının açıklaması ve iki zamanlama sorusuna verdiği yanıtlar önceki önerilerin yerine geçer:

1. **Kredi kartı:** Son ödeme tarihi geldiğinde otomatik kasa çıkışı yapılmaz. Kullanıcı ödeme kaydettiğinde, kaydedilen tarih ve tutarla genel kasa ve harcamaların ait olduğu kanal kasaları azalır. Son ödeme tarihi yaklaşan/geciken borcu belirler; ödeme kaydı için tarihin geçmesi şartı konmaz.
2. **Tek kanal kredisi:** Çekilen tutar hem genel kasaya hem ilgili kanalın kasasına yansır. Bunlar aynı paranın iki görünümüdür; genel kasaya iki kez giriş yapılmaz.
3. **Ortak kredi:** Çekilen tutar, krediyi kullanan kanallara eşit bölünür. Her aylık taksit de aynı kanallara eşit bölünür.
4. **Kredi taksitleri:** Kullanıcıdan ayrıca ödeme kaydı beklenmeden, taksit tarihinde genel kasadan ve kredinin kanal kasalarından otomatik düşer.
5. **Kart bildirimleri:** Hesap kesim tarihinde, son ödeme tarihine 3 gün kala ve son ödeme tarihinde gönderilir.
6. **Kredi bildirimleri:** Her taksidin ödeme tarihine 3 gün kala ve ödeme tarihinde gönderilir.
7. **Bildirim hedefleri:** Kullanıcının tercihi telefona bildirim + uygulama içi uyarı ve ayrıca Windows masaüstü bildirimidir. Bildirim hem kayıtlı telefona hem kayıtlı masaüstüne ulaşmalıdır.

Kartın ayrıntı düzeyi için ayrıca bir seçim gelmedi. Mevcut alışlardan harcama bağlantısı, ekstre, taksit ve ödeme takibi önerisi planda korunuyor. Bu belge plan aşamasındadır; yazılım ve canlı yayın değişikliği yapılmadı.

## Mevcut durum

- Kart kayıtları, kartla ilişkili harcamalar ve kart ödemeleri için veri/API temeli var; yönetim ekranları etkin menüde yok.
- Kart borcu açılış + harcama − ödeme olarak türetiliyor. Kalıcı ekstre ve harcama taksit planı yok. Kart ödemeleri şu anda ayrıca kasa gideri yaratmıyor.
- Kart harcamaları, gerçek banka ödeme tarihinden bağımsız olarak sonraki ay sonunda genel kasaya yansıyor.
- Kredinin çekimi genel kasaya ekleniyor; taksit tarihi gelince takvimden gider üretiliyor. Gerçek ödendi/kısmi/gecikmiş takibi tamamlanmış değil.
- Bu yüzden yalnız ekran açmak ya da yeni ödemeleri mevcut hesaplara eklemek yeterli değil: aynı çıkış iki kez sayılabilir. Kredinin mevcut gerçekleşme bayrağını değiştirmek de geçmiş sonuçları etkileyebilir.

## 1. Kredi Kartları ekranı

Kart listesinde kart/banka adı, limit, uygulamada hesaplanan toplam borç, kalan ekstre borcu ve sıradaki son ödeme tarihi gösterilir. Tam kart numarası, CVV veya banka giriş bilgisi istenmez.

Kart ayrıntısı:

- Açılış tarihi/borcu, hesap kesim günü; her ekstre için gerçek kesim ve son ödeme tarihi.
- Mevcut alış veya giderden bağlanan harcamalar; aynı alış tekrar girilmez. Tek çekim ve taksitli harcama desteklenir.
- Harcamanın kanal dağılımı alıştan gelir. Bağımsız harcamada kanal veya kanal payları seçilir. Belirsiz pay otomatik kanala yazılmaz; dağılım bekliyor olarak görünür.
- Taksitli harcamanın toplamı tek borçtur. Aylık taksitler aynı toplamı ikinci kez borca eklemez; hangi ekstrede hangi tutarın beklediğini belirler.
- Ekstre geçmişi: dönem borcu, banka ekstresinden girilen asgari ödeme, ödenen/kalan ve durum. Asgari ödeme oranı, faiz ve bankanın tatil günü uygulaması tahmin edilmez.
- Tam/kısmi ödeme, ödeme tarihi ve not/dekont. Bir ödeme bir veya birden fazla ekstreyi kapatabilir; dağılım kaydedilmeden önce görünür.
- Son ödeme tarihi geçse bile kaydedilmiş ödeme yoksa kasa değişmez; borç gecikmiş görünür. Erken ödeme kaydedilirse kasa, girilen gerçek ödeme tarihinde etkilenir.
- İade, banka faizi/ücreti ve düzeltme ayrı açıklamalı hareketler olur. Ödenmiş hareketi sessizce silmek yerine bağlı ödemeyi koruyan düzeltme/iptal kullanılır.
- Hareketi olan kart silinmez; yeni kullanıma kapatılarak geçmişi korunur.

Kartla mal alındığında alış açısından ödeme tamamlanabilir; buna karşılık kart borcu açık kalır. Arayüz bu iki durumu ayırır: “Kartla ödendi” ve “Kart borcu açık”.

## 2. Krediler ekranı

Kredi listesinde banka/kredi adı, çekim tarihi, başlangıç kredi tutarı, kalan planlı ödeme, kalan taksit sayısı ve sıradaki ödeme gösterilir.

Kredi ayrıntısı:

- Yeni çekilen kredi ile önceden çekilmiş mevcut krediyi ayrı ekleme akışı. Eski krediyi takibe almak yeniden kasa geliri yaratmaz.
- Yeni kredide çekilen tutar ve tarih kaydedilir; bu tutar genel kasaya ve krediyi kullanan kanal/kanallara aynı tarihte yansır. Kullanıcı farklı bir net giriş belirtirse kesinti ayrı açıklamalı giderdir; sistem kendiliğinden kesinti varsaymaz.
- İlk taksit tarihi açıkça girilir. Eşit taksit planı oluşturulabilir; bankanın verdiği farklı tutar/tarihler tek tek düzeltilebilir.
- Her taksit için tarih, tutar ve sabit kanal payları kaydedilir. Durumlar: bekliyor, kasaya işlendi, plan değişikliğiyle iptal edildi. Tarihi gelen taksit otomatik işler; uygulama o gün açılmasa da sonraki raporda yalnız bir kez hesaba katılır.
- Otomatik kasa etkisi bankadan ödeme doğrulaması değildir. Bu nedenle sırf taksit tarihi geçti diye “Banka ödemesi doğrulandı” denmez. İsteğe bağlı dekont/not eklemek ikinci kasa çıkışı oluşturmaz.
- Tek kanal veya “Ortak / eşit dağıtım” seçilir. Ortak kullanımda dahil kanallar görünür biçimde seçilir; seçili kanal kimlikleri ve payları krediye kaydedilir. Tüm kanallar için tek seçim kolaylığı verilebilir.
- Kredi başladıktan sonra kanal eklemek, pasifleştirmek, yeniden adlandırmak veya sırasını değiştirmek eski kredi paylarını değiştirmez. Yeni kanal ortak krediye kendiliğinden katılmaz. Borcu süren pasif kanalın takibi korunur.
- Eşit paylar kuruş hassasiyetinde saklanır. Bölünmeyen kuruşlar krediye kaydedilen sabit kanal sırasına göre dağıtılır; kanal payları toplamı girişe veya takside daima tam eşittir. Örneğin 100 TL üç kanala 33,34 + 33,33 + 33,33 TL bölünür. Tekrarlanan taksitlerde birikimli pay hesabıyla fazla kuruş aynı kanala sürekli yüklenmez; plan oluşturulurken hesaplanan paylar saklanır ve rapor sırasına göre değişmez.
- Taksit erteleme/tutar değişikliği gelecekteki planı açıklamayla revize eder. Kasaya işlenmiş taksit sessizce değiştirilmez; düzeltme öncesi kasa ve kanal farkı gösterilir.
- Erken kapama için bankanın bildirdiği kapama tutarı ve tarih girilir; kapama çıkışı bir kez oluşur, yerine geçtiği ileri taksitler iptal edilmiş olarak korunur. Uygulama kendiliğinden faiz/indirim hesaplamaz.
- “Kalan planlı ödeme” ile “kalan anapara” ayrı kavramlardır. Bankadan anapara/faiz ayrımı girilmediyse taksitlerin kalan toplamı anapara diye sunulmaz.
- Geçmişi bulunan kredi arşivlenir; silinerek geçmiş kasa etkisi kaldırılmaz.

## 3. Kasa ve kanal kuralları

Kartın elle ödeme, kredinin otomatik taksit modeli birlikte kullanılır:

| Olay | Genel kasaya etkisi | Kanal takibine etkisi |
| --- | --- | --- |
| Kartla alış/harcama | O anda nakit çıkışı olmaz | İlgili kanalın bekleyen kart yükü görünür |
| Kart ödemesi kaydı | Gerçek ödeme tarihinde bir kez azalır | Ödemenin onaylı kanal payları bir kez düşer |
| Kartın son ödeme tarihinin geçmesi | Ödeme kaydedilmediyse değişmez | Geciken kart yükü görünür; otomatik düşüm olmaz |
| Tek kanal için yeni kredi çekimi | Çekilen tutar kadar bir kez artar | Aynı tutar seçilen kanalın kasasına eklenir |
| Ortak yeni kredi çekimi | Çekilen tutar kadar bir kez artar | Tutar kredide seçilmiş kanallara eşit bölünür |
| Tek kanal kredisinin taksit tarihi | Taksit tutarı kadar otomatik azalır | Taksit aynı kanalın kasasından düşer |
| Ortak kredinin taksit tarihi | Taksit tutarı kadar otomatik azalır | Taksit kredideki aynı kanallara eşit bölünür |
| Otomatik kredi taksidine dekont/not ekleme | Yeni çıkış oluşmaz | Yeni düşüm oluşmaz |

Genel kasa girişi/çıkışı ile kanal payları aynı hareketin iki görünümüdür; birbirine eklenmez. Alış kaydı, kart borcu ve kart ödemesi arasında tek kaynak ilişkisi kurulur. Ödeme türü değişse veya aynı istek yeniden gönderilse bile yeni mükerrer gider oluşmaz. Otomatik kredi taksidi ve o taksidin kullanıcı notu/dekontu da tek hareketi temsil eder.

Kartta kısmi ödeme için önerilen önizleme: önce seçilen/eski açık ekstre; ekstre içinde kalan kanal payları oranında dağıtım. Editör kaydetmeden önce tutarları görür. Birikimli kuruş hesabı her ödemenin ve kapanan borcun toplamını korur. Onay bekleyen alış payı görünür biçimde dağılım bekliyor kalır. Açılış kart borcunun kanalı bilinmiyorsa tahmin edilmez; dağılımı geçiş ekranında belirlenir. Aynı tutar harcama tarihinde ve ödeme tarihinde ikinci kez kasa gideri sayılmaz.

Kart örneği: 12.000 TL kart harcamasının %60'ı MEZAT, %40'ı PERAKENDE olsun. Kullanıcı 2.000 TL ödeme kaydederse genel kasa 2.000 TL azalır; onaylanan harcama paylarına göre kanal payları 1.200 TL ve 800 TL olur. Kalan 10.000 TL kasa çıkışı değil, bekleyen kart borcudur. Kart harcamaları ortak krediler gibi otomatik eşit bölünmez.

Tek kanal kredi örneği: MEZAT için 120.000 TL kredi çekilirse genel kasa ve MEZAT kasası 120.000 TL artar. 12.000 TL taksidin günü gelince genel kasa ve MEZAT kasası 12.000 TL azalır.

Ortak kredi örneği: 120.000 TL kredi MEZAT, PERAKENDE ve TOPTAN için ortak çekilirse genel kasa 120.000 TL artar, her kanalın kasasına 40.000 TL eklenir. 12.000 TL aylık taksit tarihinde genel kasa 12.000 TL azalır; aynı üç kanalın her birinden 4.000 TL düşer.

Kart/kredi nakit hareketleri raporda ayrı türle gösterilir. Kredi girişine satış geliri, taksit toplamına işletme kârı gibi yanıltıcı adlar verilmez. Mevcut diğer gelir/gider kurallarının yeniden tasarlanması bu işin kapsamı değildir.

## 4. Ana sayfa ve erişim

- Menüye “Kredi Kartları” ve “Krediler” eklenir; web ve Windows aynı veriyi kullanır.
- Kasalar ekranında toplam kart borcu, kalan planlı kredi taksitleri ve yaklaşan/geciken kart ödemeleri özeti bulunur.
- 7/30 günlük kart ve kredi ödeme listesi gösterilir. Kartta “Ödeme kaydedilince düşer”, kredide “Taksit tarihinde otomatik düşer” bilgisi açıkça yazılır. Vadesi henüz gelmemiş kredi taksidi güncel kasadan düşmez.
- Bildirim zamanları kullanıcı tarafından kesinleştirildi; uygulama içindeki ödeme uyarılarına ek gönderim kanalı aşağıdaki bölümde ele alınır.
- Editör yönetir. İzleyici yalnız özet/detayları görür; alıcı banka/kart/kredi borcu ekranlarına erişmez. Alıcının kendi alışını oluşturma ve gönderme akışı korunur.

### Bildirim planı

| Kaynak | Bildirim zamanı | İçerik |
| --- | --- | --- |
| Kredi kartı ekstresi | Hesap kesim günü | Kartın hesap kesim gününün geldiği; ilgili dönem ve uygulamadaki borç özeti |
| Kredi kartı ekstresi | Son ödeme tarihinden 3 takvim günü önce | İlgili ekstrenin kalan tutarı ve son ödeme tarihi |
| Kredi kartı ekstresi | Son ödeme günü | Bugün ödenmesi gereken kalan ekstre tutarı |
| Kredi taksidi | Taksit tarihinden 3 takvim günü önce | Kredi adı, taksit numarası, tutar ve ödeme tarihi |
| Kredi taksidi | Taksit günü | Bugünkü taksit tutarı; kasa hesabına otomatik yansıdığı bilgisi |

Zamanlama kuralları:

- Tarihler İstanbul saat diliminde değerlendirilir. Varsayılan gönderim saati önerisi 09.00'dır; bu saat kullanıcı tarafından henüz seçilmedi ve ayarlardan değiştirilebilir olarak tasarlanır.
- Üç gün, üç takvim günüdür; ay/yıl geçişinde doğru tarihe gider. Bankanın girilmiş son ödeme/taksit tarihi esas alınır; hafta sonu nedeniyle kendiliğinden tarih değiştirilmez.
- Kesim günü bildirimi “Hesap kesim gününüz bugün” der. Bankadan doğrulanmış ekstre yokken “Bankanızın ekstresi kesinleşti” veya kesin banka borcu iddiası kurulmaz.
- Kartın ilgili ekstresi tamamen kapanmışsa o ekstre için henüz gönderilmemiş ödeme hatırlatmaları iptal edilir. Kısmi ödemede kalan tutar gönderim anında yeniden hesaplanır. Asgari ödeme yapılması tam kapanma sayılmaz; banka ödeme durumunu doğrulamadan hukuki/bankacılık anlamında gecikme iddiası kurulmaz, uygulamada “Son ödeme tarihi geçti, kayıtlı kalan borç var” bilgisi verilir. Bir sonraki ekstre ve kesim günü bildirimi bundan etkilenmez.
- Kartta gelecek taksit borcu olması, kapanmış eski ekstreye ödeme hatırlatması gönderme nedeni değildir. Bildirimler toplam kart borcuna değil doğru ekstre/döneme bağlanır.
- Kredinin taksit tarihinde otomatik kasadan düşmesi, bankaya ödeme onayı olarak yorumlanmaz; aynı günün bildirimi bu yüzden iptal edilmez. Kredi/taksit gerçekten erken kapatılmış veya plan revizyonuyla iptal edilmişse ilgili gelecek hatırlatmalar kaldırılır. Dekont/not ekleme ikinci kasa çıkışı üretmez.
- Ödeme/kesim tarihi değişirse henüz gönderilmemiş bildirimler yeni takvime taşınır; gönderilmiş olanların geçmişi korunur. Yeni kayıt geçmiş bir bildirim gününe denk geliyorsa geriye dönük eski hatırlatmalar peş peşe gönderilmez; bugünkü uygun bildirim ve uygulama içi durum gösterilir.
- Kart ödeme kaydı iptal edilip ekstre borcu yeniden açılırsa gelecekteki uygun hatırlatmalar yeniden değerlendirilir; geçmişte gönderilmiş bildirimler topluca tekrar gönderilmez.
- Kart kesim bildirimi, 3 gün öncesi hatırlatma ve son gün hatırlatması farklı olaylardır. Aynı güne denk gelen olaylar tek iletide açıkça birleştirilebilir; ayrı dönemlerin borçları birbirinin yerine geçmez.
- Kullanıcının istediği bu beş olay dışında kendiliğinden günlük gecikme bildirimi eklenmez; gecikme uygulamanın içinde görünmeye devam eder.

Gönderim ve güvenilirlik:

- Kesinleşen hedefler: telefona bildirim, Windows masaüstü sistem bildirimi ve uygulama içindeki bildirim merkezi. E-posta seçilmedi.
- Zamanı sunucu takip eder; uygulamanın ekranda açık olmasına bağlı bir zamanlayıcı kullanılmaz. Hedef, uygulama kapalıyken de kayıtlı telefon ve masaüstüne bildirim ulaştırmaktır. Her cihaz için bildirim izni, cihaz kaydı ve kapalı uygulamada teslim koşulları uygulama aşamasında seçilen teknolojiyle doğrulanır; yalnız açık sayfada çalışan bir uyarı bu gereksinimi karşılamaz. Bu planla henüz canlı bildirim servisi kurulmadı.
- Bildirim editörün seçtiği hedefe gider; alıcı hesaplarına kart/kredi borcu bildirimi gönderilmez. Bildirimden ilgili kart/ekstre veya kredi/taksit ayrıntısına gidilir ve oturum yetkisi kontrol edilir.
- Ayarlar'da telefon ve masaüstü için etkinlik/izin durumu ile test bildirimi bulunur. Kullanıcı tek bir cihazın bildirimini kapatabilir. Uygulama içi bildirim geçmişi ayrı tutulur; bir bildirimi okumak kart borcunu ödemez veya kredi taksidini değiştirmez.
- Her bildirim, kaynak dönem/taksit, olay türü, alıcı ve cihaz/gönderim kanalı ile kalıcı olarak kaydedilir. Aynı sunucu işi yeniden çalıştığında aynı olay tekrar üretilmez. Telefona teslim masaüstü teslimini engellemez; aynı bilgisayarda tarayıcı ve Windows uygulaması birlikte kuruluysa iki ayrı masaüstü uyarısı üretmemek için o cihazın tercih edilen teslim yolu kullanılır. Gönderim denemesi ve sonucu saklanır; sağlayıcının desteklediği tekrar koruması kullanılır. Ağ belirsizliğinde mutlak tek teslim garantisi iddia edilmez.
- Gönderimden hemen önce güncel tarih, kalan borç ve iptal durumu kontrol edilir. Sunucu kesintisi sonrası aynı güne ait kaçırılmış olay işlenir; geçmiş günlerin uyarıları topluca gönderilmez. Başarısız/teslimi belirsiz gönderim başarı diye gösterilmez.
- Bildirim göndermek kasa hareketi oluşturmaz ve borcu kapatmaz.

## 5. Geçmiş veriyi koruyan geçiş

1. Canlı veriyi değiştirmeden sunucu içinde tutarlı yedek ve ayrı deneme kopyası alınır; mevcut genel/kanal raporları başlangıç ölçümü olarak saklanır.
2. Geçiş tarihi seçilir. Mevcut kartların açık ekstreleri, ileri taksitleri, eski kartı belirsiz harcamalar ve kredilerin kalan taksitleri eşleştirilir. Belirsiz hareket kendiliğinden bir karta/kanala bağlanmaz.
3. Kartların eski ay sonu otomatik düşümleri ve yeni ödeme kayıtları eşleştirilir. Kredinin önceki genel kasa girişi ile yeni kanal payları ayrı kontrol edilir. Fark varsa editöre somut bir geçiş özeti gösterilir; farkı saklamak için açılış bakiyesi sessizce değiştirilmez.
4. Tarih öncesindeki raporlar eski kuralla korunur. Kartta eski otomatik düşüm ile yeni elle ödeme aynı tutarı ikinci kez düşürmez. Kredide eski takvim etkisi ve yeni kanal payları aynı taksidi çoğaltmaz. Kaynak başına hangi etkilerin zaten sayıldığı ve kalan yük açıkça tutulur; yalnız tarih filtresiyle yetinilmez.
5. Yeni takip için açılan mevcut kredi/kart borcu, yeni kredi girişi veya yeni alış/gider oluşturmaz. Geçmişte ödenmiş tutarları yeni ödeme diye tekrar kasadan düşürmez.
6. Yedekten geri yükleme ve geçmiş rapor eşitliği denemede doğrulanır. Yayından sonra yeni kayıt oluştuysa eski yedeğe otomatik dönülmez.

Mevcut kredinin genel kasaya zaten eklenmiş çekimi ikinci kez eklenmez. Geçmiş kanal bakiyelerinin yeni kurala geçirilmesi gerekiyorsa kanal bazında önerilen devir farkı ayrı gösterilir; kullanıcıya görünmeden eski raporlara kredi geliri eklenmez. Tarihsel krediye dair yeni paylar ve kalan plan sabit kaynak bağlantılarıyla kaydedilir.

## 6. Uygulama sırası

1. Kesinleşen karma zamanlama kuralı ve yukarıdaki örnekler kabul ölçütü yapılır; mevcut kayıtlar için geçiş önizlemesi hazırlanır.
2. Kart ekstresi/harcama taksitleri, elle kart ödemesi ve krediye ait sabit eşit kanal payları/otomatik taksitler eklenir. Mevcut kart/kredi kimlikleri korunur. Tekrar gönderme ve eşzamanlı düzenleme korumaları kullanılır; ayrı banka hesapları/transfer modülü açılmaz.
3. Kart ekranı ve mevcut alışlarla bağlantısı yapılır; sonra kredi ekranı ve ortak ödeme özeti tamamlanır.
4. Telefon, Windows masaüstü ve uygulama içi hedeflerle kartın üç, kredinin iki bildirim olayı uygulanır; izin/cihaz ayarı, test bildirimi, kalıcı bildirim kayıtları ve yeniden gönderim koruması tamamlanır.
5. Web ve Windows'ta form, yetki, kısmi ödeme, düzeltme, rapor ve bildirim davranışı doğrulanır.
6. Sunucuda veri geçişi provası, somut geçiş özeti ve kontrollü yayın yapılır. Bu planın hazırlanması uygulamayı geliştirme veya canlıya alma işlemi değildir.

## Kabul kontrolleri

- Son ödeme tarihi geçmiş kart ekstresi, kullanıcı ödeme kaydetmedikçe kasayı azaltmaz; gecikme görünür.
- Kredi taksidi kendi tarihinde otomatik genel/kanal kasasına yansır; gelecek taksit erken düşmez ve tarih geçtikten sonra her sayfa açılışında yeniden düşülmez.
- Kart alışının toplamı, taksitleri ve bankaya ödemesi ayrı ayrı aynı gideri üretmez.
- Kartta tam/kısmi ödeme ve ödeme iptali doğru borç/kasa sonucunu verir; çift tıklama/yeniden gönderme tek kayıttır. Kredi taksidine not/dekont eklemek kasa sonucunu değiştirmez.
- Çoklu kanal paylarının toplamı kuruşuna kadar ödeme tutarına eşittir; kanalı belirsiz kısım kaybolmaz.
- Yeni tek kanal kredi çekimi hem genel hem o kanalın kasasına aynı tutarı ekler; genel kasa toplamı iki kat olmaz.
- Ortak kredide giriş ve her taksidin eşit kanal payları kendi toplamlarını kuruşuna kadar korur. Sonraki kanal ekleme/ad/sıra/aktiflik değişikliği bu payları değiştirmez.
- Geçmiş kredi ekleme ikinci kez kredi geliri oluşturmaz. Genel kasaya zaten sayılmış tutarın kanallara taşınması görünür ve kontrollüdür.
- Ay sonu, Şubat, yıl değişimi ve bankadan girilen farklı son ödeme tarihleri doğru işlenir.
- İade, fazla ödeme/alacak bakiyesi, faiz/ücret ve erken kapama açık hareketlerle açıklanabilir.
- Onay bekleyen alışın sonraki kanal onayı kasa çıkışını yeniden üretmez.
- Hareketli kart/kredi arşivlenince geçmiş rapor değişmez; eski istemcinin silme/düzeltme yolu da korumaları aşamaz.
- Eski bütün satırlar ve geçiş öncesi raporlar korunur; varsa gerçek kasa mutabakat farkı ayrı ve görünürdür.
- Alıcı finans ekranlarına erişemez; izleyici değişiklik yapamaz.
- Kart için kesim günü, son ödemeden 3 gün önce ve son ödeme günü; kredi için her taksitten 3 gün önce ve taksit günü doğru İstanbul tarihinde bildirim olayı oluşur.
- Kartın ödenmiş ekstresine yeni ödeme hatırlatması gitmez; kısmi ödeme sonrası yalnız kalan tutar bildirilir. Sonraki ekstrenin ve kesim gününün bildirimleri korunur.
- Kredinin otomatik kasa düşümü aynı günün bildirimini yanlışlıkla kapatmaz. İptal edilmiş/erken kapanmış ileri taksitlere hatırlatma gitmez.
- Tekrar çalışan görev ve sunucu yeniden başlatması aynı bildirim olayını yeniden oluşturmaz; tarih değişikliği eski bekleyen olayı geçersizleştirir.
- Bildirim başarısız olsa da kasa hesabı doğru çalışır; bildirimin teslimi veya açılması hiçbir tutarı yeniden kasaya işlemez.
- İzin verilmiş telefon ve Windows masaüstü, aynı olay için kendi bildirimlerini alır; birine gönderim diğerini susturmaz. Aynı cihazdaki tekrar kayıtları/iki istemci aynı hatırlatmayı çoğaltmaz.
- Uygulama kapalıyken telefon ve masaüstü teslimi gerçek cihazlarda sınanır. İzin kapalı veya cihaz erişilemiyorsa teslim edildi denmez; uygulama içi olay kaydı korunur.

## Bu aşamada kapsam dışı

Cari/tedarikçi hesapları, stok, fatura, ERP12 entegrasyonu, ayrı banka hesapları ve hesaplar arası transferler; banka bağlantısı/otomatik ödeme; kredi önerisi veya otomatik faiz hesabı. Kart ve kredi için ödeme özeti, bütün işletmenin kapsamlı nakit planı modülüne dönüştürülmez.
