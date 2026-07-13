import { useState } from "react";
import { ayGruplar, mzAyPath, prAyPath, tpAyPath } from "../data/mock";

export default function AylikRapor() {
  const [tur, setTur] = useState<"sütun" | "çizgi">("sütun");
  const aySutun = tur !== "çizgi";
  const ayCizgi = tur === "çizgi";

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
        <span style={{ fontSize: "16.5px", fontWeight: 700, letterSpacing: "-0.01em" }}>Aylık Rapor</span>
        <div style={{ display: "flex", alignItems: "center", gap: "10px" }}>
          <div
            style={{
              display: "flex",
              gap: "2px",
              background: "#F2F0E9",
              borderRadius: "999px",
              padding: "3px",
            }}
          >
            <button
              onClick={() => setTur("sütun")}
              style={{
                border: "none",
                borderRadius: "999px",
                padding: "6px 14px",
                font: "600 12px 'IBM Plex Sans',sans-serif",
                cursor: "pointer",
                background: aySutun ? "#FFFFFF" : "transparent",
                color: aySutun ? "#20261F" : "#6F7566",
                boxShadow: aySutun ? "0 1px 3px rgba(32,38,31,0.14)" : "none",
              }}
            >
              Sütun
            </button>
            <button
              onClick={() => setTur("çizgi")}
              style={{
                border: "none",
                borderRadius: "999px",
                padding: "6px 14px",
                font: "600 12px 'IBM Plex Sans',sans-serif",
                cursor: "pointer",
                background: ayCizgi ? "#FFFFFF" : "transparent",
                color: ayCizgi ? "#20261F" : "#6F7566",
                boxShadow: ayCizgi ? "0 1px 3px rgba(32,38,31,0.14)" : "none",
              }}
            >
              Çizgi
            </button>
          </div>
          <button
            style={{
              border: "1px solid #DAD6C9",
              background: "#FFFFFF",
              borderRadius: "8px",
              padding: "7px 13px",
              font: "600 12px 'IBM Plex Sans',sans-serif",
              color: "#4E5548",
              cursor: "pointer",
              display: "flex",
              alignItems: "center",
              gap: "7px",
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
            Dışa aktar
          </button>
        </div>
      </div>
      <div style={{ padding: "20px 28px 26px", display: "flex", flexDirection: "column", gap: "14px" }}>
        <div style={{ display: "flex", gap: "6px", alignItems: "center" }}>
          <button
            style={{
              border: "1px solid #DAD6C9",
              background: "#FFFFFF",
              borderRadius: "999px",
              padding: "6px 15px",
              font: "600 12px 'IBM Plex Sans',sans-serif",
              color: "#6F7566",
              cursor: "pointer",
            }}
          >
            Nisan
          </button>
          <button
            style={{
              border: "1px solid #DAD6C9",
              background: "#FFFFFF",
              borderRadius: "999px",
              padding: "6px 15px",
              font: "600 12px 'IBM Plex Sans',sans-serif",
              color: "#6F7566",
              cursor: "pointer",
            }}
          >
            Mayıs
          </button>
          <button
            style={{
              border: "none",
              background: "#1F2A23",
              borderRadius: "999px",
              padding: "7px 16px",
              font: "600 12px 'IBM Plex Sans',sans-serif",
              color: "#FFFFFF",
              cursor: "pointer",
            }}
          >
            Haziran
          </button>
          <button
            style={{
              border: "1px dashed #CFCBBC",
              background: "transparent",
              borderRadius: "999px",
              padding: "6px 15px",
              font: "600 12px 'IBM Plex Sans',sans-serif",
              color: "#9AA08F",
              cursor: "pointer",
            }}
          >
            Temmuz · kısmi
          </button>
          <span style={{ marginLeft: "10px", fontSize: "12px", color: "#9AA08F" }}>
            Haziran 2026 · 5 dönem (29–30 Haz dahil)
          </span>
        </div>

        <div style={{ background: "#FFFFFF", border: "1px solid #E3E0D6", borderRadius: "12px", overflow: "hidden" }}>
          <div
            style={{
              display: "grid",
              gridTemplateColumns: "170px 1fr 1fr 1fr 1fr 1fr 1.2fr",
              padding: "10px 18px",
              fontSize: "10.5px",
              fontWeight: 700,
              letterSpacing: "0.07em",
              color: "#9AA08F",
              borderBottom: "1px solid #EDEAE0",
            }}
          >
            <span>KANAL</span>
            <span style={{ textAlign: "right" }}>AYLIK GELEN</span>
            <span style={{ textAlign: "right" }}>CARİ GİDEN</span>
            <span style={{ textAlign: "right" }}>SABİT GİDER</span>
            <span style={{ textAlign: "right" }}>KREDİ KARTI</span>
            <span style={{ textAlign: "right" }}>ORTAK PAY</span>
            <span style={{ textAlign: "right", paddingRight: "14px" }}>AY SONUCU</span>
          </div>
          <div
            style={{
              display: "grid",
              gridTemplateColumns: "170px 1fr 1fr 1fr 1fr 1fr 1.2fr",
              padding: "11px 18px",
              alignItems: "center",
              borderBottom: "1px solid #F1EFE8",
            }}
          >
            <span>
              <span
                style={{
                  display: "inline-flex",
                  alignItems: "center",
                  gap: "6px",
                  borderRadius: "999px",
                  padding: "3px 10px",
                  fontSize: "11px",
                  fontWeight: 700,
                  background: "#F5EAD2",
                  color: "#A16A0B",
                }}
              >
                <span style={{ width: "6px", height: "6px", borderRadius: "99px", background: "#C98A12" }} />
                MEZAT
              </span>
            </span>
            <span style={{ font: "500 13px 'IBM Plex Mono',monospace", textAlign: "right" }}>2.061.600,00</span>
            <span style={{ font: "500 13px 'IBM Plex Mono',monospace", textAlign: "right", color: "#6F7566" }}>1.244.700,00</span>
            <span style={{ font: "500 13px 'IBM Plex Mono',monospace", textAlign: "right", color: "#6F7566" }}>214.300,00</span>
            <span style={{ font: "500 13px 'IBM Plex Mono',monospace", textAlign: "right", color: "#6F7566" }}>170.200,00</span>
            <span style={{ font: "500 13px 'IBM Plex Mono',monospace", textAlign: "right", color: "#6F7566" }}>40.200,00</span>
            <span
              style={{
                font: "700 13.5px 'IBM Plex Mono',monospace",
                textAlign: "right",
                padding: "6px 14px 6px 0",
                background: "#FAF8F2",
                borderRadius: "7px",
                color: "#1B7A4E",
              }}
            >
              +392.200,00
            </span>
          </div>
          <div
            style={{
              display: "grid",
              gridTemplateColumns: "170px 1fr 1fr 1fr 1fr 1fr 1.2fr",
              padding: "11px 18px",
              alignItems: "center",
              borderBottom: "1px solid #F1EFE8",
            }}
          >
            <span>
              <span
                style={{
                  display: "inline-flex",
                  alignItems: "center",
                  gap: "6px",
                  borderRadius: "999px",
                  padding: "3px 10px",
                  fontSize: "11px",
                  fontWeight: 700,
                  background: "#E2ECF8",
                  color: "#245EA8",
                }}
              >
                <span style={{ width: "6px", height: "6px", borderRadius: "99px", background: "#3572C1" }} />
                PERAKENDE
              </span>
            </span>
            <span style={{ font: "500 13px 'IBM Plex Mono',monospace", textAlign: "right" }}>668.500,00</span>
            <span style={{ font: "500 13px 'IBM Plex Mono',monospace", textAlign: "right", color: "#6F7566" }}>331.800,00</span>
            <span style={{ font: "500 13px 'IBM Plex Mono',monospace", textAlign: "right", color: "#6F7566" }}>118.600,00</span>
            <span style={{ font: "500 13px 'IBM Plex Mono',monospace", textAlign: "right", color: "#6F7566" }}>81.900,00</span>
            <span style={{ font: "500 13px 'IBM Plex Mono',monospace", textAlign: "right", color: "#6F7566" }}>40.200,00</span>
            <span
              style={{
                font: "700 13.5px 'IBM Plex Mono',monospace",
                textAlign: "right",
                padding: "6px 14px 6px 0",
                background: "#FAF8F2",
                borderRadius: "7px",
                color: "#1B7A4E",
              }}
            >
              +96.000,00
            </span>
          </div>
          <div
            style={{
              display: "grid",
              gridTemplateColumns: "170px 1fr 1fr 1fr 1fr 1fr 1.2fr",
              padding: "11px 18px",
              alignItems: "center",
              borderBottom: "1px solid #F1EFE8",
            }}
          >
            <span>
              <span
                style={{
                  display: "inline-flex",
                  alignItems: "center",
                  gap: "6px",
                  borderRadius: "999px",
                  padding: "3px 10px",
                  fontSize: "11px",
                  fontWeight: 700,
                  background: "#ECE6F7",
                  color: "#6D4FA1",
                }}
              >
                <span style={{ width: "6px", height: "6px", borderRadius: "99px", background: "#7E5BBF" }} />
                TOPTAN
              </span>
            </span>
            <span style={{ font: "500 13px 'IBM Plex Mono',monospace", textAlign: "right" }}>362.700,00</span>
            <span style={{ font: "500 13px 'IBM Plex Mono',monospace", textAlign: "right", color: "#6F7566" }}>296.400,00</span>
            <span style={{ font: "500 13px 'IBM Plex Mono',monospace", textAlign: "right", color: "#6F7566" }}>41.700,00</span>
            <span style={{ font: "500 13px 'IBM Plex Mono',monospace", textAlign: "right", color: "#6F7566" }}>31.500,00</span>
            <span style={{ font: "500 13px 'IBM Plex Mono',monospace", textAlign: "right", color: "#6F7566" }}>40.200,00</span>
            <span
              style={{
                font: "700 13.5px 'IBM Plex Mono',monospace",
                textAlign: "right",
                padding: "6px 14px 6px 0",
                background: "#FAF8F2",
                borderRadius: "7px",
                color: "#C13A2E",
              }}
            >
              -47.100,00
            </span>
          </div>
          <div
            style={{
              display: "grid",
              gridTemplateColumns: "170px 1fr 1fr 1fr 1fr 1fr 1.2fr",
              padding: "12px 18px",
              alignItems: "center",
              background: "#FBFAF6",
            }}
          >
            <span style={{ fontSize: "12.5px", fontWeight: 700 }}>Toplam</span>
            <span style={{ font: "700 13px 'IBM Plex Mono',monospace", textAlign: "right" }}>3.092.800,00</span>
            <span style={{ font: "700 13px 'IBM Plex Mono',monospace", textAlign: "right", color: "#6F7566" }}>1.872.900,00</span>
            <span style={{ font: "700 13px 'IBM Plex Mono',monospace", textAlign: "right", color: "#6F7566" }}>374.600,00</span>
            <span style={{ font: "700 13px 'IBM Plex Mono',monospace", textAlign: "right", color: "#6F7566" }}>283.600,00</span>
            <span style={{ font: "700 13px 'IBM Plex Mono',monospace", textAlign: "right", color: "#6F7566" }}>120.600,00</span>
            <span
              style={{
                font: "700 14px 'IBM Plex Mono',monospace",
                textAlign: "right",
                padding: "6px 14px 6px 0",
                background: "#F0F5F1",
                borderRadius: "7px",
                color: "#1B7A4E",
              }}
            >
              +441.100,00
            </span>
          </div>
        </div>

        <div style={{ background: "#FFFFFF", border: "1px solid #E3E0D6", borderRadius: "12px", padding: "16px 20px 14px" }}>
          <div style={{ display: "flex", alignItems: "center", justifyContent: "space-between", marginBottom: "14px" }}>
            <div style={{ display: "flex", alignItems: "baseline", gap: "10px" }}>
              <span style={{ fontSize: "13.5px", fontWeight: 700 }}>AY SONUCU — kanal trendi</span>
              <span style={{ fontSize: "11.5px", color: "#9AA08F" }}>Nisan–Haziran 2026</span>
            </div>
            <div style={{ display: "flex", gap: "14px" }}>
              <span style={{ display: "flex", alignItems: "center", gap: "6px", fontSize: "11px", fontWeight: 600, color: "#A16A0B" }}>
                <span style={{ width: "8px", height: "8px", borderRadius: "3px", background: "#C98A12" }} />
                MEZAT
              </span>
              <span style={{ display: "flex", alignItems: "center", gap: "6px", fontSize: "11px", fontWeight: 600, color: "#245EA8" }}>
                <span style={{ width: "8px", height: "8px", borderRadius: "3px", background: "#3572C1" }} />
                PERAKENDE
              </span>
              <span style={{ display: "flex", alignItems: "center", gap: "6px", fontSize: "11px", fontWeight: 600, color: "#6D4FA1" }}>
                <span style={{ width: "8px", height: "8px", borderRadius: "3px", background: "#7E5BBF" }} />
                TOPTAN
              </span>
            </div>
          </div>
          {aySutun && (
            <div style={{ position: "relative", padding: "0 10px" }}>
              <div style={{ position: "absolute", left: "10px", right: "10px", top: "20px", borderTop: "1px dashed #EDEAE0" }} />
              <div style={{ position: "absolute", left: "10px", right: "10px", top: "75px", borderTop: "1px dashed #EDEAE0" }} />
              <div style={{ position: "absolute", left: "10px", right: "10px", top: "130px", borderTop: "1px solid #DAD6C9" }} />
              <span style={{ position: "absolute", right: "12px", top: "8px", font: "500 9.5px 'IBM Plex Mono',monospace", color: "#B0B5A4" }}>400B</span>
              <span style={{ position: "absolute", right: "12px", top: "63px", font: "500 9.5px 'IBM Plex Mono',monospace", color: "#B0B5A4" }}>200B</span>
              <span style={{ position: "absolute", right: "12px", top: "133px", font: "500 9.5px 'IBM Plex Mono',monospace", color: "#B0B5A4" }}>0</span>
              <div style={{ position: "relative", display: "flex", gap: "110px", justifyContent: "center" }}>
                {ayGruplar.map((g) => (
                  <div key={g.ad} style={{ display: "flex", flexDirection: "column", alignItems: "center", gap: "10px" }}>
                    <div style={{ display: "flex", gap: "7px" }}>
                      {g.bars.map((b, i) => (
                        <div key={i} style={{ position: "relative", width: "30px", height: "186px" }}>
                          <div
                            style={{
                              position: "absolute",
                              left: 0,
                              right: 0,
                              borderRadius: "3px",
                              top: `${b.top}px`,
                              height: `${b.h}px`,
                              background: b.c,
                            }}
                          >
                            <title>{b.t}</title>
                          </div>
                        </div>
                      ))}
                    </div>
                    <span style={{ fontSize: "12px", fontWeight: 600, color: "#6F7566" }}>{g.ad}</span>
                  </div>
                ))}
              </div>
            </div>
          )}
          {ayCizgi && (
            <svg width="100%" viewBox="0 0 640 200" style={{ display: "block" }}>
              <line x1="30" y1="20" x2="610" y2="20" stroke="#EDEAE0" strokeWidth="1" strokeDasharray="3 3" />
              <line x1="30" y1="75" x2="610" y2="75" stroke="#EDEAE0" strokeWidth="1" strokeDasharray="3 3" />
              <line x1="30" y1="130" x2="610" y2="130" stroke="#DAD6C9" strokeWidth="1" />
              <text x="614" y="23" fontSize="9.5" fill="#B0B5A4" fontFamily="IBM Plex Mono">400B</text>
              <text x="614" y="78" fontSize="9.5" fill="#B0B5A4" fontFamily="IBM Plex Mono">200B</text>
              <text x="614" y="133" fontSize="9.5" fill="#B0B5A4" fontFamily="IBM Plex Mono">0</text>
              <path d={mzAyPath} fill="none" stroke="#C98A12" strokeWidth="2.5" strokeLinejoin="round" strokeLinecap="round" />
              <path d={prAyPath} fill="none" stroke="#3572C1" strokeWidth="2.5" strokeLinejoin="round" strokeLinecap="round" />
              <path d={tpAyPath} fill="none" stroke="#7E5BBF" strokeWidth="2.5" strokeLinejoin="round" strokeLinecap="round" />
              <text x="120" y="160" fontSize="12" fontWeight="600" fill="#6F7566" fontFamily="IBM Plex Sans" textAnchor="middle">Nisan</text>
              <text x="320" y="160" fontSize="12" fontWeight="600" fill="#6F7566" fontFamily="IBM Plex Sans" textAnchor="middle">Mayıs</text>
              <text x="520" y="160" fontSize="12" fontWeight="600" fill="#6F7566" fontFamily="IBM Plex Sans" textAnchor="middle">Haziran</text>
            </svg>
          )}
        </div>
      </div>
    </div>
  );
}
