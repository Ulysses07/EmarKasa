"""deploy/restore_backup.py sınamaları (gap-geri-yukleme-durum-geri-sarma-2, -6): geri yükleme işareti, zorunlu sonraki
adımlar, sunucuyla ortak sabitler ve var olan reddetme kuralları.

Çalıştırma (depo kökünden): python3 -m unittest discover -s deploy/tests -v
Ağ ve sunucu gerekmez. Uygulamanın yedekleri kasa.db başlığında geri yükleme işareti (PRAGMA user_version) taşır;
bu sürümden önceki yedekler taşımaz ve araç onları geri açarken işaretler. Uygulama işaretli dosyayla ilk açılışta
oturumları ve izleyici girişini kapatır, kimlikleri ileri alır (Kasa.Api.Tests/GeriYuklemeTests uçtan uca sınar).
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


def yedek_zip(dizin, user_version=0, ozet_boz=False, ad="kasa-oto-20260927-030000-0a1b2c3d.zip"):
    """Sunucunun yazdığı biçimde yedek ZIP'i (kasa.db + manifest.json)."""
    dizin = Path(dizin)
    gecici = dizin / (".db-" + ad)
    veritabani(gecici, user_version)
    veri = gecici.read_bytes()
    gecici.unlink()
    manifest = {"surum": "2.1.0", "olusturuldu": "2026-09-27T03:00:00+00:00", "tur": "otomatik",
                "sha256": "0" * 64 if ozet_boz else hashlib.sha256(veri).hexdigest().upper(),
                "belgelerDahil": True, "bildirimAnahtariDahil": False, "bildirimAnahtariSha256": None}
    yol = dizin / ad
    with zipfile.ZipFile(yol, "w") as z:
        z.writestr("kasa.db", veri)
        z.writestr("manifest.json", json.dumps(manifest))
    return yol


def user_version(yol):
    with closing(sqlite3.connect(str(yol))) as db:
        return db.execute("PRAGMA user_version").fetchone()[0]


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

    def test_zorunlu_sonraki_adimlar_ve_yedek_ani_yazilir(self):
        metin = geri_ac(yedek_zip(self.dizin), self.dizin / "kasa.db")
        self.assertIn("ZORUNLU", metin)
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
