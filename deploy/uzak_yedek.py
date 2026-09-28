"""Kasa yedeklerini sunucu dışına kopyalar (devops-9).

Uygulama günlük ve elle alınan yedekleri canlı veritabanıyla aynı diske (KASA_BACKUP_DIR) yazar; VPS kaybında
ikisi birlikte gider. Bu betik yedek dizinindeki ZIP'lerden yalnız manifest SHA-256 özeti doğrulananları uzak hedefe
kopyalar, hedefte saklama kuralını uygular, uygulamanın yedek almayı bırakıp bırakmadığını ve disk doluluğunu
denetler, sonucu isteğe bağlı bir izleme adresine ("ölü adam anahtarı", ör. healthchecks.io) bildirir.

Kimlik bilgisi depoda ve bu betikte yoktur: uzak depo ve şifreleme ('crypt') rclone yapılandırmasındadır
(/root/.config/rclone/rclone.conf, izin 600). Ayarlar ortam değişkenidir; sunucuda /etc/kasa/uzak-yedek.env
(örnek: deploy/uzak-yedek.env.example). KASA_BACKUP_DIR ve KASA_DATA_DIR verilmezse /opt/kasa/deploy/.env'den
(KASA_DEPLOY_ENV) yalnız bu iki değer okunur; yayın veri/yedek dizinini değiştirince betik de onu izler.
Kurulum, zamanlayıcı ve geri dönüş: docs/deploy/operasyon-runbook.md "Sunucu dışı yedek".

Komutlar:
  python3 uzak_yedek.py gonder [--kuru]          yeni ve doğrulanmış yedekleri gönderir, hedefte saklamayı uygular
  python3 uzak_yedek.py listele                  uzaktaki yedekleri listeler
  python3 uzak_yedek.py indir AD --cikti DIZIN   uzak kopyayı indirir, manifest özetini doğrular
  python3 uzak_yedek.py dogrula                  en yeni uzak kopyayı geçici dizine indirip restore_backup.py ile açar

Çıkış kodu: 0 başarılı; 1 işlem hatası (doğrulanamayan yedek, gönderme/silme hatası, eski yedek, disk eşiği);
2 yapılandırma hatası.
Sınama: python3 -m unittest discover -s deploy/tests
"""
from __future__ import annotations

import argparse
import hashlib
import json
import math
import os
import re
import shutil
import subprocess
import sys
import tempfile
import urllib.request
import zipfile
from dataclasses import dataclass
from datetime import datetime, timedelta, timezone
from pathlib import Path
from typing import Callable, Dict, Iterable, List, Mapping, Optional, Sequence, Tuple

try:
    from zoneinfo import ZoneInfo

    _ISTANBUL = ZoneInfo("Europe/Istanbul")
except Exception:  # Python 3.8 ya da tzdata yok: Türkiye 2016'dan beri sabit UTC+3.
    _ISTANBUL = timezone(timedelta(hours=3))

# Sunucunun saklama kuralıyla aynı sabitler (Kasa.Api/Servisler/YedekServisi.cs, YedekSaklama). Gün ve ay sayısı
# hedefte ayrıca ayarlanır (varsayılan sunucudan uzun: 35 gün, 13 ay).
OTOMATIK_EN_AZ = 7
ELLE_EN_FAZLA = 10
VARSAYILAN_DEPLOY_ENV = "/opt/kasa/deploy/.env"

# YedekSaklama ve restore_backup.py ile aynı ad kalıbı; zaman UTC. '.part' (yazılmakta olan), gizli geçici
# dosyalar ve operatörün koyduğu başka adlar yedek sayılmaz, hedefte de hiç silinmez.
_AD = re.compile(r"^kasa-(?:(oto|elle|goc-oncesi)-)?([0-9]{8}-[0-9]{6})-[0-9a-f]{8}\.zip$")
_TURLER = {"oto": "otomatik", "elle": "elle", "goc-oncesi": "goc-oncesi"}
_ICERIKLER = (["kasa.db", "manifest.json"], [".kasa-push-keys.json", "kasa.db", "manifest.json"])


class YapilandirmaHatasi(Exception):
    """Ayar eksik ya da geçersiz; hiçbir şey gönderilmez (çıkış kodu 2)."""


class ArsivHatasi(Exception):
    """Yedek ZIP'i beklenen biçimde değil ya da manifest özetiyle eşleşmiyor; gönderilmez."""


