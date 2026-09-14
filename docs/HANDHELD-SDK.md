# Handheld RFID: reader drivers and vendor SDK integration

The Expo app talks to hardware through one interface, `RfidReader`
(`src/mobile/src/reader/types.ts`). Screens never import a vendor SDK.

| Driver | Class | Hardware | How it is wired |
|---|---|---|---|
| `simulated` | `SimulatedReader` | none | Pure JS. Seed it with the server's tags from **Settings → Seed simulator**. Used for demos, Expo Go and tests. |
| `zebra` | `ZebraReader` | RFD40, RFD8500, MC33xR, TC5x+RFD | Native module `ZebraRfid` built on the *Zebra RFID SDK for Android* (`com.zebra.rfid.api3`). |
| `chainway` | `ChainwayReader` | C72, C66, C61, R6 | Native module `ChainwayUhf` built on `com.rscja.deviceapi.RFIDWithUHFUHF`. |
| `ble` | `BleReader` | TSL 1128/1166, Nordic ID, generic BLE sleds | Native module `BleRfid` (react-native-ble-plx + vendor ASCII protocol). |

## Native module contract

All vendor modules expose the same surface so the JS driver (`NativeModuleReader`) is shared:

```ts
connect(): Promise<{ model: string; serial: string; battery?: number; firmware?: string }>
disconnect(): Promise<void>
startInventory(opts: { power?: number; session?: number; filterEpcPrefix?: string }): Promise<void>
stopInventory(): Promise<void>
startLocate(epc: string): Promise<void>       // emits "locate" { proximity: 0..100, rssi }
stopLocate(): Promise<void>
writeEpc(newEpc: string, currentEpc: string | null, accessPassword: string): Promise<void>
setPower(dbm: number): Promise<void>
// events via NativeEventEmitter
"tag"     { epc, tid?, rssi?, antenna? }
"locate"  { proximity, rssi? }
"status"  { status: 'ready' | 'inventory' | 'locating' | 'error' | 'disconnected' }
"trigger" { pressed: boolean }
```

## Building a dev client

Vendor SDKs are Android `.aar` libraries and cannot run in Expo Go. Create an Expo dev client:

```bash
cd src/mobile
npx expo prebuild --platform android
# drop the vendor .aar into android/app/libs and add the native module under android/app/src/main/java/...
npx expo run:android
```

A minimal Zebra module skeleton (Kotlin):

```kotlin
class ZebraRfidModule(ctx: ReactApplicationContext) : ReactContextBaseJavaModule(ctx) {
  override fun getName() = "ZebraRfid"
  private var reader: RFIDReader? = null
  @ReactMethod fun connect(promise: Promise) { /* Readers(ctx, ENUM_TRANSPORT.ALL).GetAvailableRFIDReaderList() → connect → promise.resolve(info) */ }
  @ReactMethod fun startInventory(opts: ReadableMap, promise: Promise) { reader?.Actions?.Inventory?.perform(); promise.resolve(null) }
  @ReactMethod fun stopInventory(promise: Promise) { reader?.Actions?.Inventory?.stop(); promise.resolve(null) }
  // eventReadNotify → emit "tag" { epc = tagData.tagID, rssi = tagData.peakRSSI }
  // eventStatusNotify HANDHELD_TRIGGER_PRESSED/RELEASED → emit "trigger"
  // Locate: reader.Actions.TagLocationing.Perform(epc) → tagData.LocationDistance.relativeDistance → "locate"
  // Write: TagAccess.WriteAccessParams(memoryBank EPC, offset 2, data newEpc) → reader.Actions.TagAccess.writeWait(currentEpc, params, null)
}
```

## Offline behaviour

Operations run through `runOrQueue()`: online → executed immediately; network failure → stored in
AsyncStorage with a `clientId` and replayed via `POST /api/operations/batch` (idempotent on the server).
Business rejections (4xx) are never queued — the operator sees them immediately.

## Fixed readers

Fixed readers, portals, smart cabinets and shelves do not use the app: an edge agent (LLRP, Zebra
IoT Connector, Impinj IoT interface, MQTT bridge) posts `{deviceId, reads:[{epc, antennaPort, rssi, readAt}]}`
to `POST /api/ingest/reads` using the device JWT from `POST /api/auth/device`.
