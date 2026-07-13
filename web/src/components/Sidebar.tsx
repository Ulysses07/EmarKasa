// Masaüstü sol menü — 1b–1f ekranlarında ortak. Aktif sekme route'a göre.

import { NavLink, useNavigate } from "react-router-dom";
import type { ReactNode } from "react";
import { color } from "../theme";
import { useAuth } from "../api/AuthContext";
import { logout } from "../api/auth";

type NavItem = { to: string; label: string; icon: ReactNode };

const items: NavItem[] = [
  {
    to: "/",
    label: "İşlem + Defter",
    icon: (
      <svg width="16" height="16" viewBox="0 0 16 16" fill="none">
        <path d="M3 4.5h10M3 8h10M3 11.5h6" stroke="currentColor" strokeWidth="1.7" strokeLinecap="round" />
      </svg>
    ),
  },
  {
    to: "/haftalik",
    label: "Haftalık Özet",
    icon: (
      <svg width="16" height="16" viewBox="0 0 16 16" fill="none">
        <rect x="2.5" y="3" width="11" height="10.5" rx="2" stroke="currentColor" strokeWidth="1.6" />
        <path d="M2.5 6.5h11M5.5 2v2M10.5 2v2" stroke="currentColor" strokeWidth="1.6" strokeLinecap="round" />
      </svg>
    ),
  },
  {
    to: "/aylik",
    label: "Aylık Rapor",
    icon: (
      <svg width="16" height="16" viewBox="0 0 16 16" fill="none">
        <path d="M4 13V8M8 13V3.5M12 13V9.5" stroke="currentColor" strokeWidth="1.7" strokeLinecap="round" />
      </svg>
    ),
  },
  {
    to: "/cariler",
    label: "Cariler",
    icon: (
      <svg width="16" height="16" viewBox="0 0 16 16" fill="none">
        <circle cx="5.8" cy="5.8" r="2.3" stroke="currentColor" strokeWidth="1.6" />
        <path d="M2.2 13c.5-2.4 1.9-3.6 3.6-3.6S8.9 10.6 9.4 13" stroke="currentColor" strokeWidth="1.6" strokeLinecap="round" />
        <circle cx="11.4" cy="6.4" r="1.8" stroke="currentColor" strokeWidth="1.5" />
        <path d="M11.2 9.6c1.5.1 2.4 1.2 2.7 3" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" />
      </svg>
    ),
  },
  {
    to: "/ayarlar",
    label: "Ayarlar",
    icon: (
      <svg width="16" height="16" viewBox="0 0 16 16" fill="none">
        <path d="M3 5h10M3 11h10" stroke="currentColor" strokeWidth="1.6" strokeLinecap="round" />
        <circle cx="6.2" cy="5" r="1.7" fill={color.sidebar} stroke="currentColor" strokeWidth="1.6" />
        <circle cx="10" cy="11" r="1.7" fill={color.sidebar} stroke="currentColor" strokeWidth="1.6" />
      </svg>
    ),
  },
];

export default function Sidebar() {
  const navigate = useNavigate();
  const { ayarla } = useAuth();

  async function handleCikis() {
    try {
      await logout();
    } catch {
      // Ağ hatası olsa bile yerel oturumu temizle, kullanıcı takılıp kalmasın.
    }
    ayarla(null);
    navigate("/login");
  }

  return (
    <div
      style={{
        width: 220,
        flex: "none",
        background: color.sidebar,
        color: color.sidebarText,
        display: "flex",
        flexDirection: "column",
        padding: "16px 12px 14px",
        gap: 2,
      }}
    >
      <div style={{ display: "flex", alignItems: "center", gap: 10, padding: "4px 8px 16px" }}>
        <div
          style={{
            width: 28,
            height: 28,
            borderRadius: 9,
            background: color.sidebarActive,
            color: "#EAF2E7",
            display: "flex",
            alignItems: "center",
            justifyContent: "center",
            font: "600 15px 'IBM Plex Mono', monospace",
          }}
        >
          ₺
        </div>
        <div style={{ fontSize: 14.5, fontWeight: 700, color: color.sidebarTitle, letterSpacing: "-0.01em" }}>
          Kasa Defteri
        </div>
      </div>

      {items.map((it) => (
        <NavLink
          key={it.to}
          to={it.to}
          end={it.to === "/"}
          style={({ isActive }) => ({
            display: "flex",
            alignItems: "center",
            gap: 10,
            padding: "9px 10px",
            borderRadius: 8,
            fontSize: 13.5,
            fontWeight: isActive ? 600 : 500,
            cursor: "pointer",
            textDecoration: "none",
            background: isActive ? color.sidebarActive : "transparent",
            color: isActive ? "#FFFFFF" : color.sidebarText,
          })}
        >
          {it.icon}
          {it.label}
        </NavLink>
      ))}

      <div style={{ flex: 1 }} />

      <div
        style={{
          borderTop: `1px solid ${color.sidebarActive}`,
          margin: "10px 2px 0",
          padding: "12px 8px 2px",
          display: "flex",
          alignItems: "center",
          gap: 9,
        }}
      >
        <div
          style={{
            width: 28,
            height: 28,
            borderRadius: 99,
            background: color.sidebarActive,
            color: "#EAF2E7",
            display: "flex",
            alignItems: "center",
            justifyContent: "center",
            fontSize: 11,
            fontWeight: 700,
          }}
        >
          SA
        </div>
        <div style={{ display: "flex", flexDirection: "column", gap: 1, flex: 1 }}>
          <span style={{ fontSize: 12.5, fontWeight: 600, color: "#E8EFE4" }}>Selim Arslan</span>
          <span style={{ fontSize: 10.5, color: color.sidebarTextSoft }}>Editör</span>
        </div>
        <button
          type="button"
          onClick={handleCikis}
          title="Çıkış"
          aria-label="Çıkış"
          style={{
            display: "flex",
            background: "none",
            border: "none",
            padding: 0,
            cursor: "pointer",
          }}
        >
          <svg width="15" height="15" viewBox="0 0 16 16" fill="none">
            <path
              d="M6.5 3H3.5v10h3M12 8H7M9.5 5.5 12 8l-2.5 2.5"
              stroke={color.sidebarTextSoft}
              strokeWidth="1.6"
              strokeLinecap="round"
              strokeLinejoin="round"
            />
          </svg>
        </button>
      </div>
    </div>
  );
}
