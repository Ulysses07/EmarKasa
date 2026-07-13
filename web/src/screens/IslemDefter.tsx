// 1b İşlem + Defter — masaüstü ekranı; "Kasa Defteri.dc.html" (satır 183–371) birebir port.
// Sidebar burada YOK; onu paylaşımlı shell sağlıyor. Sadece flex:1 içerik paneli.

import { useState } from "react";
import { islemler } from "../data/mock";

export default function IslemDefter() {
  const [tab, setTab] = useState<"defter" | "gelen">("defter");

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

  const gridCols = "92px 1.5fr 150px 140px 118px 1.5fr 76px";

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
        <div style={{ display: "flex", alignItems: "center", gap: 14 }}>
          <span style={{ fontSize: "16.5px", fontWeight: 700, letterSpacing: "-0.01em" }}>
            İşlem + Defter
          </span>
          <span
            style={{
              background: "#F1EFE8",
              borderRadius: 999,
              padding: "4px 11px",
              font: "500 11.5px 'IBM Plex Mono',monospace",
              color: "#6F7566",
            }}
          >
            Dönem: 6–12 Tem 2026
          </span>
        </div>
        <div style={{ display: "flex", alignItems: "center", gap: 10 }}>
          <button
            style={{
              border: "1px solid #DAD6C9",
              background: "#FFFFFF",
              borderRadius: 8,
              padding: "7px 13px",
              font: "600 12px 'IBM Plex Sans',sans-serif",
              color: "#4E5548",
              cursor: "pointer",
              display: "flex",
              alignItems: "center",
              gap: 7,
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

      {/* body */}
      <div style={{ padding: "20px 28px 26px", display: "flex", flexDirection: "column", gap: 14 }}>
        {/* tab bar */}
        <div
          style={{
            display: "flex",
            gap: 2,
            background: "#EBE9E1",
            borderRadius: 10,
            padding: 3,
            width: "fit-content",
          }}
        >
          <button
            onClick={() => setTab("defter")}
            style={{
              border: "none",
              cursor: "pointer",
              borderRadius: 8,
              padding: "7px 16px",
              font: "600 12.5px 'IBM Plex Sans',sans-serif",
              ...(tab === "defter" ? segOn : segOff),
            }}
          >
            İşlem Defteri
          </button>
          <button
            onClick={() => setTab("gelen")}
            style={{
              border: "none",
              cursor: "pointer",
              borderRadius: 8,
              padding: "7px 16px",
              font: "600 12.5px 'IBM Plex Sans',sans-serif",
              ...(tab === "gelen" ? segOn : segOff),
            }}
          >
            Gelen Girişi
          </button>
        </div>

        {tab === "defter" && (
          <div style={{ display: "flex", flexDirection: "column", gap: 14 }}>
            {/* Hızlı İşlem Girişi kartı */}
            <div
              style={{
                background: "#FFFFFF",
                border: "1px solid #E3E0D6",
                borderRadius: 12,
                padding: "16px 20px 18px",
              }}
            >
              <div
                style={{
                  display: "flex",
                  alignItems: "baseline",
                  justifyContent: "space-between",
                  marginBottom: 12,
                }}
              >
                <span style={{ fontSize: "13.5px", fontWeight: 700 }}>Hızlı İşlem Girişi</span>
                <span style={{ fontSize: "11.5px", color: "#9AA08F" }}>
                  Giden işlemler · Enter ile ekle
                </span>
              </div>
              <div
                style={{
                  display: "grid",
                  gridTemplateColumns: "128px 1.5fr 148px 148px 148px 1.2fr 96px",
                  gap: 10,
                  alignItems: "end",
                }}
              >
                <div style={{ display: "flex", flexDirection: "column", gap: 5 }}>
                  <label style={{ fontSize: 11, fontWeight: 600, color: "#6F7566" }}>Tarih</label>
                  <input
                    value="13.07.2026"
                    readOnly
                    style={{
                      height: 38,
                      border: "1px solid #DAD6C9",
                      borderRadius: 8,
                      background: "#FDFCFA",
                      padding: "0 11px",
                      font: "500 12.5px 'IBM Plex Mono',monospace",
                      color: "#20261F",
                      outline: "none",
                      minWidth: 0,
                    }}
                  />
                </div>
                <div style={{ display: "flex", flexDirection: "column", gap: 5, position: "relative" }}>
                  <label style={{ fontSize: 11, fontWeight: 600, color: "#6F7566" }}>Cari</label>
                  <input
                    value="Yıl"
                    readOnly
                    style={{
                      height: 38,
                      border: "1.5px solid #1E5F46",
                      borderRadius: 8,
                      background: "#FFFFFF",
                      padding: "0 11px",
                      font: "500 13px 'IBM Plex Sans',sans-serif",
                      color: "#20261F",
                      outline: "none",
                      minWidth: 0,
                      boxShadow: "0 0 0 3px rgba(30,95,70,0.1)",
                    }}
                  />
                  <div
                    style={{
                      position: "absolute",
                      top: "100%",
                      left: 0,
                      width: "max-content",
                      minWidth: "100%",
                      marginTop: 5,
                      background: "#FFFFFF",
                      border: "1px solid #E3E0D6",
                      borderRadius: 10,
                      boxShadow: "0 10px 26px rgba(32,38,31,0.14)",
                      zIndex: 6,
                      overflow: "hidden",
                    }}
                  >
                    <div
                      style={{
                        padding: "9px 12px",
                        background: "#F1EFE8",
                        display: "flex",
                        alignItems: "center",
                        justifyContent: "space-between",
                        gap: 10,
                        cursor: "pointer",
                      }}
                    >
                      <span style={{ fontSize: "12.5px", fontWeight: 600, whiteSpace: "nowrap" }}>
                        <span style={{ background: "#EFE8C8" }}>Yıl</span>dız Tarım Ürünleri
                      </span>
                      <span style={{ fontSize: "10.5px", color: "#9AA08F", whiteSpace: "nowrap" }}>
                        38 işlem · MEZAT
                      </span>
                    </div>
                    <div
                      style={{
                        padding: "9px 12px",
                        display: "flex",
                        alignItems: "center",
                        justifyContent: "space-between",
                        gap: 10,
                        cursor: "pointer",
                      }}
                    >
                      <span style={{ fontSize: "12.5px", whiteSpace: "nowrap" }}>
                        <span style={{ background: "#EFE8C8" }}>Yıl</span>maz Balıkçılık
                      </span>
                      <span style={{ fontSize: "10.5px", color: "#9AA08F", whiteSpace: "nowrap" }}>
                        9 işlem · MEZAT
                      </span>
                    </div>
                    <div
                      style={{
                        padding: "9px 12px",
                        display: "flex",
                        alignItems: "center",
                        justifyContent: "space-between",
                        gap: 10,
                        cursor: "pointer",
                      }}
                    >
                      <span style={{ fontSize: "12.5px", whiteSpace: "nowrap" }}>
                        <span style={{ background: "#EFE8C8" }}>Yıl</span>dırım Nakliyat
                      </span>
                      <span style={{ fontSize: "10.5px", color: "#9AA08F", whiteSpace: "nowrap" }}>
                        4 işlem · Ortak
                      </span>
                    </div>
                    <div
                      style={{
                        padding: "8px 12px",
                        borderTop: "1px dashed #E3E0D6",
                        fontSize: 12,
                        fontWeight: 600,
                        color: "#1E5F46",
                        cursor: "pointer",
                      }}
                    >
                      + Yeni cari oluştur: “Yıl”
                    </div>
                  </div>
                </div>
                <div style={{ display: "flex", flexDirection: "column", gap: 5 }}>
                  <label style={{ fontSize: 11, fontWeight: 600, color: "#6F7566" }}>Tutar (₺)</label>
                  <input
                    placeholder="0,00"
                    style={{
                      height: 38,
                      border: "1px solid #DAD6C9",
                      borderRadius: 8,
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
                <div style={{ display: "flex", flexDirection: "column", gap: 5 }}>
                  <label style={{ fontSize: 11, fontWeight: 600, color: "#6F7566" }}>Kanal</label>
                  <div
                    style={{
                      height: 38,
                      border: "1px solid #DAD6C9",
                      borderRadius: 8,
                      background: "#FDFCFA",
                      padding: "0 11px",
                      display: "flex",
                      alignItems: "center",
                      justifyContent: "space-between",
                      fontSize: "12.5px",
                      fontWeight: 600,
                      color: "#A16A0B",
                      cursor: "pointer",
                    }}
                  >
                    <span style={{ display: "flex", alignItems: "center", gap: 7 }}>
                      <span
                        style={{ width: 7, height: 7, borderRadius: 99, background: "#C98A12" }}
                      />
                      MEZAT
                    </span>
                    <span style={{ color: "#9AA08F", fontSize: 10 }}>▾</span>
                  </div>
                </div>
                <div style={{ display: "flex", flexDirection: "column", gap: 5 }}>
                  <label style={{ fontSize: 11, fontWeight: 600, color: "#6F7566" }}>Tip</label>
                  <div
                    style={{
                      height: 38,
                      border: "1px solid #DAD6C9",
                      borderRadius: 8,
                      background: "#FDFCFA",
                      padding: "0 11px",
                      display: "flex",
                      alignItems: "center",
                      justifyContent: "space-between",
                      fontSize: "12.5px",
                      color: "#20261F",
                      cursor: "pointer",
                    }}
                  >
                    <span>Cari</span>
                    <span style={{ color: "#9AA08F", fontSize: 10 }}>▾</span>
                  </div>
                </div>
                <div style={{ display: "flex", flexDirection: "column", gap: 5 }}>
                  <label style={{ fontSize: 11, fontWeight: 600, color: "#6F7566" }}>Not</label>
                  <input
                    placeholder="opsiyonel"
                    style={{
                      height: 38,
                      border: "1px solid #DAD6C9",
                      borderRadius: 8,
                      background: "#FDFCFA",
                      padding: "0 11px",
                      fontSize: "12.5px",
                      color: "#20261F",
                      outline: "none",
                      minWidth: 0,
                    }}
                  />
                </div>
                <button
                  style={{
                    height: 38,
                    border: "none",
                    borderRadius: 8,
                    background: "#1E5F46",
                    color: "#FFFFFF",
                    font: "600 13px 'IBM Plex Sans',sans-serif",
                    cursor: "pointer",
                  }}
                >
                  Ekle
                </button>
              </div>
            </div>

            {/* Defter tablosu */}
            <div
              style={{
                background: "#FFFFFF",
                border: "1px solid #E3E0D6",
                borderRadius: 12,
                overflow: "visible",
              }}
            >
              <div
                style={{
                  padding: "12px 16px",
                  borderBottom: "1px solid #EDEAE0",
                  display: "flex",
                  alignItems: "center",
                  gap: 10,
                }}
              >
                <div
                  style={{
                    display: "flex",
                    alignItems: "center",
                    gap: 8,
                    border: "1px solid #DAD6C9",
                    borderRadius: 8,
                    background: "#FDFCFA",
                    padding: "0 11px",
                    height: 34,
                    width: 230,
                  }}
                >
                  <svg width="13" height="13" viewBox="0 0 16 16" fill="none">
                    <circle cx="7" cy="7" r="4.5" stroke="#9AA08F" strokeWidth="1.6" />
                    <path d="m10.5 10.5 3 3" stroke="#9AA08F" strokeWidth="1.6" strokeLinecap="round" />
                  </svg>
                  <input
                    placeholder="Cari ara…"
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
                <div
                  style={{
                    height: 34,
                    border: "1px solid #DAD6C9",
                    borderRadius: 8,
                    background: "#FDFCFA",
                    padding: "0 11px",
                    display: "flex",
                    alignItems: "center",
                    gap: 8,
                    fontSize: 12,
                    fontWeight: 600,
                    color: "#4E5548",
                    cursor: "pointer",
                  }}
                >
                  Kanal: Tümü <span style={{ color: "#9AA08F", fontSize: 9 }}>▾</span>
                </div>
                <div
                  style={{
                    height: 34,
                    border: "1px solid #DAD6C9",
                    borderRadius: 8,
                    background: "#FDFCFA",
                    padding: "0 11px",
                    display: "flex",
                    alignItems: "center",
                    gap: 8,
                    fontSize: 12,
                    fontWeight: 600,
                    color: "#4E5548",
                    cursor: "pointer",
                  }}
                >
                  Tip: Tümü <span style={{ color: "#9AA08F", fontSize: 9 }}>▾</span>
                </div>
                <div
                  style={{
                    height: 34,
                    border: "1px solid #DAD6C9",
                    borderRadius: 8,
                    background: "#FDFCFA",
                    padding: "0 11px",
                    display: "flex",
                    alignItems: "center",
                    gap: 8,
                    fontSize: 12,
                    fontWeight: 600,
                    color: "#4E5548",
                    cursor: "pointer",
                  }}
                >
                  <svg width="12" height="12" viewBox="0 0 16 16" fill="none">
                    <rect x="2.5" y="3" width="11" height="10.5" rx="2" stroke="#9AA08F" strokeWidth="1.6" />
                    <path d="M2.5 6.5h11" stroke="#9AA08F" strokeWidth="1.6" />
                  </svg>
                  6–12 Tem <span style={{ color: "#9AA08F", fontSize: 9 }}>▾</span>
                </div>
                <a href="#1b" style={{ fontSize: 12, fontWeight: 600, textDecoration: "none" }}>
                  Temizle
                </a>
                <div style={{ flex: 1 }} />
                <span style={{ fontSize: 12, color: "#9AA08F" }}>24 işlem · 634.600,00 ₺ giden</span>
              </div>

              {/* başlık satırı */}
              <div
                style={{
                  display: "grid",
                  gridTemplateColumns: gridCols,
                  padding: "9px 16px",
                  borderBottom: "1px solid #EDEAE0",
                  fontSize: "10.5px",
                  fontWeight: 700,
                  letterSpacing: "0.07em",
                  color: "#9AA08F",
                }}
              >
                <span>TARİH</span>
                <span>CARİ</span>
                <span style={{ textAlign: "right" }}>TUTAR (₺)</span>
                <span style={{ paddingLeft: 16 }}>KANAL</span>
                <span>TİP</span>
                <span>NOT</span>
                <span />
              </div>

              {/* veri satırları */}
              {islemler.map((i, idx) => (
                <div
                  key={idx}
                  style={{
                    display: "grid",
                    gridTemplateColumns: gridCols,
                    padding: "10px 16px",
                    borderBottom: "1px solid #F1EFE8",
                    alignItems: "center",
                  }}
                >
                  <span
                    style={{ font: "500 12px 'IBM Plex Mono',monospace", color: "#6F7566" }}
                  >
                    {i.tarih}
                  </span>
                  <span style={{ fontSize: 13, fontWeight: 600 }}>{i.cari}</span>
                  <span
                    style={{ font: "600 13px 'IBM Plex Mono',monospace", textAlign: "right" }}
                  >
                    {i.tutar}
                  </span>
                  <span style={{ paddingLeft: 16 }}>
                    <span
                      style={{
                        display: "inline-flex",
                        alignItems: "center",
                        gap: 6,
                        borderRadius: 999,
                        padding: "3px 9px",
                        fontSize: "10.5px",
                        fontWeight: 700,
                        background: i.kbg,
                        color: i.kc,
                      }}
                    >
                      <span
                        style={{ width: 6, height: 6, borderRadius: 99, background: i.kdot }}
                      />
                      {i.kanal}
                    </span>
                  </span>
                  <span style={{ fontSize: 12, color: "#6F7566" }}>{i.tip}</span>
                  <span
                    style={{
                      fontSize: 12,
                      color: "#9AA08F",
                      overflow: "hidden",
                      textOverflow: "ellipsis",
                      whiteSpace: "nowrap",
                    }}
                  >
                    {i.not}
                  </span>
                  <span style={{ display: "flex", gap: 4, justifyContent: "flex-end" }}>
                    <button
                      title="Düzenle"
                      style={{
                        border: "none",
                        background: "transparent",
                        cursor: "pointer",
                        padding: 5,
                        borderRadius: 6,
                        color: "#9AA08F",
                      }}
                    >
                      <svg width="13" height="13" viewBox="0 0 16 16" fill="none">
                        <path
                          d="m9.8 3.2 3 3L6 13H3v-3l6.8-6.8ZM8.6 4.4l3 3"
                          stroke="currentColor"
                          strokeWidth="1.5"
                          strokeLinejoin="round"
                        />
                      </svg>
                    </button>
                    <button
                      title="Sil"
                      style={{
                        border: "none",
                        background: "transparent",
                        cursor: "pointer",
                        padding: 5,
                        borderRadius: 6,
                        color: "#9AA08F",
                      }}
                    >
                      <svg width="13" height="13" viewBox="0 0 16 16" fill="none">
                        <path
                          d="M3 4.5h10M6.5 4.5V3h3v1.5M4.5 4.5 5 13.5h6l.5-9"
                          stroke="currentColor"
                          strokeWidth="1.5"
                          strokeLinecap="round"
                          strokeLinejoin="round"
                        />
                      </svg>
                    </button>
                  </span>
                </div>
              ))}

              <div
                style={{
                  padding: "11px 16px",
                  display: "flex",
                  alignItems: "center",
                  justifyContent: "space-between",
                }}
              >
                <span style={{ fontSize: 12, color: "#9AA08F" }}>
                  24 işlemden 11’i gösteriliyor
                </span>
                <div style={{ display: "flex", gap: 5, alignItems: "center" }}>
                  <button
                    style={{
                      width: 28,
                      height: 28,
                      border: "1px solid #DAD6C9",
                      borderRadius: 7,
                      background: "#FFFFFF",
                      cursor: "pointer",
                      color: "#9AA08F",
                    }}
                  >
                    ‹
                  </button>
                  <span
                    style={{
                      font: "500 11.5px 'IBM Plex Mono',monospace",
                      color: "#6F7566",
                      padding: "0 5px",
                    }}
                  >
                    1 / 3
                  </span>
                  <button
                    style={{
                      width: 28,
                      height: 28,
                      border: "1px solid #DAD6C9",
                      borderRadius: 7,
                      background: "#FFFFFF",
                      cursor: "pointer",
                      color: "#4E5548",
                    }}
                  >
                    ›
                  </button>
                </div>
              </div>
            </div>
          </div>
        )}

        {tab === "gelen" && (
          <div
            style={{
              background: "#FFFFFF",
              border: "1px solid #E3E0D6",
              borderRadius: 12,
              padding: "18px 20px",
              maxWidth: 880,
            }}
          >
            <div
              style={{
                display: "flex",
                alignItems: "center",
                justifyContent: "space-between",
                marginBottom: 4,
              }}
            >
              <span style={{ fontSize: "13.5px", fontWeight: 700 }}>Haftalık Gelen Girişi</span>
              <div
                style={{
                  height: 36,
                  border: "1px solid #DAD6C9",
                  borderRadius: 8,
                  background: "#FDFCFA",
                  padding: "0 12px",
                  display: "flex",
                  alignItems: "center",
                  gap: 9,
                  font: "600 12.5px 'IBM Plex Mono',monospace",
                  color: "#20261F",
                  cursor: "pointer",
                }}
              >
                Dönem: 6–12 Tem 2026 <span style={{ color: "#9AA08F", fontSize: 10 }}>▾</span>
              </div>
            </div>
            <p style={{ margin: "0 0 16px", fontSize: 12, color: "#9AA08F", lineHeight: 1.5 }}>
              Kanal başına haftalık tek rakam. Ay sonunu bölen haftalarda iki dönem satırı oluşur
              (ör. 29–30 Haz · 1–5 Tem).
            </p>
            <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr 1fr", gap: 12 }}>
              <div
                style={{
                  border: "1px solid #EDEAE0",
                  borderRadius: 10,
                  padding: "13px 14px",
                  display: "flex",
                  flexDirection: "column",
                  gap: 9,
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
                  <span style={{ width: 6, height: 6, borderRadius: 99, background: "#C98A12" }} />
                  MEZAT
                </span>
                <input
                  value="517.300,00"
                  readOnly
                  style={{
                    height: 40,
                    border: "1px solid #DAD6C9",
                    borderRadius: 8,
                    background: "#FDFCFA",
                    padding: "0 12px",
                    font: "600 14px 'IBM Plex Mono',monospace",
                    color: "#20261F",
                    outline: "none",
                    textAlign: "right",
                    minWidth: 0,
                  }}
                />
                <span style={{ fontSize: "10.5px", color: "#9AA08F", textAlign: "right" }}>
                  geçen hafta 402.600,00
                </span>
              </div>
              <div
                style={{
                  border: "1px solid #EDEAE0",
                  borderRadius: 10,
                  padding: "13px 14px",
                  display: "flex",
                  flexDirection: "column",
                  gap: 9,
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
                  <span style={{ width: 6, height: 6, borderRadius: 99, background: "#3572C1" }} />
                  PERAKENDE
                </span>
                <input
                  value="168.200,00"
                  readOnly
                  style={{
                    height: 40,
                    border: "1px solid #DAD6C9",
                    borderRadius: 8,
                    background: "#FDFCFA",
                    padding: "0 12px",
                    font: "600 14px 'IBM Plex Mono',monospace",
                    color: "#20261F",
                    outline: "none",
                    textAlign: "right",
                    minWidth: 0,
                  }}
                />
                <span style={{ fontSize: "10.5px", color: "#9AA08F", textAlign: "right" }}>
                  geçen hafta 143.800,00
                </span>
              </div>
              <div
                style={{
                  border: "1px solid #EDEAE0",
                  borderRadius: 10,
                  padding: "13px 14px",
                  display: "flex",
                  flexDirection: "column",
                  gap: 9,
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
                  <span style={{ width: 6, height: 6, borderRadius: 99, background: "#7E5BBF" }} />
                  TOPTAN
                </span>
                <input
                  value="94.100,00"
                  readOnly
                  style={{
                    height: 40,
                    border: "1px solid #DAD6C9",
                    borderRadius: 8,
                    background: "#FDFCFA",
                    padding: "0 12px",
                    font: "600 14px 'IBM Plex Mono',monospace",
                    color: "#20261F",
                    outline: "none",
                    textAlign: "right",
                    minWidth: 0,
                  }}
                />
                <span style={{ fontSize: "10.5px", color: "#9AA08F", textAlign: "right" }}>
                  geçen hafta 76.400,00
                </span>
              </div>
            </div>
            <div
              style={{
                display: "flex",
                alignItems: "center",
                justifyContent: "space-between",
                marginTop: 16,
                borderTop: "1px dashed #E3E0D6",
                paddingTop: 14,
              }}
            >
              <span style={{ fontSize: "12.5px", color: "#6F7566" }}>
                Toplam Gelen:{" "}
                <span style={{ font: "700 14px 'IBM Plex Mono',monospace", color: "#20261F" }}>
                  779.600,00 ₺
                </span>
              </span>
              <button
                style={{
                  height: 38,
                  border: "none",
                  borderRadius: 8,
                  background: "#1E5F46",
                  color: "#FFFFFF",
                  font: "600 13px 'IBM Plex Sans',sans-serif",
                  padding: "0 22px",
                  cursor: "pointer",
                }}
              >
                Kaydet
              </button>
            </div>
          </div>
        )}
      </div>
    </div>
  );
}
