# Architecture review and consolidation (v2.0)

This document records the first-principles review of the platform after seven roadmap
releases (v1.1 – v1.7), the decisions taken, what was changed, what was deliberately left
alone, and a re-assessment of the roadmap. `ARCHITECTURE.md` describes the system as it is
now; this document explains *why* it is that way.

Scope of the review: every backend project, the web and handheld clients, the solution
templates, the test suite and the docs. Method: read the code paths end to end for the
questions listed in section 3, trace where side effects happen relative to commits, count
where abstractions had grown special cases, and check which vertical behaviours were still
expressed in C# instead of configuration.

---

## 1. Verdict in one paragraph

The core model is right and should be kept: a handful of primitives (Tag → Item → Item Type
with a lifecycle → Location tree → Party → Device/Antenna → Operation → Event → Rule) plus
templates really do cover the ~90 verticals, and every roadmap module was built *on* those
primitives rather than beside them. Growth exposed three structural weaknesses, all fixed in
this release: **side effects were not transactional** (SignalR, webhooks, notifications and
printer sends happened before `SaveChanges`), **vertical behaviour was leaking into C#**
(operation semantics were a `switch` over an enum, so a new vertical could still need code), and
**tenant isolation had one line of defence** (EF query filters only). A fourth set of issues
was accumulated sprawl: four EPC encode/decode API surfaces, four HTTP senders, version-numbered
file names, inconsistent event vocabulary ("Exit" vs "Out"), and validation bypasses (rules and
manual edits could write states the lifecycle did not know). Everything else (RTLS, billing,
EPCIS, portals, anomaly detection, …) is sound as an *optional module* and did not need to
change.

---

## 2. Strengths to keep

| Strength | Why it matters |
|---|---|
| **Small primitive set, templates as configuration.** 23 templates, 0 vertical-specific entities. | This is the reason one codebase covers the whole requirements table. Nothing in this review argues for a second model. |
| **Lifecycle as data on the item type** (`states`, `transitions[{from,to,on,incrementCycle}]`). | Verticals differ mostly in state machines; keeping them JSON means new flows are configuration. |
| **Append-only `item_events` as chain of custody**, one event per affected item per operation, including container children. | Evidence, pharma, aviation and billing all depend on this being complete and immutable. Billing and EPCIS are pure projections of it. |
| **Raw reads (`tag_reads`) kept separate from interpreted state (`items`, `presence_sessions`) and history (`item_events`).** | Allows high-volume ingestion to stay cheap and lets retention differ per dataset. |
| **Materialised `Path` on locations** for subtree queries; site RBAC and stocktakes are built on it. | Cheap subtree filters everywhere with one index. |
| **Rules as `trigger + conditions → action`** with a fixed field vocabulary and a closed action set. | Expressive enough for every template rule so far, without a scripting engine. |
| **Modular monolith with lease-coordinated background work.** | Scale-out without a second deployable; readers/jobs fail over within one interval. |
| **The same services run in requests, jobs and the demo seeder** (ambient tenant context). | One code path to test; demo scenarios exercise the real pipeline. |
| **Handheld offline queue keyed by `clientId`.** | The right shape for store-and-forward; it only needed hardening (see 4.9). |
| **Test host over the real EF model** (InMemory provider, real services). | 99 tests run in ~10 s and cover behaviour, not mocks. |

---

## 3. Weaknesses found

Grouped by the questions the review set out to answer. Severity: **A** = fix now (correctness,
isolation, or blocks growth), **B** = fix now because cheap and prevents sprawl, **C** = can wait.

### 3.1 Transaction boundaries and external side effects — **A**
- No explicit transactions; each service relied on one `SaveChanges` per request, which is fine,
  but **every external send happened before that save**: `OperationProcessor` (live events),
  `ReadIngestionService`, `PresenceService`, `RuleEngine` (alerts, webhooks, notifications),
  `GeoService`, `DeviceHealthService`, `AnomalyService`, `MaintenanceService`, `PrintQueueService`.
  A failed save left clients, ERPs and people notified about state that never existed.
