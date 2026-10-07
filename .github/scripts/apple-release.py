#!/usr/bin/env python3
"""Manual iOS signing/upload. Secrets stay in a disposable runner directory."""
from __future__ import annotations

import base64
import binascii
import datetime as dt
import hashlib
import json
import os
from pathlib import Path
import plistlib
import re
import secrets
import shlex
import shutil
import subprocess
import sys
import xml.etree.ElementTree as ET


class ReleaseError(Exception):
    pass


def project_info(root: Path) -> dict:
    def value(path, name):
        node = ET.parse(root / path).find(".//" + name)
        if node is None or not node.text:
            raise ReleaseError("Proje yayın bilgisi bulunamadı.")
        return node.text.strip()

    version = value("Directory.Build.props", "KasaSurumu")
    bundle = value("Kasa.App/Kasa.App.csproj", "ApplicationId")
    build = value("Kasa.App/Kasa.App.csproj", "ApplicationVersion")
    if not re.fullmatch(r"[0-9]+\.[0-9]+\.[0-9]+", version):
        raise ReleaseError("KasaSurumu üç parçalı sayısal sürüm olmalı.")
    return {"version": version, "bundle": bundle, "minimum_build": int(build)}


def build_number(raw: str, minimum: int) -> str:
    if not re.fullmatch(r"[1-9][0-9]{0,9}", raw) or not minimum <= int(raw) <= 2147483647:
        raise ReleaseError("Derleme numarası pozitif, proje numarasından küçük olmayan bir tam sayı olmalı.")
    return raw


def required(names):
    missing = [name for name in names if not os.environ.get(name)]
    if missing:
        # Names only; never include values or exception representations.
        raise ReleaseError("GitHub ios-release ortamında eksik secrets: " + ", ".join(missing))


SIGNING = ["IOS_DISTRIBUTION_P12_BASE64", "IOS_DISTRIBUTION_P12_PASSWORD", "IOS_APPSTORE_PROFILE_BASE64"]
UPLOAD = ["APP_STORE_CONNECT_KEY_ID", "APP_STORE_CONNECT_ISSUER_ID", "APP_STORE_CONNECT_PRIVATE_KEY_BASE64"]


def check(root):
    mode = os.environ.get("IOS_RELEASE_ACTION", "")
    if mode not in {"build-ipa", "upload-testflight"}:
        raise ReleaseError("Geçerli yayın eylemini seçin.")
    build_number(os.environ.get("IOS_BUILD_NUMBER", ""), project_info(root)["minimum_build"])
    required(SIGNING)
    if mode == "upload-testflight":
        if os.environ.get("IOS_CONFIRM_UPLOAD") != "true":
            raise ReleaseError("Apple yüklemesi için confirm_upload seçilmeli.")
        required(UPLOAD)
        validate_api_ids()
    print("Yayın girdileri ve gerekli secret adları doğrulandı; içerikleri gösterilmedi.")


def validate_api_ids():
    if not re.fullmatch(r"[A-Z0-9]{10}", os.environ.get("APP_STORE_CONNECT_KEY_ID", "")):
        raise ReleaseError("App Store Connect takım API Key ID biçimi geçersiz.")
    if not re.fullmatch(r"[0-9a-fA-F]{8}(-[0-9a-fA-F]{4}){3}-[0-9a-fA-F]{12}", os.environ.get("APP_STORE_CONNECT_ISSUER_ID", "")):
        raise ReleaseError("App Store Connect Issuer ID UUID biçiminde olmalı.")


def decoded(name):
    try:
        return base64.b64decode(re.sub(r"\s", "", os.environ[name]), validate=True)
    except (KeyError, ValueError, binascii.Error):
        raise ReleaseError(name + " geçerli Base64 içermiyor.") from None


def private_write(path: Path, data: bytes):
    path.write_bytes(data)
    path.chmod(0o600)