class HedefHatasi(Exception):
    """Uzak hedefte listeleme, gönderme, silme ya da indirme başarısız."""


@dataclass(frozen=True)
class Yedek:
    ad: str
    tur: str  # otomatik | elle | goc-oncesi
    zaman: datetime


def tani(ad: str) -> Optional[Yedek]:
    """Sunucunun verdiği yedek adından tür ve UTC zamanı; kalıba uymayan ad için None."""
    m = _AD.match(ad)
    if not m:
        return None
    try:
        zaman = datetime.strptime(m.group(2), "%Y%m%d-%H%M%S").replace(tzinfo=timezone.utc)
    except ValueError:
        return None
    return Yedek(ad, _TURLER.get(m.group(1) or "oto", "otomatik"), zaman)


def _ay(zaman: datetime) -> int:
    yerel = zaman.astimezone(_ISTANBUL)
    return yerel.year * 12 + yerel.month - 1


def silinecekler(adlar: Iterable[str], simdi: datetime, gunluk_gun: int, aylik_ay: int) -> List[str]:
    """Sunucunun saklama kuralı (YedekSaklama.Silinecekler), gün ve ay sayısı parametreli.

    Otomatik: son 'gunluk_gun' günün hepsi, içinde bulunulan ay dahil son 'aylik_ay' takvim ayının (İstanbul) ilk
    yedeği ve her durumda en yeni 7'si kalır. Elle: en yeni 10'u kalır. Göç öncesi yedekler ve kalıba uymayan
    dosyalar hiçbir zaman silinmez.
    """
    yedekler = [y for y in map(tani, set(adlar)) if y is not None]
    sirala = lambda liste: sorted(liste, key=lambda y: (y.zaman, y.ad), reverse=True)  # noqa: E731
    elle = sirala(y for y in yedekler if y.tur == "elle")
    otomatik = sirala(y for y in yedekler if y.tur == "otomatik")
    tut = {y.ad for y in otomatik[:OTOMATIK_EN_AZ]}
    tut |= {y.ad for y in otomatik if simdi - y.zaman < timedelta(days=gunluk_gun)}
    ilk_ay = _ay(simdi) - (aylik_ay - 1)
    aylik: Dict[int, str] = {}
    for y in reversed(otomatik):  # eskiden yeniye: her ayın ilk yedeği
        if _ay(y.zaman) >= ilk_ay:
            aylik.setdefault(_ay(y.zaman), y.ad)
    tut |= set(aylik.values())
    return sorted([y.ad for y in elle[ELLE_EN_FAZLA:]] + [y.ad for y in otomatik if y.ad not in tut])


def arsiv_dogrula(yol: Path) -> dict:
    """ZIP içeriğini restore_backup.py'nin kurallarıyla ve kasa.db'nin SHA-256 özetini manifestle karşılaştırır.

    Sunucu özeti, bütünlüğü (integrity_check) ve ilişkileri doğruladığı kopyadan hesaplar; eşleşen özet uzağa giden
    baytların o doğrulanmış kopya olduğunu gösterir. ZIP'in kendi CRC denetimi de okurken yapılır. Manifesti döner.
    """
    try:
        with zipfile.ZipFile(yol) as arsiv:
            if sorted(arsiv.namelist()) not in _ICERIKLER:
                raise ArsivHatasi("beklenmeyen içerik: " + ", ".join(sorted(arsiv.namelist())[:5]))
            if arsiv.getinfo("manifest.json").file_size > 8192:
                raise ArsivHatasi("manifest.json beklenenden büyük")
            manifest = json.loads(arsiv.read("manifest.json"))
            if not isinstance(manifest, dict):
                raise ArsivHatasi("manifest.json bir nesne değil")
            beklenen = manifest.get("sha256")
            if not isinstance(beklenen, str) or not re.fullmatch(r"[0-9A-Fa-f]{64}", beklenen):
                raise ArsivHatasi("manifestte sha256 özeti yok")
            ozet = hashlib.sha256()
            with arsiv.open("kasa.db") as kaynak:
                for parca in iter(lambda: kaynak.read(1024 * 1024), b""):
                    ozet.update(parca)
            if ozet.hexdigest().upper() != beklenen.upper():
                raise ArsivHatasi("kasa.db özeti manifestteki sha256 ile eşleşmiyor")
            if ".kasa-push-keys.json" in arsiv.namelist():
                anahtar = arsiv.read(".kasa-push-keys.json")
                if not manifest.get("bildirimAnahtariDahil") or \
                        hashlib.sha256(anahtar).hexdigest().upper() != str(manifest.get("bildirimAnahtariSha256") or "").upper():
                    raise ArsivHatasi("bildirim anahtarı özeti manifestle eşleşmiyor")
            elif manifest.get("bildirimAnahtariDahil"):
                raise ArsivHatasi("manifestte bildirilen bildirim anahtarı arşivde yok")
    except ArsivHatasi:
        raise
    except (zipfile.BadZipFile, zipfile.LargeZipFile, KeyError, ValueError, OSError, RuntimeError, EOFError,
            NotImplementedError) as e:
        raise ArsivHatasi("arşiv okunamadı ({})".format(e.__class__.__name__)) from e
    yedek = tani(Path(yol).name)
    # 2.3 öncesi manifestlerde 'tur' yoktur; varsa addaki türle aynı olmalıdır.
    if yedek is not None and manifest.get("tur") is not None and manifest.get("tur") != yedek.tur:
        raise ArsivHatasi("addaki tür ({}) manifestteki türle ({}) çelişiyor".format(yedek.tur, manifest.get("tur")))
    return manifest


