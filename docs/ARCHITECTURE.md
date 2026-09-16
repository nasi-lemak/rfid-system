# RFID Platform — Architecture

This describes the system as built (v2.0, after the consolidation recorded in
[`ARCHITECTURE-REVIEW.md`](ARCHITECTURE-REVIEW.md)). `DATA-MODEL.md` lists tables,
`INTEGRATION.md` the external contracts, `SOLUTION-TEMPLATES.md` the vertical mapping.

## 1. Goal and shape

One platform runs every RFID-based tracking system in the requirements table — asset,
inventory, warehouse, linen, medical, evidence, library, retail, returnable packaging,
manufacturing WIP, rental, personnel, livestock, … — without a codebase per vertical.

It is a **modular monolith**: one deployable (`Rfid.Api`) that can run as any number of
identical nodes over one PostgreSQL, with module boundaries inside the code base rather than
between services. Verticals are expressed as **configuration over a small, closed set of
primitives**; the platform never executes tenant-supplied code.

## 2. Primitives

| Primitive | What it is | Examples |
|---|---|---|
| **Tag** | A physical identifier: UHF Gen2 EPC/TID, HF/NFC, LF, BLE, active, or a barcode bound as a tag | Garment label, asset plate, ear tag, race bib, badge |
| **Item** | The thing a tag is attached to — *serialised* (one unit) or *quantity* (lot/SKU count). Items may nest (`ParentItemId`) to model containers | Laptop, sheet, pallet, keg, evidence bag, cow, medicine lot |
| **Item Type** | Schema for items: attribute definitions, flags (container, expiry, cycles, inspection), and the **lifecycle** state machine (`states`, `transitions[{from,to,on,incrementCycle}]`) | "Bed sheet" (wash cycles), "Harness" (6-monthly inspection), "Instrument tray" (container) |
| **Location** | Node in a tree with a materialised `Path` for subtree queries; kinds from site to bin; mobile locations (vehicle, vessel) | Ward 3, Dock 2, Van 17, Locker A4 |
| **Party** | Anyone who can hold custody | Nurse, customer, department, supplier |
| **Device / Antenna** | Readers and printers; antennas map to a location with an `In`/`Out`/`None` direction | Handheld, portal, gate, cabinet, tunnel, ZPL printer |
| **Operation** | A business transaction with lines. Its behaviour is an **Operation Definition**: a named composition of built-in **effects** with requirements (§5) | Receive, Issue, Sterilise, Calibrate |
| **Event** | Append-only history per item (`item_events`): the chain of custody | Moved, CustodyChanged, StateChanged, Counted, Packed … |
| **Rule / Alert** | `when <event type> and <conditions> → <action>` with a fixed field vocabulary and a closed action set | Zone exit while checked out → critical alert |
| **Workflow** | An ordered list of operation definitions with prompts and per-step inputs; every step runs as an ordinary operation stamped with a run id, so progress and audit are the operations themselves and offline execution uses the same queue | CSSD reprocessing, wash cycle, tool return & check |
| **Solution Template** | JSON preset that provisions item types, lifecycles, rules, **operation definitions** and **workflows** for a vertical | Hospital Linen, Evidence, Tool Crib, Medical Assets |

Everything else in the platform (presence, RTLS, billing, EPCIS, anomaly detection, …) is a
module that reads or writes these primitives.

## 3. Modules and ownership

```
Rfid.Domain          entities, enums, lifecycle value object            (no framework refs)
Rfid.Protocols       reader wire formats + the ingest contract           (LLRP codec/client, vendor adapters, ReadBatchRequest)
Rfid.Application     modules below, over IAppDb + ICurrentContext       (testable with InMemory EF)
Rfid.Infrastructure  AppDbContext (Npgsql, JSONB), migrations, RLS, tenant connection interceptor
Rfid.Api             controllers, auth (JWT + OIDC), SignalR hub, background hosts, transports
Rfid.Edge            on-site reader agent: LLRP/pushes → durable queue → store-and-forward   (references Protocols only)
```

`Rfid.Application` is organised by module. Namespaces are stable (`Rfid.Application.Services`
for historical services); **folders declare ownership**:

| Folder | Owns | Key types |
|---|---|---|
| `Contracts/` | Abstractions and DTOs shared by all modules | `IAppDb`, `ICurrentContext`, `ILivePublisher`, `OperationRequest`, `ReadBatchRequest` |
| `Tracking/` | Reads → item state → events; presence | `TagResolver`, `ReadIngestionService`, `IngestAdapters`, `PresenceService` |
| `Rtls/` | Positions from RSSI/ranges, smoothing, history | `PositionService`, `Trilateration`, `PositionSmoother` |
| `Operations/` | Operation definitions and execution, workflows, containers, stocktakes, imports, rule-requested operations | `OperationCatalog`, `OperationDefinitions`, `OperationProcessor`, `WorkflowCatalog`/`WorkflowService`, `ContainerRules`, `RunOperationOutboxHandler`/`IOperationRunner`, `StocktakeService`, `StocktakeScheduleService`, `ImportService` |
| `Rules/` | Event and schedule rules, alerts, notification routing, anomaly detection | `RuleEngine` (`EvaluateAsync`, `EvaluateScheduledAsync`), `NotificationService`, `AnomalyService` |
| `Devices/` | Reader health, firmware, edge configuration (protocol clients live in `Rfid.Protocols`) | `DeviceHealthService`, `LlrpDeviceConfig`, `EdgeConfigService` |
| `Encoding/` | GS1 encoding, serial pools, labels, print queue | `EncodingService`, `LabelService`, `LabelDesign`, `PrintQueueService` |
| `Integrations/` | Outbound ERP/BI delivery (via the outbox), EPCIS, warehouse export | `IntegrationService`, `IntegrationOutboxHandler`, `PayloadFormatters`, `EpcisService`, `WarehouseExportService` |
| `Inventory/` | Warehouse view over quantity items: balances, replenishment, movements, FEFO allocation (no separate ledger) | `StockService` |
| `Billing/` | Custody events → ledger → invoices | `BillingService` |
| `Analytics/` | Dashboards (widgets + overview), reports, analytics, maintenance forecasting — all over `SiteScope` | `DashboardService`, `ReportService`, `AnalyticsService`, `MaintenanceService` |
| `Geo/` | GPS fixes and geofences | `GeoService` |
| `Platform/` | Cross-cutting infrastructure | `Outbox`, `OutboxDispatcher` + `IOutboxHandler`, `LeaseService`, `RetentionService` |
| `Security/` | Site-level access, SSO mapping, hashing | `ISiteAccess`/`SiteAccess`, `SsoUserMapper`, `PasswordHasher` |
| `Templates/` | Vertical catalogue, provisioning, signed exchange packages | `SolutionTemplateCatalog`, `TemplateProvisioner`, `TemplatePackageService` |

Dependency direction: `Tracking`, `Operations` and `Rules` are the core and depend only on
`Contracts`/`Platform`. Every other folder depends on the core, never the reverse.

```
┌──────────────────────────┐   ┌────────────────────────────┐   ┌──────────────────────────┐
│ React web (Vite + TS)    │   │ React Native handheld      │   │ Fixed readers / agents   │
│ REST + SignalR           │   │ REST, offline queue        │   │ REST batches, MQTT, LLRP │
└────────────┬─────────────┘   └─────────────┬──────────────┘   └────────────┬─────────────┘
             └───────────────────────────────┼───────────────────────────────┘
                                             ▼
                     Rfid.Api  ── controllers · auth · LiveHub · background hosts
                                             ▼
                     Rfid.Application  ── Tracking · Operations · Rules ── modules
                                             ▼
                     Rfid.Infrastructure  ── EF Core / Npgsql · RLS · outbox nudge
                                             ▼
                          PostgreSQL 16  (state · events · outbox · leases)      Redis (optional SignalR backplane)
```

## 4. The unit of work and the outbox

Every request or job does its work in one `DbContext` and commits with **one `SaveChanges`**;
that call is the transaction boundary. Nothing may leave the process before it:

- **Live pushes** (SignalR `event`/`alert`), **webhooks** (rule action and integrations'
  single-shot sends) and **notifications** (e-mail/SMS/Teams/Slack) are written as rows in
  `outbox` inside the same `SaveChanges` as the business change (`Platform/Outbox.cs`).
- `OutboxDispatcher` (one node, lease `job:outbox`) delivers committed rows oldest-first with
  exponential back-off (5 s·2ⁱ, capped at 1 h) and dead-letters after 8 attempts, keeping the
  error for inspection. A `SaveChanges` interceptor nudges it in-process, so latency is
  milliseconds on the committing node and ≤ 2 s elsewhere.
- Delivery is by **kind → handler** (`IOutboxHandler`): `live.event`/`live.alert` (SignalR
  transport), `webhook` (HTTP), `notification` (channel senders), `integration` (ERP batch for one
  endpoint, cursor advanced on success) and `operation.run` (an operation a rule asked for, executed
  in a fresh tenant scope). Modules register their own handlers; the dispatcher only knows the retry
  policy. Application code never sends.
