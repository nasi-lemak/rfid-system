import { BaseReader, type InventoryOptions, type ReaderInfo, type ReaderKind } from './types';

/**
 * Software reader for development, demos and automated tests. It "sees" a pool of EPCs
 * (seeded from the server's items plus a few unknown tags) with random RSSI and read rates.
 */
export class SimulatedReader extends BaseReader {
  readonly kind: ReaderKind = 'simulated';
  private pool: string[] = [];
  private timer: ReturnType<typeof setInterval> | null = null;
  private locateTimer: ReturnType<typeof setInterval> | null = null;
  private power = 30;
  private phase = 0;

  setPool(epcs: string[]) { this.pool = [...new Set(epcs.map((e) => e.toUpperCase()))]; }
  get poolSize() { return this.pool.length; }

  async connect(): Promise<ReaderInfo> {
    this.setStatus('connecting');
    await new Promise((r) => setTimeout(r, 300));
    this.setStatus('ready');
    return { kind: 'simulated', model: 'SIM-1000', serial: 'SIM-0001', battery: 87, firmware: '1.0.0' };
  }
  async disconnect() { await this.stopInventory(); await this.stopLocate(); this.setStatus('disconnected'); }

  async startInventory(_opts?: InventoryOptions) {
    if (this.timer) return;
    this.setStatus('inventory');
    const unknown = ['E2801191A50300000000BEEF', 'E2801191A5030000000000C0'];
    this.timer = setInterval(() => {
      const all = [...this.pool, ...unknown];
      if (all.length === 0) return;
      // Each tick, a random subset of tags respond – stronger power sees more.
      const n = Math.max(1, Math.round((all.length * this.power) / 30 * (0.3 + Math.random() * 0.5)));
      for (let i = 0; i < n; i++) {
        const epc = all[Math.floor(Math.random() * all.length)];
        this.ev.emit('tag', { epc, rssi: -35 - Math.random() * 40, antenna: 1, at: Date.now() });
      }
    }, 180);
  }
  async stopInventory() { if (this.timer) { clearInterval(this.timer); this.timer = null; } if (this.status === 'inventory') this.setStatus('ready'); }

  async startLocate(epc: string) {
    await this.stopInventory();
    this.setStatus('locating');
    this.phase = 0;
    const known = this.pool.includes(epc.toUpperCase());
    this.locateTimer = setInterval(() => {
      this.phase += 0.08;
      // Pretend the user walks toward the tag: proximity rises with a bit of noise.
      const base = known ? Math.min(100, 20 + this.phase * 12) : 0;
      const p = Math.max(0, Math.min(100, base + (Math.random() - 0.5) * 14));
      this.ev.emit('locate', Math.round(p), known ? -80 + p * 0.45 : undefined);
    }, 250);
  }
  async stopLocate() { if (this.locateTimer) { clearInterval(this.locateTimer); this.locateTimer = null; } if (this.status === 'locating') this.setStatus('ready'); }

  async writeEpc(newEpc: string, opts?: { currentEpc?: string }) {
    await new Promise((r) => setTimeout(r, 400));
    const cur = opts?.currentEpc?.toUpperCase(); if (cur) this.pool = this.pool.filter((e) => e !== cur);
    this.pool.push(newEpc.toUpperCase());
  }
  async setPower(dbm: number) { this.power = Math.max(5, Math.min(30, dbm)); }

  /** Test helper: simulate the pistol trigger. */
  pressTrigger(pressed: boolean) { this.ev.emit('trigger', pressed); }
}
