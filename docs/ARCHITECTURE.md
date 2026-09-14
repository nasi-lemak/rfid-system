# RFID Platform — Architecture

## 1. Goal

One platform that can run *any* RFID-based tracking system — asset, inventory,
warehouse, linen/laundry, medical, evidence, library, retail, returnable
packaging, manufacturing WIP, rental, personnel, livestock, and so on — without
a separate codebase per vertical.

The insight is that every system in the requirements table is a combination of a
small number of **primitives** plus **configuration**:

| Primitive | What it is | Examples across verticals |
|---|---|---|
| **Tag** | A physical RFID identifier (EPC/TID/UID) of any technology (UHF Gen2, HF/NFC, LF, BLE, active) | Garment label, asset plate, animal ear tag, race bib, badge |
| **Item** | The thing a tag is attached to. Either *serialized* (one physical unit) or *quantity* (lots/SKU counts) | Laptop, sheet, pallet, keg, evidence bag, cow, medicine batch |
| **Item Type** | A configurable schema for items: custom attributes (JSON schema), lifecycle state machine, container/expiry/cycle/inspection flags | "Bed sheet" (tracks wash cycles), "Harness" (inspection every 6 months), "Surgical tray" (container) |
| **Location** | A node in a hierarchy: site → building → floor → room → zone → rack → shelf → bin. Mobile locations (vehicle, vessel, trailer) are locations too | Ward 3, Dock Door 2, Van 17, Locker A4, Fitting room 1 |
| **Party** | Someone or something that can hold custody: employee, customer, department, patient, supplier, vessel crew | Nurse J. Lee, Customer ACME, Dept. Radiology |
| **Device** | A reader: handheld, fixed portal, gate, smart cabinet/shelf/locker, tunnel. Antennas map to locations with In/Out direction | RFD40 handheld, FX9600 at dock door, cabinet in OR |
| **Operation** | A business transaction with lines (tag reads): Receive, Transfer, Issue, Return, Count, Dispatch, Inspect, Maintain, Dispose, Pack, Unpack, ProcessStage, Commission, Adjust | Checkout tool to J. Lee, Dispatch pallets to truck, Wash-stage linen |
| **Event** | Append-only history per item: Seen, Moved, CustodyChanged, StateChanged, Counted, Created, Retired… | Full chain of custody for evidence; wash history for linen |
| **Rule / Alert** | Event-driven rules: `when <event> and <conditions> → <action>` | Zone exit without checkout → alert; wash count ≥ 200 → retire; expiry < 30 days → alert |
| **Solution Template** | A JSON preset that provisions item types, lifecycles, location kinds, operations and rules for one vertical | "Hospital Linen", "Evidence Management", "Tool Crib", "Keg Management" |

`docs/SOLUTION-TEMPLATES.md` maps every row of the requirements table onto these
primitives.

## 2. System Landscape

```
┌───────────────────────────────┐    ┌──────────────────────────────┐
│  React Web (Vite + TS)        │    │  React Native Handheld (Expo)│
│  admin, dashboards, ops       │    │  scan / locate / stocktake / │
│  stocktakes, rules, templates │    │  operations / commission     │
└──────────────┬────────────────┘    │  offline queue + sync        │
               │ REST + SignalR      └──────────────┬───────────────┘
               ▼                                    │ REST (batched)
┌────────────────────────────────────────────────────▼───────────────┐
│  Rfid.Api (ASP.NET Core 8)                                          │
│  JWT auth · tenant scoping · Swagger · SignalR LiveHub              │
│  /api/ingest  ← fixed readers / edge agents / LLRP bridges / MQTT   │
├─────────────────────────────────────────────────────────────────────┤
│  Rfid.Application                                                   │
│  TagResolver · OperationProcessor · StocktakeService · RuleEngine   │
│  ReadIngestionService (zone presence, in/out portals)               │
│  TemplateProvisioner · Auth                                         │
├─────────────────────────────────────────────────────────────────────┤
│  Rfid.Domain    entities, lifecycle state machine, invariants        │
├─────────────────────────────────────────────────────────────────────┤
│  Rfid.Infrastructure   EF Core 8 + Npgsql, migrations, JSONB         │
└──────────────────────────────┬──────────────────────────────────────┘
                               ▼
                        PostgreSQL 16
```

### Backend (`src/backend`)
- **Rfid.Domain** – POCO entities and the `Lifecycle` state-machine value object. No framework references.
- **Rfid.Application** – services operating on `IAppDb` (an abstraction over the DbContext so services are testable with an in-memory provider).
- **Rfid.Infrastructure** – `AppDbContext`, Npgsql mappings (JSONB for attributes/lifecycles/rule conditions), migrations.
- **Rfid.Api** – controllers, JWT, SignalR `LiveHub` (broadcasts tag reads and alerts), Swagger, seed on startup.
- **Rfid.Tests** – xUnit tests over the application layer.

### Web (`src/web`)
Vite + React 18 + TypeScript, React Router, TanStack Query. No heavyweight UI
framework; a small design system in `src/ui`.

### Handheld (`src/mobile`)
Expo (React Native + TypeScript). The reader is abstracted behind `RfidReader`
(`start/stopInventory`, `locate`, `write`, events). Drivers: **Simulated**
(runs anywhere), **Zebra** (RFD40/RFD8500 via native module stub), **Chainway**
(C72/C66/C61 via native module stub), **BLE generic**. Reads are buffered
locally (SQLite) and synced in batches so the device works offline.

## 3. Key flows

