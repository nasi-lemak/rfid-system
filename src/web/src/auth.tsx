import { createContext, useCallback, useContext, useEffect, useMemo, useState, type ReactNode } from 'react';
import { getToken, post, setToken } from './api/client';
import type { AuthUser } from './api/types';

interface AuthState { user: AuthUser | null; token: string | null; login: (email: string, password: string) => Promise<void>; logout: () => void }
const Ctx = createContext<AuthState>(null!);

export function AuthProvider({ children }: { children: ReactNode }) {
  const [token, setTok] = useState<string | null>(getToken());
  const [user, setUser] = useState<AuthUser | null>(() => { try { const u = localStorage.getItem('rfid.user'); return u ? JSON.parse(u) : null; } catch { return null; } });

  const logout = useCallback(() => { setToken(null); setTok(null); setUser(null); try { localStorage.removeItem('rfid.user'); } catch { /* ignore */ } }, []);
  useEffect(() => { window.addEventListener('rfid:unauthorized', logout); return () => window.removeEventListener('rfid:unauthorized', logout); }, [logout]);

  const login = useCallback(async (email: string, password: string) => {
    const res = await post<{ token: string; user: AuthUser }>('/api/auth/login', { email, password });
    setToken(res.token); setTok(res.token); setUser(res.user);
    try { localStorage.setItem('rfid.user', JSON.stringify(res.user)); } catch { /* ignore */ }
  }, []);

  const value = useMemo(() => ({ user, token, login, logout }), [user, token, login, logout]);
  return <Ctx.Provider value={value}>{children}</Ctx.Provider>;
}

export const useAuth = () => useContext(Ctx);
