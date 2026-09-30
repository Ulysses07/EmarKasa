"""Kasa yedeğini doğrular ve yeni bir SQLite dosyasına geri açar.

Canlı dosyanın üzerine yazmaz. Örnek:
  python3 restore_backup.py kasa-oto-....zip --output /safe/path/recovered.db --belge-aynasi /yedekler/belgeler
Uygulamayı durdurup doğrulanmış dosyayı devreye almak ayrı dağıtım adımıdır.

Belgeler (2.4 ve sonrası, manifest 2.2.0): alış belgeleri ve ekstre PDF'leri veritabanında değil uygulamanın belge
deposundadır (varsayılan: veritabanı klasörü/belgeler; compose'da /data/belgeler). kasa.db yalnız içerik özetlerini
tutar; ZIP'teki belgeler.json yedek anındaki özet listesidir. İçeriklerin kaynağı:
  - elle indirilen yedek (Ayarlar > Şimdi yedek indir): ZIP'in içindeki belgeler/<özet> girdileri (kendi kendine yeterli),
  - sunucudaki otomatik/elle yedek: yedek aynası, --belge-aynasi <Yedek:Dizin>/belgeler (compose'da /yedekler/belgeler).
Araç her içeriğin SHA-256 özetini ve boyutunu doğrular ve çıktının yanına belgeler/<ab>/<özet> olarak açar; kasa.db ile
birlikte bu belgeler/ klasörünü de veri dizinine taşıyın. Yalnız veritabanını açmak için --belgesiz (belgeler indirilemez).
Eski yedekler (2.0.0, 2.1.0) belgeleri kasa.db içinde taşır; aynen açılır.

Sunucudaki yedek adları türü taşır; araç her adı kabul eder (indirilen dosya yeniden adlandırılmış olabilir):
  kasa-oto-YYYYMMDD-HHMMSS-xxxxxxxx.zip   günlük otomatik yedek (zaman UTC)
  kasa-elle-YYYYMMDD-HHMMSS-xxxxxxxx.zip  Ayarlar'dan alınan elle yedek
  kasa-YYYYMMDD-HHMMSS-xxxxxxxx.zip       2.3 ve öncesi; otomatik sayılır
  kasa-goc-oncesi-YYYYMMDD-HHMMSS-xxxxxxxx.zip  açılışta, bekleyen migration/veri adımından önce alınan
                                          göç öncesi yedek (xxxxxxxx: kopyanın SHA-256 özetinin başı)
Sunucu otomatik yedeklerde son 30 günün hepsini ve son 12 takvim ayının (İstanbul) ilk yedeğini,
her durumda en yeni 7'sini tutar. Elle yedeklerden en yeni 10'u tutulur ve elle yedek otomatik yedek silmez.
Bu kalıba uymayan dosyalara (ör. kasa-oncesi-gecis.zip) rotasyon dokunmaz; göç öncesi yedekler de
rotasyon dışıdır (silinmez), gereksiz olanları operatör kaldırır.
Kalıcı geçmiş için yedekleri sunucu dışına da kopyalayın.

WAL: Uygulama veritabanını WAL günlük kipinde çalıştırır; kasa.db-wal ve kasa.db-shm veritabanının
parçasıdır. Uygulama çalışırken yalnız kasa.db kopyalanmaz (yedek SQLite yedekleme API'siyle alınır).
Araç geri açtığı dosyayı -wal/-shm gerektirmeyen tek dosya (geri alma günlüğü kipi) olarak yazar;
çıktının yanında eski -wal/-shm/-journal dosyası varsa reddeder, çünkü SQLite onları yeni dosyaya
uygulardı. Canlı dosyayı değiştirirken uygulamayı durdurun, eski kasa.db-wal ve kasa.db-shm dosyalarını
kasa.db ile birlikte kenara alın; uygulama ilk açılışta dosyayı yeniden WAL kipine alır.

Geri yükleme işareti: Geri yükleme veritabanındaki bütün durumu yedek anına sarar (editör şifresi ve kurtarma kodu,
izleyici şifresi, alıcı hesapları, oturum iptalleri, bildirim abonelikleri, kayıt numarası sayaçları, kayıt
sürümleri). Uygulamanın yedekleri SQLite başlığında geri yükleme işareti taşır (PRAGMA user_version =
GERI_YUKLEME_ISARETI; canlı dosyada 0); araç işaretsiz (bu sürümden önce alınmış) yedeği geri açarken işaretler.
Araç her yedekte (eski biçimler dahil) manifestteki yedek anını ('olusturuldu') GERI_YUKLEME_TABLOSU tablosuna
yazar: uygulama veritabanı dışındaki güvenlik günlüğünün (yedek dizininde guvenlik-gunlugu.jsonl) bu andan sonraki
olaylarını yeniden uygular. Uygulama işaretli dosyayla ilk açılışta, HTTP açılmadan: yeni oturum dönemi açar
(bütün oturumlar ve tanıdık cihazlar geçersiz), kurtarma kodunu, izleyici girişini ve cihaz bildirim kayıtlarını
kapatır, yedekten sonra değiştirilen editör şifresini geçersiz kılar (editör girişi kilitlenir; kilidi yalnız
KASA_EDITOR_SIFRE_SIFIRLA=true ile yeni KASA_EDITOR_SIFRE'ye sıfırlama açar),
yedekten sonra pasife alınan ya da şifresi değişen alıcıları pasif bırakır, kayıt numarası sayaçlarını
KIMLIK_ARALIGI ileri alır, işaretleri siler, raporunu Araçlar/Güvenlik ekranına ve değişiklik geçmişine
(GeriYuklemeIslendi) yazar. Yedeği ZIP'ten elle çıkarmayın; her zaman bu aracı kullanın.
ZORUNLU sonraki adımlar: operasyon-runbook.md "Geri yüklemeden sonra".

Doğrulama: python3 -m doctest restore_backup.py
"""
import argparse
from contextlib import closing
import hashlib
import json
import os
from pathlib import Path
import re
import sqlite3
import tempfile
import zipfile

