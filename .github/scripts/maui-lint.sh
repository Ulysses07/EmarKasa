#!/usr/bin/env bash
# MAUI kaynak denetimi (Aşama 2a). Kasa.App'in XAML ve C# dosyalarında:
#  (a) {StaticResource X} / {DynamicResource X} (XAML) ve Resources["X"], Resources.TryGetValue("X", ...),
#      Resources.ContainsKey("X"), SetDynamicResource(..., "X") (C#) ile başvurulan her kaynak anahtarı uygulama
#      sözlüklerinde (Resources/Styles/Colors.xaml, Resources/Styles/Styles.xaml, App.xaml) ya da aynı dosyanın kendi
#      x:Key tanımlarında vardır. Tanımsız anahtar derlemeyi geçer, ekran açılınca çöker ya da sessizce boş kalır.
#      Taban yoktur: her ihlal hatadır.
#  (b) Renkler tema sözlüğünden gelir: Resources/ ve Platforms/ dışındaki dosyalarda doğrudan onaltılık renk (#RGB,
#      #RRGGBB, #AARRGGBB; C#'ta Color.FromArgb/FromRgba/FromHex/Parse("RRGGBB")) sayılır.
#  (c) Satır uzunluğu en çok 200 karakter (UTF-8 karakteri; CR sayılmaz).
# (b) ve (c) için mevcut aşımlar dosya başına sayı tabanıdır (maui-lint-tabani.txt): sayı artarsa hata, azalırsa taban
# düşürülmelidir (uyarı). Taban yalnız azalır: --tabani-guncelle hiçbir sayı artmamışsa tabanı güncel sayılarla yazar.
#
#   bash .github/scripts/maui-lint.sh                  denetle (CI, Git Bash, Linux, macOS)
#   bash .github/scripts/maui-lint.sh --tabani-guncelle düzelen aşımlardan sonra tabanı küçült
#
# Yalnız bash, find, grep, sed, awk, sort (GNU ya da BSD, gawk ya da mawk) kullanır; ağ ve derleme gerekmez.
set -euo pipefail
export LC_ALL=C

cd "$(dirname "${BASH_SOURCE[0]}")/../.."
UYGULAMA=Kasa.App
TABAN=.github/scripts/maui-lint-tabani.txt
SINIR=200
SOZLUKLER="$UYGULAMA/Resources/Styles/Colors.xaml $UYGULAMA/Resources/Styles/Styles.xaml $UYGULAMA/App.xaml"

guncelle=false
case "${1:-}" in
  '') ;;
  --tabani-guncelle) guncelle=true ;;
  *) echo "Kullanım: $0 [--tabani-guncelle]" >&2; exit 2 ;;
esac

gecici="$(mktemp -d)"
trap 'rm -rf "$gecici"' EXIT
hatalar=0

hata() { # dosya satır ileti
  hatalar=$((hatalar + 1))
  if [ "${GITHUB_ACTIONS:-}" = true ]; then echo "::error file=$1${2:+,line=$2}::$3"; else echo "HATA  $1${2:+:$2}  $3"; fi
}
uyari() { # dosya ileti
  if [ "${GITHUB_ACTIONS:-}" = true ]; then echo "::warning file=$1::$2"; else echo "UYARI $1  $2"; fi
}

# Denetlenen dosyalar (derleme çıktısı hariç), depo köküne göre, sıralı.
find "$UYGULAMA" \( -name bin -o -name obj \) -prune -o -type f \( -name '*.xaml' -o -name '*.cs' \) -print | sort > "$gecici/dosyalar"
[ -s "$gecici/dosyalar" ] || { echo "$UYGULAMA altında XAML/C# dosyası bulunamadı." >&2; exit 2; }
for sozluk in $SOZLUKLER; do [ -f "$sozluk" ] || { echo "Kaynak sözlüğü yok: $sozluk" >&2; exit 2; }; done

anahtarlar() { grep -hoE 'x:Key="[^"]+"' "$@" 2>/dev/null | sed -E 's/^.*x:Key="([^"]+)"$/\1/' || true; }