def signing_directory() -> Path:
    if not os.environ.get("RUNNER_TEMP") or sys.platform != "darwin":
        raise ReleaseError("Bu adım macOS GitHub runner üzerinde çalıştırılmalı.")
    parent = Path(os.environ["RUNNER_TEMP"]).resolve()
    child = parent / "kasa-ios-signing"
    if child.is_symlink() or (child.exists() and child.resolve().parent != parent):
        raise ReleaseError("Geçici imzalama yolu runner sınırının dışında.")
    return child


def secret_tool(args, *, data=None):
    result = subprocess.run(args, input=data, capture_output=True, check=False)
    if result.returncode:
        raise ReleaseError(Path(args[0]).name + " başarısız; sertifika/parola/profil eşleşmesini kontrol edin.")
    return result.stdout


def validate_profile(profile: dict, bundle: str, now=None) -> dict:
    now = now or dt.datetime.now(dt.timezone.utc)
    expiry = profile.get("ExpirationDate")
    if not isinstance(expiry, dt.datetime) or expiry.replace(tzinfo=dt.timezone.utc) <= now:
        raise ReleaseError("Provisioning profile süresi dolmuş veya son kullanma tarihi yok.")
    entitlement = profile.get("Entitlements", {})
    teams = profile.get("TeamIdentifier", [])
    prefixes = profile.get("ApplicationIdentifierPrefix", [])
    app_id = entitlement.get("application-identifier", "")
    prefix, _, profile_bundle = app_id.partition(".")
    if profile_bundle != bundle or prefix not in prefixes:
        raise ReleaseError("Profil bu uygulamanın tam Bundle ID değeriyle eşleşmiyor.")
    if len(teams) != 1 or entitlement.get("com.apple.developer.team-identifier") != teams[0]:
        raise ReleaseError("Profilin Apple takım bilgisi tutarsız.")
    if profile.get("ProvisionedDevices") or profile.get("ProvisionsAllDevices") or entitlement.get("get-task-allow"):
        raise ReleaseError("TestFlight için Development/Ad Hoc/Enterprise yerine App Store Connect profili gerekir.")
    if "iOS" not in profile.get("Platform", []):
        raise ReleaseError("Provisioning profile iOS için oluşturulmalı.")
    certificate = profile.get("DeveloperCertificates", [])
    if len(certificate) != 1 or not isinstance(certificate[0], bytes):
        raise ReleaseError("App Store profili tek dağıtım sertifikası içermeli.")
    uuid = profile.get("UUID", "")
    if not re.fullmatch(r"[0-9A-Fa-f-]{36}", uuid):
        raise ReleaseError("Provisioning profile UUID biçimi geçersiz.")
    return {"profile_uuid": uuid, "certificate_sha1": hashlib.sha1(certificate[0]).hexdigest().upper()}


def save_state(directory, state):
    private_write(directory / "state.json", json.dumps(state).encode("utf-8"))


def load_state(directory):
    try:
        return json.loads((directory / "state.json").read_text())
    except (OSError, ValueError):
        raise ReleaseError("Geçici imzalama durumu bulunamadı.") from None