- Multi-commit requests (`/api/operations/batch`, stocktake scheduling, encoding + print queue)
  are acceptable because each commit is a complete unit, but they must be idempotent to be
  retried — several were not.

### 3.2 Configurability of operations — **A**
- Operation semantics lived in a `switch (OperationType)` inside `OperationProcessor`. Templates
  could list which built-in operations a vertical uses but could not *define* one. "Sterilise",
  "Calibrate", "Decontaminate" had to be expressed as `ProcessStage` + a free-text target state,
  which loses meaning in history, rules and reports.
- `TemplateProvisioner` installed only item types and rules; `LocationKinds`, `Operations`,
  `PartyKinds` in a template were ignored.

### 3.3 Lifecycle / state machine — **B**
- `RuleAction.SetState` and `PUT /api/items/{id}` wrote `State` without checking the lifecycle.
- Transitions were keyed by the `OperationType` enum, so a template could not key a transition
  by its own operation.

### 3.4 Container semantics — **B**
- Recursive moves worked, but child moves bypassed rules and live publishing and carried no
  `OperationId`, so a pallet transfer produced child events no rule could see and no audit could
  trace to the operation.
- No cycle prevention: a container could be packed into its own descendant, after which the
  recursive move would never terminate (bounded only by luck).
- Manual re-parenting via `PUT /api/items` had no checks at all.

### 3.5 Read pipeline, offline, idempotency — **A/B**
- Operations were idempotent by `clientId`, but the key was stored in `Notes` (`"client:<id>"`)
  and matched by string, with no index.
- Read batches had no batch identity: an edge agent or handheld replaying a batch re-emitted
  Seen/Moved/In/Out events and re-fired rules.
- Handheld queue: results matched **by position** in the batch response, `attempts` was never
  consulted (infinite retry), rejected operations vanished from the device, and `enqueue()`
  did not stamp a `clientId`.

### 3.6 Multi-tenancy — **A**
- Isolation depended entirely on the EF global filter. Nine `IgnoreQueryFilters()` call sites
  (all legitimate) meant a single missed filter or a raw SQL report would leak across tenants.
- Login by e-mail across tenants was ambiguous when the same address existed twice.

### 3.7 Site-level access — **B**
- `ISiteAccess` filtered items, locations, devices and operation/stocktake writes, but alerts,
  events, raw reads, presence, positions, reports, dashboards and analytics were tenant-wide
  for site-restricted users. The batch operation endpoint skipped the site check entirely.

### 3.8 Duplication and sprawl — **B**
- EPC encode/decode was exposed four times (`/api/tags/encode/sgtin96`, `/api/tags/decode`,
  `/api/tags/encode`, `/api/tags/decode-any`, `/api/encoding/decode`, `/api/tags/describe`);
  only the last two had callers.
- Four independent "POST JSON to a URL" implementations (webhooks, integrations, notifications,
  firmware).
- `/api/items/by-epc` duplicated `/api/items/by-code` with a weaker resolver.
- Files named by release (`V15.cs`, `V16Controllers.cs`, `RoadmapControllers.cs`) instead of by
  module; 26 services in one `Services/` folder with no ownership signal.
- Event vocabulary drift: presence exits wrote `direction = "Exit"` while portal reads and every
  template rule used `"Out"`, so presence-driven exits never matched loss-prevention rules.

### 3.9 Data model, growth, retention — **C** (mostly already handled)
- `item_events`, `tag_reads`, `position_fixes`, `gps_fixes`, `heartbeats` are the growth tables;
  retention policies exist for all but `item_events` (kept forever by design). Partitioning is
  not needed at current scale and is a pure operations change when it is (BRIN on `ReadAt`
  already assumes it).
- Quantity items have no stock-balance model beyond `Item.Quantity` per row; that is adequate
  for lot/SKU counting (the requirement) and is *not* a WMS. See roadmap §7.4.
- `operation_lines` and `stocktake_lines` have no `TenantId` (isolated through their parent).

### 3.10 Advanced features in the foundation — **C**
- RTLS, billing, EPCIS, anomaly detection, maintenance forecasting, portals, GS1 encoding are
  all optional modules that hang off events/items and can be disabled without touching the
  core. None needed restructuring, only relocation into module folders.

