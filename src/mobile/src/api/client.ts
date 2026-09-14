import { getSettings } from '../store/settings';

export class ApiError extends Error { status: number; constructor(status: number, message: string) { super(message); this.status = status; } }

export async function api<T = unknown>(path: string, init: { method?: string; json?: unknown; query?: Record<string, unknown>; timeoutMs?: number } = {}): Promise<T> {
  const s = await getSettings();
  const base = s.serverUrl.replace(/\/$/, '');
  let url = base + path;
  if (init.query) { const q = Object.entries(init.query).filter(([, v]) => v !== undefined && v !== null && v !== '').map(([k, v]) => `${encodeURIComponent(k)}=${encodeURIComponent(String(v))}`).join('&'); if (q) url += (url.includes('?') ? '&' : '?') + q; }
  const headers: Record<string, string> = {};
  if (s.token) headers.Authorization = `Bearer ${s.token}`;
  if (init.json !== undefined) headers['Content-Type'] = 'application/json';
  const controller = new AbortController();
  const timer = setTimeout(() => controller.abort(), init.timeoutMs ?? 15000);
  try {
    const res = await fetch(url, { method: init.method ?? (init.json !== undefined ? 'POST' : 'GET'), headers, body: init.json !== undefined ? JSON.stringify(init.json) : undefined, signal: controller.signal });
    if (!res.ok) { let msg = `${res.status} ${res.statusText}`; try { const j = await res.json(); msg = j.error ?? msg; } catch { /* ignore */ } throw new ApiError(res.status, msg); }
    const text = await res.text();
    if (!res.headers.get('content-type')?.includes('json')) return text as unknown as T;
    return (text ? JSON.parse(text) : undefined) as T;
  } finally { clearTimeout(timer); }
}

export const get = <T,>(path: string, query?: Record<string, unknown>) => api<T>(path, { query });
export const post = <T,>(path: string, json?: unknown) => api<T>(path, { method: 'POST', json: json ?? {} });

export interface ItemSummary { id: string; name: string; identifier: string; state?: string; status: string; itemType?: { name: string; code: string; category: string; isContainer: boolean; lifecycle?: { states: string[]; transitions: { from: string; to: string; on: string }[] } | null }; currentLocation?: { id: string; name: string } | null; custodianParty?: { id: string; name: string } | null; quantity: number; unit?: string; cycleCount: number; dueBackAt?: string; expiryDate?: string; lastSeenAt?: string; attributes: Record<string, unknown>; tags: { epc: string; status: string }[] }
export interface LocationRow { id: string; parentId?: string | null; kind: string; name: string; code?: string | null; path: string }
export interface PartyRow { id: string; kind: string; name: string }
export interface ItemTypeRow { id: string; name: string; code: string; category: string; lifecycle?: { initial?: string | null; states: string[] } | null; attributeSchema: { name: string; type: string; required?: boolean }[] }
export interface OperationLineResult { epc?: string; itemId?: string; itemName?: string; result: string; message?: string; newState?: string }
export interface OperationResult { operationId: string; ok: number; unknown: number; rejected: number; lines: OperationLineResult[] }
export interface StocktakeSummary { id: string; name: string; status: string; expected: number; found: number; missing: number; unexpected: number; unknown: number }

export const Api = {
  login: (email: string, password: string) => post<{ token: string; user: { displayName: string; role: string; tenantName?: string } }>('/api/auth/login', { email, password }),
  deviceLogin: (token: string) => post<{ token: string; device: { id: string; name: string } }>('/api/auth/device', { token }),
  me: () => get<{ displayName?: string; role: string }>('/api/auth/me'),
  items: (q?: Record<string, unknown>) => get<{ items: ItemSummary[]; total: number }>('/api/items', { pageSize: 200, ...q }),
  byEpc: (epc: string) => get<{ tag: { epc: string; status: string }; item: ItemSummary | null }>(`/api/items/by-epc/${encodeURIComponent(epc)}`),
  events: (id: string) => get<{ id: string; type: string; occurredAt: string; toState?: string; data: Record<string, unknown> }[]>(`/api/items/${id}/events?take=20`),
  locations: () => get<LocationRow[]>('/api/locations'),
  parties: () => get<PartyRow[]>('/api/parties'),
  itemTypes: () => get<ItemTypeRow[]>('/api/item-types'),
  operation: (req: unknown) => post<OperationResult>('/api/operations', req),
  operationBatch: (reqs: unknown[]) => post<{ clientId?: string; ok: boolean; error?: string; result?: OperationResult }[]>('/api/operations/batch', reqs),
  ingestHandheld: (reads: { epc: string; rssi?: number; locationId?: string; readAt?: string }[], sessionId?: string) => post<{ received: number; resolved: number; unknown: number; alerts: number }>('/api/ingest/handheld', { reads, sessionId }),
  stocktakes: () => get<{ summary: StocktakeSummary; location?: string; startedAt: string }[]>('/api/stocktakes'),
  createStocktake: (name: string, locationId: string) => post<StocktakeSummary>('/api/stocktakes', { name, locationId }),
  stocktakeScans: (id: string, epcs: string[], locationId?: string) => post<StocktakeSummary>(`/api/stocktakes/${id}/scans`, { epcs, locationId }),
  stocktakeReconcile: (id: string) => post<StocktakeSummary>(`/api/stocktakes/${id}/reconcile`),
  devices: () => get<{ id: string; name: string; kind: string; config: Record<string, unknown> }[]>('/api/devices'),
  printLabel: (itemIds: string[], printerDeviceId: string) => post<{ itemId: string; ok: boolean; error?: string; epc?: string }[]>('/api/labels/print', { itemIds, printerDeviceId }),
  labelZpl: (itemId: string) => api<string>(`/api/labels/items/${itemId}`),
};
