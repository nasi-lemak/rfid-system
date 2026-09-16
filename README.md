# RFID Platform

A single, configurable platform for **every** RFID tracking use-case — asset, inventory, warehouse,
tool crib, linen/laundry, PPE, medical devices & surgical trays, evidence & samples, library,
retail & loss prevention, returnable assets/kegs/rental, manufacturing WIP/kanban/QC, personnel &
muster, livestock — built from one set of primitives plus **solution templates**.

| Layer | Stack | Path |
|---|---|---|
| API | .NET 8, ASP.NET Core, EF Core 8, Npgsql, SignalR, JWT, Swagger | `src/backend` |
| Database | PostgreSQL 16 (JSONB for attributes, lifecycles, rules) | migrations in `src/backend/Rfid.Infrastructure/Persistence/Migrations` |
| Web | React 19, TypeScript, Vite, React Router, TanStack Query | `src/web` |
| Handheld | React Native (Expo SDK 57), reader abstraction (Zebra / Chainway / BLE / simulated), offline queue | `src/mobile` |
| Edge agent | .NET 8 worker: LLRP + vendor pushes → durable on-disk queue → store-and-forward | `src/backend/Rfid.Edge` (`Dockerfile.edge`, compose profile `edge`) |
| Docs | Architecture, data model, vertical → primitive mapping, handheld SDK notes | `docs/` |

## How one platform covers ~90 systems

