const BASE = (import.meta.env.VITE_API_URL as string | undefined) ?? '';

export class ApiError extends Error {
  status: number;
  constructor(status: number, message: string) { super(message); this.status = status; }
}

export function getToken() { try { return localStorage.getItem('rfid.token'); } catch { return null; } }
export function setToken(t: string | null) { try { t ? localStorage.setItem('rfid.token', t) : localStorage.removeItem('rfid.token'); } catch { /* ignore */ } }

export async function api<T = unknown>(path: string, init: RequestInit & { json?: unknown; query?: Record<string, unknown> } = {}): Promise<T> {
  const url = new URL(BASE + path, window.location.origin);
  if (init.query) for (const [k, v] of Object.entries(init.query)) if (v !== undefined && v !== null && v !== '') url.searchParams.set(k, String(v));
  const headers: Record<string, string> = { ...(init.headers as Record<string, string> ?? {}) };
  const token = getToken();
  if (token) headers.Authorization = `Bearer ${token}`;
  let body = init.body;
  if (init.json !== undefined) { headers['Content-Type'] = 'application/json'; body = JSON.stringify(init.json); }
  const res = await fetch(url.toString(), { ...init, headers, body });
  if (res.status === 401) { setToken(null); window.dispatchEvent(new Event('rfid:unauthorized')); }
  if (!res.ok) {
    let msg = res.statusText;
    try { const j = await res.json(); msg = j.error ?? j.title ?? JSON.stringify(j); } catch { /* ignore */ }
    throw new ApiError(res.status, msg);
  }
  if (res.status === 204) return undefined as T;
  const text = await res.text();
  return (text ? JSON.parse(text) : undefined) as T;
}

export const get = <T,>(path: string, query?: Record<string, unknown>) => api<T>(path, { query });
export const post = <T,>(path: string, json?: unknown) => api<T>(path, { method: 'POST', json });
export const put = <T,>(path: string, json?: unknown) => api<T>(path, { method: 'PUT', json });
export const del = <T,>(path: string) => api<T>(path, { method: 'DELETE' });
export const hubUrl = () => `${BASE}/hubs/live`;
