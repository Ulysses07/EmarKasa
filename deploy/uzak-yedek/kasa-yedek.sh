#!/usr/bin/env bash
# Kasa Defteri · sunucu dışı yedek (kasa-yedek konteyneri).
#
# Her gece en yeni günlük yedeği (/data/yedek/kasa-YYYY-AA-GG.db) gzip ile sıkıştırır,
# rclone'un "crypt" katmanıyla şifreler ve uzak hedefe (Google Drive, başka sunucu/SFTP,
# S3 uyumlu depo …) gönderir. Uzakta son 30 günlük ve 12 aylık kopya tutulur. Haftada bir
# en yeni uzak kopya indirilir, şifresi çözülür, açılır ve SQLite "PRAGMA integrity_check"
# ile doğrulanır. Sonuç /data/uzak-yedek/durum.json'a yazılır; API /health bunu
# "uzakYedek" alanında gösterir.
#
# Kullanım (sunucuda /opt/kasa/deploy içinde):
#   docker compose run --rm kasa-yedek yedekle            # şimdi yedekle
#   docker compose run --rm kasa-yedek dogrula            # şimdi doğrula
#   docker compose run --rm kasa-yedek listele            # uzaktaki kopyalar
#   docker compose run --rm kasa-yedek geri-al [dosya]    # uzak kopyayı indir, aç, doğrula
#   docker compose run --rm kasa-yedek durum              # son durum
#   docker compose run --rm kasa-yedek rclone config      # uzak hedef tanımla
# Ayrıntı: deploy/README.md → "Sunucu dışı yedek".

set -uo pipefail
umask 027

VERI=${KASA_VERI:-/data}
YEREL_YEDEK=${KASA_YEREL_YEDEK:-$VERI/yedek}
KLASOR=${KASA_UZAK_KLASOR:-$VERI/uzak-yedek}
DURUM=$KLASOR/durum.json
export RCLONE_CONFIG=${RCLONE_CONFIG:-$KLASOR/rclone.conf}

HEDEF=${KASA_UZAK_HEDEF:-}
SIFRE=${KASA_YEDEK_SIFRE:-}
SAAT=${KASA_UZAK_SAAT:-03:30}
DOGRULAMA_GUNU=${KASA_DOGRULAMA_GUNU:-7}
GUNLUK_SAKLA=${KASA_UZAK_GUNLUK_SAKLA:-30}
AYLIK_SAKLA=${KASA_UZAK_AYLIK_SAKLA:-12}

# Şifreli uzak: ortam değişkenleriyle tanımlanır (rclone.conf'a parola yazılmaz).
SIFRELI=kasasifreli
YEREL_DESEN='^kasa-[0-9]{4}-[0-9]{2}-[0-9]{2}\.db$'
GUNLUK_DESEN='^kasa-[0-9]{4}-[0-9]{2}-[0-9]{2}\.db\.gz$'
AYLIK_DESEN='^kasa-[0-9]{4}-[0-9]{2}\.db\.gz$'

HATA=""          # kısa, güvenli açıklama (/health'te görünür)
HATA_AYRINTI=""  # rclone/sqlite çıktısının son satırı (yalnız durum dosyasında)
UYARI=""
TMP=""

log() { printf '%s %s\n' "$(date '+%Y-%m-%d %H:%M:%S')" "$*"; }

simdi_utc() { date -u '+%Y-%m-%dT%H:%M:%SZ'; }

# Çıktının boş olmayan son satırı (rclone'un tarih önekinden arınmış, en fazla 300 karakter).
son_satir() {
    printf '%s\n' "$1" | grep -v '^[[:space:]]*$' | tail -n 1 \
        | sed -E 's#^[0-9]{4}/[0-9]{2}/[0-9]{2} [0-9:]{8} ##' | cut -c1-300
}

hata_koy() {  # $1: açıklama, $2: ayrıntı (isteğe bağlı)
    HATA=$1
    HATA_AYRINTI=${2:-}
    log "HATA: $HATA${HATA_AYRINTI:+ — $HATA_AYRINTI}"
}

