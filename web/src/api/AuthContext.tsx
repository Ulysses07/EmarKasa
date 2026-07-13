import { createContext, useContext, useEffect, useState, type ReactNode } from 'react'
import { me, type Rol } from './auth'

interface AuthDurum { rol: Rol | null; yukleniyor: boolean; ayarla: (r: Rol | null) => void }
const Ctx = createContext<AuthDurum>({ rol: null, yukleniyor: true, ayarla: () => {} })

export function AuthProvider({ children }: { children: ReactNode }) {
  const [rol, setRol] = useState<Rol | null>(null)
  const [yukleniyor, setYukleniyor] = useState(true)
  useEffect(() => {
    me().then(setRol).finally(() => setYukleniyor(false))
  }, [])
  return <Ctx.Provider value={{ rol, yukleniyor, ayarla: setRol }}>{children}</Ctx.Provider>
}

export const useAuth = () => useContext(Ctx)