- **Raw read fan-out** (`reads` on the hub) is the one direct send, and it happens *after*
  commit: it is telemetry, not state.

Result: clients, ERPs and people can only ever observe committed state; all retries follow one
policy; tests can assert "nothing was sent" before dispatch.

## 5. Operations

An **operation definition** is data:

```
OperationDefinition {
  code            "Sterilise"                     // key for history, lifecycles and the API
  baseType        ProcessStage                    // built-in family (compatibility, defaults)
  eventType       StateChanged                    // event written per affected item
  requires        { toLocation, party, container, targetState, quantity, fromStates[] }
  effects         [ SetState, Move{optional}, SetAttribute{key:lastSterilisedAt, value:"{now}"} ]
  eventData       { bizStep: "..." }              // merged into every event's Data
  itemTypeCodes   [ "TRAY", "INSTRUMENT" ]        // empty = any type
}
```

The **effect vocabulary is closed** (`OperationEffectKinds`): `Move`, `SetCustodian`,
`ClearCustodian`, `SetDueBack`, `ClearDueBack`, `SetState`, `IncrementCycle`, `RecordSeen`,
`RecordInspection`, `Activate`, `Dispose`, `Pack`, `Unpack`, `AdjustQuantity`, `SetAttribute`,
`MoveQuantity` (quantity items: the line quantity leaves this lot row and lands on the matching lot
row at the destination, created if absent — the split/merge primitive a warehouse needs).
Each effect is implemented once in `OperationProcessor.RunEffectAsync` and tested once.
Effects flagged `optional` are skipped when their input (destination, party) is absent instead
of failing. `SetAttribute` values may use the placeholders `{now}`, `{user}`, `{party}`,
`{location}`.

The **14 built-in operations are definitions too** (`OperationCatalog.BuiltIn`); there is one
execution path. Templates carry `OperationDefinitions` (installed per tenant when the template
is applied, and synced into already-installed tenants on startup), and administrators can add
their own through `/api/operations/definitions`. Definitions are validated (known effects, a
non-Commission base type, requirements consistent with effects, built-in codes reserved).

**Execution** (`OperationProcessor.ProcessAsync`):
1. Resolve the definition (`request.operation` code, else `request.type`); replay detection by
   `clientId` returns the stored result.
2. Validate request-level requirements; reject a disposed container.
3. For each line: resolve the tag (EPC, TID, barcode or identifier via `TagResolver`); check
   `itemTypeCodes` / `fromStates`; **lifecycle step** — the item type's transition keyed by the
   operation *code* (custom codes fall back to the base type's transitions); run effects in
   order; write one event (plus `StateChanged` when the state moved and the main event is
   something else). Container moves recurse through `MoveAsync`, and every child move is an
   event in its own right (rules fire, outbox written, `OperationId` set).
4. One `SaveChanges`.

**Containers** (`ContainerRules`): nesting is a forest via `ParentItemId`, depth ≤ 32; packing
into self or a descendant is refused at write time (Pack effect and manual edits alike);
disposed containers cannot receive items; moving a container moves the subtree.

**Lifecycles** are enforced everywhere state is written: operations, `RuleAction.SetState` and
manual edits all refuse states the type does not know.

**Workflows** (`WorkflowDefinition { code, steps[{ key, title, prompt, operation, ask[], fixed{}, rescan, optional, onRejected }] }`)
are guided multi-step tasks. There is no workflow engine: the handheld runs each step as an
operation carrying `workflowRunId`, `workflowCode` and `workflowStep` (columns on `operations`),
with `clientId = run:step` for idempotency. Progress, completion and audit are derived from the
operations (`WorkflowService.RunsAsync`), so a run survives offline gaps through the ordinary
queue. Templates ship workflows (validated against the operations they reference); tenants edit
them and pin step inputs (`fixed`) to their own locations, parties and states.

## 6. Tracking pipeline

```
reads  ──►  TagRead (raw, per read)  ──►  item.LastSeen*  ──►  Seen / Moved events (debounced)  ──►  rules  ──►  outbox
                 │                              │
                 └──► PositionService (x/y)      └──► PresenceService (zone sessions, enter/exit, dwell)
```

- `ReadBatchRequest { deviceId?, sessionId?, batchId?, reads[] }` from handhelds, vendor
  adapters (Impinj, Zebra, generic JSON), MQTT and the LLRP client all enter
  `ReadIngestionService.IngestAsync`.
- Antenna → location (+ direction). `In` moves the item into the zone, `Out` moves it to the
  zone's parent (`data.direction = "Out"`), `None` uses best-RSSI zone resolution with a
  per-item debounce window. Presence exits (dwell timeout) emit the same `Moved{direction:
  "Out"}` shape, so one rule vocabulary covers portals and presence.
- `batchId` makes a batch idempotent: the key is committed with the reads and a replay is
  acknowledged (`duplicate: true`) without re-emitting events.
- **Positions from outside** (`POST /api/ingest/positions`): vendor RTLS engines (UWB TDoA, BLE
  AoA, vision) or an edge agent post x/y per item inside a floor-plan location. The platform stores
  fixes and updates the item exactly as its own trilateration does, idempotent by `batchId`, and a
  late fix never moves a position backwards. Solvers stay with the vendor.
- Unknown EPCs are stored (`ItemId = null`) for later commissioning.

- **Late reads.** `readAt` is authoritative. A read older than the item's `LastSeenAt` (a batch
  replayed after newer ones) is stored as raw history and counted as `late`, but never moves state
  or emits events — so store-and-forward can deliver out of order without regressing locations.

