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

Tests: `cd src/backend && dotnet test` (application layer: operations, lifecycles, containers,
stocktakes, ingestion/zone rules, rule engine, EPC encoding).

## What is implemented

**Backend**
- Multi-tenant model with global query filters; JWT for users (Admin/Operator/Viewer) and
  provisioning tokens for devices.
- Generic **operation processor**: Receive, Transfer, Issue, Return, Count, Dispatch, Inspect,
  Maintain, Dispose, Pack, Unpack, ProcessStage, Commission, Adjust — each applies location /
  custody / state / quantity / container effects, drives the item type's lifecycle state machine,
  writes append-only `item_events` (chain of custody) and evaluates rules. Idempotent via `clientId`
  for offline handhelds (`POST /api/operations/batch`).
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

## Roadmap

Native LLRP client · UWB/BLE trilateration (x/y positioning inside zones) · SAP / Dynamics / Maximo
adapters on top of the integration endpoint · label designer UI · mobile printing from the handheld.