---

## 4. Decisions and changes (ADR style)

Each decision: context → decision → consequences. All are implemented in this release unless
marked *deferred*.

### 4.1 Transactional outbox for every external side effect
**Context.** §3.1. **Decision.** Introduce `OutboxMessage` (`outbox` table) written in the same
`SaveChanges` as the business change. `ILivePublisher` (events, alerts) and `IWebhookDispatcher`
now enqueue; `NotificationService` enqueues an envelope and marks the log `Queued`. One
`OutboxDispatcher` (lease `job:outbox`, single node) delivers with exponential back-off
(5 s · 2ⁱ, capped at 1 h, 8 attempts) and dead-letters with the error kept. A `SaveChanges`
interceptor nudges the dispatcher in-process so latency stays in the tens of milliseconds.
Raw read fan-out (`PublishReadsAsync`) intentionally stays direct and post-commit: it is
telemetry, not state. **Consequences.** Nothing external can observe uncommitted state; retries
are uniform; the four HTTP senders collapse to two transports (`ILiveTransport`,
`IWebhookTransport`) plus the notification sender. Tests can assert "nothing sent before
dispatch". Cost: one extra row per event/alert (subject to retention like any other dataset).

### 4.2 Operations are definitions composed from a closed effect vocabulary
**Context.** §3.2. **Decision.** `OperationDefinition { Code, BaseType, EventType, Effects[],
Requires, EventData, ItemTypeCodes }` where `Effects` draw from a **closed** set:
`Move, SetCustodian, ClearCustodian, SetDueBack, ClearDueBack, SetState, IncrementCycle,
RecordSeen, RecordInspection, Activate, Dispose, Pack, Unpack, AdjustQuantity, SetAttribute`.
The 14 built-ins are themselves definitions in `OperationCatalog.BuiltIn` — there is one
execution path, not a fast path for built-ins and a slow one for custom operations. Templates
carry `OperationDefinitions`; tenants can add their own via `/api/operations/definitions`
(Admin). Lifecycle transitions are keyed by the operation **code** (string), falling back to the
base type's transitions for custom codes that a lifecycle does not mention. `Operation.DefinitionCode`
records which definition ran; `Operation.Type` keeps the base type for compatibility.
**Rejected alternative.** A scripting hook per operation. It would make every vertical
expressible and every deployment unreviewable; the effect vocabulary is deliberately small and
each effect is tested once. **Consequences.** "Sterilise", "Decontaminate", "Calibrate" ship as
template configuration (proof in `medical-assets` and `tool-tracking`); history shows the real
operation; the web and handheld forms derive their fields from `Requires` + `Effects`, so a new
operation needs no client change.

### 4.3 Idempotency as first-class data
**Context.** §3.5. **Decision.** `Operation.ClientId` becomes a column with a unique index per
tenant (legacy `Notes = "client:<id>"` rows still match as a fallback). Read batches accept
`BatchId`; the pair (device, batchId) is stored in `idempotency_keys` **in the same commit** as
the reads/events, and a replay returns the original result with `duplicate = true`.
**Consequences.** Store-and-forward from handhelds and edge agents is safe by default; the
same table is the home for future GPS/EPCIS capture idempotency.

### 4.4 Container invariants live in one place
**Context.** §3.4. **Decision.** `ContainerRules` (bounded ancestor walk, `MaxDepth = 32`) is used
by the `Pack` effect and by manual item edits; packing into self or a descendant is refused.
Child moves are emitted through the same event path as everything else (rules fire, outbox
written, `OperationId`/`DeviceId` set).