uyari_ekle() {
    UYARI="${UYARI:+$UYARI }$1"
    log "UYARI: $1"
}

# adim "açıklama" komut [arg…]: komutu çalıştırır, çıktısını loga yazar; başarısızsa HATA'yı doldurur.
adim() {
    local aciklama=$1 cikti kod
    shift
    cikti=$("$@" 2>&1)
    kod=$?
    [ -n "$cikti" ] && printf '%s\n' "$cikti"
    if [ "$kod" -ne 0 ]; then
        hata_koy "$aciklama" "$(son_satir "$cikti")"
        return 1
    fi
    return 0
}

# Durum dosyasını jq ifadesiyle günceller (atomik: geçici dosya + mv). Bozuksa sıfırdan başlar.
# Kullanım: durum_yaz 'jq-ifadesi' [--arg ad değer …]
durum_yaz() {
    local ifade=$1 eski='{}' gecici
    shift
    if [ -s "$DURUM" ] && jq -e 'type == "object"' "$DURUM" >/dev/null 2>&1; then
        eski=$(cat "$DURUM")
    fi
    gecici="$DURUM.$$.tmp"
    if printf '%s' "$eski" | jq "$@" "$ifade" >"$gecici"; then
        mv -f "$gecici" "$DURUM"
    else
        rm -f "$gecici"
        log "HATA: durum dosyası yazılamadı ($DURUM)."
    fi
}

# Uzak hedef ve şifreleme ayarı tamam mı? Şifreli uzağı ortam değişkenleriyle tanımlar.
yapilandirma_denetle() {
    if [ -z "$HEDEF" ]; then
        hata_koy "KASA_UZAK_HEDEF boş (deploy/.env). Sunucu dışı yedek yapılandırılmadı."
        return 1
    fi
    if [ -z "$SIFRE" ]; then
        hata_koy "KASA_YEDEK_SIFRE boş (deploy/.env); şifresiz yedek gönderilmez."
        return 1
    fi
    if [ "${HEDEF#:}" = "$HEDEF" ] && [ ! -s "$RCLONE_CONFIG" ]; then
        hata_koy "rclone ayarı yok (kasa-data/uzak-yedek/rclone.conf). Önce 'rclone config' ile uzak hedef tanımlayın."
        return 1
    fi
    sifreli_tanimla
}

sifreli_tanimla() {
    local gizli
    if ! gizli=$(printf '%s' "$SIFRE" | rclone obscure - 2>&1); then
        hata_koy "Şifreleme parolası işlenemedi." "$(son_satir "$gizli")"
        return 1
    fi
    export RCLONE_CONFIG_KASASIFRELI_TYPE=crypt
    export RCLONE_CONFIG_KASASIFRELI_REMOTE="$HEDEF"
    export RCLONE_CONFIG_KASASIFRELI_PASSWORD="$gizli"
    # Dosya adları açık kalır (kasa-2026-09-24.db.gz.bin): uzakta hangi günün yedeği olduğu
    # görülebilsin. İçerik şifrelidir.
    export RCLONE_CONFIG_KASASIFRELI_FILENAME_ENCRYPTION=off
    export RCLONE_CONFIG_KASASIFRELI_DIRECTORY_NAME_ENCRYPTION=false
}

# uzak_listele klasör desen çıktı-dosyası: uzak klasördeki desene uyan adları sıralı yazar.
# Klasör henüz yoksa boş liste döner.
uzak_listele() {
    local cikti kod
    cikti=$(rclone lsf --files-only "$SIFRELI:$1/" 2>&1)
    kod=$?
    if [ "$kod" -ne 0 ]; then
        if printf '%s' "$cikti" | grep -qi 'directory not found'; then
            : >"$3"
            return 0
        fi
        printf '%s\n' "$cikti"
        hata_koy "Uzak klasör okunamadı ($1)." "$(son_satir "$cikti")"
        return 1
    fi
    printf '%s\n' "$cikti" | grep -E "$2" | sort >"$3" || true
    return 0
}