# ---------------------------------------------------------------- ayarlar

@dataclass(frozen=True)
class Ayarlar:
    yedek_dizini: Optional[Path]
    veri_dizini: Optional[Path]
    yontem: str
    hedef: str
    sifresiz_izin: bool
    saklama: bool
    gunluk_gun: int
    aylik_ay: int
    en_fazla_saat: int
    disk_esik: int
    izleme_url: Optional[str]
    dogrula_izleme_url: Optional[str]
    rclone: str


def _env_dosyasi(yol: Path, anahtarlar: Sequence[str]) -> Dict[str, str]:
    """deploy/.env'den yalnız istenen anahtarları okur; sırlar (JWT anahtarı, şifreler) okunmaz, taşınmaz."""
    sonuc: Dict[str, str] = {}
    for satir in yol.read_text(encoding="utf-8-sig").splitlines():
        anahtar, esit, deger = satir.strip().partition("=")
        anahtar = anahtar.strip()
        if esit and anahtar in anahtarlar:
            deger = deger.strip()
            if len(deger) >= 2 and deger[0] == deger[-1] and deger[0] in "'\"":
                deger = deger[1:-1]
            sonuc[anahtar] = deger
    return sonuc


def _tam(ortam: Mapping[str, str], anahtar: str, varsayilan: int, en_az: int, en_cok: int) -> int:
    deger = (ortam.get(anahtar) or "").strip()
    if not deger:
        return varsayilan
    if not re.fullmatch(r"[0-9]+", deger) or not en_az <= int(deger) <= en_cok:
        raise YapilandirmaHatasi("{} {}-{} arasında tam sayı olmalı (verilen: {!r}).".format(anahtar, en_az, en_cok, deger))
    return int(deger)


def _evet_hayir(ortam: Mapping[str, str], anahtar: str, varsayilan: bool) -> bool:
    deger = (ortam.get(anahtar) or "").strip().lower()
    if not deger:
        return varsayilan
    if deger in ("evet", "1", "true"):
        return True
    if deger in ("hayir", "hayır", "0", "false"):
        return False
    raise YapilandirmaHatasi("{} 'evet' ya da 'hayir' olmalı (verilen: {!r}).".format(anahtar, deger))


def _adres(ortam: Mapping[str, str], anahtar: str) -> Optional[str]:
    deger = (ortam.get(anahtar) or "").strip()
    if deger and not re.match(r"^https?://[^\s/]+", deger):
        raise YapilandirmaHatasi("{} http(s) adresi olmalı.".format(anahtar))
    return deger or None