### 4.5 Postgres row-level security as the second line of tenant isolation
**Context.** §3.6. **Decision.** Every table with a `TenantId` gets `ENABLE ROW LEVEL SECURITY`
and a `tenant_isolation` policy over `current_setting('app.tenant_id')`. `TenantConnectionInterceptor`
pins each opened connection to the current tenant; a context with no tenant (pre-auth login,
cluster-wide loops, the outbox dispatcher) runs in the explicit **system scope** `'*'`. The API
connects as the non-owner role `rfid_app` (compose creates it; migrations run as the owner via
`ConnectionStrings:Migrations`). Coverage is derived from the EF model, so a new `TenantEntity`
is covered automatically at startup (`RowLevelSecurity.EnsureAsync`) and a test asserts every
`TenantEntity` maps to a covered table. **Consequences.** A missed query filter or a raw SQL
report can no longer cross tenants. Known gap: `operation_lines` and `stocktake_lines` carry no
`TenantId` and are protected only through their parent (a policy with an `EXISTS` on the parent
is the hardening step if ever needed). Login accepts an optional `tenantCode` and refuses
ambiguous e-mails instead of picking the first match.

### 4.6 Site RBAC applied to alerts, events and reads
**Context.** §3.7. Alerts, events and raw reads are now filtered to the principal's site
subtrees; `/api/operations/batch` enforces the same site check as the single endpoint.
*Deferred:* presence, positions, reports, dashboards and analytics remain tenant-wide for
site-restricted users (they are aggregate/read-only; the pattern to apply is the same helper).

### 4.7 One vocabulary, validated everywhere
Presence exits write `direction = "Out"`. `RuleAction.SetState` and `PUT /api/items` refuse
states the item type's lifecycle does not know. `/api/items/by-epc` delegates to the shared
resolver (kept as an alias for the handheld). Legacy encode/decode endpoints removed
(`/api/tags/encode*`, `/api/tags/decode*`); `/api/encoding/*` and `/api/tags/describe` remain.

### 4.8 Module folders name ownership
`Rfid.Application` is organised by module — Tracking, Rtls, Operations, Rules, Devices,
Encoding, Integrations, Billing, Analytics, Geo, Platform, Security, Templates — and controller
and entity files are named by module instead of release. Namespaces were **not** changed (no
churn for a rename); folders are the ownership signal, and the table in `ARCHITECTURE.md` §3 is
the source of truth for what belongs where.

### 4.9 Handheld queue hardening
Results matched by `clientId`; per-item exponential back-off with a hard attempt cap; rejected
operations retained in a capped, visible list; FIFO batches of 50; `enqueue()` stamps the
identity the server de-duplicates on.

### 4.10 Not changed, on purpose
- **No explicit `BeginTransaction`.** One `SaveChanges` per unit of work *is* the transaction;
  the outbox makes that sufficient. Adding explicit transactions everywhere would add noise
  without adding a guarantee.
- **No WMS/stock-balance model.** Quantity items are lots/SKUs counted per row; a
  bin-level balance ledger is a module for later (§7.4), not a core change.
- **No event partitioning yet.** Retention plus BRIN cover current volumes; partitioning is
  operational and needs no code change when it comes.
- **No scripting, no workflow engine in the core.** See §7.2/§7.3.
- **Namespaces unchanged** in the module reorganisation.

---

## 5. Migration and compatibility

| Change | Transition path |
|---|---|
| New tables `operation_definitions`, `outbox`, `idempotency_keys`; new columns `operations.DefinitionCode`, `operations.ClientId` | EF migration `Consolidation`, applied on startup. |
| Row-level security | Same migration enables RLS + policies on every tenant table and grants `rfid_app` when the role exists. Deployments that keep connecting as the owner see no behaviour change (owner bypasses RLS) — enable it by creating the role (`deploy/db-init/01-app-role.sh`) and switching `ConnectionStrings:Default`; keep `ConnectionStrings:Migrations` on the owner. |
| Operations by definition | `Operation.Type` is still written (the base type). Old rows have `DefinitionCode = NULL`; the list filter treats them as their built-in type. The `OperationRequest.Type` field still works; `Operation` (code) is optional. |
| Templates gained operation definitions | On startup each tenant's already-installed templates are re-applied idempotently (`TemplateProvisioner.SyncInstalledAsync`), adding only what is missing. |
| `clientId` moved from `Notes` to a column | Lookups match both; new rows use the column. |
| Notifications go through the outbox | `NotificationLog.Status` gains `Queued`; a log row flips to `Sent`/`Failed` when dispatched. |
| Removed endpoints | `/api/tags/encode/sgtin96`, `/api/tags/decode/{epc}`, `/api/tags/encode`, `/api/tags/decode-any/{epc}` (no clients). Use `/api/encoding/*` and `/api/tags/describe/{code}`. |
| Presence exit direction | `"Out"` instead of `"Exit"`; rules already used `"Out"`. |

