"""deploy/restore_backup.py sınamaları (gap-geri-yukleme-durum-geri-sarma-1, -2, -6): geri yükleme işareti ve yedek anı,
zorunlu sonraki adımlar, sunucuyla ortak sabitler ve var olan reddetme kuralları.

Çalıştırma (depo kökünden): python3 -m unittest discover -s deploy/tests -v
Ağ ve sunucu gerekmez. Uygulamanın yedekleri kasa.db başlığında geri yükleme işareti (PRAGMA user_version) taşır;
bu sürümden önceki yedekler taşımaz ve araç onları geri açarken işaretler. Uygulama işaretli dosyayla ilk açılışta
oturumları ve izleyici girişini kapatır, kimlikleri ileri alır (Kasa.Api.Tests/GeriYuklemeTests uçtan uca sınar).
Araç her yedekte manifestteki yedek anını işaret tablosuna (GERI_YUKLEME_TABLOSU) yazar; uygulama güvenlik günlüğünü o
andan keser.
"""
import contextlib
import hashlib
import io
import json
import re
import sqlite3
import sys
import tempfile
import unittest
import zipfile
from contextlib import closing
from pathlib import Path

DEPLOY = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(DEPLOY))

import restore_backup as rb  # noqa: E402


def veritabani(yol, user_version=0):
    """restore_backup.py'nin kabul ettiği en küçük şema: migration geçmişi ve AUTOINCREMENT'li bir tablo."""
    with closing(sqlite3.connect(str(yol))) as db:
        db.execute('CREATE TABLE "__EFMigrationsHistory" ("MigrationId" TEXT PRIMARY KEY, "ProductVersion" TEXT NOT NULL)')
        db.execute('INSERT INTO "__EFMigrationsHistory" VALUES (?, ?)', ("20260923000400_Operations", "10.0.0"))
        db.execute('CREATE TABLE "Alislar" ("Id" INTEGER PRIMARY KEY AUTOINCREMENT, "Tedarikci" TEXT NOT NULL)')
        db.execute('INSERT INTO "Alislar" ("Tedarikci") VALUES (?)', ("Kuzey Kereste",))
        db.execute("PRAGMA user_version = {:d}".format(user_version))
        db.commit()


def yedek_zip(dizin, user_version=0, ozet_boz=False, ad="kasa-oto-20260927-030000-0a1b2c3d.zip", olusturuldu="2026-09-27T03:00:00+00:00"):
    """Sunucunun yazdığı biçimde yedek ZIP'i (kasa.db + manifest.json); olusturuldu None ise manifestte yedek anı yoktur."""
    dizin = Path(dizin)
    gecici = dizin / (".db-" + ad)
    veritabani(gecici, user_version)
    veri = gecici.read_bytes()
    gecici.unlink()
    manifest = {"surum": "2.1.0", "olusturuldu": olusturuldu, "tur": "otomatik",
                "sha256": "0" * 64 if ozet_boz else hashlib.sha256(veri).hexdigest().upper(),
                "belgelerDahil": True, "bildirimAnahtariDahil": False, "bildirimAnahtariSha256": None}
    if olusturuldu is None:
        del manifest["olusturuldu"]
    yol = dizin / ad
    with zipfile.ZipFile(yol, "w") as z:
        z.writestr("kasa.db", veri)
        z.writestr("manifest.json", json.dumps(manifest))
    return yol


