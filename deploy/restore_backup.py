"""Kasa yedeğini doğrular ve yeni bir SQLite dosyasına geri açar.

Canlı dosyanın üzerine yazmaz. Örnek:
  python3 restore_backup.py kasa-....zip --output /safe/path/recovered.db
Uygulamayı durdurup doğrulanmış dosyayı devreye almak ayrı dağıtım adımıdır.
"""
import argparse
import hashlib
import json
import os
from pathlib import Path
import sqlite3
import tempfile
import zipfile


def restore(archive_path: Path, output: Path) -> None:
    output = output.resolve()
    if output.exists():
        raise ValueError("Çıktı zaten var; mevcut veritabanının üzerine yazılmaz.")
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
            with sqlite3.connect(Path(temporary).as_uri() + "?mode=ro", uri=True) as db:
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
        finally:
            Path(temporary).unlink(missing_ok=True)


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("archive", type=Path)
    parser.add_argument("--output", required=True, type=Path)
    args = parser.parse_args()
    restore(args.archive, args.output)