# Kasa.Api/Auth/GeriYuklemeIsleyici.cs: Isaret, KimlikAraligi ve IsaretTablosu ile aynı olmalıdır (iki tarafta da sınanır).
GERI_YUKLEME_ISARETI = 0x4B534759
KIMLIK_ARALIGI = 1_000_000
GERI_YUKLEME_TABLOSU = "__KasaGeriYukleme"

_BELGE_OZETI = re.compile(r"^[0-9A-F]{64}$")
BELGE_LISTESI = "belgeler.json"
GOMULU_ONEK = "belgeler/"
# Uygulamanın tek belge sınırı 10 MB'dır; sınır yalnız bozuk/kötü amaçlı listeye karşıdır.
_AZAMI_BELGE = 64 * 1024 * 1024
_ESKI_ICERIKLER = (["kasa.db", "manifest.json"], [".kasa-push-keys.json", "kasa.db", "manifest.json"])
_DEPOLU_ICERIKLER = ([BELGE_LISTESI, "kasa.db", "manifest.json"], [".kasa-push-keys.json", BELGE_LISTESI, "kasa.db", "manifest.json"])

_YEDEK_ADI = re.compile(r"^kasa-(?:(oto|elle|goc-oncesi)-)?[0-9]{8}-[0-9]{6}-[0-9a-f]{8}\.zip$")
_TURLER = {"oto": "otomatik", "elle": "elle", "goc-oncesi": "goc-oncesi"}


