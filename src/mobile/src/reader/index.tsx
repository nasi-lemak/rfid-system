import { createContext, useContext, useEffect, useMemo, useRef, useState, type ReactNode } from 'react';
import { SimulatedReader } from './SimulatedReader';
import { BleReader, ChainwayReader, ZebraReader } from './NativeReaders';
import type { ReaderKind, ReaderStatus, RfidReader, TagReadEvent } from './types';
import { useSettings } from '../store/settings';

export * from './types';

export function createReader(kind: ReaderKind): RfidReader {
  switch (kind) {
    case 'zebra': return new ZebraReader();
    case 'chainway': return new ChainwayReader();
    case 'ble': return new BleReader();
    default: return new SimulatedReader();
  }
}

interface ReaderCtx { reader: RfidReader; status: ReaderStatus; error: string | null; reconnect: () => Promise<void> }
const Ctx = createContext<ReaderCtx>(null!);

export function ReaderProvider({ children }: { children: ReactNode }) {
  const { settings } = useSettings();
  const [reader, setReader] = useState<RfidReader>(() => createReader(settings.readerKind));
  const [status, setStatus] = useState<ReaderStatus>('disconnected');
  const [error, setError] = useState<string | null>(null);
  const kindRef = useRef(settings.readerKind);

  const connect = async (r: RfidReader) => {
    setError(null);
    try { await r.connect(); } catch (e) { setError((e as Error).message); }
  };

  useEffect(() => {
    if (kindRef.current !== settings.readerKind) {
      kindRef.current = settings.readerKind;
      reader.disconnect().catch(() => {});
      const next = createReader(settings.readerKind);
      setReader(next);
    }
  }, [settings.readerKind, reader]);

  useEffect(() => {
    const off = reader.onStatus(setStatus);
    connect(reader);
    return () => { off(); reader.disconnect().catch(() => {}); };
  }, [reader]);

  const value = useMemo(() => ({ reader, status, error, reconnect: () => connect(reader) }), [reader, status, error]);
  return <Ctx.Provider value={value}>{children}</Ctx.Provider>;
}

export const useReader = () => useContext(Ctx);

/** Aggregates inventory reads into a de-duplicated list with counts and best RSSI. */
export interface SeenTag { epc: string; count: number; rssi?: number; firstAt: number; lastAt: number }

export function useInventory(active: boolean) {
  const { reader } = useReader();
  const [tags, setTags] = useState<Map<string, SeenTag>>(new Map());
  const buffer = useRef<TagReadEvent[]>([]);
  useEffect(() => {
    const off = reader.onTag((e) => { buffer.current.push(e); });
    const flush = setInterval(() => {
      if (buffer.current.length === 0) return;
      const batch = buffer.current; buffer.current = [];
      setTags((prev) => {
        const next = new Map(prev);
        for (const e of batch) {
          const t = next.get(e.epc);
          if (t) { t.count++; t.lastAt = e.at; if (e.rssi != null && (t.rssi == null || e.rssi > t.rssi)) t.rssi = e.rssi; }
          else next.set(e.epc, { epc: e.epc, count: 1, rssi: e.rssi, firstAt: e.at, lastAt: e.at });
        }
        return next;
      });
    }, 200);
    return () => { off(); clearInterval(flush); };
  }, [reader]);
  useEffect(() => {
    if (active) reader.startInventory().catch(() => {}); else reader.stopInventory().catch(() => {});
    return () => { reader.stopInventory().catch(() => {}); };
  }, [active, reader]);
  return { tags, clear: () => setTags(new Map()) };
}
