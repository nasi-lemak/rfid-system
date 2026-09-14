/**
 * Hardware abstraction for handheld RFID sleds / integrated readers.
 * Screens only ever talk to this interface; drivers live next to it.
 */
export type ReaderKind = 'simulated' | 'zebra' | 'chainway' | 'ble';

export interface TagReadEvent {
  epc: string;
  tid?: string;
  rssi?: number;      // dBm, typically -30 (near) … -80 (far)
  antenna?: number;
  at: number;         // epoch ms
}

export type ReaderStatus = 'disconnected' | 'connecting' | 'ready' | 'inventory' | 'locating' | 'error';

export interface ReaderInfo { kind: ReaderKind; model?: string; serial?: string; battery?: number; firmware?: string }

export interface InventoryOptions { power?: number; session?: 0 | 1 | 2 | 3; filterEpcPrefix?: string }

export interface RfidReader {
  readonly kind: ReaderKind;
  readonly status: ReaderStatus;
  connect(): Promise<ReaderInfo>;
  disconnect(): Promise<void>;
  /** Continuous inventory; events arrive via onTag until stopInventory. */
  startInventory(opts?: InventoryOptions): Promise<void>;
  stopInventory(): Promise<void>;
  /** Geiger-counter mode for a single EPC; emits proximity 0..100 via onLocate. */
  startLocate(epc: string): Promise<void>;
  stopLocate(): Promise<void>;
  /** Write a new EPC into the tag currently in field (optionally filtered by current EPC / TID). */
  writeEpc(newEpc: string, opts?: { currentEpc?: string; tid?: string; accessPassword?: string }): Promise<void>;
  setPower(dbm: number): Promise<void>;
  onTag(handler: (e: TagReadEvent) => void): () => void;
  onLocate(handler: (proximity: number, rssi?: number) => void): () => void;
  onStatus(handler: (s: ReaderStatus) => void): () => void;
  /** Hardware trigger (pistol grip) pressed/released. */
  onTrigger(handler: (pressed: boolean) => void): () => void;
}

/** Tiny typed event emitter shared by drivers. */
export class Emitter<T extends Record<string, unknown[]>> {
  private handlers: { [K in keyof T]?: Array<(...args: T[K]) => void> } = {};
  on<K extends keyof T>(k: K, h: (...args: T[K]) => void): () => void {
    (this.handlers[k] ??= []).push(h);
    return () => { this.handlers[k] = (this.handlers[k] ?? []).filter((x) => x !== h); };
  }
  emit<K extends keyof T>(k: K, ...args: T[K]) { (this.handlers[k] ?? []).forEach((h) => h(...args)); }
}

export type ReaderEvents = { tag: [TagReadEvent]; locate: [number, number | undefined]; status: [ReaderStatus]; trigger: [boolean] };

export abstract class BaseReader implements RfidReader {
  abstract readonly kind: ReaderKind;
  status: ReaderStatus = 'disconnected';
  protected ev = new Emitter<ReaderEvents>();
  protected setStatus(s: ReaderStatus) { this.status = s; this.ev.emit('status', s); }
  abstract connect(): Promise<ReaderInfo>;
  abstract disconnect(): Promise<void>;
  abstract startInventory(opts?: InventoryOptions): Promise<void>;
  abstract stopInventory(): Promise<void>;
  abstract startLocate(epc: string): Promise<void>;
  abstract stopLocate(): Promise<void>;
  abstract writeEpc(newEpc: string, opts?: { currentEpc?: string; tid?: string; accessPassword?: string }): Promise<void>;
  abstract setPower(dbm: number): Promise<void>;
  onTag(h: (e: TagReadEvent) => void) { return this.ev.on('tag', h); }
  onLocate(h: (p: number, rssi?: number) => void) { return this.ev.on('locate', h); }
  onStatus(h: (s: ReaderStatus) => void) { return this.ev.on('status', h); }
  onTrigger(h: (p: boolean) => void) { return this.ev.on('trigger', h); }
}
