import type { ReactNode } from 'react'
import { Routes, Route, Navigate } from 'react-router-dom'
import DesktopShell from './components/DesktopShell'
import IslemDefter from './screens/IslemDefter'
import HaftalikOzet from './screens/HaftalikOzet'
import AylikRapor from './screens/AylikRapor'
import Cariler from './screens/Cariler'
import Ayarlar from './screens/Ayarlar'
import Panel from './screens/Panel'
import Login from './screens/Login'
import { AuthProvider, useAuth } from './api/AuthContext'
import { color } from './theme'

function ShellPage({ children }: { children: ReactNode }) {
  return (
    <div style={{ minHeight: '100%', background: color.pageDark, display: 'flex', justifyContent: 'center', padding: '24px 0' }}>
      <DesktopShell>{children}</DesktopShell>
    </div>
  )
}

function Korumali({ children, editor = false }: { children: ReactNode; editor?: boolean }) {
  const { rol, yukleniyor } = useAuth()
  if (yukleniyor) return null
  if (rol === null) return <Navigate to="/login" replace />
  if (editor && rol !== 'editor') return <Navigate to="/panel" replace />
  return <>{children}</>
}

export default function App() {
  return (
    <AuthProvider>
      <Routes>
        <Route path="/" element={<Korumali editor><ShellPage><IslemDefter /></ShellPage></Korumali>} />
        <Route path="/haftalik" element={<Korumali editor><ShellPage><HaftalikOzet /></ShellPage></Korumali>} />
        <Route path="/aylik" element={<Korumali editor><ShellPage><AylikRapor /></ShellPage></Korumali>} />
        <Route path="/cariler" element={<Korumali editor><ShellPage><Cariler /></ShellPage></Korumali>} />
        <Route path="/ayarlar" element={<Korumali editor><ShellPage><Ayarlar /></ShellPage></Korumali>} />
        <Route path="/panel" element={<Korumali><Panel /></Korumali>} />
        <Route path="/login" element={<Login />} />
      </Routes>
    </AuthProvider>
  )
}