# temizle klasör desen kalacak: en yeni N kopya dışındakileri siler.
temizle() {
    local klasor=$1 desen=$2 kalacak=$3 liste i fazla
    local -a dosyalar
    liste="$TMP/liste-$klasor"
    uzak_listele "$klasor" "$desen" "$liste" || return 1
    mapfile -t dosyalar <"$liste"
    fazla=$((${#dosyalar[@]} - kalacak))
    for ((i = 0; i < fazla; i++)); do
        log "Eski uzak kopya siliniyor: $klasor/${dosyalar[i]}"
        adim "Eski kopya silinemedi ($klasor/${dosyalar[i]})." \
            rclone deletefile "$SIFRELI:$klasor/${dosyalar[i]}" || return 1
    done
    return 0
}

sikistir() { gzip -c -6 "$1" >"$2" && gzip -t "$2"; }
ac() { gzip -t "$1" && gzip -dc "$1" >"$2"; }

# butunluk db: PRAGMA integrity_check "ok" olmalı ve İşlemler tablosu okunabilmeli.
ISLEM_SAYISI=""
butunluk() {
    local sonuc
    sonuc=$(sqlite3 -readonly "$1" 'PRAGMA integrity_check;' 2>&1)
    if [ "$sonuc" != "ok" ]; then
        hata_koy "Bütünlük denetimi başarısız." "$(printf '%s\n' "$sonuc" | head -n 3 | tr '\n' ' ' | cut -c1-300)"
        return 1
    fi
    if ! ISLEM_SAYISI=$(sqlite3 -readonly "$1" 'SELECT COUNT(*) FROM "Islemler";' 2>&1); then
        hata_koy "İşlemler tablosu okunamadı; dosya bir Kasa veritabanı değil gibi." "$(son_satir "$ISLEM_SAYISI")"
        ISLEM_SAYISI=""
        return 1
    fi
    return 0
}

# ---------------------------------------------------------------- yedekle

YEREL_AD=""; UZAK_AD=""; BOYUT=""

yedekle_ic() {
    yapilandirma_denetle || return 1
    if [ ! -d "$YEREL_YEDEK" ]; then
        hata_koy "Yerel yedek klasörü yok (kasa-data/yedek). Uygulama günlük yedek alıyor mu?"
        return 1
    fi
    YEREL_AD=$(ls -1 "$YEREL_YEDEK" | grep -E "$YEREL_DESEN" | sort | tail -n 1)
    if [ -z "$YEREL_AD" ]; then
        hata_koy "Yerel günlük yedek bulunamadı (kasa-data/yedek/kasa-YYYY-AA-GG.db)."
        return 1
    fi
    UZAK_AD="$YEREL_AD.gz"
    local gz="$TMP/$UZAK_AD" ay aylik_ad aylar
    log "Gönderilecek: $YEREL_AD"
    adim "Yedek sıkıştırılamadı ($YEREL_AD)." sikistir "$YEREL_YEDEK/$YEREL_AD" "$gz" || return 1
    BOYUT=$(stat -c %s "$gz")
    adim "Yedek gönderilemedi (gunluk/$UZAK_AD)." rclone copyto "$gz" "$SIFRELI:gunluk/$UZAK_AD" || return 1

    # Aylık kopya: yedeğin ait olduğu ayın ilk gönderilen yedeği.
    ay=${YEREL_AD:5:7}
    aylik_ad="kasa-$ay.db.gz"
    aylar="$TMP/aylar"
    uzak_listele aylik "$AYLIK_DESEN" "$aylar" || return 1
    if ! grep -qx "$aylik_ad" "$aylar"; then
        adim "Aylık kopya gönderilemedi (aylik/$aylik_ad)." rclone copyto "$gz" "$SIFRELI:aylik/$aylik_ad" || return 1
        log "Aylık kopya gönderildi: aylik/$aylik_ad"
    fi

    # Saklama: başarısızsa yedek yine de gönderilmiştir → uyarı.
    temizle gunluk "$GUNLUK_DESEN" "$GUNLUK_SAKLA" || uyari_ekle "Eski günlük kopyalar silinemedi."
    temizle aylik "$AYLIK_DESEN" "$AYLIK_SAKLA" || uyari_ekle "Eski aylık kopyalar silinemedi."
    if [ "${YEREL_AD:5:10}" != "$(date '+%Y-%m-%d')" ]; then
        uyari_ekle "Bugünün yerel yedeği yok; en yeni yerel yedek ($YEREL_AD) gönderildi."
    fi
    HATA=""; HATA_AYRINTI=""
    return 0
}

yedekle() {
    local baslangic
    baslangic=$(simdi_utc)
    HATA=""; HATA_AYRINTI=""; UYARI=""
    if yedekle_ic; then
        durum_yaz '. + {sonDeneme: $d, sonBasari: $b, dosya: $f, boyutBayt: ($s | tonumber),
                        yerelYedek: $y, hata: null, hataAyrinti: null,
                        uyari: (if $u == "" then null else $u end)}' \
            --arg d "$baslangic" --arg b "$(simdi_utc)" --arg f "$UZAK_AD" --arg s "$BOYUT" \
            --arg y "$YEREL_AD" --arg u "$UYARI"
        log "Sunucu dışı yedek tamam: gunluk/$UZAK_AD ($BOYUT bayt)."
        return 0
    fi
    durum_yaz '. + {sonDeneme: $d, hata: $h, hataAyrinti: (if $a == "" then null else $a end), uyari: null}' \
        --arg d "$baslangic" --arg h "$HATA" --arg a "$HATA_AYRINTI"
    return 1
}

# ---------------------------------------------------------------- doğrula

DOGRULANAN=""

dogrula_ic() {
    yapilandirma_denetle || return 1
    local liste="$TMP/liste-dogrula"
    uzak_listele gunluk "$GUNLUK_DESEN" "$liste" || return 1
    DOGRULANAN=$(tail -n 1 "$liste")
    if [ -z "$DOGRULANAN" ]; then
        hata_koy "Uzakta doğrulanacak yedek yok (gunluk/ boş)."
        return 1
    fi
    log "Doğrulanıyor: gunluk/$DOGRULANAN"
    adim "Uzak kopya indirilemedi ya da şifresi çözülemedi (gunluk/$DOGRULANAN)." \
        rclone copyto "$SIFRELI:gunluk/$DOGRULANAN" "$TMP/$DOGRULANAN" || return 1
    adim "Uzak kopya açılamadı (gzip)." ac "$TMP/$DOGRULANAN" "$TMP/kasa.db" || return 1
    butunluk "$TMP/kasa.db" || return 1
    return 0
}

dogrula() {
    local zaman
    zaman=$(simdi_utc)
    HATA=""; HATA_AYRINTI=""; DOGRULANAN=""; ISLEM_SAYISI=""
    if dogrula_ic; then
        durum_yaz '. + {dogrulamaZamani: $z, dogrulamaDosyasi: $f, dogrulamaSonucu: "ok",
                        dogrulamaHatasi: null, dogrulamaHataAyrinti: null,
                        dogrulamaIslemSayisi: ($n | tonumber? // null)}' \
            --arg z "$zaman" --arg f "$DOGRULANAN" --arg n "$ISLEM_SAYISI"
        log "Doğrulama tamam: gunluk/$DOGRULANAN (integrity_check ok, $ISLEM_SAYISI işlem)."
        return 0
    fi
    durum_yaz '. + {dogrulamaZamani: $z, dogrulamaDosyasi: (if $f == "" then null else $f end),
                    dogrulamaSonucu: "hata", dogrulamaHatasi: $h,
                    dogrulamaHataAyrinti: (if $a == "" then null else $a end)}' \
        --arg z "$zaman" --arg f "$DOGRULANAN" --arg h "$HATA" --arg a "$HATA_AYRINTI"
    return 1
}

# ---------------------------------------------------------------- geri al / listele / durum

geri_al() {
    local istenen=${1:-} klasor ad liste hedef_klasor hedef
    yapilandirma_denetle || return 1
    case "$istenen" in
        "")
            klasor=gunluk
            liste="$TMP/liste-geri"
            uzak_listele gunluk "$GUNLUK_DESEN" "$liste" || return 1
            ad=$(tail -n 1 "$liste")
            ;;
        gunluk/* | aylik/*) klasor=${istenen%%/*}; ad=${istenen#*/} ;;
        *) ad=$istenen; if [[ $ad =~ $AYLIK_DESEN ]]; then klasor=aylik; else klasor=gunluk; fi ;;
    esac
    ad=${ad%.bin}
    if [ -z "$ad" ]; then
        hata_koy "Uzakta geri alınacak yedek yok."
        return 1
    fi
    if ! [[ $ad =~ $GUNLUK_DESEN || $ad =~ $AYLIK_DESEN ]]; then
        hata_koy "Geçersiz dosya adı: $ad (örnek: kasa-2026-09-20.db.gz ya da aylik/kasa-2026-08.db.gz)."
        return 1
    fi
    hedef_klasor="$KLASOR/geri-al"
    hedef="$hedef_klasor/${ad%.gz}"
    mkdir -p "$hedef_klasor" || return 1
    log "İndiriliyor: $klasor/$ad"
    adim "Uzak kopya indirilemedi ya da şifresi çözülemedi ($klasor/$ad)." \
        rclone copyto "$SIFRELI:$klasor/$ad" "$TMP/$ad" || return 1
    adim "Uzak kopya açılamadı (gzip)." ac "$TMP/$ad" "$TMP/kasa.db" || return 1
    butunluk "$TMP/kasa.db" || return 1
    mv -f "$TMP/kasa.db" "$hedef" || return 1
    log "Hazır: kasa-data/uzak-yedek/geri-al/${ad%.gz} (integrity_check ok, $ISLEM_SAYISI işlem)."
    log "Canlıya almak için deploy/README.md → \"Uzak kopyadan geri yükleme\" adımlarını izleyin."
    return 0
}

