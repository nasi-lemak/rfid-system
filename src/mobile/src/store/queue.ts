import AsyncStorage from '@react-native-async-storage/async-storage';
import { Api } from '../api/client';

/**
 * Offline-first queue for operations. Every operation gets a clientId so the server can
 * de-duplicate retries; the queue is flushed whenever the device is online (manually or on app focus).
 */
export interface QueuedOperation { clientId: string; createdAt: string; request: Record<string, unknown>; attempts: number; lastError?: string }
const KEY = 'rfid.queue';

export async function readQueue(): Promise<QueuedOperation[]> { try { const raw = await AsyncStorage.getItem(KEY); return raw ? JSON.parse(raw) : []; } catch { return []; } }
async function writeQueue(q: QueuedOperation[]) { try { await AsyncStorage.setItem(KEY, JSON.stringify(q)); } catch { /* ignore */ } }

export function newClientId() { return `${Date.now().toString(36)}-${Math.random().toString(36).slice(2, 8)}`; }

export async function enqueue(request: Record<string, unknown>): Promise<QueuedOperation> {
  const op: QueuedOperation = { clientId: newClientId(), createdAt: new Date().toISOString(), request, attempts: 0 };
  const q = await readQueue(); q.push(op); await writeQueue(q);
  return op;
}

/** Try the server first; if the network fails, park the operation in the queue. */
export async function runOrQueue(request: Record<string, unknown>) {
  const clientId = newClientId();
  const req = { ...request, clientId, reference: (request.reference as string) || clientId, occurredAt: new Date().toISOString() };
  try {
    const result = await Api.operation(req);
    return { online: true as const, result };
  } catch (e) {
    const err = e as { status?: number; message: string };
    if (err.status && err.status >= 400 && err.status < 500) throw e; // business error, don't queue
    const q = await readQueue(); q.push({ clientId, createdAt: req.occurredAt, request: req, attempts: 0, lastError: err.message }); await writeQueue(q);
    return { online: false as const, queued: q.length };
  }
}

export async function sync(): Promise<{ sent: number; failed: number; remaining: number }> {
  const q = await readQueue();
  if (q.length === 0) return { sent: 0, failed: 0, remaining: 0 };
  let sent = 0, failed = 0;
  try {
    const results = await Api.operationBatch(q.map((o) => o.request));
    const remaining: QueuedOperation[] = [];
    q.forEach((o, i) => {
      const r = results[i];
      if (r?.ok) sent++;
      else if (r && !r.ok && r.error) { failed++; /* business rejection: drop it, but keep the error visible in the log */ }
      else remaining.push({ ...o, attempts: o.attempts + 1 });
    });
    await writeQueue(remaining);
    return { sent, failed, remaining: remaining.length };
  } catch (e) {
    await writeQueue(q.map((o) => ({ ...o, attempts: o.attempts + 1, lastError: (e as Error).message })));
    return { sent: 0, failed: 0, remaining: q.length };
  }
}

export async function clearQueue() { await writeQueue([]); }
