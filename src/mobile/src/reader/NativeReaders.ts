import { NativeEventEmitter, NativeModules, Platform } from 'react-native';
import { BaseReader, type InventoryOptions, type ReaderInfo, type ReaderKind } from './types';

/**
 * Thin JS bindings over vendor SDK native modules. Each vendor module is expected to expose the
 * same small surface (see docs/HANDHELD-SDK.md) so the JS side stays identical:
 *
 *   connect(): Promise<{model, serial, battery, firmware}>
 *   disconnect(), startInventory(opts), stopInventory(), startLocate(epc), stopLocate()
 *   writeEpc(newEpc, currentEpc?, accessPassword?), setPower(dbm)
 *   events: "tag" {epc,tid,rssi,antenna}, "locate" {proximity,rssi}, "status" {status}, "trigger" {pressed}
 *
 * Android native implementations:
 *   - Zebra RFD40 / RFD8500 / MC33xR: com.zebra.rfid.api3 (RFID SDK for Android)
 *   - Chainway C72 / C66 / C61 / R6: com.rscja.deviceapi.RFIDWithUHFUHF
 *   - Generic BLE sleds (TSL 1128, Nordic ID): react-native-ble-plx + ASCII protocol
 * When the module is not linked (Expo Go, iOS simulator) the driver reports 'error' so the app can
 * fall back to the simulated reader.
 */
class NativeModuleReader extends BaseReader {
  private mod: any;
  private emitter: NativeEventEmitter | null = null;
  private subs: { remove(): void }[] = [];

  constructor(readonly kind: ReaderKind, private moduleName: string) {
    super();
    this.mod = (NativeModules as Record<string, any>)[moduleName];
  }

  get available() { return !!this.mod; }

  async connect(): Promise<ReaderInfo> {
    if (!this.mod) { this.setStatus('error'); throw new Error(`${this.moduleName} native module is not linked on ${Platform.OS}. Build a dev client with the vendor SDK or use the simulated reader.`); }
    this.setStatus('connecting');
    this.emitter = new NativeEventEmitter(this.mod);
    this.subs.push(this.emitter.addListener('tag', (e) => this.ev.emit('tag', { epc: String(e.epc).toUpperCase(), tid: e.tid, rssi: e.rssi, antenna: e.antenna, at: Date.now() })));
    this.subs.push(this.emitter.addListener('locate', (e) => this.ev.emit('locate', Number(e.proximity), e.rssi)));
    this.subs.push(this.emitter.addListener('status', (e) => this.setStatus(e.status)));
    this.subs.push(this.emitter.addListener('trigger', (e) => this.ev.emit('trigger', !!e.pressed)));
    const info = await this.mod.connect();
    this.setStatus('ready');
    return { kind: this.kind, ...info };
  }
  async disconnect() { this.subs.forEach((s) => s.remove()); this.subs = []; await this.mod?.disconnect?.(); this.setStatus('disconnected'); }
  async startInventory(opts?: InventoryOptions) { await this.mod.startInventory(opts ?? {}); this.setStatus('inventory'); }
  async stopInventory() { await this.mod.stopInventory(); this.setStatus('ready'); }
  async startLocate(epc: string) { await this.mod.startLocate(epc); this.setStatus('locating'); }
  async stopLocate() { await this.mod.stopLocate(); this.setStatus('ready'); }
  async writeEpc(newEpc: string, opts?: { currentEpc?: string; tid?: string; accessPassword?: string }) { await this.mod.writeEpc(newEpc, opts?.currentEpc ?? null, opts?.accessPassword ?? '00000000'); }
  async setPower(dbm: number) { await this.mod.setPower(dbm); }
}

export class ZebraReader extends NativeModuleReader { constructor() { super('zebra', 'ZebraRfid'); } }
export class ChainwayReader extends NativeModuleReader { constructor() { super('chainway', 'ChainwayUhf'); } }
export class BleReader extends NativeModuleReader { constructor() { super('ble', 'BleRfid'); } }