def prepare(root):
    required(SIGNING)
    directory = signing_directory()
    if directory.exists():
        raise ReleaseError("Geçici imzalama dizini zaten var; önce cleanup çalıştırın.")
    original_keychains = shlex.split(secret_tool(["security", "list-keychains", "-d", "user"]).decode())
    directory.mkdir(mode=0o700)
    keychain = directory / "signing.keychain-db"
    state = {
        "keychain": str(keychain),
        "password": secrets.token_urlsafe(32),
        "original_keychains": original_keychains,
        "installed_profile": None,
    }
    save_state(directory, state)
    p12 = directory / "distribution.p12"
    source_profile = directory / "distribution.mobileprovision"
    private_write(p12, decoded("IOS_DISTRIBUTION_P12_BASE64"))
    private_write(source_profile, decoded("IOS_APPSTORE_PROFILE_BASE64"))
    try:
        profile = plistlib.loads(secret_tool(["security", "cms", "-D", "-i", str(source_profile)]))
    except (ValueError, plistlib.InvalidFileException):
        raise ReleaseError("Provisioning profile Apple CMS/plist olarak okunamadı.") from None
    state.update(validate_profile(profile, project_info(root)["bundle"]))
    save_state(directory, state)
    secret_tool(["security", "create-keychain", "-p", state["password"], str(keychain)])
    secret_tool(["security", "set-keychain-settings", "-lut", "21600", str(keychain)])
    secret_tool(["security", "unlock-keychain", "-p", state["password"], str(keychain)])
    secret_tool(["security", "import", str(p12), "-P", os.environ["IOS_DISTRIBUTION_P12_PASSWORD"],
                 "-t", "cert", "-f", "pkcs12", "-k", str(keychain), "-T", "/usr/bin/codesign"])
    secret_tool(["security", "set-key-partition-list", "-S", "apple-tool:,apple:,codesign:", "-s",
                 "-k", state["password"], str(keychain)])
    identities = secret_tool(["security", "find-identity", "-v", "-p", "codesigning", str(keychain)]).decode()
    if state["certificate_sha1"] not in identities or "Apple Distribution:" not in identities:
        raise ReleaseError("P12 özel anahtarı profilin geçerli Apple Distribution sertifikasıyla eşleşmiyor.")
    secret_tool(["security", "list-keychains", "-d", "user", "-s", str(keychain), *state["original_keychains"]])
    profile_dir = Path.home() / "Library/MobileDevice/Provisioning Profiles"
    profile_dir.mkdir(parents=True, exist_ok=True)
    installed = profile_dir / (state["profile_uuid"] + ".mobileprovision")
    if installed.exists():
        raise ReleaseError("Runner'da aynı profil zaten var; mevcut dosya üzerine yazılmadı.")
    state["installed_profile"] = str(installed)
    save_state(directory, state)
    private_write(installed, source_profile.read_bytes())
    print("Geçerli App Store profili ve eşleşen dağıtım özel anahtarı geçici keychain'e alındı.")


def publish(root):
    directory = signing_directory()
    state = load_state(directory)
    info = project_info(root)
    build = build_number(os.environ.get("IOS_BUILD_NUMBER", ""), info["minimum_build"])
    for value in (state["certificate_sha1"], state["profile_uuid"]):
        print("::add-mask::" + value, flush=True)
    output = Path(os.environ["RUNNER_TEMP"]) / "kasa-ios-publish"
    output.mkdir(mode=0o700)
    subprocess.run(["dotnet", "publish", str(root / "Kasa.App/Kasa.App.csproj"), "-c", "Release",
                    "-f", "net10.0-ios", "-r", "ios-arm64", "-p:ArchiveOnBuild=true", "-p:BuildIpa=true",
                    "-p:IpaPackageDir=" + str(output),
                    "-p:ApplicationVersion=" + build, "-p:CodesignKey=" + state["certificate_sha1"],
                    "-p:CodesignProvision=" + state["profile_uuid"], "-p:CodesignKeychain=" + state["keychain"],
                    "--output", str(output)], check=True)
    packages = list(output.rglob("*.ipa"))
    if len(packages) != 1:
        raise ReleaseError("Publish çıktısında tek IPA bulunamadı.")
    artifact = Path(os.environ["RUNNER_TEMP"]) / "kasa-ios-artifact"
    artifact.mkdir(mode=0o700)
    package = artifact / f"EmarKasa-{info['version']}-{build}-ios.ipa"
    shutil.copyfile(packages[0], package)
    digest = hashlib.sha256(package.read_bytes()).hexdigest()
    (artifact / "SHA256SUMS.txt").write_text(digest + "  " + package.name + "\n")
    (artifact / "build.json").write_text(json.dumps({
        "version": info["version"], "build": build, "bundle_id": info["bundle"],
        "source_commit": os.environ.get("GITHUB_SHA", ""), "sdk": "10.0.401",
        "workload_set": "10.0.401.1", "xcode": "27.0",
    }, ensure_ascii=False, indent=2) + "\n")
    if os.environ.get("GITHUB_OUTPUT"):
        with open(os.environ["GITHUB_OUTPUT"], "a") as target:
            target.write("version=" + info["version"] + "\n")
    print("IPA ve SHA-256 üretildi.")