def ayarlari_oku(ortam: Mapping[str, str]) -> Ayarlar:
    deploy_env = Path(ortam.get("KASA_DEPLOY_ENV") or VARSAYILAN_DEPLOY_ENV)
    dosya: Dict[str, str] = {}
    if deploy_env.is_file():
        try:
            dosya = _env_dosyasi(deploy_env, ("KASA_BACKUP_DIR", "KASA_DATA_DIR"))
        except (OSError, UnicodeDecodeError) as e:
            raise YapilandirmaHatasi("{} okunamadı ({}).".format(deploy_env, e.__class__.__name__)) from e
    yedek = (ortam.get("KASA_BACKUP_DIR") or dosya.get("KASA_BACKUP_DIR") or "").strip()
    veri = (ortam.get("KASA_DATA_DIR") or dosya.get("KASA_DATA_DIR") or "").strip()
    yontem = (ortam.get("KASA_UZAK_YONTEM") or "rclone").strip().lower()
    if yontem not in ("rclone", "dizin"):
        raise YapilandirmaHatasi("KASA_UZAK_YONTEM 'rclone' ya da 'dizin' olmalı (verilen: {!r}).".format(yontem))
    hedef = (ortam.get("KASA_UZAK_HEDEF") or "").strip()
    if not hedef:
        raise YapilandirmaHatasi("KASA_UZAK_HEDEF tanımsız (ör. kasa-sifreli:yedekler); /etc/kasa/uzak-yedek.env dosyasına yazın.")
    return Ayarlar(
        yedek_dizini=Path(yedek) if yedek else None,
        veri_dizini=Path(veri) if veri else None,
        yontem=yontem,
        hedef=hedef,
        sifresiz_izin=_evet_hayir(ortam, "KASA_UZAK_SIFRESIZ", False),
        saklama=_evet_hayir(ortam, "KASA_UZAK_SAKLAMA", True),
        gunluk_gun=_tam(ortam, "KASA_UZAK_GUNLUK_GUN", 35, 1, 3650),
        aylik_ay=_tam(ortam, "KASA_UZAK_AYLIK_AY", 13, 1, 600),
        en_fazla_saat=_tam(ortam, "KASA_YEDEK_EN_FAZLA_SAAT", 48, 1, 24 * 60),
        disk_esik=_tam(ortam, "KASA_DISK_ESIK_YUZDE", 80, 1, 100),
        izleme_url=_adres(ortam, "KASA_UZAK_IZLEME_URL"),
        dogrula_izleme_url=_adres(ortam, "KASA_UZAK_DOGRULA_IZLEME_URL"),
        rclone=(ortam.get("KASA_RCLONE") or "rclone").strip(),
    )


# ---------------------------------------------------------------- hedefler

Calistirici = Callable[[Sequence[str]], Tuple[int, str, str]]


def _calistir(komut: Sequence[str]) -> Tuple[int, str, str]:
    # Ayar dosyasında boş bırakılmış RCLONE_CONFIG rclone'u yapılandırmasız (bellekte) çalıştırır; boşsa aktarılmaz.
    ortam = {k: v for k, v in os.environ.items() if not (k == "RCLONE_CONFIG" and not v.strip())}
    try:
        p = subprocess.run(list(komut), capture_output=True, text=True, encoding="utf-8", errors="replace", env=ortam)
    except OSError as e:
        return 127, "", str(e)
    return p.returncode, p.stdout, p.stderr


def _son_satir(metin: str) -> str:
    satirlar = [s.strip() for s in metin.splitlines() if s.strip()]
    return (satirlar[-1] if satirlar else "ayrıntı yok")[:300]


class RcloneHedefi:
    """rclone uzağı (tercihen 'crypt'). Uzak ve anahtarları rclone.conf'tadır; komut satırına kimlik bilgisi girmez."""

    def __init__(self, hedef: str, rclone: str = "rclone", calistir: Optional[Calistirici] = None):
        self.hedef = hedef
        self.rclone = rclone
        self.calistir = calistir or _calistir

    def _yol(self, ad: str) -> str:
        return self.hedef + ad if self.hedef.endswith(":") else self.hedef.rstrip("/") + "/" + ad

    def _komut(self, *arg: str) -> Tuple[int, str, str]:
        return self.calistir([self.rclone, *arg])

    def uzak_turleri(self) -> Dict[str, str]:
        kod, cikti, hata = self._komut("listremotes", "--long")
        if kod != 0:
            raise YapilandirmaHatasi("rclone çalıştırılamadı ({}): {}".format(self.rclone, _son_satir(hata or cikti)))
        turler = {}
        for satir in cikti.splitlines():
            ad, _, tur = satir.strip().partition(":")
            if ad:
                turler[ad] = tur.strip()
        return turler

    def listele(self) -> Dict[str, int]:
        kod, cikti, hata = self._komut("lsf", "--files-only", "--format", "ps", "--separator", "\t", self.hedef)
        if kod != 0:
            if "directory not found" in hata.lower():  # ilk çalıştırma: klasör ilk gönderimde oluşur
                return {}
            raise HedefHatasi("Uzak hedef listelenemedi: " + _son_satir(hata))
        sonuc = {}
        for satir in cikti.splitlines():
            ad, _, boyut = satir.rpartition("\t")
            if ad and boyut.strip().isdigit():
                sonuc[ad] = int(boyut)
        return sonuc

    def gonder(self, yerel: Path, ad: str) -> None:
        # --immutable: hedefte aynı adla farklı içerik varsa üzerine yazmaz, hata verir.
        kod, _, hata = self._komut("copyto", "--immutable", str(yerel), self._yol(ad))
        if kod != 0:
            raise HedefHatasi("{} gönderilemedi: {}".format(ad, _son_satir(hata)))

    def sil(self, ad: str) -> None:
        kod, _, hata = self._komut("deletefile", self._yol(ad))
        if kod != 0:
            raise HedefHatasi("{} hedefte silinemedi: {}".format(ad, _son_satir(hata)))

    def indir(self, ad: str, yerel: Path) -> None:
        kod, _, hata = self._komut("copyto", self._yol(ad), str(yerel))
        if kod != 0:
            raise HedefHatasi("{} indirilemedi: {}".format(ad, _son_satir(hata)))


