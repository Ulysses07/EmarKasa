// 1e Cari Yönetimi — "Kasa Defteri.dc.html" (satır 662–707) içerik panosundan birebir portlandı.
// Not: soldaki 220px sidebar shell tarafından sağlanır, buraya dahil değildir.

import { useState, useEffect, useCallback } from "react";
import { apiGet, apiPost, apiPut } from "../api/client";
import { carilerAdapt } from "../data/adapters";
import type { CariDto, IslemDto } from "../api/tipler";
import { color } from "../theme";

const GRID = "1.7fr 110px 130px 170px 110px 210px";

export default function Cariler() {
  const [hamCariler, setHamCariler] = useState<CariDto[] | null>(null);
  const [islemler, setIslemler] = useState<IslemDto[]>([]);
  const [hata, setHata] = useState<string | null>(null);
  const [yeniAd, setYeniAd] = useState("");

  const yukle = useCallback(() => {
    Promise.all([apiGet<CariDto[]>("/cariler"), apiGet<IslemDto[]>("/islemler")])
      .then(([c, i]) => {
        setHamCariler(c);
        setIslemler(i);
      })
      .catch((e) => setHata(String(e)));
  }, []);
  useEffect(() => {
    yukle();
  }, [yukle]);

  async function cariEkle(ad: string) {
    if (!ad.trim()) return;
    try {
      await apiPost("/cariler", { ad: ad.trim(), aktif: true });
      setYeniAd("");
      yukle();
    } catch (e) {
      setHata(String(e));
    }
  }
  async function cariToggle(dto: CariDto) {
    try {
      await apiPut(`/cariler/${dto.id}`, { ad: dto.ad, aktif: !dto.aktif });
      yukle();
    } catch (e) {
      setHata(String(e));
    }
  }

  if (hamCariler === null && !hata) {
    return <div style={{ padding: 24, color: color.sub }}>Yükleniyor…</div>;
  }
  if (hata) {
    return <div style={{ padding: 24, color: color.neg }}>Veri alınamadı.</div>;
  }

  const raw = hamCariler ?? [];
  const satirlar = carilerAdapt(raw, islemler);

  return (
    <div style={{ flex: 1, minWidth: 0, display: "flex", flexDirection: "column" }}>
      {/* topbar */}
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
          Cari Yönetimi
        </span>
        <div style={{ display: "flex", alignItems: "center", gap: "10px" }}>
          <div
            style={{
              display: "flex",
              alignItems: "center",
              gap: "8px",
              border: "1px solid #DAD6C9",
              borderRadius: "8px",
              background: "#FDFCFA",
              padding: "0 11px",
              height: "34px",
              width: "230px",
            }}
          >
            <svg width="13" height="13" viewBox="0 0 16 16" fill="none">
              <circle cx="7" cy="7" r="4.5" stroke="#9AA08F" strokeWidth="1.6" />
              <path d="m10.5 10.5 3 3" stroke="#9AA08F" strokeWidth="1.6" strokeLinecap="round" />
            </svg>
            <input
              placeholder="Yeni cari adı…"
              value={yeniAd}
              onChange={(e) => setYeniAd(e.target.value)}
              onKeyDown={(e) => {
                if (e.key === "Enter") cariEkle(yeniAd);
              }}
              style={{
                border: "none",
                background: "transparent",
                outline: "none",
                fontSize: "12.5px",
                color: "#20261F",
                width: "100%",
              }}
            />
          </div>
          <button
            onClick={() => cariEkle(yeniAd)}
            style={{
              height: "34px",
              border: "none",
              borderRadius: "8px",
              background: "#1E5F46",
              color: "#FFFFFF",
              font: "600 12.5px 'IBM Plex Sans', sans-serif",
              padding: "0 15px",
              cursor: "pointer",
            }}
          >
            + Yeni Cari
          </button>
        </div>
      </div>

      {/* body */}
      <div style={{ padding: "20px 28px 26px", display: "flex", flexDirection: "column", gap: "14px" }}>
        {/* birleştirme önerisi */}
        <div
          style={{
            background: "#FDF6E7",
            border: "1px solid #EAD9A8",
            borderRadius: "10px",
            padding: "11px 16px",
            display: "flex",
            alignItems: "center",
            gap: "12px",
          }}
        >
          <svg width="16" height="16" viewBox="0 0 16 16" fill="none">
            <path d="M8 2 14.5 13.5h-13L8 2Z" stroke="#A16A0B" strokeWidth="1.5" strokeLinejoin="round" />
            <path d="M8 6.5v3M8 11.5v.1" stroke="#A16A0B" strokeWidth="1.5" strokeLinecap="round" />
          </svg>
          <span style={{ fontSize: "12.5px", color: "#6d5a22", flex: 1 }}>
            Olası mükerrer kayıt: <strong>“Mehmet Kaya”</strong> ile <strong>“M. Kaya”</strong> aynı cari
            olabilir.
          </span>
          <button
            style={{
              height: "30px",
              border: "1px solid #C98A12",
              borderRadius: "7px",
              background: "#FFFFFF",
              color: "#A16A0B",
              font: "600 12px 'IBM Plex Sans', sans-serif",
              padding: "0 13px",
              cursor: "pointer",
            }}
          >
            İncele ve birleştir
          </button>
          <button
            style={{
              border: "none",
              background: "transparent",
              color: "#B0A375",
              fontSize: "12px",
              cursor: "pointer",
            }}
          >
            Yoksay
          </button>
        </div>

        {/* cari tablosu */}
        <div
          style={{
            background: "#FFFFFF",
            border: "1px solid #E3E0D6",
            borderRadius: "12px",
            overflow: "hidden",
          }}
        >
          {/* başlık satırı */}
          <div
            style={{
              display: "grid",
              gridTemplateColumns: GRID,
              padding: "10px 18px",
              fontSize: "10.5px",
              fontWeight: 700,
              letterSpacing: "0.07em",
              color: "#9AA08F",
              borderBottom: "1px solid #EDEAE0",
            }}
          >
            <span>CARİ</span>
            <span style={{ textAlign: "right" }}>İŞLEM</span>
            <span style={{ textAlign: "right" }}>SON İŞLEM</span>
            <span style={{ textAlign: "right" }}>TOPLAM HACİM (₺)</span>
            <span style={{ paddingLeft: "24px" }}>DURUM</span>
            <span style={{ textAlign: "right" }}>İŞLEMLER</span>
          </div>

          {satirlar.map((c, i) => (
            <div
              key={raw[i].id}
              style={{
                display: "grid",
                gridTemplateColumns: GRID,
                padding: "11px 18px",
                alignItems: "center",
                borderBottom: "1px solid #F1EFE8",
                opacity: c.op,
              }}
            >
              <span style={{ display: "flex", alignItems: "center", gap: "10px" }}>
                <span
                  style={{
                    width: "28px",
                    height: "28px",
                    borderRadius: "99px",
                    background: "#F1EFE8",
                    color: "#6F7566",
                    display: "flex",
                    alignItems: "center",
                    justifyContent: "center",
                    fontSize: "10.5px",
                    fontWeight: 700,
                  }}
                >
                  {c.bas}
                </span>
                <span style={{ fontSize: "13px", fontWeight: 600 }}>{c.ad}</span>
                {c.uyari && (
                  <span
                    style={{
                      background: "#FDF6E7",
                      border: "1px solid #EAD9A8",
                      color: "#A16A0B",
                      borderRadius: "999px",
                      padding: "2px 8px",
                      fontSize: "10px",
                      fontWeight: 700,
                    }}
                  >
                    mükerrer?
                  </span>
                )}
              </span>
              <span
                style={{ font: "500 12.5px 'IBM Plex Mono', monospace", textAlign: "right", color: "#6F7566" }}
              >
                {c.islem}
              </span>
              <span
                style={{ font: "500 12.5px 'IBM Plex Mono', monospace", textAlign: "right", color: "#6F7566" }}
              >
                {c.son}
              </span>
              <span style={{ font: "600 13px 'IBM Plex Mono', monospace", textAlign: "right" }}>
                {c.hacim}
              </span>
              <span style={{ paddingLeft: "24px" }}>
                <span
                  style={{
                    borderRadius: "999px",
                    padding: "3px 10px",
                    fontSize: "10.5px",
                    fontWeight: 700,
                    background: c.dbg,
                    color: c.dc,
                  }}
                >
                  {c.durum}
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
                <a href="#1e" style={{ textDecoration: "none" }}>
                  Birleştir
                </a>
                <a href="#1e" style={{ textDecoration: "none" }}>
                  Düzenle
                </a>
                <a
                  href="#1e"
                  onClick={(e) => {
                    e.preventDefault();
                    cariToggle(raw[i]);
                  }}
                  style={{ textDecoration: "none", color: "#9AA08F" }}
                >
                  {c.eylem}
                </a>
              </span>
            </div>
          ))}

          <div style={{ padding: "11px 18px", fontSize: "12px", color: "#9AA08F" }}>
            {raw.length} cari · {raw.filter((c) => !c.aktif).length} pasif
          </div>
        </div>
      </div>
    </div>
  );
}
