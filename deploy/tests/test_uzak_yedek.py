"""deploy/uzak_yedek.py sınamaları (devops-9): ad ve saklama kuralı, manifest SHA-256 doğrulaması, ayarlar,
dizin ve rclone hedefleri, kuru çalıştırma, geri açarak doğrulama ve komut satırı.

Çalıştırma (depo kökünden): python3 -m unittest discover -s deploy/tests -v
Ağ, rclone ya da sunucu gerekmez: rclone komutları sahte çalıştırıcıyla, uzak hedef geçici dizinle sınanır. Zaman
sabit verilir; yalnız komut satırı sınaması betiğin gerçek saatini kullanır ve yedek adını o saate göre üretir.
"""
import contextlib
import hashlib
import io
import json
import os
import py_compile
import sqlite3
import subprocess
import sys
import tempfile
import unittest
import zipfile
from collections import namedtuple
from contextlib import closing
from datetime import datetime, timedelta, timezone
from pathlib import Path
from unittest import mock

DEPLOY = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(DEPLOY))

import uzak_yedek as uy  # noqa: E402

SIMDI = datetime(2026, 9, 27, 3, 0, 0, tzinfo=timezone.utc)
Kullanim = namedtuple("Kullanim", "total used free")


def ad(onek, zaman, ek=0):
    """Sunucunun ürettiği ad: kasa-[oto-|elle-|goc-oncesi-]YYYYMMDD-HHMMSS-xxxxxxxx.zip (zaman UTC)."""
    return "kasa-{}{}-{:08x}.zip".format(onek, zaman.astimezone(timezone.utc).strftime("%Y%m%d-%H%M%S"), ek)


def veritabani(yol):
    """restore_backup.py'nin kabul ettiği en küçük şema: migration geçmişi ve bir tablo."""
    with closing(sqlite3.connect(str(yol))) as db:
        db.execute('CREATE TABLE "__EFMigrationsHistory" ("MigrationId" TEXT PRIMARY KEY, "ProductVersion" TEXT NOT NULL)')
        db.execute('INSERT INTO "__EFMigrationsHistory" VALUES (?, ?)', ("20260923000400_Operations", "10.0.0"))
        db.execute('CREATE TABLE "Islemler" ("Id" INTEGER PRIMARY KEY, "Tutar" INTEGER NOT NULL)')
        db.execute('INSERT INTO "Islemler" ("Tutar") VALUES (12500)')
        db.commit()


def yedek_zip(dizin, dosya_adi, tur="otomatik", ozet_boz=False, anahtar=False, fazla_dosya=False):
    """Sunucunun yazdığı biçimde yedek ZIP'i (kasa.db + manifest.json [+ bildirim anahtarı])."""
    dizin = Path(dizin)
    gecici = dizin / (".db-" + dosya_adi)
    veritabani(gecici)
    veri = gecici.read_bytes()
    gecici.unlink()
    anahtar_bayt = json.dumps({"PublicKey": "P" * 40, "PrivateKey": "Q" * 40}).encode() if anahtar else None
    manifest = {"surum": "2.1.0", "olusturuldu": "2026-09-27T03:00:00+00:00",
                "sha256": "0" * 64 if ozet_boz else hashlib.sha256(veri).hexdigest().upper(),
                "belgelerDahil": True, "bildirimAnahtariDahil": anahtar,
                "bildirimAnahtariSha256": hashlib.sha256(anahtar_bayt).hexdigest().upper() if anahtar else None}
    if tur is not None:
        manifest["tur"] = tur
    yol = dizin / dosya_adi
    with zipfile.ZipFile(yol, "w") as z:
        z.writestr("kasa.db", veri)
        if anahtar:
            z.writestr(".kasa-push-keys.json", anahtar_bayt)
        if fazla_dosya:
            z.writestr("baska.txt", b"x")
        z.writestr("manifest.json", json.dumps(manifest))
    return yol