### Server vs edge
The server owns interpretation (location, state, events, rules). Protocol handling is
transport and lives in `Rfid.Protocols` (LLRP codec/client, Impinj/Zebra/generic adapters, the
ingest contract), shared by two hosts:

- the **API** (`LlrpReaderService`, `MqttIngestService`, vendor ingest endpoints) for sites with a
  reliable link — one connection owner per reader, coordinated by leases;
- the **edge agent** (`Rfid.Edge`, see `EDGE-AGENT.md`) for sites that must survive WAN loss: it
  drives readers locally, queues batches on disk and forwards them with `batchId = agent:seq`.
  Readers marked `edgeManaged` are skipped by the server's supervisor; readers assigned to a
  gateway (`edgeGatewayId`) are pulled by that agent from `GET /api/edge/config` as a versioned
  desired configuration (`EdgeConfigService`), so reader settings are managed centrally.

Nothing downstream depends on where the reads came from.

## 7. Rules and notifications

`Rule { kind: Event | Schedule, trigger: EventType, intervalMinutes, conditions[{field, op, value}], action, params, severity }`.

- **Event rules** run synchronously inside the unit of work that produced the event. Fields address
  the event, item, item type, locations, party and `data.*`.
- **Schedule rules** are swept once a minute per tenant (`RuleSchedulerService`, lease `job:rulescheduler`)
  and evaluate the same conditions against every live item — with time-derived fields such as
  `item.hoursSinceSeen`, `item.daysUntilInspection`, `item.cyclesRemaining`, `item.daysOverdue`.
  Alert actions are de-duplicated per (rule, item) while an alert stays open, so a sweep never storms.
- **Actions** (closed set): `CreateAlert`, `Notify`, `SetState` (lifecycle-validated), `Webhook`
  and `RunOperation` — any operation definition (`Dispose` at max cycles, `Maintain` after a failed
  inspection). `RunOperation` is written to the outbox and executed **after** the triggering unit of
  work committed, in its own tenant scope, idempotent per message; it never re-enters the processor
  mid-operation.

Alerts are rows; alert fan-out, webhooks and notifications go through the outbox. Notification
channels, catch-all routing and escalation policies live in `Rules/`.

## 8. Multi-tenancy and security

Two independent layers isolate tenants:

1. **Application** — every tenant-scoped entity derives from `TenantEntity`; `AppDbContext`
   applies a global query filter on `ICurrentContext.TenantId` and stamps inserts. Requests get
   the tenant from the JWT; background jobs run per tenant inside `AmbientContext.Use(tenantId)`.
   `IgnoreQueryFilters()` is used only for pre-authentication lookups, cluster-wide loops and the
   outbox dispatcher.
2. **Database** — every table with a `TenantId` has Postgres **row-level security** with the
   `tenant_isolation` policy over `current_setting('app.tenant_id')`. `TenantConnectionInterceptor`
   sets it on every opened connection; a context without a tenant runs in the explicit
   **system scope** `'*'`. Coverage is derived from the EF model (`RowLevelSecurity.TenantTables`),
   re-applied at startup, and asserted by a test for every `TenantEntity`. The API connects as
   the non-owner role `rfid_app` (owners bypass RLS); migrations run as the owner
   (`ConnectionStrings:Migrations`). Tables without `TenantId` (`tenants`, `solution_templates`,
   `worker_leases`) are global; `operation_lines` and `stocktake_lines` carry `TenantId` too, so RLS
   covers every table that holds tenant data.