# ---- (a) kaynak anahtarları ----
# shellcheck disable=SC2086 # sözlük listesi bilerek sözcüklere bölünür (boşluksuz yollar)
anahtarlar $SOZLUKLER | sort -u > "$gecici/genel"
[ -s "$gecici/genel" ] || { echo "Kaynak sözlüklerinde x:Key bulunamadı." >&2; exit 2; }
basvuru_sayisi=0
while IFS= read -r dosya; do
  case "$dosya" in "$UYGULAMA"/Platforms/*) continue ;; esac
  { cat "$gecici/genel"; anahtarlar "$dosya"; } | sort -u > "$gecici/tanimli"
  case "$dosya" in
    # Anahtar, biçimlendirme sınırlayıcısına (boşluk, virgül ya da }) kadar bütünüyle alınır: geçersiz karakterli bir anahtar
    # (ör. AppBg-TYPO) geçerli ön ekine (AppBg) kısalıp tanımlı sayılmasın.
    *.xaml) grep -noE '\{(Static|Dynamic)Resource[[:space:]]+(Key=)?[^[:space:],}]+' "$dosya" \
              | sed -E 's/^([0-9]+):.*Resource[[:space:]]+(Key=)?/\1 /' || true ;;
    *.cs) { grep -noE 'Resources[[:space:]]*\??\[[[:space:]]*"[^"]+"' "$dosya" || true
            grep -noE 'Resources[[:space:]]*\??\.[[:space:]]*(TryGetValue|ContainsKey)[[:space:]]*\([[:space:]]*"[^"]+"' "$dosya" || true
            grep -noE 'SetDynamicResource[[:space:]]*\([^,()]+,[[:space:]]*"[^"]+"' "$dosya" || true
          } | sed -E 's/^([0-9]+):.*"([^"]+)"$/\1 \2/' ;;
  esac > "$gecici/basvurular"
  while read -r satir anahtar; do
    basvuru_sayisi=$((basvuru_sayisi + 1))
    grep -qxF "$anahtar" "$gecici/tanimli" || hata "$dosya" "$satir" "'$anahtar' kaynak anahtarı Colors.xaml, Styles.xaml, App.xaml ya da bu dosyada tanımlı değil."
  done < "$gecici/basvurular"
done < "$gecici/dosyalar"
# Desenler bozulursa denetim sessizce boş geçmesin.
[ "$basvuru_sayisi" -ge 100 ] || { echo "Kaynak başvuruları okunamadı ($basvuru_sayisi); desenleri denetleyin." >&2; exit 2; }

# ---- (b) doğrudan hex renk, (c) uzun satır: dosya başına sayılar (dosya başına tek awk geçişi) ----
# Hex: '#' ve ardından tam 3, 4, 6 ya da 8 onaltılık basamak; '#' öncesinde harf/rakam/_/& olmaz (&#1234; karakter
# başvurusu, #region değildir). C#'ta ayrıca '#'siz dizeyle Color.FromArgb/FromRgba/FromHex/Parse("RRGGBB").
# Satır: UTF-8 karakter sayısı; bayt kipinde (LC_ALL=C) uzunluktan devam baytları (0x80-0xBF) düşülür, sondaki CR sayılmaz.
# shellcheck disable=SC2016 # awk programı tek tırnakta; kabuk değişkeni açılmaz
SAYAC='
  function onaltilik(t,  n) { n = length(t); return t ~ /^[0-9A-Fa-f]+$/ && (n == 3 || n == 4 || n == 6 || n == 8) }
  {
    sub(/\r$/, "")
    if (renk) {
      s = $0; bas = 0
      while (match(s, /#[0-9A-Za-z_]+/)) {
        once = (bas + RSTART > 1) ? substr($0, bas + RSTART - 1, 1) : ""
        if (once !~ /[&0-9A-Za-z_]/ && onaltilik(substr(s, RSTART + 1, RLENGTH - 1))) hex++
        bas += RSTART + RLENGTH - 1; s = substr(s, RSTART + RLENGTH)
      }
      if (cs) {
        s = $0
        while (match(s, /Color\.(FromArgb|FromRgba|FromHex|Parse)[ \t]*\([ \t]*"[0-9A-Za-z]*"/)) {
          d = substr(s, RSTART, RLENGTH); sub(/^[^"]*"/, "", d); sub(/"$/, "", d)
          if (onaltilik(d)) hex++
          s = substr(s, RSTART + RLENGTH)
        }
      }
    }
    t = $0; if (length($0) - gsub(/[\200-\277]/, "", t) > sinir) uzun++
  }
  END { print hex + 0, uzun + 0 }'
: > "$gecici/simdiki"
while IFS= read -r dosya; do
  renk=1; case "$dosya" in "$UYGULAMA"/Resources/*|"$UYGULAMA"/Platforms/*) renk=0 ;; esac
  cs=0; case "$dosya" in *.cs) cs=1 ;; esac
  read -r hex uzun < <(awk -v sinir="$SINIR" -v renk="$renk" -v cs="$cs" "$SAYAC" "$dosya")
  [ "$hex" -eq 0 ] || printf 'hex\t%s\t%d\n' "$dosya" "$hex" >> "$gecici/simdiki"
  [ "$uzun" -eq 0 ] || printf 'satir\t%s\t%d\n' "$dosya" "$uzun" >> "$gecici/simdiki"
done < "$gecici/dosyalar"
sort -o "$gecici/simdiki" "$gecici/simdiki"

# Taban yoksa (ilk kurulum) yalnız --tabani-guncelle onu mevcut sayılarla oluşturur; denetim tabansız geçmez.
ilk=false
# Windows çalışma kopyasında taban CRLF olabilir (core.autocrlf): CR atılır.
if [ -f "$TABAN" ]; then tr -d '\r' < "$TABAN" | grep -vE '^(#|$)' | sort > "$gecici/taban" || true; else ilk=true; : > "$gecici/taban"; fi
# Karşılaştırma: tür<TAB>dosya<TAB>şimdiki<TAB>taban (yalnız farklı olanlar). Taban dosyası boş olabilir: ilk dosya
# NR == FNR yerine adıyla ayrılır.
awk -F '\t' 'FILENAME == ARGV[1] { taban[$1 FS $2] = $3; next } { simdi[$1 FS $2] = $3 }
  END { for (k in simdi) if (simdi[k] != taban[k] + 0) print k FS simdi[k] FS taban[k] + 0
        for (k in taban) if (!(k in simdi)) print k FS 0 FS taban[k] }' "$gecici/taban" "$gecici/simdiki" | sort > "$gecici/farklar"

anahtar_hatalari=$hatalar; artan=0; azalan=0
if ! { $guncelle && $ilk; }; then
  while IFS=$'\t' read -r tur dosya simdi eski; do
    if [ "$tur" = hex ]; then ne="doğrudan hex renk"; cozum="rengi Resources/Styles/Colors.xaml'a anahtar olarak ekleyip {StaticResource} ile kullanın"
    else ne="$SINIR karakterden uzun satır"; cozum="satırı bölün"; fi
    if [ "$simdi" -gt "$eski" ]; then
      artan=$((artan + 1))
      hata "$dosya" "" "$ne sayısı $eski → $simdi arttı; $cozum (taban: $TABAN)."
    else
      azalan=$((azalan + 1))
      uyari "$dosya" "$ne sayısı $eski → $simdi azaldı; tabanı düşürün: bash .github/scripts/maui-lint.sh --tabani-guncelle"
    fi
  done < "$gecici/farklar"
fi

if $guncelle; then
  if [ "$anahtar_hatalari" -gt 0 ] || [ "$artan" -gt 0 ]; then
    echo "Taban güncellenmedi: önce tanımsız anahtarları ve artan sayıları giderin (taban yalnız azalır)." >&2; exit 1
  fi
  {
    echo "# maui-lint.sh tabanı: (b) doğrudan hex renk ve (c) $SINIR karakteri aşan satır sayısı, dosya başına (tür<TAB>dosya<TAB>sayı)."
    echo "# Sayılar yalnız azalır: artış CI'ı kırar. Düzeltmeden sonra: bash .github/scripts/maui-lint.sh --tabani-guncelle"
    cat "$gecici/simdiki"
  } > "$TABAN"
  echo "Taban yazıldı: $TABAN ($(wc -l < "$gecici/simdiki" | tr -d ' ') satır)."
  exit 0
fi

$ilk && hata "$TABAN" "" "Taban dosyası yok; ilk kurulumda bash .github/scripts/maui-lint.sh --tabani-guncelle ile oluşturun."
toplam() { awk -F '\t' -v t="$1" '$1 == t { s += $3 } END { print s + 0 }' "$gecici/simdiki"; }
echo "maui-lint: $(wc -l < "$gecici/dosyalar" | tr -d ' ') dosya, $basvuru_sayisi kaynak başvurusu ($anahtar_hatalari tanımsız); doğrudan hex renk $(toplam hex), $SINIR karakteri aşan satır $(toplam satir)."
if [ "$hatalar" -gt 0 ]; then echo "maui-lint: $hatalar hata." >&2; exit 1; fi
if [ "$azalan" -gt 0 ]; then echo "maui-lint: $azalan dosyada sayı tabanın altında; tabanı düşürün."; else echo "maui-lint: taban içinde."; fi