listele() {
    yapilandirma_denetle || return 1
    local k
    for k in gunluk aylik; do
        echo "== $k/"
        rclone lsl "$SIFRELI:$k/" 2>&1 | grep -v 'directory not found' || true
    done
    return 0
}

durum_goster() {
    if [ -s "$DURUM" ]; then
        jq . "$DURUM" 2>/dev/null || cat "$DURUM"
    else
        echo "Durum dosyası yok: sunucu dışı yedek henüz hiç çalışmadı (yapılandırılmadı)."
    fi
}

# ---------------------------------------------------------------- zamanlama

# is komut [arg…]: kilit + geçici çalışma klasörüyle bir iş çalıştırır.
is() {
    local kod
    if ! mkdir -p "$KLASOR" 2>/dev/null || [ ! -w "$KLASOR" ]; then
        log "HATA: $KLASOR yazılamıyor. Sunucuda: sudo chown -R 1654:1654 /opt/kasa/deploy/kasa-data"
        return 1
    fi
    exec 9>"$KLASOR/.kilit"
    if command -v flock >/dev/null 2>&1 && ! flock -n 9; then
        log "Başka bir yedek işlemi sürüyor; bu çalıştırma atlandı."
        exec 9>&-
        return 1
    fi
    TMP=$(mktemp -d /tmp/kasa-yedek.XXXXXX)
    "$@"
    kod=$?
    rm -rf "$TMP"
    TMP=""
    exec 9>&-
    return "$kod"
}