class DizinHedefi:
    """Bağlı bir dizin (ör. şifreli bağlı disk) ve sınamalar için. Şifreleme yapmaz; dizin yoksa (disk bağlı
    değilse) yerel diske sessizce yazmamak için hata verir, dizin oluşturmaz."""

    def __init__(self, dizin: Path):
        self.dizin = Path(dizin)

    def _var(self) -> None:
        if not self.dizin.is_dir():
            raise HedefHatasi("Hedef dizin yok ya da bağlı değil: {}".format(self.dizin))

    def listele(self) -> Dict[str, int]:
        self._var()
        try:
            return {e.name: e.stat().st_size for e in os.scandir(self.dizin) if e.is_file()}
        except OSError as e:
            raise HedefHatasi("Hedef dizin okunamadı: {}".format(e)) from e

    def gonder(self, yerel: Path, ad: str) -> None:
        self._var()
        son, gecici = self.dizin / ad, self.dizin / (".{}.part".format(ad))
        if son.exists():
            raise HedefHatasi("{} hedefte zaten var; üzerine yazılmaz.".format(ad))
        try:
            shutil.copyfile(yerel, gecici)
            os.replace(gecici, son)
        except OSError as e:
            raise HedefHatasi("{} gönderilemedi: {}".format(ad, e)) from e
        finally:
            gecici.unlink(missing_ok=True)

    def sil(self, ad: str) -> None:
        try:
            (self.dizin / ad).unlink()
        except OSError as e:
            raise HedefHatasi("{} hedefte silinemedi: {}".format(ad, e)) from e

    def indir(self, ad: str, yerel: Path) -> None:
        self._var()
        try:
            shutil.copyfile(self.dizin / ad, yerel)
        except OSError as e:
            raise HedefHatasi("{} indirilemedi: {}".format(ad, e)) from e


def hedef_olustur(ayarlar: Ayarlar, calistir: Optional[Calistirici] = None):
    """Hedefi kurar. Finansal veri sunucu dışına şifresiz gitmez: rclone uzağı 'crypt' türünde olmalı, 'dizin'
    hedefi şifresizdir; ikisinden sapmak için KASA_UZAK_SIFRESIZ=evet bilerek yazılır."""
    if ayarlar.yontem == "dizin":
        if not ayarlar.sifresiz_izin:
            raise YapilandirmaHatasi("'dizin' hedefi şifreleme yapmaz; bilerek seçiyorsanız KASA_UZAK_SIFRESIZ=evet yazın.")
        return DizinHedefi(Path(ayarlar.hedef))
    hedef = RcloneHedefi(ayarlar.hedef, ayarlar.rclone, calistir)
    uzak = ayarlar.hedef.split(":", 1)[0]
    turler = hedef.uzak_turleri()
    if ":" not in ayarlar.hedef or uzak not in turler:
        raise YapilandirmaHatasi("'{}' rclone'da tanımlı değil (rclone listremotes --long); KASA_UZAK_HEDEF '<uzak>:<klasör>' biçiminde, "
                                 "rclone.conf'ta tanımlı bir uzağı göstermeli.".format(uzak))
    if turler[uzak] != "crypt" and not ayarlar.sifresiz_izin:
        raise YapilandirmaHatasi("'{}' uzağı '{}' türünde; yedekler şifreli gitmeli. Üzerine bir 'crypt' uzağı tanımlayıp onu gösterin "
                                 "ya da bilerek KASA_UZAK_SIFRESIZ=evet yazın.".format(uzak, turler[uzak]))
    return hedef


