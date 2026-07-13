// Kasa Defteri rota tablosu. Masaüstü editör ekranları DesktopShell içinde;
// mobil ortak paneli (1a) ve giriş (1g) tam ekran, kabuk dışı.

import type { ReactNode } from "react";
import { Routes, Route } from "react-router-dom";
import DesktopShell from "./components/DesktopShell";
import IslemDefter from "./screens/IslemDefter";
import HaftalikOzet from "./screens/HaftalikOzet";
import AylikRapor from "./screens/AylikRapor";
import Cariler from "./screens/Cariler";
import Ayarlar from "./screens/Ayarlar";
import Panel from "./screens/Panel";
import Login from "./screens/Login";
import { color } from "./theme";

function ShellPage({ children }: { children: ReactNode }) {
  return (
    <div
      style={{
        minHeight: "100%",
        background: color.pageDark,
        display: "flex",
        justifyContent: "center",
        padding: "24px 0",
      }}
    >
      <DesktopShell>{children}</DesktopShell>
    </div>
  );
}

export default function App() {
  return (
    <Routes>
      <Route path="/" element={<ShellPage><IslemDefter /></ShellPage>} />
      <Route path="/haftalik" element={<ShellPage><HaftalikOzet /></ShellPage>} />
      <Route path="/aylik" element={<ShellPage><AylikRapor /></ShellPage>} />
      <Route path="/cariler" element={<ShellPage><Cariler /></ShellPage>} />
      <Route path="/ayarlar" element={<ShellPage><Ayarlar /></ShellPage>} />
      <Route path="/panel" element={<Panel />} />
      <Route path="/login" element={<Login />} />
    </Routes>
  );
}