Every system in the requirements table decomposes into the same primitives — **Tag → Item → Item Type
(attributes + lifecycle state machine) → Location tree → Party (custody) → Device/Antenna → Operation →
Event → Rule/Alert** — and a **Solution Template** is just JSON configuration over them. Installing
"Hospital Linen" creates the *Linen* type (wash-cycle counter, `Clean→Issued→Soiled→InWash→Clean`
lifecycle) and rules like *"contaminated linen entered a clean zone → critical alert"*; installing
"Tool Crib" creates *Tool* with checkout/in transitions and the *FOD: tool left hangar while checked
out* rule. See [`docs/SOLUTION-TEMPLATES.md`](docs/SOLUTION-TEMPLATES.md) for the full mapping and
[`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md) for the design.

## Quick start (Docker)

```bash
docker compose up --build
# Web:      http://localhost:8080   (admin@demo.local / admin123, operator@demo.local / operator123)
# API:      http://localhost:5080/swagger
# Postgres: localhost:5432 (postgres/postgres)
```

The API migrates the database and, on first start, seeds a demo tenant with **all 23 solution
templates installed and a realistic demo site for every vertical** — hospital pharmacy, distribution
centre, aircraft hangar tool crib, hospital linen service, evidence store, library, flagship store,
brewery & plant hire, gearbox line, campus & 10K race, farm, museum, hotel, central kitchen & cold
chain, vehicle depot & MRO, waste depot, airport baggage hall, data centre, offshore supply base,
school & events — 279 tagged items across 24 sites, 71 readers and several days of replayed history (154 operations, ~1,900 reads) that raise the verticals' own alerts.
Control it with `Seed__Scenarios=all|none|<comma list>`; load a single vertical into any tenant later
with **Load demo data** on the *Solution templates* page (`POST /api/templates/{code}/demo`).

## Quick start (local dev)

```bash
# 1. PostgreSQL 16 on localhost:5432 with database "rfid" (or edit src/backend/Rfid.Api/appsettings.json)
# 2. API
cd src/backend && dotnet run --project Rfid.Api            # http://localhost:5080, Swagger at /swagger
# 3. Web (proxies /api and /hubs to :5080)
cd src/web && npm install && npm run dev                     # http://localhost:5173
# 4. Handheld
cd src/mobile && npm install && npx expo start               # Expo Go with the simulated reader
```

Tests: `cd src/backend && dotnet test` (99 tests over the real services: operations and
definitions, lifecycles, containers, stocktakes, ingestion/zone rules, rule engine, outbox,
idempotency, RLS coverage, EPC encoding, every roadmap module).

Production note: `docker-compose.yml` connects the API as the restricted `rfid_app` role so
Postgres row-level security applies (`deploy/db-init/` creates the role; migrations run as the
owner through `ConnectionStrings__Migrations`). Set `APP_DB_PASSWORD`, `JWT_KEY`, `ADMIN_PASSWORD`.

## What is implemented

**Backend**
- Multi-tenant model with global query filters; JWT for users (Admin/Operator/Viewer) and
  provisioning tokens for devices.
- **Operations as configuration**: every operation — the 14 built-ins (Receive, Transfer, Issue,
  Return, Count, Dispatch, Inspect, Maintain, Dispose, Pack, Unpack, ProcessStage, Commission,
  Adjust) and template/tenant-defined ones such as `Sterilise` or `Calibrate` — is a definition
  composed from a closed set of effects (move, custody, due-back, state, cycles, inspection,
  pack/unpack, quantity, attribute). One processor drives the item type's lifecycle (transitions
  keyed by operation code), writes append-only `item_events` (chain of custody, including every
  container child) and evaluates rules. Idempotent via `clientId` (`POST /api/operations/batch`).
- **Transactional outbox**: live pushes, webhooks and notifications are committed with the state
  that caused them and delivered afterwards with retries — nothing external ever sees a rolled-back
  change. Read batches are idempotent by `batchId` for store-and-forward edge agents.
- **Tenant isolation twice**: EF query filters plus Postgres row-level security on every tenant
  table, pinned per connection; per-site RBAC filters items, locations, devices, alerts, events,
  reads and operations.
- **Read ingestion** for fixed readers / portals / cabinets / gates: antenna → location + In/Out
  direction, debounced Seen/Moved events, rule evaluation, live fan-out over SignalR.
- **Stocktakes**: expected set from a location subtree, streaming scans, found / missing /
  unexpected / unknown reconciliation, apply (flag missing, relocate unexpected).
- **Rule engine**: `trigger + conditions → CreateAlert | SetState | Webhook`, conditions address
  item fields, derived values (`daysUntilExpiry`, `cyclesRemaining`, `overdue`), item-type, location
  attributes (`toLocation.clean`), party and event data (`data.direction`).
- **Solution template catalog** (23 templates covering the whole requirements table, see
  `docs/SOLUTION-TEMPLATES.md`) with idempotent provisioning and a **demo scenario per template**
  replayed through the real services; GS1 SGTIN-96 encode/decode; dashboard, event log, alerts, devices,
  users, lookups.

**Web**: dashboard, live reads, alerts, event log, items (filters: expiring, inspection due,
overdue, low stock, not seen, in custody), item detail with history and one-click operations, tags &
commissioning of unknown reads, operations, stocktakes, item types (lifecycle editor), locations
tree, parties, readers & antennas with token provisioning, rules builder, solution templates, users.

**Handheld**: login (user or device token), scan/inventory with server resolution and sightings,
lookup with history and quick actions, locate (geiger), stocktake (live counts pushed in batches),
operations (all types, pickers, per-line results), commission (read/encode EPC, bind to typed item),
settings (reader driver, RF power, current location, offline queue sync). Vendor SDK integration
notes in [`docs/HANDHELD-SDK.md`](docs/HANDHELD-SDK.md).

## Integration & operations (v1.1)

See [`docs/INTEGRATION.md`](docs/INTEGRATION.md).

- **Reader bridges**: Impinj IoT Interface and Zebra IoT Connector payloads accepted natively
  (`/api/ingest/impinj`, `/api/ingest/zebra`), generic JSON, and an **MQTT** subscriber that routes
  topics to devices.
- **Presence engine** for active RFID / BLE / UWB: zone sessions with dwell timeouts, best-RSSI zone
  resolution, live occupancy, **muster roll-call** and **checkpoint timing** (race / process gates).
- **Integrations**: cursor-based, HMAC-signed, retried delivery of events and alerts to ERP/EAM/BI;
  pull-style CSV/JSON **reports** (12) incl. a fixed-asset depreciation register.
- **Labels & encoding**: ZPL generation with RFID encode, raw-TCP printing to printer devices,
  SGTIN-96 / GRAI-96 / GIAI-96 encoders.
- **Scheduled stocktakes** with auto-reconcile, run by a background service.
- **Template export / import** for per-tenant customisation.

## v1.2

- **Native LLRP client** — connect directly to LLRP 1.0.1 readers (Impinj, Zebra FX, Alien): ROSpec
  inventory, RO_ACCESS_REPORT streaming, keepalives, auto-reconnect; configured per device (`llrpHost`).
- **x/y positioning** — antennas as anchors, RSSI → distance (log-distance model), non-linear least
  squares trilateration; floor-plan view with anchors and live positions.
- **ERP adapters** — SAP S/4 asset master/transfers, Dynamics 365 customer assets, IBM Maximo MXASSET
  payload formats plus Bearer / Basic / OAuth2 client-credentials authentication on integration endpoints.
- **Label designer** — visual mm-based designer compiled to ZPL per item type (text, Code-128, QR,
  boxes, RFID encode).
- **Handheld printing** — network printers via the server or Bluetooth mobile printers.

## v1.3

- **LLRP reader configuration** — transmit power (mapped to the reader's capability table), Gen2
  session/population, antenna selection, GPI-triggered inventory, GPO control, capabilities/status API.
- **UWB ranging + Kalman smoothing** — `rangeM` on reads, range-weighted trilateration, per-item
  constant-velocity smoothing.
- **Inbound ERP sync** — CSV/JSON asset-master import with dry run, upsert, EPC binding and
  read-only reconciliation (missing on either side, field differences).
- **Print queue** — durable print jobs with retries, reprint audit trail (`LabelPrinted` events),
  label-stock tracking with low-stock alerts.

## v1.4

- **Multi-node deployment** — run any number of API replicas against one PostgreSQL. Background
  jobs (`job:*`), the MQTT subscriber and every LLRP reader connection (`llrp:{deviceId}`) are
  coordinated with database leases; a node that dies is replaced within one interval. Kalman
  position tracks live on the item row so any node continues them; set `Redis:ConnectionString`
  for a SignalR backplane so live updates reach clients on every node. `GET /api/cluster` and the
  **Cluster & system** page show nodes, leases and feature flags.
- **RTLS history** — position fixes are sampled on movement (`position_fixes`, retention
  `Positions:RetentionDays`); the floor plan gains a dwell **heat map** and a per-item **path
  replay** with a time slider (`/api/positions/heatmap`, `/api/positions/history`).
- **SSO (OIDC)** — configure `Oidc:*` (Entra ID, Keycloak, Okta, Auth0 …): the web app offers
  *Sign in with SSO* (authorization-code + PKCE), the API accepts the provider's tokens as a second
  bearer scheme, users are provisioned on first login and IdP groups map to roles (`Oidc:RoleMap`).
- **Per-site RBAC** — users can be restricted to sites with a role per site (Users → *Sites*):
  items, locations and readers are filtered to those subtrees and operations/stocktakes are
  refused elsewhere.
- **Handheld offline maps** — the app caches master data and floor plans (Settings → *Download for
  offline*) and the new **Floor plan** screen (SVG) shows anchors and positions, highlighting the item
  you looked up, even without coverage.

## v1.5

- **Reader health SLAs & firmware** — heartbeats (`POST /api/devices/{id}/heartbeat`, recorded
  automatically for LLRP readers) drive Online / Degraded / Offline per device SLA, with critical
  alerts on outage and uptime %. Firmware releases per vendor/model roll out to devices, which poll
  `GET /api/devices/{id}/firmware/pending` and report progress.
- **Geofenced GPS assets** — `POST /api/ingest/gps` from telematics units, GPS trackers or the
  handheld; circle/polygon fences raise enter/exit/dwell alerts, fire rules
  (`GeofenceEntered`/`GeofenceExited`) and move items to a linked location. Map page with
  OpenStreetMap tiles (`VITE_MAP_TILES`), fence drawing and track replay.
- **Configurable dashboards** — dashboards are stored widget lists (stat, breakdown, trend,
  presence, devices, stocktakes, list, fences, text) evaluated server-side; shared or private, with
  an in-page editor.
- **Notification channels & escalation** — e-mail (SMTP), SMS (Twilio-compatible), Microsoft Teams,
  Slack and webhooks; rules notify channels directly, catch-all channels take every alert above a
  severity, and escalation policies re-notify on a schedule while alerts stay unacknowledged.
  Every delivery is logged.
- **Warehouse export & analytics** — datasets (items, events, reads, alerts, operations, position
  and GPS fixes, presence sessions, stocktakes, heartbeats) export as Parquet/CSV to a
  day-partitioned landing zone, incrementally on a schedule or on demand; Analytics page with
  activity trends, utilisation, dwell, inventory accuracy, alert response and reader uptime.

## v1.6

- **Handheld GPS & geofencing** — the app can attach its GPS position to scans and operations
  (posting fixes for the EPCs it saw), shows your position against the tenant's geofences on an
  offline-capable local map, and warns inside restricted zones.
- **Anomaly detection** — a 14-day baseline per reader/location/item flags read-rate spikes and drops
  (same hour of day), off-hours activity, unknown-tag surges, items flapping between zones and
  excessive movement; findings are scored in standard deviations, de-duplicated while open and
  raise warning alerts when strong. Anomalies page with hour-of-day profiles.
- **GS1 encoding at scale** — SSCC-96 joins SGTIN/GRAI/GIAI; serial pools hand out contiguous,
  never-reused serials to the web wizard, handhelds and integrations; batches create unassigned
  tags, bind items lacking tags or create items + tags, with duplicate checks and label queuing.
- **Multi-language UI** — English, Bahasa Melayu, 中文, Español, Deutsch, Français in the web app
  (navigation, sign-in, dashboard chrome; translations fall back to English per string) and the
  handheld; language selector on the sign-in page and sidebar.
- **Audit log & retention** — every mutating API call is recorded with user, route, entity, status
  and a redacted body; Audit page with filters and detail. Per-dataset retention policies (reads,
  fixes, sessions, heartbeats, logs, closed alerts…) run every 6 hours; item events stay forever
  unless you choose otherwise.

## v1.7

- **Barcode / 2D fallback & hybrid flows** — the handheld scans Code128, GS1-128, EAN/UPC, QR and
  DataMatrix with the camera; codes resolve to items as bound barcode tags, GS1 element strings /
  Digital Links (matched to SGTIN/SSCC/GRAI/GIAI tags), or plain identifiers, so one operation can
  mix RFID reads and barcode scans (`GET /api/items/by-code/{code}`).
- **Returnable-asset billing** — rate cards (deposit, cycle fee, daily rental after free days, late
  and loss fees) turn custody events into a per-party ledger; periodic invoices with printable HTML,
  issue / pay / void lifecycle and account balances.
- **Supplier / customer portals** — users scoped to a party get a read-only portal: items in their
  custody and history, due-back dates, activity, invoices and charges; the API refuses everything
  else for portal logins.
- **Predictive maintenance** — transparent risk score per item from cycle wear, inspection failures,
  overdue inspections, service-interval drift, usage intensity, age and open alerts; predicted
  service date from the dominant driver; daily snapshots, trends and alerts.
- **EPCIS 2.0** — item events as EPCIS ObjectEvents/AggregationEvents (JSON-LD, CBV steps and
  dispositions, EPC/SGLN URNs, business transactions) with the simple event query and a capture
  endpoint that turns partner documents into reads and operations.

## v2.0 — architecture consolidation

A first-principles review of the whole platform before further expansion; the findings,
decisions and the re-assessed roadmap are in
[`docs/ARCHITECTURE-REVIEW.md`](docs/ARCHITECTURE-REVIEW.md), and
[`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md) now describes the system as built.