def yedek_turu(name: str):
    """Sunucunun verdiği yedek adından türü döner; kalıba uymayan adlar için None.

    >>> yedek_turu("kasa-oto-20260927-030000-0a1b2c3d.zip")
    'otomatik'
    >>> yedek_turu("kasa-elle-20260927-101500-0a1b2c3d.zip")
    'elle'
    >>> yedek_turu("kasa-20260920-030000-0a1b2c3d.zip")  # eski ad
    'otomatik'
    >>> yedek_turu("kasa-goc-oncesi-20260928-060000-0a1b2c3d.zip")
    'goc-oncesi'
    >>> yedek_turu("kasa-yedek-2026-09-27.zip") is None
    True
    >>> yedek_turu("kasa-oncesi-gecis.zip") is None
    True
    """
    match = _YEDEK_ADI.match(name)
    if not match:
        return None
    return _TURLER.get(match.group(1), "otomatik")


_KALINTI_EKLERI = ("-wal", "-shm", "-journal")


def kalinti_dosyalari(path: Path):
    """Veritabanı yolunun yanında duran SQLite günlük dosyaları (-wal, -shm, -journal).

    >>> import tempfile
    >>> d = Path(tempfile.mkdtemp())
    >>> kalinti_dosyalari(d / "kasa.db")
    []
    >>> (d / "kasa.db-wal").write_bytes(b"eski")
    4
    >>> [p.name for p in kalinti_dosyalari(d / "kasa.db")]
    ['kasa.db-wal']
    """
    return [path.with_name(path.name + ek) for ek in _KALINTI_EKLERI if path.with_name(path.name + ek).exists()]


def wal_kipinde_mi(path: Path) -> bool:
    """SQLite başlığının 18-19. baytları 2 ise dosya WAL kipindedir ve -wal/-shm ile açılır.

    >>> import tempfile, sqlite3
    >>> d = Path(tempfile.mkdtemp())
    >>> with closing(sqlite3.connect(d / "wal.db")) as db:
    ...     _ = db.execute("PRAGMA journal_mode=WAL").fetchone(); _ = db.execute("CREATE TABLE t(x)"); db.commit()
    >>> wal_kipinde_mi(d / "wal.db")
    True
    >>> rollback_kipine_al(d / "wal.db"); wal_kipinde_mi(d / "wal.db"), kalinti_dosyalari(d / "wal.db")
    (False, [])
    >>> with closing(sqlite3.connect(d / "duz.db")) as db:
    ...     _ = db.execute("CREATE TABLE t(x)"); db.commit()
    >>> wal_kipinde_mi(d / "duz.db")
    False
    """
    with open(path, "rb") as f:
        header = f.read(100)
    return len(header) == 100 and header[:16] == b"SQLite format 3\x00" and 2 in (header[18], header[19])


def rollback_kipine_al(path: Path) -> None:
    """WAL başlıklı kopyayı tek dosyalık geri alma günlüğü kipine çevirir (içerik değişmez). Kopyanın yanında
    -wal olmadığından SQLite yalnız başlığı günceller; geçici -wal/-shm kapanışta silinir."""
    with closing(sqlite3.connect(path)) as db:
        if db.execute("PRAGMA journal_mode=DELETE").fetchone() != ("delete",):
            raise ValueError("Yedek kopyası tek dosya kipine çevrilemedi.")