def upload():
    required(UPLOAD)
    validate_api_ids()
    directory = signing_directory()
    key_id = os.environ["APP_STORE_CONNECT_KEY_ID"]
    key_dir = Path.home() / ".appstoreconnect/private_keys"
    key_dir.mkdir(mode=0o700, parents=True, exist_ok=True)
    key = key_dir / ("AuthKey_" + key_id + ".p8")
    if key.exists():
        raise ReleaseError("Runner'da aynı API anahtarı var; üzerine yazılmadı.")
    state = load_state(directory)
    state["api_key_file"] = str(key)
    save_state(directory, state)
    private_write(key, decoded("APP_STORE_CONNECT_PRIVATE_KEY_BASE64"))
    secret_tool(["openssl", "pkey", "-in", str(key), "-check", "-noout"])
    packages = list((Path(os.environ["RUNNER_TEMP"]) / "kasa-ios-artifact").glob("*.ipa"))
    if len(packages) != 1:
        raise ReleaseError("Yüklenecek tek imzalı IPA bulunamadı.")
    auth = ["--apiKey", key_id, "--apiIssuer", os.environ["APP_STORE_CONNECT_ISSUER_ID"]]
    for action in ("--validate-app", "--upload-app"):
        subprocess.run(["xcrun", "altool", action, "--file", str(packages[0]), "--type", "ios",
                        *auth, "--output-format", "json"], check=True)
    print("Apple yüklemeyi kabul etti; TestFlight işleme/uyumluluk/cihaz kontrolü ayrıca tamamlanmalı.")


def cleanup():
    directory = signing_directory()
    if not directory.exists():
        return
    state = load_state(directory)
    failures = []
    def remove_file(raw, expected_parent, expected_suffix):
        if not raw:
            return
        path = Path(raw)
        if path.parent.resolve() != expected_parent.resolve() or not path.name.endswith(expected_suffix):
            raise ReleaseError("Temizleme yolu beklenen kimlik dizini dışında.")
        path.unlink(missing_ok=True)
    try:
        secret_tool(["security", "list-keychains", "-d", "user", "-s", *state["original_keychains"]])
    except ReleaseError:
        failures.append("keychain arama listesi geri yüklenemedi")
    keychain = directory / "signing.keychain-db"
    if keychain.exists():
        try:
            secret_tool(["security", "delete-keychain", str(keychain)])
        except ReleaseError:
            failures.append("geçici keychain kaldırılamadı")
    remove_file(state.get("installed_profile"), Path.home() / "Library/MobileDevice/Provisioning Profiles", ".mobileprovision")
    remove_file(state.get("api_key_file"), Path.home() / ".appstoreconnect/private_keys", ".p8")
    shutil.rmtree(directory)
    if failures:
        raise ReleaseError("; ".join(failures))
    print("Geçici keychain, profil ve API özel anahtarı temizlendi.")


def main():
    root = Path(__file__).resolve().parents[2]
    commands = {"check": lambda: check(root), "prepare": lambda: prepare(root),
                "publish": lambda: publish(root), "upload": upload, "cleanup": cleanup}
    if len(sys.argv) != 2 or sys.argv[1] not in commands:
        raise ReleaseError("Komut: check | prepare | publish | upload | cleanup")
    commands[sys.argv[1]]()


if __name__ == "__main__":
    try:
        main()
    except (ReleaseError, subprocess.CalledProcessError) as error:
        message = str(error) if isinstance(error, ReleaseError) else "Derleme veya Apple yükleme aracı başarısız."
        print("::error::" + message, file=sys.stderr)
        sys.exit(1)

