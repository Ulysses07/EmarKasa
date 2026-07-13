import { useHaftalik } from '../data/hooks'
import { color } from "../theme";
import type { KanalSatir } from "../data/types";

const HAFTA_SAYISI = 3;

const gridCols = "170px 1fr 1fr 1fr 1.15fr";

export default function HaftalikOzet() {
  const { veri, yukleniyor, hata } = useHaftalik()
  if (yukleniyor) return <div style={{ padding: 24, color: color.sub }}>Yükleniyor…</div>
  if (hata) return <div style={{ padding: 24, color: color.neg }}>Veri alınamadı.</div>
  const bloklar = (veri ?? []).slice(0, HAFTA_SAYISI)
  const haftaSayisiEtiket = String(HAFTA_SAYISI);

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
          Haftalık Özet
        </span>
        <div style={{ display: "flex", alignItems: "center", gap: "10px" }}>
          <div
            style={{
              height: "34px",
              border: `1px solid ${color.borderInput}`,
              borderRadius: "8px",
              background: color.inputBg,
              padding: "0 12px",
              display: "flex",
              alignItems: "center",
              gap: "8px",
              fontSize: "12px",
              fontWeight: 600,
              color: "#4E5548",
              cursor: "pointer",
            }}
          >
            Son {haftaSayisiEtiket} dönem{" "}
            <span style={{ color: "#9AA08F", fontSize: "9px" }}>▾</span>
          </div>
          <a href="#1c" style={{ fontSize: "12px", fontWeight: 600, textDecoration: "none" }}>
            Tümünü gör
          </a>
        </div>
      </div>
      <div
        style={{
          padding: "20px 28px 26px",
          display: "flex",
          flexDirection: "column",
          gap: "16px",
        }}
      >
        {bloklar.map((h) => (
          <div
            key={h.donem}
            style={{
              background: "#FFFFFF",
              border: "1px solid #E3E0D6",
              borderRadius: "12px",
              overflow: "hidden",
            }}
          >
            <div
              style={{
                padding: "13px 18px",
                display: "flex",
                alignItems: "center",
                justifyContent: "space-between",
                borderBottom: "1px solid #EDEAE0",
              }}
            >
              <div style={{ display: "flex", alignItems: "center", gap: "10px" }}>
                <span style={{ fontSize: "14.5px", fontWeight: 700, letterSpacing: "-0.01em" }}>
                  {h.donem}
                </span>
                {h.split && (
                  <span
                    style={{
                      background: "#F1EFE8",
                      border: "1px dashed #CFCBBC",
                      color: "#6F7566",
                      borderRadius: "999px",
                      padding: "2px 9px",
                      fontSize: "10.5px",
                      fontWeight: 600,
                    }}
                  >
                    bölünmüş hafta
                  </span>
                )}
              </div>
              <div style={{ display: "flex", alignItems: "center", gap: "16px" }}>
                <span style={{ fontSize: "11.5px", color: "#9AA08F" }}>
                  Kasa Sonucu{" "}
                  <span
                    style={{
                      font: "700 13px 'IBM Plex Mono',monospace",
                      color: h.tSonucC,
                      marginLeft: "4px",
                    }}
                  >
                    {h.tSonuc}
                  </span>
                </span>
                <span
                  style={{
                    background: "#F0F5F1",
                    borderRadius: "9px",
                    padding: "6px 13px",
                    fontSize: "11.5px",
                    color: "#4E5548",
                  }}
                >
                  Kasa Devri{" "}
                  <span
                    style={{
                      font: "700 14px 'IBM Plex Mono',monospace",
                      color: "#1E5F46",
                      marginLeft: "5px",
                    }}
                  >
                    {h.tDevir}
                  </span>
                </span>
              </div>
            </div>
            <div
              style={{
                display: "grid",
                gridTemplateColumns: gridCols,
                padding: "8px 18px",
                fontSize: "10.5px",
                fontWeight: 700,
                letterSpacing: "0.07em",
                color: "#9AA08F",
                borderBottom: "1px solid #F1EFE8",
              }}
            >
              <span>KANAL</span>
              <span style={{ textAlign: "right" }}>GELEN</span>
              <span style={{ textAlign: "right" }}>GİDEN</span>
              <span style={{ textAlign: "right" }}>SONUÇ</span>
              <span style={{ textAlign: "right", paddingRight: "14px" }}>KANAL DEVRİ</span>
            </div>
            {h.rows.map((r: KanalSatir) => (
              <div
                key={r.k}
                style={{
                  display: "grid",
                  gridTemplateColumns: gridCols,
                  padding: "9px 18px",
                  alignItems: "center",
                  borderBottom: "1px solid #F6F4EE",
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
                      background: r.kbg,
                      color: r.kc,
                    }}
                  >
                    <span
                      style={{
                        width: "6px",
                        height: "6px",
                        borderRadius: "99px",
                        background: r.kdot,
                      }}
                    />
                    {r.k}
                  </span>
                </span>
                <span
                  style={{ font: "500 13px 'IBM Plex Mono',monospace", textAlign: "right" }}
                >
                  {r.gelen}
                </span>
                <span
                  style={{
                    font: "500 13px 'IBM Plex Mono',monospace",
                    textAlign: "right",
                    color: "#6F7566",
                  }}
                >
                  {r.giden}
                </span>
                <span
                  style={{
                    font: "600 13px 'IBM Plex Mono',monospace",
                    textAlign: "right",
                    color: r.sonucC,
                  }}
                >
                  {r.sonuc}
                </span>
                <span
                  style={{
                    font: "600 13px 'IBM Plex Mono',monospace",
                    textAlign: "right",
                    padding: "5px 14px 5px 0",
                    background: "#FAF8F2",
                    borderRadius: "7px",
                    color: r.devirC,
                  }}
                >
                  {r.devir}
                </span>
              </div>
            ))}
            <div
              style={{
                display: "grid",
                gridTemplateColumns: gridCols,
                padding: "8px 18px",
                alignItems: "center",
                borderBottom: "1px solid #F6F4EE",
              }}
            >
              <span style={{ fontSize: "12px", color: "#6F7566", paddingLeft: "2px" }}>
                Ortak giderler
              </span>
              <span />
              <span
                style={{
                  font: "500 13px 'IBM Plex Mono',monospace",
                  textAlign: "right",
                  color: "#C13A2E",
                }}
              >
                {h.ortak}
              </span>
              <span
                style={{
                  font: "500 12px 'IBM Plex Mono',monospace",
                  textAlign: "right",
                  color: "#B0B5A4",
                }}
              >
                —
              </span>
              <span
                style={{
                  font: "500 12px 'IBM Plex Mono',monospace",
                  textAlign: "right",
                  paddingRight: "14px",
                  color: "#B0B5A4",
                }}
              >
                —
              </span>
            </div>
            <div
              style={{
                display: "grid",
                gridTemplateColumns: gridCols,
                padding: "11px 18px",
                alignItems: "center",
                background: "#FBFAF6",
              }}
            >
              <span style={{ fontSize: "12.5px", fontWeight: 700 }}>Toplam</span>
              <span
                style={{ font: "700 13px 'IBM Plex Mono',monospace", textAlign: "right" }}
              >
                {h.tGelen}
              </span>
              <span
                style={{
                  font: "700 13px 'IBM Plex Mono',monospace",
                  textAlign: "right",
                  color: "#6F7566",
                }}
              >
                {h.tGiden}
              </span>
              <span
                style={{
                  font: "700 13px 'IBM Plex Mono',monospace",
                  textAlign: "right",
                  color: h.tSonucC,
                }}
              >
                {h.tSonuc}
              </span>
              <span
                style={{
                  font: "700 13.5px 'IBM Plex Mono',monospace",
                  textAlign: "right",
                  padding: "6px 14px 6px 0",
                  background: "#F0F5F1",
                  borderRadius: "7px",
                  color: "#1E5F46",
                }}
              >
                {h.tDevir}
              </span>
            </div>
          </div>
        ))}
      </div>
    </div>
  );
}