def isaretle(path: Path, yedek_ani=None) -> bool:
    """Veritabanı başlığına geri yükleme işaretini (PRAGMA user_version) yazar; zaten işaretliyse başlığa dokunmaz.
    Uygulamanın yedekleri işaretlidir; işaretsiz olan bu sürümden önce alınmış yedektir. İşaret yazıldıysa True.
    yedek_ani (manifestteki 'olusturuldu' metni) verilirse GERI_YUKLEME_TABLOSU'na tek satır olarak yazılır (eski biçimli
    yedekler dahil): uygulama güvenlik günlüğünün bu andan sonraki olaylarını yeniden uygular, sonra tabloyu düşürür.
    Yazdıktan sonra SQLite hızlı bütünlük denetimi yapılır.

    >>> import tempfile, sqlite3
    >>> d = Path(tempfile.mkdtemp())
    >>> with closing(sqlite3.connect(d / "eski.db")) as db:
    ...     _ = db.execute("CREATE TABLE t(x)"); db.commit()
    >>> isaretle(d / "eski.db", "2026-09-27T03:00:00+00:00"), isaretle(d / "eski.db", "2026-09-28T03:00:00+00:00")
    (True, False)
    >>> with closing(sqlite3.connect(d / "eski.db")) as db:
    ...     db.execute("PRAGMA user_version").fetchone()[0] == GERI_YUKLEME_ISARETI, db.execute(
    ...         'SELECT "YedekZamani", "Arac" FROM "__KasaGeriYukleme"').fetchall()
    (True, [('2026-09-28T03:00:00+00:00', 'restore_backup.py')])
    """
    with closing(sqlite3.connect(path)) as db:
        yeni = db.execute("PRAGMA user_version").fetchone()[0] != GERI_YUKLEME_ISARETI
        if yeni:
            db.execute("PRAGMA user_version = {:d}".format(GERI_YUKLEME_ISARETI))
        if isinstance(yedek_ani, str) and 0 < len(yedek_ani) <= 64:
            db.execute('CREATE TABLE IF NOT EXISTS "{}" ("YedekZamani" TEXT NOT NULL, "Arac" TEXT NOT NULL)'.format(GERI_YUKLEME_TABLOSU))
            db.execute('DELETE FROM "{}"'.format(GERI_YUKLEME_TABLOSU))
            db.execute('INSERT INTO "{}" ("YedekZamani", "Arac") VALUES (?, ?)'.format(GERI_YUKLEME_TABLOSU), (yedek_ani, "restore_backup.py"))
        db.commit()
        if db.execute("PRAGMA quick_check").fetchall() != [("ok",)]:
            raise ValueError("İşaretlenen kopya SQLite bütünlük denetiminden geçmedi.")
        return yeni


def sonraki_adimlar() -> str:
    """Geri açılan dosyayı canlıya alan operatörün zorunlu adımları (runbook: "Geri yüklemeden sonra").

    >>> "YENİ bir izleyici şifresi" in sonraki_adimlar() and "1.000.000 ileri" in sonraki_adimlar()
    True
    >>> "KASA_EDITOR_SIFRE" in sonraki_adimlar() and "kurtarma kodu" in sonraki_adimlar()
    True
    >>> "KASA_EDITOR_SIFRE_SIFIRLA=true" in sonraki_adimlar() and "KASA_EDITOR_SIFRE_SIFIRLA=false" in sonraki_adimlar()
    True
    """
    aralik = "{:,}".format(KIMLIK_ARALIGI).replace(",", ".")
    return "\n".join([
        "ZORUNLU: Uygulamayı bu dosyayla açmadan ÖNCE deploy/.env'de KASA_EDITOR_SIFRE'yi yeni, en az 12 karakterlik ve daha",
        "  önce kullanılmamış bir değere çevirin ve KASA_EDITOR_SIFRE_SIFIRLA=true yapın: ilk açılışta editör şifresi bu değere",
        "  sıfırlanır, yedekteki ve yedekten sonraki şifreler geçmez. Bayrak açılmazsa ve yedekten sonra editör şifresi",
        "  değiştirildiyse editör girişi kilitli kalır (ortamdaki şifre de geçmez); bayrağı açıp yeniden başlatın.",
        "Uygulama bu dosyayla ilk açılışta geri yüklemeyi tanır ve işler:",
        "  - bütün oturumlar (editör, izleyici, alıcı) ve tanıdık cihazlar geçersiz olur; herkes yeniden giriş yapar,",
        "  - kurtarma kodu iptal edilir, cihaz bildirim kayıtları kapatılır,",
        "  - izleyici girişi kapatılır: yedekteki eski izleyici şifresi de, yedekten sonra belirlenen de geçersizdir,",
        "  - güvenlik günlüğündeki (yedek dizininde guvenlik-gunlugu.jsonl) yedekten sonraki şifre ve alıcı kararları",
        "    yeniden uygulanır (yedekteki editör şifresi geçersiz kılınır, alıcılar pasif bırakılır),",
        "  - yeni kayıt numaraları yedekteki en yüksek numaradan " + aralik + " ileri başlar.",
        "Açılıştan sonra yeni ortam şifresiyle editör olarak girin, şifreyi hemen değiştirin ve Araçlar/Güvenlik'teki geri yükleme",
        "raporunu okuyun; Ayarlar'dan YENİ bir izleyici şifresi belirleyin (eski şifreyi yeniden kullanmayın), yeni kurtarma kodu",
        "üretin. Ardından KASA_EDITOR_SIFRE_SIFIRLA=false yapıp 'docker compose ... up -d' ile yeniden oluşturun.",
        "Kalan adımlar: operasyon-runbook.md 'Geri yüklemeden sonra'.",
    ])


