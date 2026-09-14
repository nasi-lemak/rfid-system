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

The API migrates the database and seeds a demo tenant (locations, IT assets, tools, pallets with
cartons, stock lots, a handheld, a dock-door portal and an exit gate) on first start.

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
- **Solution template catalog** (14 templates covering the whole requirements table) with
  idempotent provisioning; GS1 SGTIN-96 encode/decode; dashboard, event log, alerts, devices,
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

## Roadmap

LLRP / Zebra IoT Connector / Impinj bridges and MQTT ingestion · active RFID / BLE / UWB location
engine (dwell, trilateration) · ERP/EAM connectors (depreciation, work orders) · label printing &
encoding service · scheduled stocktakes & report builder · per-tenant template customisation UI.