class Sessiz:
    """Betiğin çıktısını yakalar (sınama çıktısını kirletmez, iletiler denetlenebilir)."""

    def __enter__(self):
        self.out, self.err = io.StringIO(), io.StringIO()
        self._a = contextlib.redirect_stdout(self.out)
        self._b = contextlib.redirect_stderr(self.err)
        self._a.__enter__()
        self._b.__enter__()
        return self

    def __exit__(self, *hata):
        self._b.__exit__(*hata)
        self._a.__exit__(*hata)
        return False

    @property
    def metin(self):
        return self.out.getvalue() + self.err.getvalue()


class AdVeSaklamaTests(unittest.TestCase):
    def test_sunucunun_yedek_adlari_turu_ve_utc_zamani_ile_taninir(self):
        zaman = datetime(2026, 9, 1, 3, 0, 0, tzinfo=timezone.utc)
        for dosya, tur in [("kasa-oto-20260901-030000-0a1b2c3d.zip", "otomatik"),
                           ("kasa-20260901-030000-0a1b2c3d.zip", "otomatik"),  # 2.3 ve öncesinin adı
                           ("kasa-elle-20260901-030000-0a1b2c3d.zip", "elle"),
                           ("kasa-goc-oncesi-20260901-030000-0a1b2c3d.zip", "goc-oncesi")]:
            y = uy.tani(dosya)
            self.assertIsNotNone(y, dosya)
            self.assertEqual((y.ad, y.tur, y.zaman), (dosya, tur, zaman))

    def test_kaliba_uymayan_dosya_yedek_sayilmaz(self):
        for dosya in ["kasa-oto-20260901-030000-0a1b2c3d.zip.part", "kasa-oto-20260901-030000-0A1B2C3D.zip",
                      "kasa-oncesi-gecis.zip", "kasa-yedek-2026-09-27.zip", "kasa-20261399-030000-0a1b2c3d.zip",
                      ".0a1b2c3d4e5f.db", "../kasa-oto-20260901-030000-0a1b2c3d.zip"]:
            self.assertIsNone(uy.tani(dosya), dosya)

    def test_otomatik_saklama_sunucunun_kuraliyla_ayni_gunleri_ve_aylari_tutar(self):
        # Kasa.Api.Tests/YedekSaklamaTests ile aynı senaryo: 400 günlük geçmiş, 30 gün + 12 ay.
        adlar = [ad("oto-", SIMDI - timedelta(days=g), g) for g in range(400)]
        silinecek = set(uy.silinecekler(adlar, SIMDI, gunluk_gun=30, aylik_ay=12))
        kalan = sorted(uy.tani(a).zaman for a in adlar if a not in silinecek)
        aylar = [datetime(2025 + (9 + i) // 12, (9 + i) % 12 + 1, 1, 3, 0, tzinfo=timezone.utc) for i in range(11)]  # Ekim 2025 - Ağustos 2026
        self.assertEqual(sorted(aylar + [SIMDI - timedelta(days=g) for g in range(30)]), kalan)

    def test_elle_yedeklerden_en_yeni_10u_kalir_goc_oncesi_ve_yabanci_dosya_hic_silinmez(self):
        elle = [ad("elle-", SIMDI - timedelta(days=100 + i), i) for i in range(25)]
        goc = [ad("goc-oncesi-", SIMDI - timedelta(days=900 + i), i) for i in range(3)]
        silinecek = uy.silinecekler(elle + goc + ["kasa-oncesi-gecis.zip"], SIMDI, gunluk_gun=35, aylik_ay=13)
        self.assertEqual(sorted(elle[10:]), sorted(silinecek))

    def test_aylik_temsilci_istanbul_takvim_ayina_gore_secilir(self):
        yeni = [ad("oto-", SIMDI - timedelta(days=g), g) for g in range(7)]
        haziran_sonu_utc = ad("oto-", datetime(2026, 6, 30, 22, 30, tzinfo=timezone.utc), 100)  # İstanbul'da 1 Temmuz 01:30
        temmuz5 = ad("oto-", datetime(2026, 7, 5, 3, 0, tzinfo=timezone.utc), 101)
        haziran15 = ad("oto-", datetime(2026, 6, 15, 3, 0, tzinfo=timezone.utc), 102)
        self.assertEqual([temmuz5], uy.silinecekler(yeni + [haziran_sonu_utc, temmuz5, haziran15], SIMDI, 30, 12))


class ArsivTests(unittest.TestCase):
    def setUp(self):
        self._tmp = tempfile.TemporaryDirectory()
        self.dizin = Path(self._tmp.name)

    def tearDown(self):
        self._tmp.cleanup()

    def test_gecerli_arsiv_manifest_ozetiyle_dogrulanir(self):
        self.assertEqual("otomatik", uy.arsiv_dogrula(yedek_zip(self.dizin, ad("oto-", SIMDI)))["tur"])
        self.assertTrue(uy.arsiv_dogrula(yedek_zip(self.dizin, ad("elle-", SIMDI), tur="elle", anahtar=True))["bildirimAnahtariDahil"])
        self.assertNotIn("tur", uy.arsiv_dogrula(yedek_zip(self.dizin, ad("", SIMDI), tur=None)))  # 2.3 öncesi manifest

    def test_ozeti_manifestle_eslesmeyen_arsiv_reddedilir(self):
        with self.assertRaisesRegex(uy.ArsivHatasi, "özet"):
            uy.arsiv_dogrula(yedek_zip(self.dizin, ad("oto-", SIMDI), ozet_boz=True))

    def test_bozuk_ya_da_beklenmeyen_icerikli_arsiv_reddedilir(self):
        bozuk = self.dizin / ad("oto-", SIMDI, 1)
        bozuk.write_bytes(b"PK\x03\x04 yarim kalmis")
        with self.assertRaises(uy.ArsivHatasi):
            uy.arsiv_dogrula(bozuk)
        with self.assertRaisesRegex(uy.ArsivHatasi, "içerik"):
            uy.arsiv_dogrula(yedek_zip(self.dizin, ad("oto-", SIMDI, 2), fazla_dosya=True))

    def test_ad_turu_manifest_turuyle_celisirse_reddedilir(self):
        with self.assertRaisesRegex(uy.ArsivHatasi, "tür"):
            uy.arsiv_dogrula(yedek_zip(self.dizin, ad("elle-", SIMDI), tur="otomatik"))


class AyarTests(unittest.TestCase):
    def setUp(self):
        self._tmp = tempfile.TemporaryDirectory()
        self.dizin = Path(self._tmp.name)
        self.yok = str(self.dizin / "yok.env")

    def tearDown(self):
        self._tmp.cleanup()

    def test_uzak_hedef_tanimsizsa_yapilandirma_hatasi(self):
        with self.assertRaisesRegex(uy.YapilandirmaHatasi, "KASA_UZAK_HEDEF"):
            uy.ayarlari_oku({"KASA_DEPLOY_ENV": self.yok, "KASA_BACKUP_DIR": str(self.dizin)})

    def test_yedek_ve_veri_dizini_deploy_env_dosyasindan_okunur_ortam_degiskeni_once_gelir(self):
        env = self.dizin / ".env"
        env.write_text("KASA_JWT_KEY=gizli-anahtar\nKASA_DATA_DIR=/opt/kasa/deploy/kasa-data-x\n"
                       "KASA_BACKUP_DIR='/opt/kasa/deploy/yedekler-x'\n", encoding="utf-8")
        a = uy.ayarlari_oku({"KASA_DEPLOY_ENV": str(env), "KASA_UZAK_HEDEF": "kasa-sifreli:yedekler"})
        self.assertEqual((Path("/opt/kasa/deploy/yedekler-x"), Path("/opt/kasa/deploy/kasa-data-x")), (a.yedek_dizini, a.veri_dizini))
        self.assertNotIn("gizli-anahtar", repr(a))
        b = uy.ayarlari_oku({"KASA_DEPLOY_ENV": str(env), "KASA_UZAK_HEDEF": "x:y", "KASA_BACKUP_DIR": "/baska"})
        self.assertEqual(Path("/baska"), b.yedek_dizini)

    def test_varsayilanlar_ve_gecersiz_degerler(self):
        a = uy.ayarlari_oku({"KASA_DEPLOY_ENV": self.yok, "KASA_UZAK_HEDEF": "kasa-sifreli:yedekler"})
        self.assertEqual(("rclone", True, 35, 13, 48, 80), (a.yontem, a.saklama, a.gunluk_gun, a.aylik_ay, a.en_fazla_saat, a.disk_esik))
        for anahtar, deger in [("KASA_UZAK_GUNLUK_GUN", "0"), ("KASA_UZAK_AYLIK_AY", "on"), ("KASA_DISK_ESIK_YUZDE", "101"),
                               ("KASA_UZAK_SAKLAMA", "belki"), ("KASA_UZAK_YONTEM", "ftp"), ("KASA_UZAK_IZLEME_URL", "ftp://x")]:
            with self.assertRaisesRegex(uy.YapilandirmaHatasi, anahtar):
                uy.ayarlari_oku({"KASA_DEPLOY_ENV": self.yok, "KASA_UZAK_HEDEF": "x:y", anahtar: deger})

    def test_sifresiz_hedef_ancak_acik_izinle_kabul_edilir(self):
        cagrilar = []

        def calistir(komut):
            cagrilar.append(list(komut))
            return 0, "kasa-depo:   s3\nkasa-sifreli: crypt\n", ""

        ortam = {"KASA_DEPLOY_ENV": self.yok, "KASA_UZAK_HEDEF": "kasa-depo:kova"}
        with self.assertRaisesRegex(uy.YapilandirmaHatasi, "crypt"):
            uy.hedef_olustur(uy.ayarlari_oku(ortam), calistir)
        self.assertIsInstance(uy.hedef_olustur(uy.ayarlari_oku(dict(ortam, KASA_UZAK_HEDEF="kasa-sifreli:yedekler")), calistir), uy.RcloneHedefi)
        uy.hedef_olustur(uy.ayarlari_oku(dict(ortam, KASA_UZAK_SIFRESIZ="evet")), calistir)
        with self.assertRaisesRegex(uy.YapilandirmaHatasi, "tanımlı değil"):
            uy.hedef_olustur(uy.ayarlari_oku(dict(ortam, KASA_UZAK_HEDEF="baska:yol")), calistir)
        dizin = dict(ortam, KASA_UZAK_YONTEM="dizin", KASA_UZAK_HEDEF=str(self.dizin))
        with self.assertRaisesRegex(uy.YapilandirmaHatasi, "KASA_UZAK_SIFRESIZ"):
            uy.hedef_olustur(uy.ayarlari_oku(dizin))
        self.assertIsInstance(uy.hedef_olustur(uy.ayarlari_oku(dict(dizin, KASA_UZAK_SIFRESIZ="evet"))), uy.DizinHedefi)


class GonderTests(unittest.TestCase):
    def setUp(self):
        self._tmp = tempfile.TemporaryDirectory()
        kok = Path(self._tmp.name)
        self.yerel, self.uzak, self.veri = kok / "yedekler", kok / "uzak", kok / "veri"
        for d in (self.yerel, self.uzak, self.veri):
            d.mkdir()
        self.ortam = {"KASA_DEPLOY_ENV": str(kok / "yok.env"), "KASA_BACKUP_DIR": str(self.yerel), "KASA_DATA_DIR": str(self.veri),
                      "KASA_UZAK_YONTEM": "dizin", "KASA_UZAK_HEDEF": str(self.uzak), "KASA_UZAK_SIFRESIZ": "evet",
                      "KASA_UZAK_IZLEME_URL": "https://izleme.example/ping/abc"}
        self.bildirimler = []
        self.doluluk = Kullanim(100, 40, 60)

    def tearDown(self):
        self._tmp.cleanup()

    def calistir(self, kuru=False, **ek):
        ayarlar = uy.ayarlari_oku(dict(self.ortam, **ek))
        with Sessiz() as s:
            kod = uy.gonder(ayarlar, uy.hedef_olustur(ayarlar), kuru=kuru, simdi=SIMDI,
                            disk_kullanimi=lambda _: self.doluluk, bildir=lambda url, ok: self.bildirimler.append((url, ok)))
        return kod, s.metin

    def uzaktakiler(self):
        return sorted(p.name for p in self.uzak.iterdir())

    def test_yalniz_dogrulanan_yedekler_gonderilir_ikinci_calisma_yeniden_gondermez(self):
        oto = yedek_zip(self.yerel, ad("oto-", SIMDI - timedelta(hours=2), 1)).name
        elle = yedek_zip(self.yerel, ad("elle-", SIMDI - timedelta(days=3), 2), tur="elle", anahtar=True).name
        bozuk = yedek_zip(self.yerel, ad("oto-", SIMDI - timedelta(days=1), 3), ozet_boz=True).name
        (self.yerel / (ad("oto-", SIMDI, 4) + ".part")).write_bytes(b"yarim")
        (self.yerel / "kasa-oncesi-gecis.zip").write_bytes(b"operatorun dosyasi")

        kod, metin = self.calistir()
        self.assertEqual(1, kod)
        self.assertIn(bozuk, metin)
        self.assertEqual(sorted([oto, elle]), self.uzaktakiler())
        self.assertEqual([("https://izleme.example/ping/abc", False)], self.bildirimler)

        (self.yerel / bozuk).unlink()
        oncesi = {p.name: p.stat().st_mtime_ns for p in self.uzak.iterdir()}
        kod, metin = self.calistir()
        self.assertEqual(0, kod, metin)
        self.assertEqual(oncesi, {p.name: p.stat().st_mtime_ns for p in self.uzak.iterdir()})
        self.assertEqual(("https://izleme.example/ping/abc", True), self.bildirimler[-1])

    def test_kuru_calistirma_hedefe_yazmaz_silmez_ve_bildirmez(self):
        yeni = yedek_zip(self.yerel, ad("oto-", SIMDI - timedelta(hours=1), 1)).name
        elle = [yedek_zip(self.uzak, ad("elle-", SIMDI - timedelta(days=400 + i), i), tur="elle").name for i in range(11)]
        oncesi = self.uzaktakiler()

        kod, metin = self.calistir(kuru=True)
        self.assertEqual(0, kod, metin)
        self.assertIn("[kuru] gönderilecek: " + yeni, metin)
        self.assertIn("[kuru] hedefte silinecek: " + elle[10], metin)
        self.assertEqual(oncesi, self.uzaktakiler())
        self.assertEqual([], self.bildirimler)

    def test_hedefte_saklama_suresi_dolanlari_siler_goc_oncesini_ve_yabanci_dosyayi_korur(self):
        yedek_zip(self.yerel, ad("oto-", SIMDI - timedelta(hours=1), 1))
        eskiler = [yedek_zip(self.uzak, ad("elle-", SIMDI - timedelta(days=200 + i), i), tur="elle").name for i in range(12)]
        goc = yedek_zip(self.uzak, ad("goc-oncesi-", SIMDI - timedelta(days=800), 5), tur="goc-oncesi").name
        (self.uzak / "benioku.txt").write_text("operatör notu", encoding="utf-8")

        kod, metin = self.calistir()
        self.assertEqual(0, kod, metin)
        kalan = self.uzaktakiler()
        self.assertTrue(set(eskiler[:10]) <= set(kalan))
        self.assertFalse(set(eskiler[10:]) & set(kalan))
        self.assertIn(goc, kalan)
        self.assertIn("benioku.txt", kalan)

        yedek_zip(self.uzak, ad("elle-", SIMDI - timedelta(days=300), 77), tur="elle")
        kod, _ = self.calistir(KASA_UZAK_SAKLAMA="hayir")
        self.assertEqual(0, kod)
        self.assertIn(ad("elle-", SIMDI - timedelta(days=300), 77), self.uzaktakiler())

    def test_saklama_disinda_kalacak_eski_yerel_yedek_gonderilmez(self):
        for g in range(7):  # en yeni 7 yedek yaşından bağımsız korunur; eski yedekler bu yedinin dışında kalsın
            yedek_zip(self.yerel, ad("oto-", SIMDI - timedelta(days=g, hours=1), 10 + g))
        ayin_ilki = yedek_zip(self.yerel, ad("oto-", datetime(2026, 6, 1, 3, tzinfo=timezone.utc), 2)).name
        ayin_ortasi = yedek_zip(self.yerel, ad("oto-", datetime(2026, 6, 15, 3, tzinfo=timezone.utc), 3)).name
        kod, metin = self.calistir()
        self.assertEqual(0, kod, metin)
        self.assertIn(ayin_ilki, self.uzaktakiler())
        self.assertNotIn(ayin_ortasi, self.uzaktakiler())

    def test_uygulama_gunluk_yedek_almiyorsa_hata_verir(self):
        yedek_zip(self.yerel, ad("oto-", SIMDI - timedelta(days=3), 1))
        kod, metin = self.calistir()
        self.assertEqual(1, kod)
        self.assertIn("En yeni otomatik yedek", metin)
        self.assertEqual(1, len(self.uzaktakiler()))  # eski de olsa doğrulanmış yedek yine gönderilir

    def test_disk_doluluk_esigi_asilinca_hata_verir(self):
        yedek_zip(self.yerel, ad("oto-", SIMDI - timedelta(hours=1), 1))
        self.doluluk = Kullanim(1000, 850, 150)
        kod, metin = self.calistir()
        self.assertEqual(1, kod)
        self.assertIn("%85", metin)
        kod, _ = self.calistir(KASA_DISK_ESIK_YUZDE="90")
        self.assertEqual(0, kod)

    def test_hedef_dizini_yoksa_bos_dizin_acmaz_ve_hata_verir(self):
        yedek_zip(self.yerel, ad("oto-", SIMDI - timedelta(hours=1), 1))
        self.uzak.rmdir()  # bağlı disk takılı değil
        kod, metin = self.calistir()
        self.assertEqual(1, kod)
        self.assertFalse(self.uzak.exists())
        self.assertIn("hedef", metin.lower())

    def test_listelemeden_sonra_rotasyonla_silinen_yerel_yedek_hata_sayilmaz(self):
        yedek_zip(self.yerel, ad("oto-", SIMDI - timedelta(hours=1), 1))
        gercek = uy._yerel_yedekler
        hayalet = ad("oto-", SIMDI - timedelta(days=2), 2)
        with mock.patch.object(uy, "_yerel_yedekler", lambda d: dict(gercek(d), **{hayalet: Path(d) / hayalet})):
            kod, metin = self.calistir()
        self.assertEqual(0, kod, metin)
        self.assertIn("atlandı (yerelde artık yok): " + hayalet, metin)

    def test_uzaktaki_kopyanin_boyutu_farkliysa_hata_verir(self):
        dosya = yedek_zip(self.yerel, ad("oto-", SIMDI - timedelta(hours=1), 1)).name
        (self.uzak / dosya).write_bytes(b"yarim kopya")
        kod, metin = self.calistir()
        self.assertEqual(1, kod)
        self.assertIn("boyut", metin)


class RcloneTests(unittest.TestCase):
    def setUp(self):
        self._tmp = tempfile.TemporaryDirectory()
        self.yerel = Path(self._tmp.name)
        self.cagrilar = []
        self.uzak = {}

    def tearDown(self):
        self._tmp.cleanup()

    @staticmethod
    def uzak_adi(yol):
        """'uzak:klasör/ad' ya da kökteki 'uzak:ad' yolundan dosya adı."""
        return yol.rsplit(":", 1)[1].rsplit("/", 1)[-1]

    def sahte(self, komut):
        komut = list(komut)
        self.cagrilar.append(komut)
        alt = komut[1]
        if alt == "listremotes":
            return 0, "kasa-depo:    b2\nkasa-sifreli: crypt\n", ""
        if alt == "lsf":
            if not self.uzak:
                return 3, "", "2026/09/27 03:00:00 ERROR : : error listing: directory not found\n"
            return 0, "".join("{}\t{}\n".format(a, b) for a, b in sorted(self.uzak.items())), ""
        if alt == "copyto":
            self.uzak[self.uzak_adi(komut[-1])] = Path(komut[-2]).stat().st_size
            return 0, "", ""
        if alt == "deletefile":
            del self.uzak[self.uzak_adi(komut[-1])]
            return 0, "", ""
        return 1, "", "beklenmeyen komut"

    def test_crypt_uzagina_immutable_kopyalar_ve_komut_satirinda_kimlik_bilgisi_tasimaz(self):
        dosya = yedek_zip(self.yerel, ad("oto-", SIMDI - timedelta(hours=1), 1)).name
        ayarlar = uy.ayarlari_oku({"KASA_DEPLOY_ENV": str(self.yerel / "yok.env"), "KASA_BACKUP_DIR": str(self.yerel),
                                   "KASA_UZAK_HEDEF": "kasa-sifreli:yedekler", "KASA_RCLONE": "/usr/bin/rclone"})
        with Sessiz() as s:
            kod = uy.gonder(ayarlar, uy.hedef_olustur(ayarlar, self.sahte), simdi=SIMDI,
                            disk_kullanimi=lambda _: Kullanim(10, 1, 9), bildir=lambda *_: None)
        self.assertEqual(0, kod, s.metin)
        self.assertEqual(["/usr/bin/rclone", "listremotes", "--long"], self.cagrilar[0])
        gonderme = [c for c in self.cagrilar if c[1] == "copyto"]
        self.assertEqual([["/usr/bin/rclone", "copyto", "--immutable", str(self.yerel / dosya), "kasa-sifreli:yedekler/" + dosya]], gonderme)
        self.assertTrue(all(c[1] != "deletefile" for c in self.cagrilar))
        argumanlar = [p.lower() for c in self.cagrilar for p in c if p != str(self.yerel / dosya)]
        self.assertFalse([p for p in argumanlar if "pass" in p or "key" in p or "secret" in p], argumanlar)

    def test_kok_hedefte_yol_iki_nokta_ile_birlesir_ve_silme_deletefile_kullanir(self):
        hedef = uy.RcloneHedefi("kasa-sifreli:", calistir=self.sahte)
        self.uzak["kasa-elle-20260101-000000-00000001.zip"] = 5
        hedef.sil("kasa-elle-20260101-000000-00000001.zip")
        self.assertEqual(["rclone", "deletefile", "kasa-sifreli:kasa-elle-20260101-000000-00000001.zip"], self.cagrilar[-1])

    def test_rclone_calistirilamazsa_yapilandirma_hatasi_ve_cikis_kodu_2(self):
        def yok(_):
            return 127, "", "No such file or directory: 'rclone'"
        with Sessiz() as s:
            kod = uy.main(["gonder"], {"KASA_DEPLOY_ENV": str(self.yerel / "yok.env"), "KASA_BACKUP_DIR": str(self.yerel),
                                       "KASA_UZAK_HEDEF": "kasa-sifreli:yedekler"}, calistir=yok)
        self.assertEqual(2, kod)
        self.assertIn("rclone", s.metin)


class DogrulaVeIndirTests(unittest.TestCase):
    def setUp(self):
        self._tmp = tempfile.TemporaryDirectory()
        kok = Path(self._tmp.name)
        self.uzak, self.cikti = kok / "uzak", kok / "cikti"
        self.uzak.mkdir()
        self.cikti.mkdir()
        self.ayarlar = uy.ayarlari_oku({"KASA_DEPLOY_ENV": str(kok / "yok.env"), "KASA_UZAK_YONTEM": "dizin",
                                        "KASA_UZAK_HEDEF": str(self.uzak), "KASA_UZAK_SIFRESIZ": "evet",
                                        "KASA_UZAK_DOGRULA_IZLEME_URL": "https://izleme.example/ping/dogrula"})
        self.hedef = uy.hedef_olustur(self.ayarlar)
        self.bildirimler = []

    def tearDown(self):
        self._tmp.cleanup()

    def dogrula(self):
        with Sessiz() as s:
            kod = uy.dogrula(self.ayarlar, self.hedef, simdi=SIMDI, bildir=lambda url, ok: self.bildirimler.append((url, ok)))
        return kod, s.metin

    def test_en_yeni_uzak_kopya_geri_acilarak_sinanir(self):
        yedek_zip(self.uzak, ad("oto-", SIMDI - timedelta(days=2), 1))
        en_yeni = yedek_zip(self.uzak, ad("oto-", SIMDI - timedelta(hours=5), 2)).name
        yedek_zip(self.uzak, ad("elle-", SIMDI - timedelta(hours=1), 3), tur="elle")
        kod, metin = self.dogrula()
        self.assertEqual(0, kod, metin)
        self.assertIn(en_yeni, metin)
        self.assertEqual([("https://izleme.example/ping/dogrula", True)], self.bildirimler)

    def test_bozuk_ya_da_eski_uzak_kopya_hata_verir(self):
        yedek_zip(self.uzak, ad("oto-", SIMDI - timedelta(hours=5), 2), ozet_boz=True)
        self.assertEqual(1, self.dogrula()[0])
        self.assertEqual(("https://izleme.example/ping/dogrula", False), self.bildirimler[-1])
        for p in self.uzak.iterdir():
            p.unlink()
        yedek_zip(self.uzak, ad("oto-", SIMDI - timedelta(days=4), 2))
        kod, metin = self.dogrula()
        self.assertEqual(1, kod)
        self.assertIn("saat önce", metin)

    def test_indir_dogrulanmis_kopyayi_yazar_ve_var_olan_dosyanin_uzerine_yazmaz(self):
        dosya = yedek_zip(self.uzak, ad("oto-", SIMDI - timedelta(hours=5), 2)).name
        with Sessiz():
            self.assertEqual(0, uy.indir(self.hedef, dosya, self.cikti))
            self.assertEqual(1, uy.indir(self.hedef, dosya, self.cikti))
            self.assertEqual(2, uy.indir(self.hedef, "../" + dosya, self.cikti))
        self.assertEqual([dosya], sorted(p.name for p in self.cikti.iterdir()))
        uy.arsiv_dogrula(self.cikti / dosya)


class KomutSatiriTests(unittest.TestCase):
    def test_betikler_derlenir(self):
        with tempfile.TemporaryDirectory() as d:
            for betik in ("uzak_yedek.py", "restore_backup.py"):
                py_compile.compile(str(DEPLOY / betik), cfile=str(Path(d) / (betik + "c")), doraise=True)

    def test_komut_satirindan_kuru_calistirma_hedefe_dokunmaz(self):
        with tempfile.TemporaryDirectory() as d:
            kok = Path(d)
            yerel, uzak = kok / "yedekler", kok / "uzak"
            yerel.mkdir()
            uzak.mkdir()
            dosya = yedek_zip(yerel, ad("oto-", datetime.now(timezone.utc) - timedelta(minutes=5), 1)).name
            ortam = dict(os.environ, PYTHONIOENCODING="utf-8", KASA_DEPLOY_ENV=str(kok / "yok.env"), KASA_BACKUP_DIR=str(yerel),
                         KASA_UZAK_YONTEM="dizin", KASA_UZAK_HEDEF=str(uzak), KASA_UZAK_SIFRESIZ="evet", KASA_DISK_ESIK_YUZDE="100")
            ortam.pop("KASA_DATA_DIR", None)
            ortam.pop("KASA_UZAK_IZLEME_URL", None)
            p = subprocess.run([sys.executable, str(DEPLOY / "uzak_yedek.py"), "gonder", "--kuru"], env=ortam,
                               capture_output=True, text=True, encoding="utf-8", timeout=120)
            self.assertEqual(0, p.returncode, p.stdout + p.stderr)
            self.assertIn("[kuru] gönderilecek: " + dosya, p.stdout)
            self.assertEqual([], list(uzak.iterdir()))

            p = subprocess.run([sys.executable, str(DEPLOY / "uzak_yedek.py"), "--help"], capture_output=True, text=True,
                               encoding="utf-8", env=dict(os.environ, PYTHONIOENCODING="utf-8"), timeout=60)
            self.assertEqual(0, p.returncode)
            self.assertIn("dogrula", p.stdout)


if __name__ == "__main__":
    unittest.main()