def belge_listesi(ham: bytes, beklenen_ozet) -> tuple:
    """belgeler.json'u manifestteki özetiyle doğrular; ((özet, boyut) listesi, eksik özetler) döner.

    >>> ham = json.dumps({"belgeler": [{"ozet": "A" * 64, "boyut": 3}], "eksik": []}).encode()
    >>> belge_listesi(ham, hashlib.sha256(ham).hexdigest().upper())
    ([('AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA', 3)], [])
    >>> belge_listesi(ham, "0" * 64)
    Traceback (most recent call last):
    ValueError: Belge listesi sağlama toplamı eşleşmiyor.
    """
    if not isinstance(beklenen_ozet, str) or hashlib.sha256(ham).hexdigest().upper() != beklenen_ozet.upper():
        raise ValueError("Belge listesi sağlama toplamı eşleşmiyor.")
    veri = json.loads(ham)
    if not isinstance(veri, dict) or not isinstance(veri.get("belgeler"), list):
        raise ValueError("Geçersiz belge listesi.")
    liste = []
    for b in veri["belgeler"]:
        ozet, boyut = (b.get("ozet"), b.get("boyut")) if isinstance(b, dict) else (None, None)
        if not isinstance(ozet, str) or not _BELGE_OZETI.match(ozet) or not isinstance(boyut, int) or not 0 <= boyut <= _AZAMI_BELGE:
            raise ValueError("Geçersiz belge listesi girdisi.")
        liste.append((ozet, boyut))
    if len({o for o, _ in liste}) != len(liste):
        raise ValueError("Belge listesinde yinelenen özet var.")
    eksik = veri.get("eksik") or []
    if not isinstance(eksik, list) or not all(isinstance(e, str) for e in eksik):
        raise ValueError("Geçersiz belge listesi.")
    return liste, eksik


def dosya_ozeti(yol: Path) -> str:
    h = hashlib.sha256()
    with open(yol, "rb") as f:
        while chunk := f.read(1024 * 1024):
            h.update(chunk)
    return h.hexdigest().upper()


