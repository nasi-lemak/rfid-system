import { createContext, useCallback, useContext, useEffect, useMemo, useState, type ReactNode } from 'react';
import { get, getToken, post, setToken } from './api/client';
import type { AuthUser } from './api/types';

interface AuthState { user: AuthUser | null; token: string | null; /** true after the API rejected the stored token (session expired); cleared by the next sign-in */ sessionExpired: boolean; login: (email: string, password: string) => Promise<void>; loginWithToken: (token: string) => Promise<void>; logout: () => void }
const Ctx = createContext<AuthState>(null!);

export function AuthProvider({ children }: { children: ReactNode }) {
  const [token, setTok] = useState<string | null>(getToken());
  const [user, setUser] = useState<AuthUser | null>(() => { try { const u = localStorage.getItem('rfid.user'); return u ? JSON.parse(u) : null; } catch { return null; } });

  const [sessionExpired, setSessionExpired] = useState(false);
  const logout = useCallback(() => { setToken(null); setTok(null); setUser(null); try { localStorage.removeItem('rfid.user'); } catch { /* ignore */ } }, []);
  // A 401 on any call means the session is gone: sign out, but tell the user why on the login page.
  useEffect(() => { const expired = () => { if (user) setSessionExpired(true); logout(); }; window.addEventListener('rfid:unauthorized', expired); return () => window.removeEventListener('rfid:unauthorized', expired); }, [logout, user]);

  const login = useCallback(async (email: string, password: string) => {
    const res = await post<{ token: string; user: AuthUser }>('/api/auth/login', { email, password });
    setToken(res.token); setTok(res.token); setUser(res.user); setSessionExpired(false);
    try { localStorage.setItem('rfid.user', JSON.stringify(res.user)); } catch { /* ignore */ }
  }, []);

  /** SSO: the browser holds an external (OIDC) token; the API maps it to a platform user via /api/auth/me. */
  const loginWithToken = useCallback(async (t: string) => {
    setToken(t);
    try {
      const me = await get<AuthUser & { sites?: { siteLocationId: string; role: string }[] }>('/api/auth/me');
      setTok(t); setUser(me); setSessionExpired(false);
      try { localStorage.setItem('rfid.user', JSON.stringify(me)); } catch { /* ignore */ }
    } catch (e) { setToken(null); throw e; }
  }, []);

  const value = useMemo(() => ({ user, token, sessionExpired, login, loginWithToken, logout }), [user, token, sessionExpired, login, loginWithToken, logout]);
  return <Ctx.Provider value={value}>{children}</Ctx.Provider>;
}

export const useAuth = () => useContext(Ctx);