# ---------------------------------------------------------------- izleme

def izleme_bildir(url: Optional[str], basarili: bool) -> None:
    """healthchecks.io biçimi: başarıda adrese, hatada adres + '/fail'e GET. Bildirim hatası sonucu değiştirmez;
    adres gizli kimlik taşıdığından günlüğe yazılmaz."""
    if not url:
        return
    adres = url if basarili else url.rstrip("/") + "/fail"
    try:
        istek = urllib.request.Request(adres, headers={"User-Agent": "kasa-uzak-yedek"})
        with urllib.request.urlopen(istek, timeout=10) as yanit:  # noqa: S310 (adres yapılandırmadan, http/https denetlendi)
            yanit.read(256)
    except Exception as e:  # ağ, DNS, TLS: yalnız uyarı
        print("UYARI: izleme adresine bildirilemedi ({}).".format(e.__class__.__name__), file=sys.stderr)


def _doluluk(kullanim) -> int:
    """df ile aynı hesap: kullanılan / (kullanılan + kullanılabilir), yukarı yuvarlanmış yüzde."""
    toplam = kullanim.used + kullanim.free
    return math.ceil(kullanim.used * 100 / toplam) if toplam else 0


# ---------------------------------------------------------------- komutlar

def _yerel_yedekler(dizin: Path) -> Dict[str, Path]:
    return {e.name: Path(e.path) for e in os.scandir(dizin) if e.is_file() and tani(e.name) is not None}


def _boyut(yol: Path) -> Optional[int]:
    """Dosya boyutu; uygulamanın rotasyonu dosyayı listelemeden sonra sildiyse None."""
    try:
        return yol.stat().st_size
    except OSError:
        return None


