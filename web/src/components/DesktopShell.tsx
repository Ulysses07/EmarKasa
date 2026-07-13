// Masaüstü editör iskeleti: sabit sol menü + esnek içerik. 1b–1f sarmalı.

import type { ReactNode } from "react";
import { color } from "../theme";
import Sidebar from "./Sidebar";

export default function DesktopShell({ children }: { children: ReactNode }) {
  return (
    <div style={{ width: 1440, display: "flex", background: color.appBg, minHeight: 700 }}>
      <Sidebar />
      <div style={{ flex: 1, minWidth: 0, display: "flex", flexDirection: "column" }}>{children}</div>
    </div>
  );
}
