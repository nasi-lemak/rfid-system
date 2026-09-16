import AsyncStorage from '@react-native-async-storage/async-storage';
import { Api, ApiError } from '../api/client';

/**
 * Offline-first, store-and-forward queue for operations.
 *
 * Invariants:
 *  - every request carries a clientId before it is sent the first time, so any retry (online or from the
 *    queue) is idempotent on the server (the server keys operations by clientId);
 *  - results are matched by clientId, never by position, so partial/ reordered batch responses are safe;
 *  - transport failures retry with exponential backoff and a hard attempt cap; business rejections are
 *    never retried but are kept in a separate, capped "rejected" list so the operator can see them.
 */
export interface QueuedOperation { clientId: string; createdAt: string; request: Record<string, unknown>; attempts: number; lastError?: string; nextAttemptAt?: string }
export interface RejectedOperation { clientId: string; createdAt: string; rejectedAt: string; request: Record<string, unknown>; error: string }
const KEY = 'rfid.queue';
const REJECTED_KEY = 'rfid.queue.rejected';
export const MAX_ATTEMPTS = 20;
export const BATCH_SIZE = 50;
const MAX_REJECTED = 100;

export async function readQueue(): Promise<QueuedOperation[]> { try { const raw = await AsyncStorage.getItem(KEY); return raw ? JSON.parse(raw) : []; } catch { return []; } }
export async function readRejected(): Promise<RejectedOperation[]> { try { const raw = await AsyncStorage.getItem(REJECTED_KEY); return raw ? JSON.parse(raw) : []; } catch { return []; } }
async function writeQueue(q: QueuedOperation[]) { try { await AsyncStorage.setItem(KEY, JSON.stringify(q)); } catch { /* ignore */ } }
async function writeRejected(r: RejectedOperation[]) { try { await AsyncStorage.setItem(REJECTED_KEY, JSON.stringify(r.slice(-MAX_REJECTED))); } catch { /* ignore */ } }

export function newClientId() { return `${Date.now().toString(36)}-${Math.random().toString(36).slice(2, 8)}`; }

/** Backoff: 5s · 10s · 20s … capped at 10 minutes. */
export function backoffMs(attempts: number) { return Math.min(5000 * 2 ** Math.max(0, attempts - 1), 10 * 60_000); }

/** Stamps the request with the identity the server de-duplicates on. Idempotent: an existing clientId is kept. */
export function stamp(request: Record<string, unknown>) {
  const clientId = (request.clientId as string) || newClientId();
  return { ...request, clientId, reference: (request.reference as string) || clientId, occurredAt: (request.occurredAt as string) || new Date().toISOString() };
}

/** Park an operation without trying the network (explicit offline mode). */
export async function enqueue(request: Record<string, unknown>): Promise<QueuedOperation> {
  const req = stamp(request);
  const op: QueuedOperation = { clientId: req.clientId, createdAt: req.occurredAt, request: req, attempts: 0 };
  const q = await readQueue(); q.push(op); await writeQueue(q);
  return op;
}

/** Try the server first; if the network fails, park the operation in the queue. */
export async function runOrQueue(request: Record<string, unknown>) {
  const req = stamp(request);
  try {
    const result = await Api.operation(req);
    return { online: true as const, result };
  } catch (e) {
    const err = e as { status?: number; message: string };
    // A business error (4xx) is shown, not queued. 401 is the exception: the session expired, the work is still
    // valid, so it waits in the queue for the operator to sign in again.
    if (err.status && err.status >= 400 && err.status < 500 && err.status !== 401) throw e;
    const q = await readQueue();
    q.push({ clientId: req.clientId, createdAt: req.occurredAt, request: req, attempts: 1, lastError: err.message, nextAttemptAt: new Date(Date.now() + backoffMs(1)).toISOString() });
    await writeQueue(q);
    return { online: false as const, queued: q.length };
  }
}

export interface SyncResult { sent: number; failed: number; remaining: number; deferred: number; unauthorized?: boolean }

/**
 * Flush the queue in FIFO batches. `force` ignores per-item backoff (manual "Sync now").
 * Operations that keep failing at the transport level are moved to the rejected list after MAX_ATTEMPTS.
 */
export async function sync(force = false): Promise<SyncResult> {
  const q = await readQueue();
  if (q.length === 0) return { sent: 0, failed: 0, remaining: 0, deferred: 0 };
  const now = Date.now();
  const due = q.filter((o) => force || !o.nextAttemptAt || Date.parse(o.nextAttemptAt) <= now);
  const deferred = q.filter((o) => !due.includes(o));
  if (due.length === 0) return { sent: 0, failed: 0, remaining: q.length, deferred: deferred.length };

  let sent = 0, failed = 0;
  const rejected = await readRejected();
  const remaining: QueuedOperation[] = [...deferred];
  let unauthorized = false;
  for (let i = 0; i < due.length; i += BATCH_SIZE) {
    const chunk = due.slice(i, i + BATCH_SIZE);
    if (unauthorized) { remaining.push(...chunk); continue; } // session expired: keep everything untouched until re-login
    try {
      const results = await Api.operationBatch(chunk.map((o) => o.request));
      const byId = new Map<string, { clientId?: string; ok: boolean; error?: string }>();
      for (const r of results) if (r?.clientId) byId.set(r.clientId, r);
      for (const o of chunk) {
        const r = byId.get(o.clientId);
        if (r?.ok) { sent++; continue; }
        if (r && !r.ok) { failed++; rejected.push({ clientId: o.clientId, createdAt: o.createdAt, rejectedAt: new Date().toISOString(), request: o.request, error: r.error || 'Rejected' }); continue; }
        remaining.push(retryLater(o, 'No result for this operation in the batch response'));
      }
    } catch (e) {
      if (e instanceof ApiError && e.status === 401) { unauthorized = true; remaining.push(...chunk); continue; } // not an attempt: nothing was tried on the operator's behalf
      const msg = (e as Error).message;
      for (const o of chunk) remaining.push(retryLater(o, msg));
    }
  }
  // Attempt cap: stop hammering the server with something that can never be delivered, but keep it visible.
  const kept: QueuedOperation[] = [];
  for (const o of remaining) {
    if (o.attempts >= MAX_ATTEMPTS) { failed++; rejected.push({ clientId: o.clientId, createdAt: o.createdAt, rejectedAt: new Date().toISOString(), request: o.request, error: `Gave up after ${o.attempts} attempts: ${o.lastError ?? 'transport error'}` }); }
    else kept.push(o);
  }
  kept.sort((a, b) => a.createdAt.localeCompare(b.createdAt));
  await writeQueue(kept);
  await writeRejected(rejected);
  return { sent, failed, remaining: kept.length, deferred: deferred.length, unauthorized };
}

function retryLater(o: QueuedOperation, error: string): QueuedOperation {
  const attempts = o.attempts + 1;
  return { ...o, attempts, lastError: error, nextAttemptAt: new Date(Date.now() + backoffMs(attempts)).toISOString() };
}

export async function clearQueue() { await writeQueue([]); }
export async function clearRejected() { await writeRejected([]); }
