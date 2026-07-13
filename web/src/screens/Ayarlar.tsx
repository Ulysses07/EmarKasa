import { useState, useEffect, useCallback } from "react";
import { apiGet, apiPut } from "../api/client";
import type { AyarlarDto, KanalDto } from "../api/tipler";
import { fmt } from "../format";
import { color } from "../theme";

export default function Ayarlar() {
  const [ayarlar, setAyarlar] = useState<AyarlarDto | null>(null);
  const [kanallar, setKanallar] = useState<KanalDto[]>([]);
  const [hata, setHata] = useState<string | null>(null);
  const [mesaj, setMesaj] = useState<string | null>(null);

  const [takip, setTakip] = useState("");
  const [kasaDevri, setKasaDevri] = useState("");
  const [yeniSifre, setYeniSifre] = useState("");

  const yukle = useCallback(() => {
    Promise.all([apiGet<AyarlarDto>("/ayarlar"), apiGet<KanalDto[]>("/kanallar")])
      .then(([a, k]) => {
        setAyarlar(a);
        setKanallar(k);
        setTakip(a.takipBaslangic);
        setKasaDevri(String(a.kasaAcilisDevri));
      })
      .catch((e) => setHata(String(e)));
  }, []);
  useEffect(() => {
    yukle();
  }, [yukle]);

  async function ayarlarKaydet() {
    const devir = parseFloat(kasaDevri.replace(/\./g, "").replace(",", "."));
    if (!takip || isNaN(devir)) return;
    try {
      await apiPut("/ayarlar", { takipBaslangic: takip, kasaAcilisDevri: devir });
      setMesaj("Ayarlar kaydedildi.");
      yukle();
    } catch (e) {
      setHata(String(e));
    }
  }
  async function sifreKaydet() {
    if (!yeniSifre.trim()) return;
    try {
      await apiPut("/ayarlar/izleyici-sifre", { yeniSifre });
      setYeniSifre("");
      setMesaj("İzleyici şifresi güncellendi.");
    } catch (e) {
      setHata(String(e));
    }
  }
  async function kanalDevirKaydet(k: KanalDto, deger: string) {
    const acilisDevri = parseFloat(deger.replace(/\./g, "").replace(",", "."));
    if (isNaN(acilisDevri)) return;
    try {
      await apiPut(`/kanallar/${k.id}`, { ...k, acilisDevri });
      yukle();
    } catch (e) {
      setHata(String(e));
    }
  }

  if (ayarlar === null && !hata) {
    return <div style={{ padding: 24, color: color.sub }}>Yükleniyor…</div>;
  }
  if (hata) {
    return <div style={{ padding: 24, color: color.neg }}>Veri alınamadı.</div>;
  }

  const kanalByAd = (ad: string) => kanallar.find((k) => k.ad === ad);

  return (
    <div style={{ flex: 1, minWidth: 0, display: "flex", flexDirection: "column" }}>
      <div
        style={{
          background: "#FFFFFF",
          borderBottom: "1px solid #E7E4DA",
          padding: "13px 28px",
          display: "flex",
          alignItems: "center",
          justifyContent: "space-between",
        }}
      >
        <span style={{ fontSize: "16.5px", fontWeight: 700, letterSpacing: "-0.01em" }}>
          Ayarlar
        </span>
        <div style={{ display: "flex", alignItems: "center", gap: 12 }}>
          {mesaj && (
            <span style={{ fontSize: "12px", fontWeight: 600, color: color.pos }}>{mesaj}</span>
          )}
          <button
            onClick={ayarlarKaydet}
            style={{
              height: "34px",
              border: "none",
              borderRadius: "8px",
              background: "#1E5F46",
              color: "#FFFFFF",
              font: "600 12.5px 'IBM Plex Sans',sans-serif",
              padding: "0 18px",
              cursor: "pointer",
            }}
          >
            Kaydet
          </button>
        </div>
      </div>
      <div
        style={{
          padding: "20px 28px 26px",
          display: "grid",
          gridTemplateColumns: "1.25fr 1fr",
          gap: "14px",
          alignItems: "start",
        }}
      >
        <div style={{ display: "flex", flexDirection: "column", gap: "14px" }}>
          <div
            style={{
              background: "#FFFFFF",
              border: "1px solid #E3E0D6",
              borderRadius: "12px",
              padding: "16px 20px 18px",
            }}
          >
            <div
              style={{
                display: "flex",
                alignItems: "baseline",
                justifyContent: "space-between",
                marginBottom: "4px",
              }}
            >
              <span style={{ fontSize: "13.5px", fontWeight: 700 }}>Kanallar</span>
              <button
                style={{
                  border: "1px dashed #CFCBBC",
                  background: "transparent",
                  borderRadius: "8px",
                  padding: "6px 13px",
                  font: "600 12px 'IBM Plex Sans',sans-serif",
                  color: "#1E5F46",
                  cursor: "pointer",
                }}
              >
                + Kanal ekle
              </button>
            </div>
            <p style={{ margin: "0 0 12px", fontSize: "11.5px", color: "#9AA08F" }}>
              Pasifleştirilen kanalın geçmiş kayıtları korunur; yeni girişe kapanır.
            </p>
            <div
              style={{
                display: "grid",
                gridTemplateColumns: "1.4fr 1.1fr 90px 130px",
                padding: "7px 12px",
                fontSize: "10.5px",
                fontWeight: 700,
                letterSpacing: "0.07em",
                color: "#9AA08F",
                borderBottom: "1px solid #EDEAE0",
              }}
            >
              <span>KANAL</span>
              <span style={{ textAlign: "right" }}>AÇILIŞ DEVRİ (₺)</span>
              <span style={{ paddingLeft: "20px" }}>DURUM</span>
              <span style={{ textAlign: "right" }}>İŞLEMLER</span>
            </div>
            <div
              style={{
                display: "grid",
                gridTemplateColumns: "1.4fr 1.1fr 90px 130px",
                padding: "10px 12px",
                alignItems: "center",
                borderBottom: "1px solid #F1EFE8",
              }}
            >
              <span
                style={{
                  display: "inline-flex",
                  alignItems: "center",
                  gap: "6px",
                  background: "#F5EAD2",
                  color: "#A16A0B",
                  borderRadius: "999px",
                  padding: "3px 10px",
                  fontSize: "11px",
                  fontWeight: 700,
                  width: "fit-content",
                }}
              >
                <span
                  style={{
                    width: "6px",
                    height: "6px",
                    borderRadius: "99px",
                    background: "#C98A12",
                  }}
                />
                MEZAT
              </span>
              <input
                key={kanalByAd("MEZAT")?.acilisDevri}
                defaultValue={fmt(kanalByAd("MEZAT")?.acilisDevri ?? 0)}
                onBlur={(e) => {
                  const k = kanalByAd("MEZAT");
                  if (k) kanalDevirKaydet(k, e.target.value);
                }}
                onKeyDown={(e) => {
                  if (e.key === "Enter") (e.target as HTMLInputElement).blur();
                }}
                style={{
                  height: "34px",
                  border: "1px solid #DAD6C9",
                  borderRadius: "8px",
                  background: "#FDFCFA",
                  padding: "0 11px",
                  font: "500 12.5px 'IBM Plex Mono',monospace",
                  color: "#20261F",
                  outline: "none",
                  textAlign: "right",
                  minWidth: 0,
                }}
              />
              <span style={{ paddingLeft: "20px" }}>
                <span
                  style={{
                    background: "#E3F1E8",
                    color: "#1B7A4E",
                    borderRadius: "999px",
                    padding: "3px 9px",
                    fontSize: "10.5px",
                    fontWeight: 700,
                  }}
                >
                  Aktif
                </span>
              </span>
              <span
                style={{
                  display: "flex",
                  gap: "12px",
                  justifyContent: "flex-end",
                  fontSize: "12px",
                  fontWeight: 600,
                }}
              >
                <a href="#1f" style={{ textDecoration: "none" }}>
                  Adlandır
                </a>
                <a href="#1f" style={{ textDecoration: "none", color: "#9AA08F" }}>
                  Pasifleştir
                </a>
              </span>
            </div>
            <div
              style={{
                display: "grid",
                gridTemplateColumns: "1.4fr 1.1fr 90px 130px",
                padding: "10px 12px",
                alignItems: "center",
                borderBottom: "1px solid #F1EFE8",
              }}
            >
              <span
                style={{
                  display: "inline-flex",
                  alignItems: "center",
                  gap: "6px",
                  background: "#E2ECF8",
                  color: "#245EA8",
                  borderRadius: "999px",
                  padding: "3px 10px",
                  fontSize: "11px",
                  fontWeight: 700,
                  width: "fit-content",
                }}
              >
                <span
                  style={{
                    width: "6px",
                    height: "6px",
                    borderRadius: "99px",
                    background: "#3572C1",
                  }}
                />
                PERAKENDE
              </span>
              <input
                key={kanalByAd("PERAKENDE")?.acilisDevri}
                defaultValue={fmt(kanalByAd("PERAKENDE")?.acilisDevri ?? 0)}
                onBlur={(e) => {
                  const k = kanalByAd("PERAKENDE");
                  if (k) kanalDevirKaydet(k, e.target.value);
                }}
                onKeyDown={(e) => {
                  if (e.key === "Enter") (e.target as HTMLInputElement).blur();
                }}
                style={{
                  height: "34px",
                  border: "1px solid #DAD6C9",
                  borderRadius: "8px",
                  background: "#FDFCFA",
                  padding: "0 11px",
                  font: "500 12.5px 'IBM Plex Mono',monospace",
                  color: "#20261F",
                  outline: "none",
                  textAlign: "right",
                  minWidth: 0,
                }}
              />
              <span style={{ paddingLeft: "20px" }}>
                <span
                  style={{
                    background: "#E3F1E8",
                    color: "#1B7A4E",
                    borderRadius: "999px",
                    padding: "3px 9px",
                    fontSize: "10.5px",
                    fontWeight: 700,
                  }}
                >
                  Aktif
                </span>
              </span>
              <span
                style={{
                  display: "flex",
                  gap: "12px",
                  justifyContent: "flex-end",
                  fontSize: "12px",
                  fontWeight: 600,
                }}
              >
                <a href="#1f" style={{ textDecoration: "none" }}>
                  Adlandır
                </a>
                <a href="#1f" style={{ textDecoration: "none", color: "#9AA08F" }}>
                  Pasifleştir
                </a>
              </span>
            </div>
            <div
              style={{
                display: "grid",
                gridTemplateColumns: "1.4fr 1.1fr 90px 130px",
                padding: "10px 12px",
                alignItems: "center",
              }}
            >
              <span
                style={{
                  display: "inline-flex",
                  alignItems: "center",
                  gap: "6px",
                  background: "#ECE6F7",
                  color: "#6D4FA1",
                  borderRadius: "999px",
                  padding: "3px 10px",
                  fontSize: "11px",
                  fontWeight: 700,
                  width: "fit-content",
                }}
              >
                <span
                  style={{
                    width: "6px",
                    height: "6px",
                    borderRadius: "99px",
                    background: "#7E5BBF",
                  }}
                />
                TOPTAN
              </span>
              <input
                key={kanalByAd("TOPTAN")?.acilisDevri}
                defaultValue={fmt(kanalByAd("TOPTAN")?.acilisDevri ?? 0)}
                onBlur={(e) => {
                  const k = kanalByAd("TOPTAN");
                  if (k) kanalDevirKaydet(k, e.target.value);
                }}
                onKeyDown={(e) => {
                  if (e.key === "Enter") (e.target as HTMLInputElement).blur();
                }}
                style={{
                  height: "34px",
                  border: "1px solid #DAD6C9",
                  borderRadius: "8px",
                  background: "#FDFCFA",
                  padding: "0 11px",
                  font: "500 12.5px 'IBM Plex Mono',monospace",
                  color: "#20261F",
                  outline: "none",
                  textAlign: "right",
                  minWidth: 0,
                }}
              />
              <span style={{ paddingLeft: "20px" }}>
                <span
                  style={{
                    background: "#E3F1E8",
                    color: "#1B7A4E",
                    borderRadius: "999px",
                    padding: "3px 9px",
                    fontSize: "10.5px",
                    fontWeight: 700,
                  }}
                >
                  Aktif
                </span>
              </span>
              <span
                style={{
                  display: "flex",
                  gap: "12px",
                  justifyContent: "flex-end",
                  fontSize: "12px",
                  fontWeight: 600,
                }}
              >
                <a href="#1f" style={{ textDecoration: "none" }}>
                  Adlandır
                </a>
                <a href="#1f" style={{ textDecoration: "none", color: "#9AA08F" }}>
                  Pasifleştir
                </a>
              </span>
            </div>
          </div>
          <div
            style={{
              background: "#FFFFFF",
              border: "1px solid #E3E0D6",
              borderRadius: "12px",
              padding: "16px 20px 18px",
            }}
          >
            <span
              style={{
                fontSize: "13.5px",
                fontWeight: 700,
                display: "block",
                marginBottom: "12px",
              }}
            >
              Genel
            </span>
            <div
              style={{
                display: "grid",
                gridTemplateColumns: "1fr 1fr 1fr",
                gap: "12px",
              }}
            >
              <div style={{ display: "flex", flexDirection: "column", gap: "5px" }}>
                <label style={{ fontSize: "11px", fontWeight: 600, color: "#6F7566" }}>
                  Kasa açılış devri (₺)
                </label>
                <input
                  value={kasaDevri}
                  onChange={(e) => setKasaDevri(e.target.value)}
                  style={{
                    height: "38px",
                    border: "1px solid #DAD6C9",
                    borderRadius: "8px",
                    background: "#FDFCFA",
                    padding: "0 11px",
                    font: "500 13px 'IBM Plex Mono',monospace",
                    color: "#20261F",
                    outline: "none",
                    textAlign: "right",
                    minWidth: 0,
                  }}
                />
              </div>
              <div style={{ display: "flex", flexDirection: "column", gap: "5px" }}>
                <label style={{ fontSize: "11px", fontWeight: 600, color: "#6F7566" }}>
                  Takip başlangıcı
                </label>
                <input
                  type="date"
                  value={takip}
                  onChange={(e) => setTakip(e.target.value)}
                  style={{
                    height: "38px",
                    border: "1px solid #DAD6C9",
                    borderRadius: "8px",
                    background: "#FDFCFA",
                    padding: "0 11px",
                    font: "500 13px 'IBM Plex Mono',monospace",
                    color: "#20261F",
                    outline: "none",
                    minWidth: 0,
                  }}
                />
              </div>
              <div style={{ display: "flex", flexDirection: "column", gap: "5px" }}>
                <label style={{ fontSize: "11px", fontWeight: 600, color: "#6F7566" }}>
                  Hafta başlangıcı
                </label>
                <div
                  style={{
                    height: "38px",
                    border: "1px solid #DAD6C9",
                    borderRadius: "8px",
                    background: "#FDFCFA",
                    padding: "0 11px",
                    display: "flex",
                    alignItems: "center",
                    justifyContent: "space-between",
                    fontSize: "12.5px",
                    cursor: "pointer",
                  }}
                >
                  <span>Pazartesi</span>
                  <span style={{ color: "#9AA08F", fontSize: "10px" }}>▾</span>
                </div>
              </div>
            </div>
            <p style={{ margin: "12px 0 0", fontSize: "11.5px", color: "#9AA08F" }}>
              Haftalar Pazartesi başlar; ay sonunda dönem ikiye bölünür (ör. 29–30 Haz · 1–5
              Tem).
            </p>
          </div>
        </div>
        <div style={{ display: "flex", flexDirection: "column", gap: "14px" }}>
          <div
            style={{
              background: "#FFFFFF",
              border: "1px solid #E3E0D6",
              borderRadius: "12px",
              padding: "16px 20px 18px",
            }}
          >
            <span
              style={{
                fontSize: "13.5px",
                fontWeight: 700,
                display: "block",
                marginBottom: "4px",
              }}
            >
              İzleyici erişimi
            </span>
            <p
              style={{
                margin: "0 0 12px",
                fontSize: "11.5px",
                color: "#9AA08F",
                lineHeight: 1.5,
              }}
            >
              Tüm ortaklar tek şifreyle, salt görüntüleme modunda girer. Mobilde düzenleme
              kapalıdır.
            </p>
            <div style={{ display: "flex", gap: "8px", alignItems: "end" }}>
              <div
                style={{
                  display: "flex",
                  flexDirection: "column",
                  gap: "5px",
                  flex: 1,
                }}
              >
                <label style={{ fontSize: "11px", fontWeight: 600, color: "#6F7566" }}>
                  Ortak şifresi
                </label>
                <input
                  value={yeniSifre}
                  onChange={(e) => setYeniSifre(e.target.value)}
                  placeholder="Yeni şifre"
                  type="password"
                  onKeyDown={(e) => {
                    if (e.key === "Enter") sifreKaydet();
                  }}
                  style={{
                    height: "38px",
                    border: "1px solid #DAD6C9",
                    borderRadius: "8px",
                    background: "#FDFCFA",
                    padding: "0 11px",
                    font: "600 14px 'IBM Plex Mono',monospace",
                    color: "#20261F",
                    outline: "none",
                    minWidth: 0,
                  }}
                />
              </div>
              <button
                onClick={sifreKaydet}
                style={{
                  height: "38px",
                  border: "1px solid #DAD6C9",
                  borderRadius: "8px",
                  background: "#FFFFFF",
                  color: "#4E5548",
                  font: "600 12.5px 'IBM Plex Sans',sans-serif",
                  padding: "0 14px",
                  cursor: "pointer",
                }}
              >
                Yenile
              </button>
            </div>
            <p style={{ margin: "10px 0 0", fontSize: "11px", color: "#9AA08F" }}>
              Son değişiklik: 14 May 2026
            </p>
          </div>
          <div
            style={{
              background: "#FFFFFF",
              border: "1px solid #E3E0D6",
              borderRadius: "12px",
              padding: "16px 20px 18px",
            }}
          >
            <span
              style={{
                fontSize: "13.5px",
                fontWeight: 700,
                display: "block",
                marginBottom: "4px",
              }}
            >
              Dışa aktarma
            </span>
            <p
              style={{
                margin: "0 0 12px",
                fontSize: "11.5px",
                color: "#9AA08F",
                lineHeight: 1.5,
              }}
            >
              Tüm defter, haftalık özet ve aylık rapor sayfalarıyla birlikte indirilir.
            </p>
            <div style={{ display: "flex", gap: "8px" }}>
              <button
                style={{
                  height: "38px",
                  border: "1px solid #1E5F46",
                  borderRadius: "8px",
                  background: "#FFFFFF",
                  color: "#1E5F46",
                  font: "600 12.5px 'IBM Plex Sans',sans-serif",
                  padding: "0 16px",
                  cursor: "pointer",
                  display: "flex",
                  alignItems: "center",
                  gap: "8px",
                }}
              >
                <svg width="13" height="13" viewBox="0 0 16 16" fill="none">
                  <path
                    d="M8 2.5v8M5 7.5 8 10.5l3-3M3 13.5h10"
                    stroke="currentColor"
                    strokeWidth="1.6"
                    strokeLinecap="round"
                    strokeLinejoin="round"
                  />
                </svg>
                Excel (.xlsx)
              </button>
              <button
                style={{
                  height: "38px",
                  border: "1px solid #DAD6C9",
                  borderRadius: "8px",
                  background: "#FFFFFF",
                  color: "#4E5548",
                  font: "600 12.5px 'IBM Plex Sans',sans-serif",
                  padding: "0 16px",
                  cursor: "pointer",
                }}
              >
                CSV
              </button>
            </div>
            <p style={{ margin: "10px 0 0", fontSize: "11px", color: "#9AA08F" }}>
              Son dışa aktarma: 30 Haz 2026 · selim@…
            </p>
          </div>
        </div>
      </div>
    </div>
  );
}
