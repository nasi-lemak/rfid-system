import { HubConnectionBuilder, LogLevel, type HubConnection } from '@microsoft/signalr';
import { createContext, useContext, useEffect, useMemo, useRef, useState, type ReactNode } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import { getToken, hubUrl } from './api/client';
import { useAuth } from './auth';

export interface LiveRead { epc: string; itemId?: string | null; itemName?: string | null; deviceId?: string | null; locationId?: string | null; rssi?: number | null; readAt: string }
export interface LiveAlert { id: string; itemId?: string; severity: string; message: string; raisedAt: string }
interface LiveState { connected: boolean; reads: LiveRead[]; alerts: LiveAlert[]; clearReads: () => void }
const Ctx = createContext<LiveState>({ connected: false, reads: [], alerts: [], clearReads: () => {} });

/** Keeps a SignalR connection to /hubs/live and buffers the last few hundred reads/alerts for the UI. */
export function LiveProvider({ children }: { children: ReactNode }) {
  const { token } = useAuth();
  const qc = useQueryClient();
  const [connected, setConnected] = useState(false);
  const [reads, setReads] = useState<LiveRead[]>([]);
  const [alerts, setAlerts] = useState<LiveAlert[]>([]);
  const conn = useRef<HubConnection | null>(null);

  useEffect(() => {
    if (!token) return;
    const c = new HubConnectionBuilder().withUrl(hubUrl(), { accessTokenFactory: () => getToken() ?? '' }).withAutomaticReconnect().configureLogging(LogLevel.Critical).build();
    c.on('reads', (batch: LiveRead[]) => setReads((r) => [...batch.slice().reverse(), ...r].slice(0, 300)));
    c.on('alert', (a: LiveAlert) => { setAlerts((x) => [a, ...x].slice(0, 100)); qc.invalidateQueries({ queryKey: ['alerts'] }); qc.invalidateQueries({ queryKey: ['dashboard'] }); });
    c.on('event', () => { qc.invalidateQueries({ queryKey: ['events'] }); qc.invalidateQueries({ queryKey: ['items'] }); });
    c.onreconnected(() => setConnected(true)); c.onreconnecting(() => setConnected(false)); c.onclose(() => setConnected(false));
    let cancelled = false;
    c.start().then(() => { if (!cancelled) setConnected(true); }).catch(() => { if (!cancelled) setConnected(false); });
    conn.current = c;
    // StrictMode mounts effects twice in dev; stopping during negotiation is expected there.
    return () => { cancelled = true; c.stop().catch(() => {}); conn.current = null; };
  }, [token, qc]);

  const value = useMemo(() => ({ connected, reads, alerts, clearReads: () => setReads([]) }), [connected, reads, alerts]);
  return <Ctx.Provider value={value}>{children}</Ctx.Provider>;
}
export const useLive = () => useContext(Ctx);