- **Transactional outbox** for every external side effect (SignalR, webhooks, notifications).
- **Operation definitions**: built-ins and vertical operations share one effect vocabulary and one
  execution path; templates ship operations (`Decontaminate`, `Sterilise`, `Calibrate`); admins can
  define their own; web and handheld forms are generated from the definition.
- **Idempotency as data**: `operations.ClientId` column + unique index; `batchId` on read batches.
- **Container invariants**: cycle prevention, bounded depth, child moves are first-class events
  that carry the operation and fire rules.
- **Row-level security** on all tenant tables with a restricted runtime role; login disambiguation
  by `tenantCode`; site RBAC extended to alerts, events, reads and batch operations.
- **Consolidation**: lifecycle validation in rules and manual edits, one direction vocabulary
  (`Out`), duplicate encode/decode endpoints removed, module folders instead of release-numbered
  files, hardened handheld queue (match by `clientId`, back-off, attempt cap, visible rejections).

## v2.1 — foundation items from the re-assessed roadmap

- **Reader edge agent** (`Rfid.Edge`, [`docs/EDGE-AGENT.md`](docs/EDGE-AGENT.md)) — drives LLRP readers
  and receives Impinj/Zebra/generic pushes on site, stores batches on disk and forwards them in order
  with monotonic batch ids; the server acknowledges replays and never lets a late batch move state
  backwards. Protocol code moved to a shared `Rfid.Protocols` library used by both hosts; a `Gateway`
  device kind and an `edgeManaged` reader flag complete the picture.
