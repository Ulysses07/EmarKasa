"""Kasa yedeğini doğrular ve yeni bir SQLite dosyasına geri açar.

Canlı dosyanın üzerine yazmaz. Örnek:
  python3 restore_backup.py kasa-oto-....zip --output /safe/path/recovered.db
Uygulamayı durdurup doğrulanmış dosyayı devreye almak ayrı dağıtım adımıdır.

Sunucudaki yedek adları türü taşır; araç her adı kabul eder (indirilen dosya yeniden adlandırılmış olabilir):
  kasa-oto-YYYYMMDD-HHMMSS-xxxxxxxx.zip   günlük otomatik yedek (zaman UTC)
  kasa-elle-YYYYMMDD-HHMMSS-xxxxxxxx.zip  Ayarlar'dan alınan elle yedek
  kasa-YYYYMMDD-HHMMSS-xxxxxxxx.zip       2.3 ve öncesi; otomatik sayılır
Sunucu otomatik yedeklerde son 30 günün hepsini ve son 12 takvim ayının (İstanbul) ilk yedeğini,
her durumda en yeni 7'sini tutar. Elle yedeklerden en yeni 10'u tutulur ve elle yedek otomatik yedek silmez.
Bu kalıba uymayan dosyalara (ör. kasa-oncesi-gecis.zip) rotasyon dokunmaz.
Kalıcı geçmiş için yedekleri sunucu dışına da kopyalayın.

WAL: Uygulama veritabanını WAL günlük kipinde çalıştırır; kasa.db-wal ve kasa.db-shm veritabanının
parçasıdır. Uygulama çalışırken yalnız kasa.db kopyalanmaz (yedek SQLite yedekleme API'siyle alınır).
Araç geri açtığı dosyayı -wal/-shm gerektirmeyen tek dosya (geri alma günlüğü kipi) olarak yazar;
çıktının yanında eski -wal/-shm/-journal dosyası varsa reddeder, çünkü SQLite onları yeni dosyaya
uygulardı. Canlı dosyayı değiştirirken uygulamayı durdurun, eski kasa.db-wal ve kasa.db-shm dosyalarını
kasa.db ile birlikte kenara alın; uygulama ilk açılışta dosyayı yeniden WAL kipine alır.

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

_YEDEK_ADI = re.compile(r"^kasa-(?:(oto|elle)-)?[0-9]{8}-[0-9]{6}-[0-9a-f]{8}\.zip$")


def yedek_turu(name: str):
    """Sunucunun verdiği yedek adından türü döner; kalıba uymayan adlar için None.

    >>> yedek_turu("kasa-oto-20260927-030000-0a1b2c3d.zip")
    'otomatik'
    >>> yedek_turu("kasa-elle-20260927-101500-0a1b2c3d.zip")
    'elle'
    >>> yedek_turu("kasa-20260920-030000-0a1b2c3d.zip")  # eski ad
    'otomatik'
    >>> yedek_turu("kasa-yedek-2026-09-27.zip") is None
    True
    >>> yedek_turu("kasa-oncesi-gecis.zip") is None
    True
    """
    match = _YEDEK_ADI.match(name)
    if not match:
        return None
    return "elle" if match.group(1) == "elle" else "otomatik"


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


def restore(archive_path: Path, output: Path) -> None:
    output = output.resolve()
    if output.exists():
        raise ValueError("Çıktı zaten var; mevcut veritabanının üzerine yazılmaz.")
    if kalinti_dosyalari(output):
        raise ValueError("Çıktının yanında eski -wal/-shm/-journal dosyası var; SQLite bunları geri açılan dosyaya uygular. Boş bir çıktı yolu seçin.")
    if not output.parent.is_dir():
        raise ValueError("Çıktı klasörü mevcut olmalıdır.")
    with zipfile.ZipFile(archive_path) as archive:
        entries = sorted(archive.namelist())
        if entries not in (["kasa.db", "manifest.json"], [".kasa-push-keys.json", "kasa.db", "manifest.json"]):
            raise ValueError("Beklenmeyen yedek içeriği.")
        if archive.getinfo("manifest.json").file_size > 8192:
            raise ValueError("Geçersiz yedek bilgisi.")
        manifest = json.loads(archive.read("manifest.json"))
        if manifest.get("surum") not in ("2.0.0", "2.1.0"):
            raise ValueError("Bu araç yalnız 2.0.0 ve 2.1.0 yedeklerini destekler.")
        # 'tur' sonradan eklendi; eski manifestlerde yoktur, tür o zaman dosya adından okunur.
        tur = manifest.get("tur")
        if tur is not None and tur not in ("otomatik", "elle"):
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
            print("Canlıya alırken uygulamayı durdurun; eski kasa.db-wal ve kasa.db-shm dosyalarını kasa.db ile birlikte kenara alın.")
        finally:
            Path(temporary).unlink(missing_ok=True)
            for kalinti in kalinti_dosyalari(Path(temporary)):
                kalinti.unlink(missing_ok=True)


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("archive", type=Path)
    parser.add_argument("--output", required=True, type=Path)
    args = parser.parse_args()
    restore(args.archive, args.output)