def gonder(ayarlar: Ayarlar, hedef, kuru: bool = False, simdi: Optional[datetime] = None,
           disk_kullanimi: Callable = shutil.disk_usage, bildir: Callable = izleme_bildir) -> int:
    simdi = simdi or datetime.now(timezone.utc)
    if ayarlar.yedek_dizini is None or not ayarlar.yedek_dizini.is_dir():
        print("YAPILANDIRMA HATASI: yedek dizini yok: {} (KASA_BACKUP_DIR ya da KASA_DEPLOY_ENV).".format(ayarlar.yedek_dizini), file=sys.stderr)
        return 2
    hatalar: List[str] = []
    try:
        yerel = _yerel_yedekler(ayarlar.yedek_dizini)
    except OSError as e:
        yerel = {}
        hatalar.append("Yedek dizini okunamadı: {}".format(e))

    # Uygulama günlük yedek almayı bırakırsa gönderim "başarılı" görünmeye devam etmesin.
    otomatikler = [tani(a).zaman for a in yerel if tani(a).tur == "otomatik"]
    if not otomatikler:
        hatalar.append("Yedek dizininde otomatik yedek yok; uygulamanın günlük yedeği çalışmıyor olabilir (Ayarlar > Yedek durumu).")
    elif simdi - max(otomatikler) > timedelta(hours=ayarlar.en_fazla_saat):
        hatalar.append("En yeni otomatik yedek {:.0f} saat önce alınmış (eşik {} saat); uygulamanın günlük yedeği durmuş olabilir."
                       .format((simdi - max(otomatikler)).total_seconds() / 3600, ayarlar.en_fazla_saat))

    gonderilen: Dict[str, Optional[int]] = {}  # ad -> gönderilmeden hemen önceki yerel boyut
    silinen: List[str] = []
    dogrulanamayan = 0
    try:
        uzak = {a: b for a, b in hedef.listele().items() if tani(a) is not None}
    except HedefHatasi as e:
        uzak = None
        hatalar.append(str(e))
    if uzak is not None:
        silinecek = set(silinecekler(set(yerel) | set(uzak), simdi, ayarlar.gunluk_gun, ayarlar.aylik_ay)) if ayarlar.saklama else set()
        for ad in sorted(set(yerel) & set(uzak)):
            if _boyut(yerel[ad]) not in (None, uzak[ad]):
                hatalar.append("{}: hedefteki kopyanın boyutu ({}) yereldekinden ({}) farklı; hedefteki kopyayı inceleyin."
                               .format(ad, uzak[ad], _boyut(yerel[ad])))
        adaylar = sorted(a for a in yerel if a not in uzak)
        disarida = [a for a in adaylar if a in silinecek]
        if disarida:
            print("Hedefteki saklama süresi dışında kaldığı için gönderilmeyen eski yerel yedek: {}".format(len(disarida)))
        for ad in (a for a in adaylar if a not in silinecek):
            try:
                arsiv_dogrula(yerel[ad])
            except ArsivHatasi as e:
                if not yerel[ad].exists():  # bu arada uygulamanın saklama kuralıyla silindi: hata değil
                    print("atlandı (yerelde artık yok): " + ad)
                    continue
                dogrulanamayan += 1
                hatalar.append("{}: doğrulanamadı, gönderilmedi ({}).".format(ad, e))
                continue
            if kuru:
                print("[kuru] gönderilecek: " + ad)
                continue
            try:
                boyut = _boyut(yerel[ad])
                hedef.gonder(yerel[ad], ad)
                gonderilen[ad] = boyut
                print("gönderildi: " + ad)
            except HedefHatasi as e:
                hatalar.append(str(e))
        if gonderilen:
            try:
                son = hedef.listele()
            except HedefHatasi as e:
                son = {}
                hatalar.append(str(e))
            for ad, boyut in gonderilen.items():
                if son.get(ad) != boyut:
                    hatalar.append("{}: gönderildikten sonra hedefte doğru boyutla görünmüyor.".format(ad))
        for ad in sorted(silinecek & set(uzak)):
            if kuru:
                print("[kuru] hedefte silinecek: " + ad)
                continue
            try:
                hedef.sil(ad)
                silinen.append(ad)
                print("hedefte saklama süresi dolan kopya silindi: " + ad)
            except HedefHatasi as e:
                hatalar.append(str(e))

    for etiket, yol in (("yedek dizini", ayarlar.yedek_dizini), ("veri dizini", ayarlar.veri_dizini)):
        if yol is None:
            continue
        try:
            yuzde = _doluluk(disk_kullanimi(str(yol)))
        except OSError as e:
            hatalar.append("{} için disk doluluğu okunamadı ({}): {}".format(etiket, yol, e))
            continue
        if yuzde >= ayarlar.disk_esik:
            hatalar.append("Disk doluluğu %{} ({}: {}); eşik %{}. Disk dolarsa SQLite yazmaları ve yeni yedekler başarısız olur."
                           .format(yuzde, etiket, yol, ayarlar.disk_esik))

    print("Özet{}: {} gönderildi, {} hedefte silindi, {} doğrulanamadı; hedefte {} yedek.".format(
        " (kuru)" if kuru else "", len(gonderilen), len(silinen), dogrulanamayan,
        "?" if uzak is None else len(set(uzak) | set(gonderilen)) - len(silinen)))
    for h in hatalar:
        print("HATA: " + h, file=sys.stderr)
    if not kuru:
        bildir(ayarlar.izleme_url, not hatalar)
    return 1 if hatalar else 0


def listele(hedef) -> int:
    try:
        uzak = hedef.listele()
    except HedefHatasi as e:
        print("HATA: " + str(e), file=sys.stderr)
        return 1
    yedekler = sorted((y for y in map(tani, uzak) if y is not None), key=lambda y: (y.zaman, y.ad))
    for y in yedekler:
        print("{}\t{}\t{}\t{:%Y-%m-%d %H:%M} UTC".format(y.ad, uzak[y.ad], y.tur, y.zaman))
    print("{} yedek.".format(len(yedekler)))
    return 0


def indir(hedef, ad: str, cikti: Path) -> int:
    """Uzak kopyayı çıktı dizinine indirir ve manifest özetini doğrular; var olan dosyanın üzerine yazmaz."""
    if tani(ad) is None:
        print("HATA: geçersiz yedek adı: {!r} (listele çıktısındaki adı verin).".format(ad), file=sys.stderr)
        return 2
    cikti = Path(cikti)
    son, gecici = cikti / ad, cikti / (".{}.part".format(ad))
    if not cikti.is_dir():
        print("HATA: çıktı dizini yok: {}".format(cikti), file=sys.stderr)
        return 2
    if son.exists() or gecici.exists():
        print("HATA: {} zaten var; üzerine yazılmaz.".format(son), file=sys.stderr)
        return 1
    try:
        hedef.indir(ad, gecici)
        arsiv_dogrula(gecici)
        os.rename(gecici, son)
    except (HedefHatasi, ArsivHatasi, OSError) as e:
        gecici.unlink(missing_ok=True)
        print("HATA: {}".format(e), file=sys.stderr)
        return 1
    print("İndirildi ve manifest özeti doğrulandı: {}".format(son))
    print("Sonraki adım (yeni dosyaya geri açar, canlının üzerine yazmaz):")
    print("  python3 {} {} --output <yeni-veri-dizini>/kasa.db".format(Path(__file__).resolve().with_name("restore_backup.py"), son))
    return 0