# Bir sonraki SAAT'e (Türkiye saati) kadar kaç saniye var.
bekleme_suresi() {
    local s d n simdi hedef fark
    IFS=: read -r s d n <<<"$(date '+%H:%M:%S')"
    simdi=$((10#$s * 3600 + 10#$d * 60 + 10#$n))
    hedef=$((10#${SAAT%%:*} * 3600 + 10#${SAAT#*:} * 60))
    fark=$(((hedef - simdi + 86400) % 86400))
    [ "$fark" -eq 0 ] && fark=86400
    echo "$fark"
}

zamanla() {
    local bekle
    trap 'log "Durduruluyor."; exit 0' TERM INT
    if ! [[ $SAAT =~ ^([01][0-9]|2[0-3]):[0-5][0-9]$ ]]; then
        log "UYARI: KASA_UZAK_SAAT geçersiz ('$SAAT'); 03:30 kullanılıyor."
        SAAT=03:30
    fi
    if ! [[ $DOGRULAMA_GUNU =~ ^[1-7]$ ]]; then
        log "UYARI: KASA_DOGRULAMA_GUNU geçersiz ('$DOGRULAMA_GUNU'); 7 (Pazar) kullanılıyor."
        DOGRULAMA_GUNU=7
    fi
    log "Başladı: her gün $SAAT yedek, haftanın $DOGRULAMA_GUNU. günü (1=Pzt … 7=Paz) doğrulama."
    [ -z "$HEDEF" ] && log "KASA_UZAK_HEDEF boş: kurulum yapılana kadar sunucu dışı yedek alınmaz (deploy/README.md)."
    while true; do
        bekle=$(bekleme_suresi)
        log "Sonraki çalışma $((bekle / 3600)) sa $((bekle % 3600 / 60)) dk sonra."
        sleep "$bekle" &
        wait $!
        if [ -z "$HEDEF" ]; then
            log "KASA_UZAK_HEDEF boş: sunucu dışı yedek atlandı."
        else
            is yedekle || true
            if [ "$(date '+%u')" = "$DOGRULAMA_GUNU" ]; then
                is dogrula || true
            fi
        fi
        # Aynı dakikada ikinci kez çalışmasın.
        sleep 61 &
        wait $!
    done
}

sayi_denetle() {
    if ! [[ $GUNLUK_SAKLA =~ ^[1-9][0-9]*$ ]]; then GUNLUK_SAKLA=30; fi
    if ! [[ $AYLIK_SAKLA =~ ^[1-9][0-9]*$ ]]; then AYLIK_SAKLA=12; fi
}

yardim() {
    cat <<'METIN'
Kullanım: docker compose run --rm kasa-yedek <komut>
  yedekle            En yeni günlük yedeği şimdi şifreleyip uzağa gönder.
  dogrula            En yeni uzak kopyayı indir, çöz, aç ve bütünlüğünü denetle.
  listele            Uzaktaki günlük ve aylık kopyaları listele.
  geri-al [dosya]    Uzak kopyayı kasa-data/uzak-yedek/geri-al/ içine indir ve doğrula
                     (dosya verilmezse en yeni günlük kopya; örn. kasa-2026-09-20.db.gz
                     ya da aylik/kasa-2026-08.db.gz).
  durum              Son yedek/doğrulama durumunu göster.
  rclone …           rclone'u doğrudan çalıştır (örn. "rclone config").
  zamanla            (Varsayılan) Her gece yedekle, haftada bir doğrula.
METIN
}

sayi_denetle
komut=${1:-zamanla}
[ "$#" -gt 0 ] && shift
case "$komut" in
    zamanla) zamanla ;;
    yedekle) is yedekle ;;
    dogrula) is dogrula ;;
    geri-al) is geri_al "$@" ;;
    listele) is listele ;;
    durum) durum_goster ;;
    rclone)
        mkdir -p "$KLASOR" 2>/dev/null || true
        if [ -n "$HEDEF" ] && [ -n "$SIFRE" ]; then sifreli_tanimla || true; fi
        exec rclone "$@"
        ;;
    yardim | -h | --help | help) yardim ;;
    *)
        echo "Bilinmeyen komut: $komut" >&2
        yardim >&2
        exit 2
        ;;
esac
