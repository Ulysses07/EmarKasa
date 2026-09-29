#!/usr/bin/env bash
# Uçtan uca denetimleri resmi Playwright Linux imajında koşturur. Ekran görüntüsü tabanları (testler/__ekran__) YALNIZ
# böyle üretilir: yazı tipi ve çizim platforma bağlıdır, CI'daki "UI screenshots (Linux)" işi aynı imajda karşılaştırır.
#
#   bash Kasa.Ui.E2E/linux-kosu.sh                                 axe + ARIA + ekran görüntüsü (Linux)
#   bash Kasa.Ui.E2E/linux-kosu.sh --update-snapshots=changed      değişen ekran/ARIA tabanlarını Linux'ta yenile
#   KASA_E2E_ERISIM=0 bash Kasa.Ui.E2E/linux-kosu.sh               yalnız ekran görüntüleri
#
# Ek bağımsız değişkenler `playwright test`e geçer. Git Bash, Linux ve macOS'ta çalışır; yalnız Docker gerekir. Depo
# konteynere bağlanır ama derleme ve npm kurulumu konteyner içindeki kopyada yapılır (Windows bin/obj ve node_modules
# bozulmaz); sonunda yalnız tabanlar (testler/__ekran__, testler/__aria__, testler/axe-tabani.json) ve rapor geri yazılır.
# İmajlar tek kaynaktan okunur (etiket + özet): Playwright imajı ci.yml'deki "UI screenshots (Linux)" işinden (sürümü
# @playwright/test ile aynı olmalı; testler/kurulum.setup.mjs denetler), .NET SDK imajı depo Dockerfile'ının derleme
# aşamasından. SDK bir Docker birimine bir kez kopyalanır (indirme betiği çalıştırılmaz).
set -euo pipefail

if [ "${KASA_E2E_KONTEYNER:-}" = 1 ]; then
  # ---- konteyner içi ----
  export PATH="/opt/dotnet:$PATH" DOTNET_ROOT=/opt/dotnet DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1 NUGET_PACKAGES=/nuget
  mkdir -p /is
  tar -C /depo --exclude=./.git --exclude='*/bin' --exclude='*/obj' --exclude='*/node_modules' \
    --exclude=./Kasa.Ui.E2E/test-results --exclude=./Kasa.Ui.E2E/playwright-report --exclude=./Kasa.Ui.E2E/.oturum -cf - . | tar -C /is -xf -
  cd /is/Kasa.Ui.E2E
  npm ci --no-audit --no-fund
  dotnet build Sunucu/Kasa.Ui.E2E.Sunucu.csproj -c Release -nologo -v:q
  kod=0
  KASA_E2E_DERLENDI=1 KASA_E2E_EKRAN=1 npx playwright test --reporter=list,html "$@" || kod=$?
  # Eksik tabanı Playwright her koşuda yazar; tabanlar ve rapor depoya geri kopyalanır, kaynak koda dokunulmaz.
  hedef=/depo/Kasa.Ui.E2E
  rm -rf "$hedef/testler/__ekran__" "$hedef/testler/__aria__" "$hedef/test-results" "$hedef/playwright-report"
  cp -a testler/__aria__ testler/axe-tabani.json "$hedef/testler/"
  if [ -d testler/__ekran__ ]; then cp -a testler/__ekran__ "$hedef/testler/"; fi
  cp -a test-results playwright-report "$hedef/" 2>/dev/null || true
  # Linux ana makinede dosyalar root'a kalmasın: depo kökünün sahibine verilir (Docker Desktop'ta etkisizdir).
  chown -R "$(stat -c %u:%g /depo)" "$hedef/testler" "$hedef/test-results" "$hedef/playwright-report" 2>/dev/null || true
  exit "$kod"
fi

# ---- ana makine ----
kok="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
# Git Bash: Docker'a Windows yolu verilir, MSYS yol çevirisi kapatılır.
if kok_docker="$(cd "$kok" && pwd -W 2>/dev/null)"; then export MSYS_NO_PATHCONV=1; else kok_docker="$kok"; fi
PLAYWRIGHT_IMAJ="$(grep -oE 'mcr\.microsoft\.com/playwright:v[0-9.]+-[a-z]+@sha256:[0-9a-f]{64}' "$kok/.github/workflows/ci.yml" | head -n 1)"
DOTNET_IMAJ="$(sed -nE 's#^FROM (mcr\.microsoft\.com/dotnet/sdk:[^ @]+@sha256:[0-9a-f]{64}).*#\1#p' "$kok/Dockerfile" | head -n 1)"
if [ -z "$PLAYWRIGHT_IMAJ" ] || [ -z "$DOTNET_IMAJ" ]; then echo "İmaj adları ci.yml ya da Dockerfile'dan okunamadı." >&2; exit 2; fi
DOTNET_BIRIMI="kasa-e2e-dotnet-$(printf '%s' "${DOTNET_IMAJ##*sha256:}" | cut -c1-24)"
echo "Playwright imajı: $PLAYWRIGHT_IMAJ"
echo ".NET SDK imajı:   $DOTNET_IMAJ (birim $DOTNET_BIRIMI)"

if ! docker run --rm -v "$DOTNET_BIRIMI:/opt/dotnet" "$PLAYWRIGHT_IMAJ" test -x /opt/dotnet/dotnet; then
  echo ".NET SDK ($DOTNET_IMAJ) $DOTNET_BIRIMI birimine kopyalanıyor (bir kez)..."
  docker run --rm -v "$DOTNET_BIRIMI:/hedef" "$DOTNET_IMAJ" cp -a /usr/share/dotnet/. /hedef/
fi

docker run --rm --init --ipc=host \
  -e KASA_E2E_KONTEYNER=1 -e KASA_E2E_ERISIM -e KASA_E2E_AXE_TABANI -e CI \
  -v "$kok_docker:/depo" -v "$DOTNET_BIRIMI:/opt/dotnet:ro" -v kasa-e2e-nuget:/nuget -v kasa-e2e-npm:/root/.npm \
  "$PLAYWRIGHT_IMAJ" bash /depo/Kasa.Ui.E2E/linux-kosu.sh "$@"