def depolu_zip(dizin, belgeler, gomulu=False, ad="kasa-oto-20261003-030000-0a1b2c3d.zip", ayna=None, bozuk_belge=None):
    """Belge deposu biçimi (manifest 2.2.0): kasa.db yalnız özetleri taşır; belgeler.json listedir. gomulu: elle indirilen
    kendi kendine yeterli yedek (belgeler/<özet> girdileri); ayna: sunucudaki yedek aynası (<ayna>/<ab>/<özet>)."""
    dizin = Path(dizin)
    gecici = dizin / (".db-" + ad)
    veritabani(gecici, rb.GERI_YUKLEME_ISARETI)
    veri = gecici.read_bytes()
    gecici.unlink()
    liste = json.dumps({"belgeler": [{"ozet": hashlib.sha256(b).hexdigest().upper(), "boyut": len(b)} for b in belgeler], "eksik": []}).encode()
    manifest = {"surum": "2.2.0", "olusturuldu": "2026-10-03T03:00:00+00:00", "tur": "otomatik", "sha256": hashlib.sha256(veri).hexdigest().upper(),
                "belgelerDahil": False, "belgeDeposu": True, "belgeSayisi": len(belgeler), "belgeToplamBayt": sum(map(len, belgeler)),
                "belgeListesiSha256": hashlib.sha256(liste).hexdigest().upper(), "eksikBelgeSayisi": 0, "belgelerGomulu": gomulu,
                "bildirimAnahtariDahil": False, "bildirimAnahtariSha256": None}
    yol = dizin / ad
    with zipfile.ZipFile(yol, "w") as z:
        z.writestr("kasa.db", veri)
        z.writestr("belgeler.json", liste)
        z.writestr("manifest.json", json.dumps(manifest))
        if gomulu:
            for b in belgeler:
                z.writestr("belgeler/" + hashlib.sha256(b).hexdigest().upper(), bozuk_belge if bozuk_belge is not None else b)
    if ayna is not None:
        for b in belgeler:
            ozet = hashlib.sha256(b).hexdigest().upper()
            (Path(ayna) / ozet[:2]).mkdir(parents=True, exist_ok=True)
            (Path(ayna) / ozet[:2] / ozet).write_bytes(bozuk_belge if bozuk_belge is not None else b)
    return yol


def user_version(yol):
    with closing(sqlite3.connect(str(yol))) as db:
        return db.execute("PRAGMA user_version").fetchone()[0]


def isaret_tablosu(yol):
    """Geri yükleme işaret tablosunun satırları; tablo yoksa None."""
    with closing(sqlite3.connect(str(yol))) as db:
        if not db.execute("SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = ?", (rb.GERI_YUKLEME_TABLOSU,)).fetchone():
            return None
        return db.execute('SELECT "YedekZamani", "Arac" FROM "{}"'.format(rb.GERI_YUKLEME_TABLOSU)).fetchall()


def geri_ac(zip_yolu, cikti):
    """Aracı çalıştırır, standart çıktısını döner."""
    tampon = io.StringIO()
    with contextlib.redirect_stdout(tampon):
        rb.restore(Path(zip_yolu), Path(cikti))
    return tampon.getvalue()