def belge_yaz(kaynak, ozet: str, boyut: int, belge_dizini: Path) -> bool:
    """Akışı belge_dizini/<ab>/<özet> olarak yazar; özet ve boyut tutmazsa hiçbir şey bırakmadan reddeder. Aynı içerik zaten
    varsa dokunmaz (False), farklı içerik varsa reddeder. Yazıldıysa True.

    >>> import io, tempfile
    >>> d = Path(tempfile.mkdtemp())
    >>> ozet = hashlib.sha256(b"abc").hexdigest().upper()
    >>> belge_yaz(io.BytesIO(b"abc"), ozet, 3, d), belge_yaz(io.BytesIO(b"abc"), ozet, 3, d)
    (True, False)
    >>> (d / ozet[:2] / ozet).read_bytes()
    b'abc'
    >>> belge_yaz(io.BytesIO(b"abd"), hashlib.sha256(b"abd").hexdigest().upper(), 2, d)  # doctest: +ELLIPSIS
    Traceback (most recent call last):
    ValueError: Belge ... boyutu eşleşmiyor.
    """
    alt = belge_dizini / ozet[:2]
    alt.mkdir(parents=True, exist_ok=True, mode=0o700)
    hedef = alt / ozet
    if hedef.is_symlink():
        raise ValueError("Çıktıdaki belge yolu sembolik bağlantı; boş bir klasör seçin.")
    if hedef.exists():
        if dosya_ozeti(hedef) == ozet:
            return False
        raise ValueError("Çıktıda {} farklı içerik taşıyor; boş bir klasör seçin.".format(Path(GOMULU_ONEK, ozet[:2], ozet)))
    fd, gecici = tempfile.mkstemp(prefix=".kasa-belge-", dir=alt)
    try:
        h = hashlib.sha256()
        toplam = 0
        with os.fdopen(fd, "wb") as f:
            while chunk := kaynak.read(1024 * 1024):
                toplam += len(chunk)
                if toplam > boyut:
                    raise ValueError("Belge {}… boyutu eşleşmiyor.".format(ozet[:12]))
                h.update(chunk)
                f.write(chunk)
            f.flush()
            os.fsync(f.fileno())
        if toplam != boyut:
            raise ValueError("Belge {}… boyutu eşleşmiyor.".format(ozet[:12]))
        if h.hexdigest().upper() != ozet:
            raise ValueError("Belge {}… içeriği özetiyle eşleşmiyor.".format(ozet[:12]))
        os.chmod(gecici, 0o600)
        os.replace(gecici, hedef)
        return True
    finally:
        Path(gecici).unlink(missing_ok=True)


