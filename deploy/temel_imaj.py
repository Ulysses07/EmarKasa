"""Dockerfile'daki sabit temel imaj özetlerinin kayıtta çözüldüğünü ve güncel olduğunu denetler (devops-12).

Her dış FROM satırı etiket + @sha256 özetiyle sabittir (biçimi Kasa.Api.Tests/DepoHijyeniTests de denetler). .NET,
OpenSSL ve Debian yamaları ancak özet güncellenince imaja girer. Bu betik yalnız kayıt meta verisini okur
('docker buildx imagetools inspect'), imaj indirmez: sabit özetin kayıtta bulunduğunu doğrular ve aynı ana sürüm
etiketinin (ör. aspnet:10.0) güncel özetiyle karşılaştırır. CI'daki 'Pinned base images' işi ve sunucudaki güncelleme
akışı (deploy/README.md "Güncelleme" 6. adım) aynı denetimi bu betikle yapar.

Kullanım:
  python3 deploy/temel_imaj.py [--dockerfile YOL] [--github]

Çıkış kodu: 0 bütün özetler güncel; 1 daha yeni özet var (önce docs/deploy/operasyon-runbook.md "Özet güncelleme");
2 Dockerfile okunamadı, FROM satırı sabitlenmemiş, özet kayıtta çözülemedi ya da docker çalıştırılamadı.
Sınama: python3 -m unittest discover -s deploy/tests
"""
from __future__ import annotations

import argparse
import re
import subprocess
import sys
from dataclasses import dataclass
from pathlib import Path
from typing import Callable, List, Optional, Sequence, Tuple

GUNCELLEME = "docs/deploy/operasyon-runbook.md 'Özet güncelleme'"
_FROM = re.compile(r"^FROM\s+(?P<kaynak>\S+)(?:\s+AS\s+(?P<asama>\S+))?\s*$", re.IGNORECASE)
# <depo>:<ana sürüm><yama ve ek>@sha256:<64 küçük onaltılık>; ör. mcr.microsoft.com/dotnet/aspnet:10.0.12@sha256:...
_SABIT = re.compile(r"^(?P<depo>[^@:\s]+):(?P<etiket>(?P<ana>[0-9]+\.[0-9]+)[^@\s]*)@(?P<ozet>sha256:[0-9a-f]{64})$")

Calistirici = Callable[[Sequence[str]], Tuple[int, str, str]]


class DenetimHatasi(Exception):
    """Dockerfile ya da kayıt beklenen durumda değil (çıkış kodu 2)."""


@dataclass(frozen=True)
class Imaj:
    depo: str
    etiket: str
    ana: str
    ozet: str

    @property
    def sabit(self) -> str:
        return "{}:{}@{}".format(self.depo, self.etiket, self.ozet)

    @property
    def degisken(self) -> str:
        return "{}:{}".format(self.depo, self.ana)


def imajlar(dockerfile: str) -> List[Imaj]:
    """Dış temel imajlar. Önceki aşamadan türeyen 'FROM build AS x' dış imaj değildir; 'scratch' sabitlenecek bir şey
    taşımaz. Etiket + @sha256 özetiyle sabitlenmemiş dış imaj DenetimHatasi'dır."""
    sonuc: List[Imaj] = []
    asamalar = {"scratch"}
    for satir in dockerfile.splitlines():
        satir = satir.strip()
        if not re.match(r"^FROM\s", satir, re.IGNORECASE):
            continue
        m = _FROM.match(satir)
        if not m:
            raise DenetimHatasi("'{}' satırı 'FROM <imaj>[ AS <aşama>]' biçiminde değil.".format(satir))
        kaynak = m.group("kaynak")
        if kaynak.lower() not in asamalar:
            s = _SABIT.match(kaynak)
            if not s:
                raise DenetimHatasi("'{}' etiket ve @sha256 özetiyle sabitlenmemiş.".format(kaynak))
            sonuc.append(Imaj(s.group("depo"), s.group("etiket"), s.group("ana"), s.group("ozet")))
        if m.group("asama"):
            asamalar.add(m.group("asama").lower())
    if not sonuc:
        raise DenetimHatasi("Dockerfile'da sabitlenmiş dış temel imaj yok.")
    return sonuc


def _calistir(komut: Sequence[str]) -> Tuple[int, str, str]:
    try:
        p = subprocess.run(list(komut), capture_output=True, text=True, encoding="utf-8", errors="replace", timeout=120)
    except OSError as e:
        return 127, "", str(e)
    except subprocess.TimeoutExpired:
        return 124, "", "zaman aşımı (120 sn)"
    return p.returncode, p.stdout, p.stderr


def _son_satir(metin: str) -> str:
    satirlar = [s.strip() for s in metin.splitlines() if s.strip()]
    return (satirlar[-1] if satirlar else "ayrıntı yok")[:300]


def kayittaki_ozet(referans: str, calistir: Calistirici) -> str:
    """Referansın kayıttaki manifest (çoklu mimari listesi) özeti; imaj indirilmez."""
    kod, cikti, hata = calistir(["docker", "buildx", "imagetools", "inspect", referans, "--format", "{{json .Manifest.Digest}}"])
    if kod == 127:
        raise DenetimHatasi("docker çalıştırılamadı ({}); docker ve buildx eklentisi gerekir.".format(_son_satir(hata)))
    ozet = cikti.strip().strip('"')
    if kod != 0 or not re.fullmatch(r"sha256:[0-9a-f]{64}", ozet):
        raise DenetimHatasi("'{}' kayıtta çözülemedi: {}".format(referans, _son_satir(hata or cikti)))
    return ozet


def denetle(dockerfile: Path, calistir: Optional[Calistirici] = None, github: bool = False) -> int:
    calistir = calistir or _calistir

    def hata(ileti: str) -> int:
        # GitHub Actions iş akışı komutlarını (::error) standart çıktıdan okur.
        print(("::error file=Dockerfile::" if github else "HATA: ") + ileti, file=sys.stdout if github else sys.stderr)
        return 2

    try:
        liste = imajlar(Path(dockerfile).read_text(encoding="utf-8"))
    except OSError as e:
        return hata("{} okunamadı ({}).".format(dockerfile, e.__class__.__name__))
    except DenetimHatasi as e:
        return hata("{} Özet uydurmayın; {} adımlarıyla kayıttan okuyun.".format(e, GUNCELLEME))
    eski = False
    try:
        for imaj in liste:
            if kayittaki_ozet(imaj.sabit, calistir) != imaj.ozet:
                return hata("'{}' kayıtta başka bir özete çözüldü.".format(imaj.sabit))
            guncel = kayittaki_ozet(imaj.degisken, calistir)
            if guncel == imaj.ozet:
                print("{} güncel.".format(imaj.sabit))
                continue
            eski = True
            ileti = "{} için daha yeni özet var: {} (sabit: {}). Önce özeti güncelleyin: {}.".format(
                imaj.degisken, guncel, imaj.ozet, GUNCELLEME)
            print(("::warning file=Dockerfile::" if github else "ESKİ: ") + ileti)
    except DenetimHatasi as e:
        return hata(str(e))
    return 1 if eski else 0


def main(argv: Optional[Sequence[str]] = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--dockerfile", type=Path, default=Path(__file__).resolve().parents[1] / "Dockerfile",
                        help="denetlenecek Dockerfile (varsayılan: depo kökündeki)")
    parser.add_argument("--github", action="store_true", help="GitHub Actions uyarı/hata notu biçiminde yaz")
    args = parser.parse_args(argv)
    return denetle(args.dockerfile, github=args.github)


if __name__ == "__main__":
    sys.exit(main())