- **Integrations on the outbox** — ERP/BI batches are outbox messages (one in flight per endpoint,
  cursor advances only on success, dead-letters are rebuilt from the cursor); new per-endpoint
  filters by item type and site.
- **Schedule rules and `RunOperation`** — rules can sweep items on an interval (`hoursSinceSeen`,
  `daysUntilInspection`, `cyclesRemaining`, …) with per-item de-duplication, and any rule can run an
  operation definition (auto-Dispose at max cycles, Maintain after a failed inspection), executed
  after commit through the outbox.
- **EPCIS vocabulary as definition data** — built-in and template operations declare their CBV
  bizStep/disposition; capture maps steps back to definitions.

## v2.2 — guided workflows and central reader configuration

- **Workflows** — templates and tenants define guided multi-step tasks (CSSD reprocessing, wash
  cycle, tool return & check) as ordered operation definitions with prompts and per-step inputs.
  The handheld runs them step by step; every step is an ordinary operation stamped with the run id,
  so runs work offline through the same queue and the web shows run history derived from operations.
- **Edge configuration pull** — readers assigned to a gateway are pulled by the agent as a versioned
  desired configuration; only changed readers reconnect; the last configuration survives offline
  restarts.
- **Site RBAC** extended to presence, floor plans, heat maps and position history.

## Roadmap

Re-assessed from first principles in [`docs/ARCHITECTURE-REVIEW.md`](docs/ARCHITECTURE-REVIEW.md) §7.
Foundation items shipped in v2.1 (edge agent, integrations on the outbox, schedule rules +
`RunOperation`, EPCIS bizStep as data); v2.2 delivered guided workflows and edge configuration pull.
Near-term: guided **workflows** as ordered operation definitions, device configuration management,
analytics consolidation. Long-term modules: WMS stock balances as an event projection, vendor
RTLS position ingest (UWB TDoA / BLE AoA engines), 3D digital-twin views, signed template
marketplace, native partitioning of high-volume tables.