def restore(archive_path: Path, output: Path, belge_aynasi=None, belgesiz: bool = False) -> None:
    output = output.resolve()
    if output.exists():
        raise ValueError("Çıktı zaten var; mevcut veritabanının üzerine yazılmaz.")
    if kalinti_dosyalari(output):
        raise ValueError("Çıktının yanında eski -wal/-shm/-journal dosyası var; SQLite bunları geri açılan dosyaya uygular. Boş bir çıktı yolu seçin.")
    if not output.parent.is_dir():
        raise ValueError("Çıktı klasörü mevcut olmalıdır.")
    with zipfile.ZipFile(archive_path) as archive:
        entries = sorted(archive.namelist())
        temel = [e for e in entries if not e.startswith(GOMULU_ONEK)]
        gomulu = {e[len(GOMULU_ONEK):] for e in entries if e.startswith(GOMULU_ONEK)}
        if "manifest.json" not in entries:
            raise ValueError("Beklenmeyen yedek içeriği.")
        if archive.getinfo("manifest.json").file_size > 8192:
            raise ValueError("Geçersiz yedek bilgisi.")
        manifest = json.loads(archive.read("manifest.json"))
        if not isinstance(manifest, dict):
            raise ValueError("Geçersiz yedek bilgisi.")
        surum = manifest.get("surum")
        if surum in ("2.0.0", "2.1.0"):
            if entries not in _ESKI_ICERIKLER:
                raise ValueError("Beklenmeyen yedek içeriği.")
        elif surum == "2.2.0":
            if temel not in _DEPOLU_ICERIKLER or not all(_BELGE_OZETI.match(o) for o in gomulu):
                raise ValueError("Beklenmeyen yedek içeriği.")
        else:
            raise ValueError("Bu araç yalnız 2.0.0, 2.1.0 ve 2.2.0 yedeklerini destekler.")
        # 'tur' sonradan eklendi; eski manifestlerde yoktur, tür o zaman dosya adından okunur.
        tur = manifest.get("tur")
        if tur is not None and tur not in ("otomatik", "elle", "goc-oncesi"):
            raise ValueError("Geçersiz yedek türü.")
        key_bytes = None
        key_path = output.parent / ".kasa-push-keys.json"
        if ".kasa-push-keys.json" in entries:
            if archive.getinfo(".kasa-push-keys.json").file_size > 2048:
                raise ValueError("Geçersiz bildirim anahtarı dosyası.")
            key_bytes = archive.read(".kasa-push-keys.json")
            if not manifest.get("bildirimAnahtariDahil") or hashlib.sha256(key_bytes).hexdigest().upper() != manifest.get("bildirimAnahtariSha256"):
                raise ValueError("Bildirim anahtarı sağlama toplamı eşleşmiyor.")
            keys = json.loads(key_bytes)
            if set(keys) != {"PublicKey", "PrivateKey"} or not all(isinstance(v, str) and 30 <= len(v) <= 100 for v in keys.values()):
                raise ValueError("Geçersiz bildirim anahtarları.")
            if key_path.is_symlink() or (key_path.exists() and key_path.read_bytes() != key_bytes):
                raise ValueError("Çıktı klasöründe farklı bir bildirim anahtarı var. Boş bir klasör seçin.")
        elif manifest.get("bildirimAnahtariDahil"):
            raise ValueError("Yedekte beklenen bildirim anahtarı eksik.")
        belgeler, eksik = None, []
        if surum == "2.2.0":
            if archive.getinfo(BELGE_LISTESI).file_size > 128 * 1024 * 1024:
                raise ValueError("Geçersiz belge listesi.")
            belgeler, eksik = belge_listesi(archive.read(BELGE_LISTESI), manifest.get("belgeListesiSha256"))
            if gomulu and gomulu != {o for o, _ in belgeler}:
                raise ValueError("ZIP'teki belgeler belge listesiyle eşleşmiyor.")
            if manifest.get("belgelerGomulu") and belgeler and not gomulu:
                raise ValueError("Yedekte beklenen gömülü belgeler eksik.")
            if belgeler and not gomulu and belge_aynasi is None and not belgesiz:
                raise ValueError("Bu yedeğin belgeleri ZIP'te değil. Sunucudaki yedek aynasını --belge-aynasi <Yedek:Dizin>/belgeler ile verin "
                                 "(elle indirilen yedek belgeleri içerir) ya da yalnız veritabanı için --belgesiz kullanın.")
        size = archive.getinfo("kasa.db").file_size
        if size <= 0 or size > 4 * 1024**3:
            raise ValueError("Desteklenmeyen veritabanı büyüklüğü.")
        fd, temporary = tempfile.mkstemp(prefix=".kasa-restore-", suffix=".db", dir=output.parent)
        try:
            digest = hashlib.sha256()
            with os.fdopen(fd, "wb") as target, archive.open("kasa.db") as source:
                total = 0
                while chunk := source.read(1024 * 1024):
                    total += len(chunk)
                    if total > size:
                        raise ValueError("Veritabanı boyutu eşleşmiyor.")
                    digest.update(chunk)
                    target.write(chunk)
            if digest.hexdigest().upper() != manifest.get("sha256"):
                raise ValueError("Yedek sağlama toplamı eşleşmiyor.")
            # Uygulamanın yedekleri tek dosyadır; WAL başlıklı bir kopya (ör. elle alınmış) doğrulamadan önce çevrilir.
            if wal_kipinde_mi(Path(temporary)):
                rollback_kipine_al(Path(temporary))
            # closing: 'with connect()' bağlantıyı kapatmaz; Windows'ta açık dosya geçici kopyanın silinmesini engeller.
            with closing(sqlite3.connect(Path(temporary).as_uri() + "?mode=ro", uri=True)) as db:
                if db.execute("PRAGMA integrity_check").fetchall() != [("ok",)]:
                    raise ValueError("SQLite bütünlük kontrolü başarısız.")
                if db.execute("PRAGMA foreign_key_check").fetchone():
                    raise ValueError("Veritabanında geçersiz ilişki var.")
                migrations = db.execute('SELECT "MigrationId" FROM "__EFMigrationsHistory"').fetchall()
                if ("20260923000400_Operations",) not in migrations:
                    raise ValueError("Beklenen uygulama şeması yok.")
            # Belgeler veritabanından önce açılır: bir içerik doğrulanamazsa çıktı veritabanı hiç oluşmaz. İçerik adresli
            # dosyalardır; yarıda kalan açılış yeniden çalıştırılabilir (doğru olanlar atlanır).
            belge_dizini = output.parent / "belgeler"
            acilan = 0
            if belgeler and not (belgesiz and not gomulu):
                for ozet, boyut in belgeler:
                    if gomulu:
                        if archive.getinfo(GOMULU_ONEK + ozet).file_size != boyut:
                            raise ValueError("Belge {}… boyutu eşleşmiyor.".format(ozet[:12]))
                        with archive.open(GOMULU_ONEK + ozet) as kaynak:
                            belge_yaz(kaynak, ozet, boyut, belge_dizini)
                    else:
                        ayna = Path(belge_aynasi) / ozet[:2] / ozet
                        if not ayna.is_file():
                            raise ValueError("Belge yedek aynasında yok: {}".format(ayna))
                        with open(ayna, "rb") as kaynak:
                            belge_yaz(kaynak, ozet, boyut, belge_dizini)
                    acilan += 1
            # Doğrulanmış kopyaya geri yükleme işareti ve yedek anı: işaretsiz eski yedek de uygulamada geri yükleme olarak
            # işlenir, güvenlik günlüğü yedek anından kesilir.
            isaretlendi = isaretle(Path(temporary), manifest.get("olusturuldu"))
            # Neither the database nor the VAPID identity may overwrite existing data.
            key_created = False
            try:
                if key_bytes is not None and not key_path.exists():
                    key_fd = os.open(key_path, os.O_WRONLY | os.O_CREAT | os.O_EXCL, 0o600)
                    key_created = True
                    with os.fdopen(key_fd, "wb") as key_file:
                        key_file.write(key_bytes)
                os.link(temporary, output)
            except Exception:
                if key_created:
                    key_path.unlink(missing_ok=True)
                raise
            print("Yedek doğrulandı ve yeni dosyaya geri açıldı:", output)
            print("Yedek türü:", tur or yedek_turu(Path(archive_path).name) or "belirtilmemiş")
            print("Yedek anı (UTC):", manifest.get("olusturuldu") or "belirtilmemiş")
            if isaretlendi:
                print("Bu sürümden önce alınmış (işaretsiz) yedek: geri yükleme işareti eklendi.")
            if belgeler is not None:
                if acilan:
                    print("{} belge içeriği özet ve boyutuyla doğrulandı ve açıldı: {}".format(acilan, belge_dizini))
                    print("Bu belgeler/ klasörünü kasa.db ile birlikte uygulamanın belge deposuna (varsayılan: veritabanı klasörü/belgeler; compose'da /data/belgeler) koyun.")
                elif belgeler:
                    print("UYARI: --belgesiz: {} belge açılmadı; uygulamada bu belgeler indirilemez.".format(len(belgeler)))
                if eksik:
                    print("UYARI: Yedek anında {} belge içeriği bulunamamıştı; bunlar hiçbir kaynaktan geri açılamaz.".format(len(eksik)))
            print("Canlıya alırken uygulamayı durdurun; eski kasa.db-wal ve kasa.db-shm dosyalarını kasa.db ile birlikte kenara alın.")
            print(sonraki_adimlar())
        finally:
            Path(temporary).unlink(missing_ok=True)
            for kalinti in kalinti_dosyalari(Path(temporary)):
                kalinti.unlink(missing_ok=True)


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("archive", type=Path)
    parser.add_argument("--output", required=True, type=Path)
    parser.add_argument("--belge-aynasi", type=Path, default=None,
                        help="Sunucudaki yedeğin belgeleri: <Yedek:Dizin>/belgeler (elle indirilen yedekte gerekmez).")
    parser.add_argument("--belgesiz", action="store_true", help="Yalnız veritabanını aç; belgeleri açma.")
    args = parser.parse_args()
    restore(args.archive, args.output, belge_aynasi=args.belge_aynasi, belgesiz=args.belgesiz)
