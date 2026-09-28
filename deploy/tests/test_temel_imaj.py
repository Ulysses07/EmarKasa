"""deploy/temel_imaj.py sınamaları (devops-12): FROM satırlarının ayrıştırılması, sabit özetin kayıtta çözülmesi, aynı
ana sürüm etiketinin güncel özetiyle karşılaştırma ve çıkış kodları.

Çalıştırma (depo kökünden): python3 -m unittest discover -s deploy/tests -v
Ağ ve docker gerekmez: 'docker buildx imagetools inspect' sahte çalıştırıcıyla sınanır.
"""
import contextlib
import io
import os
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

DEPLOY = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(DEPLOY))

import temel_imaj as ti  # noqa: E402

SDK_OZET = "sha256:" + "1" * 63 + "a"
ASPNET_OZET = "sha256:" + "2" * 63 + "b"
YENI_OZET = "sha256:" + "3" * 63 + "c"
DOCKERFILE = """# yorum
FROM mcr.microsoft.com/dotnet/sdk:10.0.401@{sdk} AS build
RUN echo derle
FROM build AS test
FROM mcr.microsoft.com/dotnet/aspnet:10.0.12@{aspnet} AS runtime
ENTRYPOINT ["dotnet", "Kasa.Api.dll"]
""".format(sdk=SDK_OZET, aspnet=ASPNET_OZET)


class Kayit:
    """Sahte kayıt: referans -> özet. Bilinmeyen referans 'docker buildx imagetools inspect' gibi 1 ile döner."""

    def __init__(self, ozetler):
        self.ozetler = dict(ozetler)
        self.cagrilar = []

    def __call__(self, komut):
        komut = list(komut)
        self.cagrilar.append(komut)
        if komut[:4] != ["docker", "buildx", "imagetools", "inspect"] or komut[5:] != ["--format", "{{json .Manifest.Digest}}"]:
            return 1, "", "beklenmeyen komut"
        if komut[4] not in self.ozetler:
            return 1, "", "ERROR: {}: not found".format(komut[4])
        return 0, '"{}"\n'.format(self.ozetler[komut[4]]), ""


def guncel_kayit():
    return Kayit({
        "mcr.microsoft.com/dotnet/sdk:10.0.401@" + SDK_OZET: SDK_OZET,
        "mcr.microsoft.com/dotnet/sdk:10.0": SDK_OZET,
        "mcr.microsoft.com/dotnet/aspnet:10.0.12@" + ASPNET_OZET: ASPNET_OZET,
        "mcr.microsoft.com/dotnet/aspnet:10.0": ASPNET_OZET,
    })


class TemelImajTests(unittest.TestCase):
    def setUp(self):
        self._tmp = tempfile.TemporaryDirectory()
        self.dockerfile = Path(self._tmp.name) / "Dockerfile"
        self.dockerfile.write_text(DOCKERFILE, encoding="utf-8")

    def tearDown(self):
        self._tmp.cleanup()

    def denetle(self, kayit, github=False):
        out, err = io.StringIO(), io.StringIO()
        with contextlib.redirect_stdout(out), contextlib.redirect_stderr(err):
            kod = ti.denetle(self.dockerfile, calistir=kayit, github=github)
        return kod, out.getvalue() + err.getvalue()

    def test_from_satirlari_ayristirilir_onceki_asama_dis_imaj_sayilmaz(self):
        imajlar = ti.imajlar(DOCKERFILE)
        self.assertEqual([("mcr.microsoft.com/dotnet/sdk", "10.0.401", "10.0", SDK_OZET),
                          ("mcr.microsoft.com/dotnet/aspnet", "10.0.12", "10.0", ASPNET_OZET)],
                         [(i.depo, i.etiket, i.ana, i.ozet) for i in imajlar])

    def test_depodaki_dockerfile_iki_dotnet_imajini_sabit_ozetle_tasir(self):
        imajlar = ti.imajlar((DEPLOY.parent / "Dockerfile").read_text(encoding="utf-8"))
        self.assertEqual(["mcr.microsoft.com/dotnet/sdk", "mcr.microsoft.com/dotnet/aspnet"], [i.depo for i in imajlar])

    def test_guncel_ozetler_0_ile_doner_ve_yalniz_meta_veri_okur(self):
        kayit = guncel_kayit()
        kod, metin = self.denetle(kayit)
        self.assertEqual(0, kod, metin)
        self.assertEqual(2, metin.count(" güncel."))
        self.assertEqual({"inspect"}, {c[3] for c in kayit.cagrilar})  # pull ya da build yok
        self.assertEqual(4, len(kayit.cagrilar))

    def test_daha_yeni_ozet_varsa_1_ile_doner_ve_iki_ozeti_yazar(self):
        kayit = guncel_kayit()
        kayit.ozetler["mcr.microsoft.com/dotnet/aspnet:10.0"] = YENI_OZET
        kod, metin = self.denetle(kayit)
        self.assertEqual(1, kod)
        self.assertIn(YENI_OZET, metin)
        self.assertIn(ASPNET_OZET, metin)
        self.assertIn("operasyon-runbook.md", metin)

        kod, metin = self.denetle(kayit, github=True)
        self.assertEqual(1, kod)
        self.assertIn("::warning file=Dockerfile::mcr.microsoft.com/dotnet/aspnet:10.0", metin)

    def test_sabitlenmemis_from_satiri_docker_cagrilmadan_2_ile_doner(self):
        self.dockerfile.write_text("FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime\n", encoding="utf-8")
        kayit = guncel_kayit()
        kod, metin = self.denetle(kayit, github=True)
        self.assertEqual(2, kod)
        self.assertIn("::error file=Dockerfile::", metin)
        self.assertIn("sabitlenmemiş", metin)
        self.assertEqual([], kayit.cagrilar)

    def test_kayitta_cozulmeyen_ozet_2_ile_doner(self):
        kayit = guncel_kayit()
        del kayit.ozetler["mcr.microsoft.com/dotnet/sdk:10.0.401@" + SDK_OZET]
        kod, metin = self.denetle(kayit)
        self.assertEqual(2, kod)
        self.assertIn("çözülemedi", metin)

    def test_docker_calistirilamazsa_2_ile_doner(self):
        kod, metin = self.denetle(lambda _: (127, "", "No such file or directory: 'docker'"))
        self.assertEqual(2, kod)
        self.assertIn("docker", metin)

    def test_dockerfile_yoksa_ya_da_dis_imaj_yoksa_2_ile_doner(self):
        self.dockerfile.write_text("FROM scratch\n", encoding="utf-8")
        self.assertEqual(2, self.denetle(guncel_kayit())[0])
        self.dockerfile.unlink()
        self.assertEqual(2, self.denetle(guncel_kayit())[0])

    def test_komut_satiri(self):
        ortam = dict(os.environ, PYTHONIOENCODING="utf-8")
        p = subprocess.run([sys.executable, str(DEPLOY / "temel_imaj.py"), "--help"], capture_output=True, text=True,
                           encoding="utf-8", env=ortam, timeout=60)
        self.assertEqual(0, p.returncode)
        self.assertIn("--dockerfile", p.stdout)
        p = subprocess.run([sys.executable, str(DEPLOY / "temel_imaj.py"), "--dockerfile", str(Path(self._tmp.name) / "yok")],
                           capture_output=True, text=True, encoding="utf-8", env=ortam, timeout=60)
        self.assertEqual(2, p.returncode)


if __name__ == "__main__":
    unittest.main()