def dogrula(ayarlar: Ayarlar, hedef, simdi: Optional[datetime] = None, bildir: Callable = izleme_bildir) -> int:
    """En yeni uzak kopyayı (tercihen otomatik) geçici dizine indirir, restore_backup.py ile yeni bir dosyaya geri
    açarak sınar (özet, SQLite bütünlüğü, ilişkiler, şema) ve geçici dizini siler. Uzaktaki en yeni otomatik kopya
    eşikten eskiyse (gönderim durmuşsa) de hata verir."""
    simdi = simdi or datetime.now(timezone.utc)
    hatalar: List[str] = []
    try:
        yedekler = [y for y in map(tani, hedef.listele()) if y is not None]
        otomatik = [y for y in yedekler if y.tur == "otomatik"]
        secilen = max(otomatik or yedekler, key=lambda y: (y.zaman, y.ad)) if yedekler else None
        if secilen is None:
            hatalar.append("Hedefte doğrulanacak yedek yok.")
        else:
            if not otomatik or simdi - secilen.zaman > timedelta(hours=ayarlar.en_fazla_saat):
                hatalar.append("Hedefteki en yeni otomatik yedek {} saat önce ({}); günlük gönderim durmuş olabilir.".format(
                    "?" if not otomatik else "{:.0f}".format((simdi - secilen.zaman).total_seconds() / 3600), secilen.ad))
            sys.path.insert(0, str(Path(__file__).resolve().parent))
            import restore_backup  # aynı dizindeki geri yükleme aracı: tek doğrulama kaynağı

            with tempfile.TemporaryDirectory(prefix="kasa-uzak-dogrula-") as gecici:
                zip_yolu = Path(gecici) / secilen.ad
                hedef.indir(secilen.ad, zip_yolu)
                arsiv_dogrula(zip_yolu)
                restore_backup.restore(zip_yolu, Path(gecici) / "kasa.db")
            print("Doğrulandı: {} indirildi, geri açıldı ve sınandı; geçici dosyalar silindi.".format(secilen.ad))
    except (HedefHatasi, ArsivHatasi) as e:
        hatalar.append(str(e))
    except Exception as e:  # restore_backup: ValueError, sqlite3.DatabaseError, OSError
        hatalar.append("Geri açma doğrulaması başarısız: {} ({}).".format(e, e.__class__.__name__))
    for h in hatalar:
        print("HATA: " + h, file=sys.stderr)
    bildir(ayarlar.dogrula_izleme_url, not hatalar)
    return 1 if hatalar else 0


def main(argv: Optional[Sequence[str]] = None, ortam: Optional[Mapping[str, str]] = None,
         calistir: Optional[Calistirici] = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    komutlar = parser.add_subparsers(dest="komut", required=True)
    g = komutlar.add_parser("gonder", help="yeni ve doğrulanmış yedekleri gönder, hedefte saklamayı uygula")
    g.add_argument("--kuru", action="store_true", help="yalnız ne yapılacağını yaz; göndermez, silmez, bildirmez")
    komutlar.add_parser("listele", help="uzaktaki yedekleri listele")
    i = komutlar.add_parser("indir", help="uzak kopyayı indirip manifest özetini doğrula")
    i.add_argument("ad")
    i.add_argument("--cikti", required=True, type=Path)
    komutlar.add_parser("dogrula", help="en yeni uzak kopyayı geçici dizinde geri açarak sına")
    args = parser.parse_args(argv)
    try:
        ayarlar = ayarlari_oku(os.environ if ortam is None else ortam)
        hedef = hedef_olustur(ayarlar, calistir)
    except YapilandirmaHatasi as e:
        print("YAPILANDIRMA HATASI: {}".format(e), file=sys.stderr)
        return 2
    if args.komut == "gonder":
        return gonder(ayarlar, hedef, kuru=args.kuru)
    if args.komut == "listele":
        return listele(hedef)
    if args.komut == "indir":
        return indir(hedef, args.ad, args.cikti)
    return dogrula(ayarlar, hedef)


if __name__ == "__main__":
    sys.exit(main())