---

## 6. Tests added for the new invariants (`Rfid.Tests/ConsolidationTests.cs`)

- Live events and webhooks are rows until dispatch; nothing is sent during the request; the
  dispatcher delivers everything that was committed.
- Outbox back-off and dead-lettering after the attempt cap.
- Operation replay by `clientId` returns the same operation and writes no second event.
- Read batch replay by `batchId` is acknowledged, not re-applied.
- Nested containers: subtree moves, child events carry the operation and fire rules, self/descendant
  packing refused.
- Template-defined operation (`Sterilise`) runs through the lifecycle, increments cycles, sets an
  attribute, records the code in history, and is refused for item types it does not apply to.
- Tenant-defined operation falls back to base-type transitions; definition validation rejects
  unknown effects and inconsistent requirements; every built-in is a valid definition.
- Rules cannot write unknown states; presence exits use `"Out"`.
- Every `TenantEntity` maps to a table covered by an RLS policy; system scope is explicit.

Suite: 99 tests, all passing. Verified end to end on Postgres 16 running as the `rfid_app`
role: 45 of 50 tables under RLS (the five without `TenantId`: `tenants`, `solution_templates`,
`worker_leases`, `operation_lines`, `stocktake_lines`), cross-tenant insert refused, template
sync installed the three new operations for the demo tenant, `Issue → Return → Decontaminate →
Sterilise` on a tray via the API.

---

## 7. Roadmap, re-assessed from first principles

For each candidate: the real-world problem, who benefits, how it classifies (**platform
capability** = everyone gets it, **optional module** = enabled per tenant, **vertical feature** =
template content), whether the current architecture supports it cleanly, what has to evolve,
dependencies, and horizon (**foundation** = next release, **near-term**, **long-term**).

### 7.1 Edge agents and store-and-forward — *platform capability, foundation*
**Problem.** Fixed readers in warehouses, hangars and farms lose connectivity; today reads
either arrive late in one burst (re-firing debounce logic) or are lost. **Verticals.** Every
fixed-reader deployment; critical for cold chain, evidence, aviation. **Fit.** Good now:
`BatchId` idempotency, the `ReadBatchRequest` contract and the vendor adapters are exactly what
an agent needs; LLRP/MQTT clients already exist in `Devices/`. **Evolution.** Package the
existing LLRP client + adapters + a local queue as a container (`rfid-edge`) that talks only
`POST /api/ingest/reads` with monotonically increasing `batchId`s; move per-reader LLRP
supervision from the API to the agent where an agent is present (the API keeps LLRP for
agent-less sites). Ingestion should treat `readAt` as authoritative and the debounce window per
device, not per arrival. **Dependencies.** None new. **Why first.** It is the piece that makes
"offline is normal" true for fixed infrastructure, not only handhelds, and it removes the last
reason for the server to hold TCP sessions to readers.

### 7.2 Configurable operations → guided workflows — *platform capability (operations) then optional module (workflows), near-term*
**Problem.** Operators follow multi-step procedures (receive → inspect → put-away; decontaminate
→ pack → sterilise → issue) and today each step is a separate screen. **Verticals.** Healthcare
CSSD, laundry, MRO, evidence intake, retail receiving. **Fit.** Operation definitions (4.2) are
the required substrate and are done. A workflow is then an ordered list of operation codes with
per-step required inputs and completion conditions — configuration again, not a general
workflow engine. **Evolution.** `WorkflowDefinition { Steps[{operation, prompt, requires,
next}] }` on templates; handheld renders steps from definitions the same way it renders one
operation. **Dependencies.** 4.2. **Not** a BPMN engine; loops and branching beyond
"result → next step" are out of scope.

