// Kasa Defteri tasarım token'ları — "Kasa Defteri.dc.html" handoff'undan çıkarıldı.

export const font = {
  sans: "'IBM Plex Sans', system-ui, sans-serif",
  mono: "'IBM Plex Mono', monospace",
} as const;

export const color = {
  // yüzeyler
  pageDark: "#E9E7E1",
  appBg: "#F5F4EF",
  card: "#FFFFFF",
  inputBg: "#FDFCFA",
  hover: "#FAF9F5",
  // metin
  ink: "#20261F",
  sub: "#6F7566",
  muted: "#9AA08F",
  faint: "#B0B5A4",
  // çizgiler
  border: "#E3E0D6",
  borderSoft: "#EDEAE0",
  borderInput: "#DAD6C9",
  rowLine: "#F1EFE8",
  // marka yeşili
  green: "#1E5F46",
  greenDark: "#174D38",
  greenSoft: "#F0F5F1",
  // durum
  pos: "#1B7A4E",
  neg: "#C13A2E",
  // sidebar (koyu)
  sidebar: "#1F2A23",
  sidebarActive: "#33453A",
  sidebarHover: "#28352C",
  sidebarText: "#C9D2C6",
  sidebarTextSoft: "#8FA08F",
  sidebarTitle: "#F2F5EE",
} as const;

export type KanalAd = "MEZAT" | "PERAKENDE" | "TOPTAN" | "Ortak";

export const kanalRenk: Record<KanalAd, { c: string; bg: string; dot: string }> = {
  MEZAT: { c: "#A16A0B", bg: "#F5EAD2", dot: "#C98A12" },
  PERAKENDE: { c: "#245EA8", bg: "#E2ECF8", dot: "#3572C1" },
  TOPTAN: { c: "#6D4FA1", bg: "#ECE6F7", dot: "#7E5BBF" },
  Ortak: { c: "#5C6470", bg: "#E9ECEF", dot: "#7A828E" },
};