class GeriYuklemeIsaretiTests(unittest.TestCase):
    def setUp(self):
        self._d = tempfile.TemporaryDirectory()
        self.dizin = Path(self._d.name)

    def tearDown(self):
        self._d.cleanup()

    def test_isaretsiz_eski_yedek_geri_acilirken_isaretlenir(self):
        cikti = self.dizin / "kasa.db"
        metin = geri_ac(yedek_zip(self.dizin, user_version=0), cikti)
        self.assertEqual(rb.GERI_YUKLEME_ISARETI, user_version(cikti))
        self.assertIn("işaretsiz", metin)
        # Kayıtlar ve bütünlük korunur; araç kimlik sayaçlarına dokunmaz (uygulama ilk açılışta ileri alır).
        with closing(sqlite3.connect(str(cikti))) as db:
            self.assertEqual([("Kuzey Kereste",)], db.execute('SELECT "Tedarikci" FROM "Alislar"').fetchall())
            self.assertEqual([("ok",)], db.execute("PRAGMA integrity_check").fetchall())
            self.assertEqual([("Alislar", 1)], db.execute("SELECT name, seq FROM sqlite_sequence").fetchall())
        self.assertEqual([], rb.kalinti_dosyalari(cikti))

    def test_uygulamanin_isaretli_yedegi_isaretli_kalir(self):
        cikti = self.dizin / "kasa.db"
        metin = geri_ac(yedek_zip(self.dizin, user_version=rb.GERI_YUKLEME_ISARETI), cikti)
        self.assertEqual(rb.GERI_YUKLEME_ISARETI, user_version(cikti))
        self.assertNotIn("işaretsiz", metin)

    def test_yedek_ani_eski_ve_isaretli_yedekte_isaret_tablosuna_yazilir(self):
        for i, surum in enumerate((0, rb.GERI_YUKLEME_ISARETI)):
            cikti = self.dizin / "kasa-{}.db".format(i)
            geri_ac(yedek_zip(self.dizin, user_version=surum, ad="kasa-oto-2026092{}-030000-0a1b2c3d.zip".format(i)), cikti)
            self.assertEqual([("2026-09-27T03:00:00+00:00", "restore_backup.py")], isaret_tablosu(cikti))
            with closing(sqlite3.connect(str(cikti))) as db:
                self.assertEqual([("ok",)], db.execute("PRAGMA integrity_check").fetchall())

    def test_manifestte_yedek_ani_yoksa_tablo_yazilmaz_isaret_yazilir(self):
        cikti = self.dizin / "kasa.db"
        geri_ac(yedek_zip(self.dizin, olusturuldu=None), cikti)
        self.assertIsNone(isaret_tablosu(cikti))
        self.assertEqual(rb.GERI_YUKLEME_ISARETI, user_version(cikti))

    def test_zorunlu_sonraki_adimlar_ve_yedek_ani_yazilir(self):
        metin = geri_ac(yedek_zip(self.dizin), self.dizin / "kasa.db")
        self.assertIn("ZORUNLU", metin)
        self.assertIn("KASA_EDITOR_SIFRE", metin)
        self.assertIn("kurtarma kodu iptal edilir", metin)
        self.assertIn("guvenlik-gunlugu.jsonl", metin)
        self.assertIn("YENİ bir izleyici şifresi", metin)
        self.assertIn("izleyici girişi kapatılır", metin)
        self.assertIn("1.000.000 ileri", metin)
        self.assertIn("Yedek anı (UTC): 2026-09-27T03:00:00+00:00", metin)
        self.assertIn("operasyon-runbook.md 'Geri yüklemeden sonra'", metin)

    def test_ozeti_tutmayan_yedek_reddedilir_cikti_yazilmaz(self):
        cikti = self.dizin / "kasa.db"
        with self.assertRaisesRegex(ValueError, "sağlama toplamı"):
            geri_ac(yedek_zip(self.dizin, ozet_boz=True), cikti)
        self.assertFalse(cikti.exists())
        self.assertEqual([], [p.name for p in self.dizin.iterdir() if p.name.startswith(".kasa-restore-")])

    def test_var_olan_ciktinin_uzerine_yazilmaz_ve_isaretlenmez(self):
        cikti = self.dizin / "kasa.db"
        veritabani(cikti, user_version=0)
        with self.assertRaisesRegex(ValueError, "üzerine yazılmaz"):
            geri_ac(yedek_zip(self.dizin), cikti)
        self.assertEqual(0, user_version(cikti))


