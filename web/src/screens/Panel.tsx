import { useState } from "react";
import { usePanelSeri } from "../data/hooks";
import { color } from "../theme";

const segOn = {
  background: "#FFFFFF",
  color: "#20261F",
  boxShadow: "0 1px 3px rgba(32,38,31,0.14)",
} as const;
const segOff = {
  background: "transparent",
  color: "#6F7566",
  boxShadow: "none",
} as const;

export default function Panel() {
  const [trend, setTrend] = useState<"kasa" | "kanallar">("kasa");
  const trendKasa = trend === "kasa";
  const { veri, yukleniyor, hata } = usePanelSeri();

  if (yukleniyor) return <div style={{ padding: 24, color: color.sub }}>Yükleniyor…</div>;
  if (hata || !veri) return <div style={{ padding: 24, color: color.neg }}>Veri alınamadı.</div>;
  const { kasaPath, kasaArea, kasaDotY, mzPath, prPath, tpPath, mzDotY, prDotY, tpDotY } = veri;

  return (
    <div
      style={{
        minHeight: "100vh",
        background: color.pageDark,
        display: "flex",
        justifyContent: "center",
        alignItems: "flex-start",
        padding: 24,
      }}
    >
      <div
        style={{
          width: 390,
          height: 820,
          background: "#F5F4EF",
          borderRadius: 28,
          overflow: "hidden",
          boxShadow: "0 8px 40px rgba(0,0,0,.15)",
        }}
      >
        <div
          style={{
            height: "100%",
            boxSizing: "border-box",
            background: "#F5F4EF",
            display: "flex",
            flexDirection: "column",
            fontFamily: "'IBM Plex Sans',sans-serif",
            color: "#20261F",
            overflow: "hidden",
            paddingTop: 16,
          }}
        >
          <div
            style={{
              display: "flex",
              alignItems: "center",
              justifyContent: "space-between",
              padding: "6px 16px 10px",
            }}
          >
            <div style={{ display: "flex", alignItems: "center", gap: 9 }}>
              <div
                style={{
                  width: 26,
                  height: 26,
                  borderRadius: 8,
                  background: "#1F2A23",
                  color: "#EAF2E7",
                  display: "flex",
                  alignItems: "center",
                  justifyContent: "center",
                  font: "600 14px 'IBM Plex Mono',monospace",
                }}
              >
                ₺
              </div>
              <div
                style={{
                  fontSize: 15,
                  fontWeight: 700,
                  letterSpacing: "-0.01em",
                }}
              >
                Kasa Defteri
              </div>
            </div>
            <div
              style={{
                display: "flex",
                alignItems: "center",
                gap: 6,
                background: "#E9ECEF",
                color: "#5C6470",
                borderRadius: 999,
                padding: "4px 10px",
                fontSize: 11,
                fontWeight: 600,
              }}
            >
              <svg width="12" height="12" viewBox="0 0 16 16" fill="none">
                <path
                  d="M1.5 8s2.4-4.2 6.5-4.2S14.5 8 14.5 8s-2.4 4.2-6.5 4.2S1.5 8 1.5 8Z"
                  stroke="currentColor"
                  strokeWidth="1.5"
                />
                <circle
                  cx="8"
                  cy="8"
                  r="1.8"
                  stroke="currentColor"
                  strokeWidth="1.5"
                />
              </svg>
              İzleyici
            </div>
          </div>
          <div
            style={{
              flex: 1,
              overflowY: "auto",
              scrollbarWidth: "none",
              padding: "2px 14px 44px",
              display: "flex",
              flexDirection: "column",
              gap: 10,
            }}
          >
            <div
              style={{
                background: "#1F2A23",
                borderRadius: 16,
                padding: "18px 18px 16px",
                color: "#EAF2E7",
              }}
            >
              <div
                style={{
                  fontSize: 10.5,
                  fontWeight: 600,
                  letterSpacing: "0.12em",
                  color: "#9FB2A2",
                }}
              >
                GÜNCEL KASA
              </div>
              <div
                style={{
                  font: "600 31px 'IBM Plex Mono',monospace",
                  letterSpacing: "-0.02em",
                  marginTop: 6,
                }}
              >
                1.308.800,00{" "}
                <span style={{ fontSize: 19, color: "#9FB2A2" }}>₺</span>
              </div>
              <div
                style={{
                  display: "flex",
                  alignItems: "center",
                  gap: 8,
                  marginTop: 10,
                }}
              >
                <span
                  style={{
                    background: "#2C4536",
                    color: "#8FD6AC",
                    borderRadius: 999,
                    padding: "3px 9px",
                    font: "600 11.5px 'IBM Plex Mono',monospace",
                  }}
                >
                  +145.000,00
                </span>
                <span style={{ fontSize: 11.5, color: "#9FB2A2" }}>
                  bu hafta · 6–12 Tem devri
                </span>
              </div>
            </div>

            <div
              style={{ display: "flex", flexDirection: "column", gap: 8 }}
            >
              <div
                style={{
                  background: "#FFFFFF",
                  border: "1px solid #E3E0D6",
                  borderRadius: 14,
                  padding: "12px 14px",
                  display: "flex",
                  alignItems: "center",
                  justifyContent: "space-between",
                }}
              >
                <div
                  style={{
                    display: "flex",
                    flexDirection: "column",
                    gap: 5,
                  }}
                >
                  <span
                    style={{
                      display: "inline-flex",
                      alignItems: "center",
                      gap: 6,
                      background: "#F5EAD2",
                      color: "#A16A0B",
                      borderRadius: 999,
                      padding: "3px 9px",
                      fontSize: 11,
                      fontWeight: 700,
                      width: "fit-content",
                    }}
                  >
                    <span
                      style={{
                        width: 6,
                        height: 6,
                        borderRadius: 99,
                        background: "#C98A12",
                      }}
                    />
                    MEZAT
                  </span>
                  <span style={{ fontSize: 10.5, color: "#9AA08F" }}>
                    kümülatif devir
                  </span>
                </div>
                <div
                  style={{
                    display: "flex",
                    flexDirection: "column",
                    alignItems: "flex-end",
                    gap: 3,
                  }}
                >
                  <span style={{ font: "600 17px 'IBM Plex Mono',monospace" }}>
                    1.000.300,00
                  </span>
                  <span
                    style={{
                      font: "500 11.5px 'IBM Plex Mono',monospace",
                      color: "#1B7A4E",
                    }}
                  >
                    +104.700,00 bu hafta
                  </span>
                </div>
              </div>
              <div
                style={{
                  background: "#FFFFFF",
                  border: "1px solid #E3E0D6",
                  borderRadius: 14,
                  padding: "12px 14px",
                  display: "flex",
                  alignItems: "center",
                  justifyContent: "space-between",
                }}
              >
                <div
                  style={{
                    display: "flex",
                    flexDirection: "column",
                    gap: 5,
                  }}
                >
                  <span
                    style={{
                      display: "inline-flex",
                      alignItems: "center",
                      gap: 6,
                      background: "#E2ECF8",
                      color: "#245EA8",
                      borderRadius: 999,
                      padding: "3px 9px",
                      fontSize: 11,
                      fontWeight: 700,
                      width: "fit-content",
                    }}
                  >
                    <span
                      style={{
                        width: 6,
                        height: 6,
                        borderRadius: 99,
                        background: "#3572C1",
                      }}
                    />
                    PERAKENDE
                  </span>
                  <span style={{ fontSize: 10.5, color: "#9AA08F" }}>
                    kümülatif devir
                  </span>
                </div>
                <div
                  style={{
                    display: "flex",
                    flexDirection: "column",
                    alignItems: "flex-end",
                    gap: 3,
                  }}
                >
                  <span style={{ font: "600 17px 'IBM Plex Mono',monospace" }}>
                    393.000,00
                  </span>
                  <span
                    style={{
                      font: "500 11.5px 'IBM Plex Mono',monospace",
                      color: "#1B7A4E",
                    }}
                  >
                    +48.800,00 bu hafta
                  </span>
                </div>
              </div>
              <div
                style={{
                  background: "#FFFFFF",
                  border: "1px solid #E3E0D6",
                  borderRadius: 14,
                  padding: "12px 14px",
                  display: "flex",
                  alignItems: "center",
                  justifyContent: "space-between",
                }}
              >
                <div
                  style={{
                    display: "flex",
                    flexDirection: "column",
                    gap: 5,
                  }}
                >
                  <span
                    style={{
                      display: "inline-flex",
                      alignItems: "center",
                      gap: 6,
                      background: "#ECE6F7",
                      color: "#6D4FA1",
                      borderRadius: 999,
                      padding: "3px 9px",
                      fontSize: 11,
                      fontWeight: 700,
                      width: "fit-content",
                    }}
                  >
                    <span
                      style={{
                        width: 6,
                        height: 6,
                        borderRadius: 99,
                        background: "#7E5BBF",
                      }}
                    />
                    TOPTAN
                  </span>
                  <span style={{ fontSize: 10.5, color: "#9AA08F" }}>
                    kümülatif devir
                  </span>
                </div>
                <div
                  style={{
                    display: "flex",
                    flexDirection: "column",
                    alignItems: "flex-end",
                    gap: 3,
                  }}
                >
                  <span style={{ font: "600 17px 'IBM Plex Mono',monospace" }}>
                    112.700,00
                  </span>
                  <span
                    style={{
                      font: "500 11.5px 'IBM Plex Mono',monospace",
                      color: "#1B7A4E",
                    }}
                  >
                    +16.800,00 bu hafta
                  </span>
                </div>
              </div>
            </div>

            <div
              style={{
                background: "#FFFFFF",
                border: "1px solid #E3E0D6",
                borderRadius: 14,
                padding: 14,
              }}
            >
              <div
                style={{
                  display: "flex",
                  alignItems: "baseline",
                  justifyContent: "space-between",
                  marginBottom: 10,
                }}
              >
                <span style={{ fontSize: 13, fontWeight: 700 }}>Bu Ay</span>
                <span style={{ fontSize: 11, color: "#9AA08F" }}>
                  Temmuz · 1–12
                </span>
              </div>
              <div
                style={{ display: "flex", flexDirection: "column", gap: 7 }}
              >
                <div
                  style={{
                    display: "flex",
                    alignItems: "center",
                    justifyContent: "space-between",
                  }}
                >
                  <span
                    style={{
                      display: "flex",
                      alignItems: "center",
                      gap: 7,
                      fontSize: 12.5,
                      fontWeight: 600,
                      color: "#A16A0B",
                    }}
                  >
                    <span
                      style={{
                        width: 7,
                        height: 7,
                        borderRadius: 99,
                        background: "#C98A12",
                      }}
                    />
                    MEZAT
                  </span>
                  <span
                    style={{
                      font: "600 13px 'IBM Plex Mono',monospace",
                      color: "#1B7A4E",
                    }}
                  >
                    +155.400,00
                  </span>
                </div>
                <div
                  style={{
                    display: "flex",
                    alignItems: "center",
                    justifyContent: "space-between",
                  }}
                >
                  <span
                    style={{
                      display: "flex",
                      alignItems: "center",
                      gap: 7,
                      fontSize: 12.5,
                      fontWeight: 600,
                      color: "#245EA8",
                    }}
                  >
                    <span
                      style={{
                        width: 7,
                        height: 7,
                        borderRadius: 99,
                        background: "#3572C1",
                      }}
                    />
                    PERAKENDE
                  </span>
                  <span
                    style={{
                      font: "600 13px 'IBM Plex Mono',monospace",
                      color: "#1B7A4E",
                    }}
                  >
                    +88.400,00
                  </span>
                </div>
                <div
                  style={{
                    display: "flex",
                    alignItems: "center",
                    justifyContent: "space-between",
                  }}
                >
                  <span
                    style={{
                      display: "flex",
                      alignItems: "center",
                      gap: 7,
                      fontSize: 12.5,
                      fontWeight: 600,
                      color: "#6D4FA1",
                    }}
                  >
                    <span
                      style={{
                        width: 7,
                        height: 7,
                        borderRadius: 99,
                        background: "#7E5BBF",
                      }}
                    />
                    TOPTAN
                  </span>
                  <span
                    style={{
                      font: "600 13px 'IBM Plex Mono',monospace",
                      color: "#1B7A4E",
                    }}
                  >
                    +23.400,00
                  </span>
                </div>
                <div
                  style={{
                    display: "flex",
                    alignItems: "center",
                    justifyContent: "space-between",
                    borderTop: "1px dashed #E3E0D6",
                    paddingTop: 7,
                  }}
                >
                  <span style={{ fontSize: 12, color: "#6F7566" }}>
                    Ortak giderler
                  </span>
                  <span
                    style={{
                      font: "500 13px 'IBM Plex Mono',monospace",
                      color: "#C13A2E",
                    }}
                  >
                    -48.200,00
                  </span>
                </div>
                <div
                  style={{
                    display: "flex",
                    alignItems: "center",
                    justifyContent: "space-between",
                  }}
                >
                  <span style={{ fontSize: 12.5, fontWeight: 700 }}>
                    Kasa (net)
                  </span>
                  <span
                    style={{
                      font: "700 13.5px 'IBM Plex Mono',monospace",
                      color: "#1B7A4E",
                    }}
                  >
                    +219.000,00
                  </span>
                </div>
              </div>
            </div>

            <div
              style={{
                background: "#FFFFFF",
                border: "1px solid #E3E0D6",
                borderRadius: 14,
                padding: "14px 14px 10px",
              }}
            >
              <div
                style={{
                  display: "flex",
                  alignItems: "center",
                  justifyContent: "space-between",
                  marginBottom: 10,
                }}
              >
                <span style={{ fontSize: 13, fontWeight: 700 }}>Trend</span>
                <div
                  style={{
                    display: "flex",
                    background: "#EFEDE6",
                    borderRadius: 9,
                    padding: 2,
                    gap: 2,
                  }}
                >
                  <button
                    onClick={() => setTrend("kasa")}
                    style={{
                      border: "none",
                      cursor: "pointer",
                      borderRadius: 7,
                      padding: "4px 11px",
                      font: "600 11.5px 'IBM Plex Sans',sans-serif",
                      ...(trendKasa ? segOn : segOff),
                    }}
                  >
                    Kasa
                  </button>
                  <button
                    onClick={() => setTrend("kanallar")}
                    style={{
                      border: "none",
                      cursor: "pointer",
                      borderRadius: 7,
                      padding: "4px 11px",
                      font: "600 11.5px 'IBM Plex Sans',sans-serif",
                      ...(!trendKasa ? segOn : segOff),
                    }}
                  >
                    Kanallar
                  </button>
                </div>
              </div>
              {trendKasa ? (
                <svg
                  width="100%"
                  viewBox="0 0 336 156"
                  style={{ display: "block" }}
                >
                  <line
                    x1="14"
                    y1="115.2"
                    x2="322"
                    y2="115.2"
                    stroke="#EDEAE0"
                    strokeWidth="1"
                  />
                  <line
                    x1="14"
                    y1="64.8"
                    x2="322"
                    y2="64.8"
                    stroke="#EDEAE0"
                    strokeWidth="1"
                  />
                  <line
                    x1="14"
                    y1="14.4"
                    x2="322"
                    y2="14.4"
                    stroke="#EDEAE0"
                    strokeWidth="1"
                  />
                  <text
                    x="322"
                    y="111.2"
                    textAnchor="end"
                    fontSize="9"
                    fill="#B0B5A4"
                    fontFamily="IBM Plex Mono"
                  >
                    0,7M
                  </text>
                  <text
                    x="322"
                    y="60.8"
                    textAnchor="end"
                    fontSize="9"
                    fill="#B0B5A4"
                    fontFamily="IBM Plex Mono"
                  >
                    1,0M
                  </text>
                  <text
                    x="322"
                    y="10.4"
                    textAnchor="end"
                    fontSize="9"
                    fill="#B0B5A4"
                    fontFamily="IBM Plex Mono"
                  >
                    1,3M
                  </text>
                  <path d={kasaArea} fill="#E7F0EA" opacity="0.8" />
                  <path
                    d={kasaPath}
                    fill="none"
                    stroke="#1E5F46"
                    strokeWidth="2"
                    strokeLinejoin="round"
                    strokeLinecap="round"
                  />
                  <circle
                    cx="322"
                    cy={kasaDotY}
                    r="3.5"
                    fill="#1E5F46"
                    stroke="#FFFFFF"
                    strokeWidth="1.5"
                  />
                </svg>
              ) : (
                <svg
                  width="100%"
                  viewBox="0 0 336 156"
                  style={{ display: "block" }}
                >
                  <line
                    x1="14"
                    y1="102"
                    x2="322"
                    y2="102"
                    stroke="#EDEAE0"
                    strokeWidth="1"
                  />
                  <line
                    x1="14"
                    y1="72"
                    x2="322"
                    y2="72"
                    stroke="#EDEAE0"
                    strokeWidth="1"
                  />
                  <line
                    x1="14"
                    y1="42"
                    x2="322"
                    y2="42"
                    stroke="#EDEAE0"
                    strokeWidth="1"
                  />
                  <line
                    x1="14"
                    y1="12"
                    x2="322"
                    y2="12"
                    stroke="#EDEAE0"
                    strokeWidth="1"
                  />
                  <text
                    x="322"
                    y="98"
                    textAnchor="end"
                    fontSize="9"
                    fill="#B0B5A4"
                    fontFamily="IBM Plex Mono"
                  >
                    250B
                  </text>
                  <text
                    x="322"
                    y="68"
                    textAnchor="end"
                    fontSize="9"
                    fill="#B0B5A4"
                    fontFamily="IBM Plex Mono"
                  >
                    500B
                  </text>
                  <text
                    x="322"
                    y="38"
                    textAnchor="end"
                    fontSize="9"
                    fill="#B0B5A4"
                    fontFamily="IBM Plex Mono"
                  >
                    750B
                  </text>
                  <text
                    x="322"
                    y="8"
                    textAnchor="end"
                    fontSize="9"
                    fill="#B0B5A4"
                    fontFamily="IBM Plex Mono"
                  >
                    1,0M
                  </text>
                  <path
                    d={mzPath}
                    fill="none"
                    stroke="#C98A12"
                    strokeWidth="2"
                    strokeLinejoin="round"
                    strokeLinecap="round"
                  />
                  <path
                    d={prPath}
                    fill="none"
                    stroke="#3572C1"
                    strokeWidth="2"
                    strokeLinejoin="round"
                    strokeLinecap="round"
                  />
                  <path
                    d={tpPath}
                    fill="none"
                    stroke="#7E5BBF"
                    strokeWidth="2"
                    strokeLinejoin="round"
                    strokeLinecap="round"
                  />
                  <circle
                    cx="322"
                    cy={mzDotY}
                    r="3"
                    fill="#C98A12"
                    stroke="#FFFFFF"
                    strokeWidth="1.5"
                  />
                  <circle
                    cx="322"
                    cy={prDotY}
                    r="3"
                    fill="#3572C1"
                    stroke="#FFFFFF"
                    strokeWidth="1.5"
                  />
                  <circle
                    cx="322"
                    cy={tpDotY}
                    r="3"
                    fill="#7E5BBF"
                    stroke="#FFFFFF"
                    strokeWidth="1.5"
                  />
                </svg>
              )}
              <div
                style={{
                  display: "flex",
                  justifyContent: "space-between",
                  padding: "2px 2px 4px",
                  font: "500 9.5px 'IBM Plex Mono',monospace",
                  color: "#B0B5A4",
                }}
              >
                <span>31 May</span>
                <span>21 Haz</span>
                <span>12 Tem</span>
              </div>
              {!trendKasa && (
                <div
                  style={{
                    display: "flex",
                    gap: 12,
                    padding: "4px 2px 6px",
                  }}
                >
                  <span
                    style={{
                      display: "flex",
                      alignItems: "center",
                      gap: 5,
                      fontSize: 10.5,
                      fontWeight: 600,
                      color: "#A16A0B",
                    }}
                  >
                    <span
                      style={{
                        width: 6,
                        height: 6,
                        borderRadius: 99,
                        background: "#C98A12",
                      }}
                    />
                    MEZAT
                  </span>
                  <span
                    style={{
                      display: "flex",
                      alignItems: "center",
                      gap: 5,
                      fontSize: 10.5,
                      fontWeight: 600,
                      color: "#245EA8",
                    }}
                  >
                    <span
                      style={{
                        width: 6,
                        height: 6,
                        borderRadius: 99,
                        background: "#3572C1",
                      }}
                    />
                    PERAKENDE
                  </span>
                  <span
                    style={{
                      display: "flex",
                      alignItems: "center",
                      gap: 5,
                      fontSize: 10.5,
                      fontWeight: 600,
                      color: "#6D4FA1",
                    }}
                  >
                    <span
                      style={{
                        width: 6,
                        height: 6,
                        borderRadius: 99,
                        background: "#7E5BBF",
                      }}
                    />
                    TOPTAN
                  </span>
                </div>
              )}
            </div>

            <div
              style={{
                textAlign: "center",
                fontSize: 10.5,
                color: "#9AA08F",
                padding: "2px 0 6px",
              }}
            >
              Son güncelleme: bugün 09:42 · salt görüntüleme
            </div>
          </div>
        </div>
      </div>
    </div>
  );
}