**Authentication**: JWT for users (`Admin`/`Operator`/`Viewer`), device tokens for readers
(`Device`), OIDC as a second bearer scheme with JIT provisioning. Login takes an optional
`tenantCode` and refuses an e-mail that exists in more than one tenant.

**Site RBAC** (`ISiteAccess`): users may be restricted to site subtrees with a role per site;
items, locations, devices, alerts, events, raw reads, presence (zones, muster, timing), positions
(floor plans, heat maps, history) and operation/stocktake writes (single and batch) are
filtered/enforced by `Path`. Aggregates use the same rule through one helper, `SiteScope`
(`Security/SiteScope.cs`): reports, analytics, dashboard widgets, the overview summary and reader
uptime all take a scope and see only locations inside the principal's sites, items there, and
events/alerts/reads/operations/sessions touching them.

**Portals**: party-scoped read-only principals see only `/api/portal/*`.

**Audit**: `AuditMiddleware` records every mutating call (route, entity, status, redacted body).
**Retention**: per-dataset policies with defaults; `item_events` are kept unless a policy says
otherwise.

## 9. Idempotency and offline

| Channel | Key | Where stored | Behaviour on replay |
|---|---|---|---|
| Operations (single or `/batch`) | `clientId` | `operations.ClientId` (unique per tenant) | Original result returned |
| Read batches | `batchId` (+ device) | `idempotency_keys` (scope `read-batch`), same commit | Original counts returned, `duplicate: true` |
| Integrations | per-endpoint cursor + one outbox message in flight | `integration_endpoints.EventCursor/InFlightMessageId` | At-least-once, ordered per endpoint; cursor moves only on success |
| Rule-requested operations | outbox message id | `operations.ClientId = rule:{rule}:{message}` | Exactly once per message |
| Billing | watermark | `billing_cursors` | Exactly-once accrual |

The handheld queue (`src/mobile/src/store/queue.ts`) stamps `clientId` before the first send,
flushes FIFO in batches of 50, matches results by `clientId`, backs off exponentially per item
with a hard cap, and keeps rejected operations visible.

## 10. Background work and scale-out

All nodes are identical. Work that must run once cluster-wide takes a **database lease**
(`worker_leases`, TTL 3× interval): tenant loops (`job:*` — presence sweep, schedules, print
queue, monitoring, retention, anomaly detection, billing, maintenance, warehouse export,
**outbox**), the MQTT subscriber, and each LLRP reader connection (`llrp:{deviceId}`). Position
tracks live on the item row; SignalR uses a Redis backplane when configured. Losing a node loses
nothing: leases expire and another node continues.

## 11. Data growth

High-volume tables: `tag_reads`, `item_events`, `position_fixes`, `gps_fixes`,
`device_heartbeats`, `outbox`. Reads carry a BRIN index on `ReadAt`; events index
`(ItemId, OccurredAt DESC)`. Retention policies prune everything except events by default;
`outbox` rows are pruned once processed. Native range partitioning is the next step when a
tenant exceeds ~10⁸ rows in a table and needs no code change. Heavy analytics belong in the
customer's warehouse via the Parquet export; in-app analytics stay operational.

## 12. Clients

- **Web** (`src/web`, Vite + React + TS, TanStack Query): every screen is a view over the
  primitives. The operation form derives its inputs from the operation definition
  (`requires` + `effects`), so template-defined operations need no UI change.
- **Handheld** (`src/mobile`, Expo): reader abstraction (`RfidReader`) over Zebra/Chainway/BLE/
  simulated drivers, camera barcodes, offline cache of master data, floor plans and operation
  definitions, store-and-forward queue.

## 13. Adding a vertical

1. Add a `SolutionTemplate`: item types with lifecycles, rules, and **operation definitions**
   composed from the effect vocabulary (see `medical-assets`: `Decontaminate`, `Sterilise`;
   `tool-tracking`: `Calibrate`).
2. Key lifecycle transitions by your operation codes.
3. Optionally a demo scenario.

No C# is needed. If a vertical needs an effect the vocabulary lacks, that is a platform
change: add the effect once, with a test, and every vertical can use it.

## 14. Testing

`Rfid.Tests` runs the real services over the real EF model with the InMemory provider (99
tests). `ConsolidationTests` pins the architectural invariants: outbox-before-delivery, replay
idempotency, container cycles and subtree moves, definition-composed operations, lifecycle
validation, direction vocabulary, and RLS coverage of every tenant entity. `EdgeAgentTests` cover
the durable queue, ordered forwarding, poison handling and late-read safety; `FoundationTests`
cover schedule rules, `RunOperation` through the outbox, integration filters and EPCIS vocabulary
from definitions.