class BelgeDeposuBicimiTests(unittest.TestCase):
    """Manifest 2.2.0: belgeler kasa.db dışında; her içerik özet ve boyutuyla doğrulanıp çıktının yanına belgeler/<ab>/<özet>
    olarak açılır; doğrulanamayan içerik varsa çıktı veritabanı hiç oluşmaz."""
    BELGELER = [b"%PDF-1.7 fatura", b"\x89PNG\r\n\x1a\n dekont", b"%PDF-1.4 ekstre"]

    def setUp(self):
        self._d = tempfile.TemporaryDirectory()
        self.dizin = Path(self._d.name)
        self.cikti = self.dizin / "cikti"
        self.cikti.mkdir()

    def tearDown(self):
        self._d.cleanup()

    def belge(self, icerik):
        ozet = hashlib.sha256(icerik).hexdigest().upper()
        return self.cikti / "belgeler" / ozet[:2] / ozet

    def test_elle_indirilen_kendi_kendine_yeterli_yedek_belgeleriyle_acilir(self):
        metin = geri_ac(depolu_zip(self.dizin, self.BELGELER, gomulu=True), self.cikti / "kasa.db")
        self.assertIn("3 belge içeriği", metin)
        for b in self.BELGELER:
            self.assertEqual(b, self.belge(b).read_bytes())
        self.assertEqual(rb.GERI_YUKLEME_ISARETI, user_version(self.cikti / "kasa.db"))
        # Belge deposu biçiminde de yedek anı işaret tablosundadır.
        self.assertEqual([("2026-10-03T03:00:00+00:00", "restore_backup.py")], isaret_tablosu(self.cikti / "kasa.db"))

    def test_sunucu_yedegi_yedek_aynasiyla_acilir_aynasiz_reddedilir(self):
        yol = depolu_zip(self.dizin, self.BELGELER, ayna=self.dizin / "ayna")
        with self.assertRaisesRegex(ValueError, "--belge-aynasi"):
            geri_ac(yol, self.cikti / "kasa.db")
        self.assertFalse((self.cikti / "kasa.db").exists())
        tampon = io.StringIO()
        with contextlib.redirect_stdout(tampon):
            rb.restore(yol, self.cikti / "kasa.db", belge_aynasi=self.dizin / "ayna")
        for b in self.BELGELER:
            self.assertEqual(b, self.belge(b).read_bytes())

    def test_ozeti_tutmayan_belge_reddedilir_veritabani_olusmaz(self):
        yol = depolu_zip(self.dizin, self.BELGELER[:1], gomulu=True, bozuk_belge=b"%PDF-1.7 fatur!")
        with self.assertRaisesRegex(ValueError, "özetiyle eşleşmiyor"):
            geri_ac(yol, self.cikti / "kasa.db")
        self.assertFalse((self.cikti / "kasa.db").exists())
        self.assertFalse(self.belge(self.BELGELER[0]).exists())

    def test_belgesiz_yalniz_veritabanini_acar(self):
        tampon = io.StringIO()
        with contextlib.redirect_stdout(tampon):
            rb.restore(depolu_zip(self.dizin, self.BELGELER), self.cikti / "kasa.db", belgesiz=True)
        self.assertTrue((self.cikti / "kasa.db").exists())
        self.assertIn("--belgesiz: 3 belge açılmadı", tampon.getvalue())
        self.assertFalse((self.cikti / "belgeler").exists())

    def test_liste_ozeti_tutmazsa_ya_da_bilinmeyen_girdi_varsa_reddedilir(self):
        yol = depolu_zip(self.dizin, self.BELGELER, gomulu=True)
        bozuk = self.dizin / "bozuk.zip"
        with zipfile.ZipFile(yol) as eski, zipfile.ZipFile(bozuk, "w") as yeni:
            for girdi in eski.namelist():
                yeni.writestr(girdi, eski.read(girdi) if girdi != "belgeler.json" else eski.read(girdi).replace(b"\"eksik\": []", b"\"eksik\": [\"X\"]"))
        with self.assertRaisesRegex(ValueError, "Belge listesi sağlama toplamı"):
            geri_ac(bozuk, self.cikti / "kasa.db")
        fazla = self.dizin / "fazla.zip"
        with zipfile.ZipFile(yol) as eski, zipfile.ZipFile(fazla, "w") as yeni:
            for girdi in eski.namelist():
                yeni.writestr(girdi, eski.read(girdi))
            yeni.writestr("belgeler/../../kasa.db", b"x")
        with self.assertRaisesRegex(ValueError, "Beklenmeyen yedek içeriği"):
            geri_ac(fazla, self.cikti / "kasa.db")


class OrtakSabitTests(unittest.TestCase):
    def test_isaret_ve_aralik_sunucuyla_ayni(self):
        kaynak = (DEPLOY.parent / "Kasa.Api/Auth/GeriYuklemeIsleyici.cs").read_text(encoding="utf-8")
        isaret = re.search(r"public const int Isaret = 0x([0-9A-Fa-f]+);", kaynak)
        aralik = re.search(r"public const int KimlikAraligi = ([0-9_]+);", kaynak)
        self.assertIsNotNone(isaret)
        self.assertIsNotNone(aralik)
        self.assertEqual(rb.GERI_YUKLEME_ISARETI, int(isaret.group(1), 16))
        self.assertEqual(rb.KIMLIK_ARALIGI, int(aralik.group(1).replace("_", "")))

    def test_isaret_sqlite_user_version_araligina_sigar(self):
        # PRAGMA user_version 32 bit işaretli tam sayıdır; canlı dosyanın değeri (0) işaret olamaz.
        self.assertTrue(0 < rb.GERI_YUKLEME_ISARETI < 2 ** 31)


if __name__ == "__main__":
    unittest.main()