### 3.1 Read → Item resolution
1. A reader produces `(epc, tid?, rssi, antenna, ts)`.
2. `TagResolver` finds `Tag` by EPC (falls back to TID) and its `Item`.
3. Unknown EPCs are stored as `TagRead` with `ItemId = null` so they can be
   commissioned later (or flagged as *unexpected*).

### 3.2 Handheld operation
`POST /api/operations` with `{type, fromLocationId?, toLocationId?, partyId?, lines:[{epc, qty?}]}`.
`OperationProcessor` validates, resolves tags, applies the effect of the
operation type (location/custody/state/quantity/container changes), writes one
`ItemEvent` per affected item, runs rules, and returns per-line results
(`Ok / Unknown / Unexpected / Rejected`).

### 3.3 Fixed reader / portal
`POST /api/ingest/reads` (batched) → `ReadIngestionService`:
- Antenna → Location (+ direction) mapping decides whether the item **entered**
  or **left** a zone.
- Item `LastSeen*` updated; `Seen` / `Moved` events emitted with debounce.
- Rules fire (e.g. *loss prevention*: item in state `OnFloor` seen at `Exit` →
  alert; *FOD/tool control*: tool seen leaving hangar while checked out → alert).

### 3.4 Stocktake
Create a `Stocktake` for a location (+ optional item type). Expected = items
whose current location is within that subtree. Handheld posts scanned EPCs in
batches; the service reconciles **found / missing / unexpected** and can
*apply* the result (move unexpected items in, mark missing as `Missing`).

### 3.5 Rules
`Rule { trigger: EventType, conditions: [{field, op, value}], action, params }`.
Fields address the event and item (`item.state`, `item.cycleCount`,
`item.expiryDate`, `toLocation.kind`, `event.data.direction` …). Actions:
`CreateAlert`, `SetState`, `Webhook`. Rules are evaluated synchronously in the
same unit of work so alerts are transactional with the event.

## 4. Multi-tenancy & security
- Every row carries `TenantId`; a global EF query filter scopes all reads.
- JWT bearer auth; roles `Admin`, `Operator`, `Viewer`, `Device`.
- Devices authenticate with a long-lived device token (role `Device`) limited to
  ingestion and operation endpoints.

## 5. Extending to a new vertical
1. Add a `SolutionTemplate` JSON (item types + lifecycles + rules).
2. (Optional) add a new `OperationType` if none of the existing ones fit — most
   verticals need none.
3. (Optional) add a mobile "workflow" screen that pre-fills an operation.

## 6. Background services & integration (v1.1)

| Component | Role |
|---|---|
| `IngestAdapters` + `VendorIngestController` | Normalise Impinj IoT Interface / Zebra IoT Connector / generic JSON into `ReadBatchRequest` |
| `MqttIngestService` | Subscribes to the broker, matches topics to devices (`Config.mqttTopic`), feeds the same pipeline |
| `PresenceService` + `PresenceSweeperService` | Zone sessions (enter/refresh/exit with dwell timeout), occupancy, muster, checkpoint timing |
| `IntegrationService` + `IntegrationDispatcherService` | Cursor-based signed delivery of events/alerts to external endpoints with back-off |
| `StocktakeScheduleService` + `StocktakeSchedulerService` | Opens scheduled stocktakes, auto-reconciles after a window |
| `ReportService` | 12 cross-vertical reports (JSON/CSV) incl. depreciation |
| `LabelService` + `RawPrinterClient` | ZPL rendering with RFID encode; raw 9100 printing |
| `Gs1` | GRAI-96 / GIAI-96 encoders next to SGTIN-96 |
| `TemplateProvisioner.ExportAsync/ApplyDefinitionAsync` | Per-tenant template export/import |

Background jobs run per tenant inside an ambient scope (`AmbientContext`), so the same tenant-filtered
`DbContext` and services are used in requests and jobs alike.

### v1.2 additions

| Component | Role |
|---|---|
| `Llrp/LlrpCodec`, `Llrp/LlrpClient`, `LlrpReaderService` | LLRP 1.0.1 binary codec (messages, TLV/TV parameters), inventory client, per-device supervisor |
| `Positioning/Trilateration`, `PositionService` | RSSI → distance, least-squares x/y, floor-plan queries; hooked into ingestion |
| `Integrations/PayloadFormatters` | Generic / SAP / Dynamics 365 / Maximo payload shapes; `IOAuthTokenProvider` for OAuth2 |
| `Labels/LabelDesign`, `LabelCompiler` | Declarative label document → ZPL template |
| Mobile `reader/Printer.ts` | Network (server) or Bluetooth (native module) label printing |

### v1.3 additions

| Component | Role |
|---|---|
| `LlrpConfigCodec` + client options | GET_READER_CAPABILITIES, per-antenna power/session config, GPI-triggered ROSpec, GPO writes, GPI events; `LlrpReaderService` exposes status and GPO control |
| `PositionSmoother` / `KalmanTrack`, `ReadRequest.RangeM` | Range-aware trilateration and per-item Kalman smoothing |
| `ImportService` | CSV/JSON asset-master upsert with dry run and reconciliation |
| `PrintQueueService` + `PrintQueueWorkerService`, `PrintJob` | Durable print jobs, retries, reprint audit, label stock alerts |

## 7. Roadmap
- Multi-node deployment: distributed LLRP/print workers and shared position tracks (Redis).
- RTLS heat maps and path replay from position history.
- SSO (OIDC) and per-site role-based access.
- Offline floor plans on the handheld.