### 7.3 Automation / rules — *platform capability, near-term hardening; long-term: scheduled and aggregate rules*
**Problem.** Current rules react to a single event. Real needs also include time-based ("not
seen for 48 h", "inspection due in 7 days") and aggregate ("more than 3 unexpected items at a
gate in 10 min") conditions. **Fit.** The rule vocabulary is right; what is missing is
*triggers* that are not events. `StocktakeSchedule`, escalation and anomaly detection already
run on the lease-aware minute loop. **Evolution.** Add `RuleTrigger.Schedule` evaluated by the
same loop against item queries, and let anomaly detectors emit rule-visible events. Keep the
action set closed (`CreateAlert | SetState | Webhook | Notify | RunOperation`); `RunOperation`
(e.g. auto-`Dispose` at max cycles) is the one addition that pays for itself across verticals.

### 7.4 WMS — *optional module, long-term; do not fold into the core*
**Problem.** Bin-level stock balances, put-away/pick suggestions, waves. **Verticals.**
Distribution, 3PL, retail back-room. **Fit.** The core tracks *items* (serialised or lot rows);
a WMS needs a *balance* ledger per (SKU, lot, bin) derived from quantity events, plus task
generation. That is a projection over `item_events` (`QuantityChanged`, `Moved`) — the same
pattern as billing — so it belongs in a module, not in `Item`. **Evolution.** `StockBalance`
table maintained by an event projector; `PickTask`/`PutawayTask` as workflow steps (7.2).
**Dependencies.** 7.2 for tasks; an event projector runner (see 7.9). Explicitly a module: most
of the ~90 verticals never need it.

### 7.5 ERP integrations — *platform capability (outbound/inbound contracts), vertical content (mappings), near-term*
**Problem.** Assets and stock live in SAP/Dynamics/Maximo; the platform must be a reliable
event source and accept master data. **Fit.** Good: `IntegrationService` is cursor-based
(at-least-once), payload formatters exist for three ERPs, import/reconciliation exists. **Evolution.**
Move integration delivery onto the outbox (one retry/dead-letter model instead of two); keep
formatters as data-only mappings; add per-endpoint event filters (`itemType`, `eventType`,
site) so a tenant can route only what an ERP wants. Nothing structural.

### 7.6 EPCIS / GS1 — *optional module, near-term completion*
**Problem.** Trading partners (pharma DSCSA, food traceability, retail) exchange EPCIS 2.0.
**Fit.** Done for ObjectEvent/AggregationEvent projection and capture; bizStep mapping is a
switch that should become data (`OperationDefinition.EventData["bizStep"]` — the definition
model already allows it). **Evolution.** Move the bizStep/disposition mapping onto operation
definitions; add `TransformationEvent` when manufacturing templates need it; expose the query
interface with pagination. Dependencies: 4.2 (done).

### 7.7 RTLS: UWB TDoA, BLE AoA — *optional module, long-term; vendor engines, not in-house math*
**Problem.** Sub-metre positioning for high-value assets, people safety, sports. **Fit.** The
platform already ingests ranges (`rangeM`) and computes positions with Kalman smoothing. TDoA
and AoA require synchronised anchors and vendor location engines; those engines output x/y
(or ranges) — exactly what `PositionService` consumes today. **Evolution.** A *position ingest*
contract (`POST /api/ingest/positions {itemOrTag, x, y, z?, accuracy, at}`) so vendor engines
(Pozyx, Quuppa, Sewio, Wiliot…) plug in without the platform re-implementing their solvers.
Keep the built-in trilateration for simple RSSI/ranging sites. No core change.

### 7.8 Device management — *platform capability, near-term*
**Problem.** Fleets of hundreds of readers and handhelds need config, health, firmware and
certificates managed centrally. **Fit.** Health SLAs, firmware rollouts and LLRP configuration
exist. **Evolution.** Device *desired configuration* as a versioned document with agent pull
(pairs naturally with 7.1); certificate/token rotation; device groups per site. The edge agent
is the missing execution arm.

### 7.9 Digital twins — *optional module, long-term; a view, not a model*
**Problem.** Operations staff want a spatial picture (racks, zones, vehicles) with live
occupancy. **Fit.** Locations, positions, presence and geofences already hold everything a
twin displays; the current floor-plan SVG and map are 2D twins. **Evolution.** A 3D renderer
over the same APIs; the only backend addition is location geometry (`Attributes.geometry`).
Dependency: none. Priority low relative to the operational items above.

### 7.10 Reporting / analytics — *platform capability (warehouse export) + module (in-app analytics), near-term hardening*
**Fit.** Parquet/CSV export with watermarks is the right foundation: heavy analytics belong in
the customer's warehouse, in-app analytics stay operational. **Evolution.** Consolidate
`DashboardController` (`/api/dashboard`), `DashboardService`, `AnalyticsService` and
`ReportService` behind one query model with the same `ISiteAccess` filtering (the review found
they overlap); add scheduled report delivery via the outbox (`notification` kind).

### 7.11 Portals — *optional module, done; extend by configuration*
Party-scoped read-only access exists. Next steps are per-portal visibility settings (which
datasets a supplier may see) rather than new code paths.

### 7.12 Marketplace / template exchange — *optional module, long-term; needs a signed template format first*
**Problem.** Partners want to package a vertical (types, lifecycles, rules, **operations**,
workflows, dashboards, label designs) and distribute it. **Fit.** Export/import exists for
types, rules and now operations. **Evolution.** Extend `TemplateDefinition` with dashboards,
label designs, workflows and location kinds; version and sign the document; keep installation
idempotent (already true). A marketplace UI is a thin layer once the format is complete.

### 7.13 Automation of retention/partitioning at scale — *platform capability, long-term*
Move `tag_reads`, `position_fixes`, `gps_fixes`, `outbox` to native range partitions by month
when a tenant exceeds ~10⁸ rows; retention becomes `DROP PARTITION`. No code change beyond the
migration.

### Status (v2.1)

Delivered: **7.1** edge agent (`Rfid.Edge` + shared `Rfid.Protocols`, late-read safety in
ingestion), **7.5** integrations on the outbox with item-type/site filters, **7.3** schedule
triggers with per-item de-duplication and the `RunOperation` action (executed post-commit via the
outbox), **7.6** bizStep/disposition as operation-definition data with capture mapping. Also done
from §8: outbox retention dataset. v2.2 delivered **7.2** guided workflows (steps as
operations, no engine) and the first part of **7.8** (desired reader configuration pulled by the
edge agent), plus site filtering for presence and positions. v2.3 closed the rest of §8 (site scoping for
reports/dashboards/analytics via `SiteScope`, line-table isolation, edge MQTT bridge) and delivered
the **7.7** position-ingest contract for vendor RTLS engines and **7.10** query-model consolidation
(one scoped query model behind `/api/dashboard`, widgets, reports and analytics).

### Ordering recommendation

1. **Foundation (next):** 7.1 edge agent, 7.5 integrations on the outbox, 7.3 `RunOperation`
   action + scheduled triggers, 7.6 bizStep-as-data.
2. **Near-term:** 7.2 workflows, 7.8 device configuration, 7.10 analytics consolidation.
3. **Long-term / modules:** 7.4 WMS, 7.7 position ingest for vendor RTLS, 7.9 3D twin, 7.12
   marketplace, 7.13 partitioning.

Each item above is expressible on the current primitives; none requires a new core entity
beyond configuration documents (workflow, device config, stock balance projection).

---

## 8. Open items carried forward

- Apply `ISiteAccess` filtering to presence, positions, reports, dashboards and analytics
  (pattern established in alerts/events).
- Move `IntegrationService` delivery onto the outbox (7.5).
- `operation_lines` / `stocktake_lines`: add `TenantId` or an `EXISTS` policy if a compliance
  requirement demands table-level isolation for line tables.
- Translate `EpcisService` bizStep switch into `OperationDefinition.EventData` (7.6).
- Outbox retention: add the `outbox` dataset to `RetentionService` defaults (processed rows,
  30 days).
