# Integration guide

## Inbound: getting reads into the platform

All readers authenticate with a **device token** (Admin → Readers & devices → *Issue token*), exchanged
for a JWT at `POST /api/auth/device {"token": "..."}`. Handhelds may also use a user login.

| Source | How |
|---|---|
| Handheld app | `POST /api/operations` (business transactions), `POST /api/ingest/handheld` (free-scan sightings), `POST /api/stocktakes/{id}/scans` |
| Any reader / edge agent | `POST /api/ingest/reads` `{deviceId?, sessionId?, batchId?, reads:[{epc, tid?, antennaPort?, rssi?, readAt?, locationId?}]}` — send a monotonically increasing `batchId` per device for store-and-forward: a replayed batch is acknowledged (`duplicate: true`) and not re-applied |
| Impinj R700 (IoT Interface) | Point the reader's HTTP POST / webhook at `POST /api/ingest/impinj?deviceId=<id>` — `tagInventoryEvent` payloads (single or array) are accepted as-is (`epcHex` or base64 `epc`, `peakRssiCdbm`, `antennaPort`, `tidHex`, `timestamp`) |
| Zebra FX7500 / FX9600 (IoT Connector) | HTTP POST endpoint → `POST /api/ingest/zebra?deviceId=<id>` (`data.idHex`, `antenna`, `peakRssi`, `TID`) |
| Anything else that can POST JSON | `POST /api/ingest/generic` — `{reads:[…]}`, `[…]`, a single read object, or an array of EPC strings; fields `epc/epcHex/idHex/tag/id`, `rssi/peakRssi`, `antennaPort/antenna/port`, `readAt/timestamp/time` |
| **MQTT** | Set `Mqtt:Enabled=true`, `Mqtt:Host`, `Mqtt:Topics` in `appsettings.json` (or env `Mqtt__Enabled` …). Give each device a `Config.mqttTopic` (exact topic or `+`/`#` wildcard pattern) and optionally `Config.vendor` (`impinj`/`zebra`). Payloads on matching topics go through the same adapters. |
| **LLRP** (native client) | Set `Config.llrpHost` (and optional `llrpPort`, default 5084) on a Fixed/Portal/Gate device. The API's `LlrpReaderService` connects to the reader, resets its configuration, adds/enables/starts a continuous Gen2 inventory ROSpec on all antennas and streams `RO_ACCESS_REPORT`s (EPC-96 / EPCData, antenna, peak RSSI, timestamps, seen count) into ingestion; keepalives are acknowledged and connections re-established automatically. Works with Impinj Speedway/R700 (LLRP mode), Zebra FX-series, Alien and other LLRP 1.0.1 readers. Set `llrpEnabled: false` to pause. **Configuration** (device Config): `llrpPower` (dBm, mapped to the reader's power table from `GET_READER_CAPABILITIES`), `llrpSession` (Gen2 S0–S3), `llrpTagPopulation`, `llrpAntennas` ("1,2"), `llrpGpiStart` (start inventory while a GPI is high — photo-eye/conveyor portals), `llrpReportEveryN`. `GET /api/devices/{id}/llrp/status` shows connection, capabilities (manufacturer, firmware, antennas, GPIO count, power table); `POST …/llrp/gpo {port,state}` drives stack lights/buzzers; `POST …/llrp/reconnect` applies new settings. |

Antenna → location mapping (with `In` / `Out` direction for portals) turns raw reads into zone
movements and rule evaluation. When several antennas or gateways see one tag in the same batch, the
**strongest RSSI** decides the zone ("nearest reader" resolution for BLE / active tags).

## Positioning (x/y trilateration)

**External engines.** `POST /api/ingest/positions {deviceId?, batchId?, fixes:[{epc|itemId, locationId,
x, y, z?, accuracyM?, at?}]}` accepts positions computed by a vendor RTLS engine (UWB TDoA, BLE AoA,
camera) or an edge agent: `locationId` is the floor plan the coordinates are relative to; fixes update
the item's position and history exactly like the built-in trilateration; `batchId` makes the batch
idempotent and an older fix never moves a position backwards. Response: `{received, applied, unknown,
rejected, duplicate}`.

Give antennas anchor coordinates (`X`, `Y` in metres; Readers & devices) and the zone a size
(location attributes `widthM`, `heightM`). When one tag is seen by several positioned antennas in a
batch, ingestion converts RSSI to distance with the log-distance model (`RssiAt1m`, default −45 dBm;
`PathLossExponent`, default 2.2 — both per antenna) and solves the position by non-linear least
squares (Gauss-Newton from a weighted centroid; 2 anchors → along the line, 1 → at the anchor). The
estimate and an accuracy figure (fit residual) are stored on the item (`PositionX/Y/At/AccuracyM`).

- `GET /api/positions/floor-plans` — zones that have anchors
- `GET /api/positions/floor-plans/{locationId}?maxAgeMinutes=720` — anchors + recent item positions
- Web: Presence & location → **Floor plan (x/y)**

Typical sources: BLE gateways (Kontakt.io, Minew, Aruba), UWB anchors, or multi-antenna UHF readers
with directional antennas. **UWB / ranging systems** post the measured distance directly as
`rangeM` on each read (`/api/ingest/reads`, or `rangeM`/`range`/`distance` in generic JSON); ranges
are weighted 4× over RSSI-derived distances in the solver. Successive fixes per item are smoothed by a
constant-velocity **Kalman filter** (per axis; resets on zone change or after 10 min without a fix),
so jittery RSSI positions settle while moving tags are still tracked.

## Presence engine

Every resolved sighting opens or refreshes a `PresenceSession` (item × zone). A session closes when the
item is seen in another zone or when the zone's dwell timeout expires (default 5 min; per-location
attribute `presenceTimeoutSec`), emitting a `Moved` event with `data.direction = "Exit"` and
`data.dwellSeconds` — so rules such as *badge left restricted area* or *asset left ward* fire on
exit too.

- `GET /api/presence/zones?under=<locationId>` — live occupancy per zone
- `GET /api/presence/muster?siteId=` — muster roll-call (accounted = latest zone is a `MusterPoint`; checkpoints ignored)
- `GET /api/presence/timing?eventLocationId=` — checkpoint splits & ranking (locations of kind `Checkpoint`, ordered by attribute `order`)
- `GET /api/presence/items/{itemId}` — an item's session history (dwell analytics)
- `GET /api/reports/dwell` — dwell report

### Edge agent (store-and-forward)

For sites that must keep reading through WAN outages, run the **edge agent** (`Rfid.Edge`,
`docker compose --profile edge up`): it drives LLRP readers and/or receives Impinj/Zebra/generic
pushes locally, queues batches on disk and forwards them in order with `batchId = {agent}:{seq}`;
the server acknowledges replays (`duplicate: true`) and never lets a late batch move state backwards
(`late` count). Mark readers it drives as `edgeManaged` on the device so the server does not open a
second LLRP connection. Full guide: [`EDGE-AGENT.md`](EDGE-AGENT.md).

## Delivery guarantees (outbox)

Every external side effect — SignalR `event`/`alert` pushes, rule webhooks and notification
channel deliveries — is written to the `outbox` table in the same database commit as the state
change that caused it and delivered afterwards by a single dispatcher with exponential back-off
(8 attempts, then dead-lettered with the error kept). Consumers therefore never see state that
was rolled back, and a slow or failing endpoint never blocks an operation. Integration
endpoints (below) also deliver through the outbox: their cursor is the source of truth and only
advances when a batch was accepted, one batch is in flight per endpoint (ordering), and a batch that
exhausts its retries is dead-lettered for inspection while the next pass rebuilds the same data from
the cursor. Delivery is therefore at-least-once; de-duplicate on `eventId`.

## Operation definitions

`GET /api/operations/definitions` lists every operation a tenant can run: the 14 built-ins plus
template- or tenant-defined ones (e.g. `Sterilise`, `Calibrate`). Each carries `requires`
(which inputs are mandatory) and `effects` (the built-in effects it composes), which is how the
web and handheld forms decide what to ask for. Run one with `POST /api/operations
{operation: "Sterilise", lines: [...]}` (`type` is still accepted for built-ins). Administrators
manage custom definitions with `POST/PUT/DELETE /api/operations/definitions`;
`GET /api/operations/definitions/effects` returns the closed effect vocabulary. History records
`definitionCode` on each operation.

## Outbound: ERP / EAM / BI / webhooks

Create endpoints under **Integrations** (or `POST /api/integrations`). The dispatcher (every 15 s)
POSTs every new item event (optionally filtered by type) and alert since the endpoint's **cursor**,
in batches of `batchSize`, as:

```json
{
  "endpoint": "ERP", "sentAt": "…", "tenantId": "…",
  "events": [ { "id", "type": "Moved", "occurredAt", "item": { "id", "identifier", "name", "type": "ASSET", "state", "status", "quantity", "lotNumber", "attributes", "epc" },
               "fromLocation": { "name", "code", "kind" }, "toLocation": {…}, "fromParty": {…}, "toParty": {…}, "fromState", "toState", "operationId", "deviceId", "data": {…} } ],
  "alerts": [ { "id", "severity", "message", "status", "raisedAt", "item": {…}, "location": {…} } ]
}
```

Headers: `X-Rfid-Endpoint`, `X-Rfid-Timestamp`, `X-Rfid-Signature: sha256=<hex HMAC-SHA256(secret, body)>`
plus any custom headers configured on the endpoint (e.g. an API key). A 2xx advances the cursor;
anything else backs off exponentially (10 s … 1 h) and retries — the cursor never moves past
undelivered data. `POST /api/integrations/{id}/deliver` forces an immediate attempt; the endpoint's
cursor can be rewound to replay history.

Rules can also call a webhook directly (action `Webhook`) for single, immediate notifications.

Per-endpoint filters: `eventTypes` (none = all), `itemTypeCodes` (none = all), `siteLocationId`
(events at or about items in that site subtree), `includeAlerts`. `GET /api/integrations` shows the
cursor, the in-flight outbox message, failure count and last error; `POST /api/integrations/{id}/deliver`
builds the next batch and pushes the outbox immediately.

## Workflows

`GET /api/workflows` lists the guided workflows (template-shipped and tenant-defined); Admins manage
them with `POST/PUT/DELETE /api/workflows`. A step is `{ key, title, prompt, operation, ask[],
fixed{toLocationId, partyId, targetState, containerItemId, reference}, rescan, optional, onRejected }`.
Clients execute a run by posting each step as an ordinary operation:

```json
POST /api/operations
{ "operation": "Sterilise", "workflowRunId": "wf-…", "workflowCode": "cssd-reprocess", "workflowStep": "sterilise",
  "clientId": "wf-…:sterilise", "toLocationId": "…", "lines": [{ "epc": "3034…" }] }
```

`GET /api/workflows/runs` groups operations by run id (steps done, ok/rejected lines, completed);
`GET /api/workflows/runs/{runId}` returns the steps and lines. Because steps are operations, runs
work offline through the handheld queue and are idempotent per step.

## Edge configuration pull

An edge agent authenticated with its gateway device token calls `GET /api/edge/config` and receives
the readers assigned to it (`edgeManaged: true`, `edgeGatewayId: <gateway>`) with their LLRP
options and a content `revision`; the agent reconnects only readers whose options changed and keeps
the last configuration on disk for offline restarts. Administrators can inspect a gateway's
configuration with `GET /api/edge/config?gatewayId=…`.

## Rules: schedules and operations

Rules have a `kind`. **Event** rules react to one item event (trigger = event type). **Schedule**
rules (`intervalMinutes`) sweep every live item on that interval and evaluate the same conditions,
with time-derived fields: `item.hoursSinceSeen`, `item.daysSinceSeen`, `item.daysUntilExpiry`,
`item.daysUntilInspection`, `item.daysSinceInspection`, `item.cyclesRemaining`, `item.daysOverdue`,
`item.ageDays`. Alert actions fire once per (rule, item) while the alert is open.

Action `RunOperation` runs any operation definition for the matching item — `params:
{operation: "Dispose" | "Maintain" | "Sterilise" …, toLocationId?, partyId?, targetState?, reference?}`
(templates in `reference`/`targetState` may use `{item.identifier}` etc.). The operation is queued in
the outbox and executed after the triggering change is committed, once per message
(`operations.ClientId = rule:…`); a business rejection is recorded on the operation, not retried.
Examples: *cyclesRemaining ≤ 0 → Dispose*; *Inspected with data.result contains Fail → Maintain to
the workshop*; *hoursSinceSeen > 48 → alert*.

### Vendor formats & authentication

`Format` selects the payload shape and `AuthType` the credential handling (both editable in the UI):

| Format | Shape | Mapping keys |
|---|---|---|
| `Generic` | The envelope above (camelCase) | – |
| `SapAssetManagement` | `MessageHeader`, `AssetMaster[]` (CompanyCode, AssetNumber, AssetDescription, AssetClass, CostCenter, Location, AcquisitionValue, CapitalizationDate, InventoryNumber = EPC…), `AssetTransfer[]` (custody changes), `AssetMovement[]`, `AssetRetirement[]`, `Notifications[]` (alerts → M1/M2) | `companyCode`, `plant`, `costCenterAttribute` |
| `Dynamics365` | `value[]` of `msdyn_customerasset` records (serial number = identifier, EPC, location, state, custodian) + `transactions[]` + `alerts[]` | `accountId`, `siteId` |
| `Maximo` | `MXASSET.ASSET[]` (ASSETNUM, DESCRIPTION, SITEID, ORGID, LOCATION, STATUS, SERIALNUM, RFIDTAG, PURCHASEPRICE, ASSETTRANS[]) + `MXSR.SR[]` for alerts | `siteId`, `orgId` |

Vendor formats keep the vendor's exact field names. They are starting points: point the endpoint at
your middleware (SAP CPI/PI, Power Automate/Dataverse Web API, Maximo MIF/OSLC) and adjust the
mapping there or extend the formatter.

Auth: `None` (signature only), `Bearer` (`ApiToken`), `Basic` (`Username`/`Password`), or
`OAuth2ClientCredentials` (`TokenUrl`, `ClientId`, `ClientSecret`, `Scope`; tokens are cached until
expiry). Authentication failures back off like delivery failures.

### Inbound ERP sync (asset master → items)

`POST /api/import/items` with `{csv: "...", dryRun, defaultType, createMissingLocations, createMissingParties, updateExisting, source}`
(or `{json: "[...]"}` / `{rows: [...]}`), or raw CSV to `POST /api/import/items/csv?dryRun=&defaultType=`.
Columns are matched loosely (`AssetNumber|Identifier|SerialNumber`, `Description|Name`, `AssetClass|Type`,
`Location|Room`, `Owner|Custodian|AssignedTo`, `AcquisitionValue|Cost`, `CapitalizationDate|PurchasedAt`,
`EPC|RFID|Tag`, `Quantity`, `Lot`, `Expiry`); other columns become attributes. Rows upsert by
identifier, can create locations/custodians, bind EPCs, and record an `Imported` event per change.
`dryRun` returns the per-row change list without writing. `POST /api/import/reconcile` compares an ERP
list with the platform: matched count, identifiers missing on either side and field differences
(name, location, custodian, state, cost, quantity). Reports: `imports`. Web: **ERP import**.

## Pull-style exports & reports

`GET /api/reports` lists the catalog; `GET /api/reports/{code}?format=csv|json&days=…&from=…&to=…`.
Reports: `inventory`, `missing`, `custody`, `overdue`, `expiry`, `inspection`, `cycles`,
`depreciation` (fixed-asset register with straight-line NBV from `Item.Cost`, `PurchasedAt`,
`ItemType.UsefulLifeMonths`), `movements`, `stocktakes`, `reads`, `dwell`.

## Scheduled stocktakes

`POST /api/stocktake-schedules {name, locationId, itemTypeId?, intervalDays, timeOfDay:"06:00", enabled, autoReconcileHours?}`.
A background service opens the stocktake when due (raising an *Info* alert) and, if `autoReconcileHours`
is set, reconciles it automatically after that window so missing items get flagged without anyone
pressing a button.

## Labels & encoding

- `GET /api/labels/items/{id}` → ZPL for the item (default 4×2" layout: `^RFW` RFID encode of the
  EPC, Code-128 of the identifier, name/type/location). Item types can carry their own `LabelTemplate`
  with placeholders `{name} {identifier} {epc} {type} {location} {state} {lot} {expiry} {attributes.x}`.
- `POST /api/labels/print {itemIds, printerDeviceId, template?}` → sends ZPL to a `Printer` device
  (`Config.host`, `Config.port` default 9100) — Zebra ZT411R / ZD621R and compatible RFID printers.
- Encoding and decoding live under `/api/encoding/*` (batches, serial pools, `GET /api/encoding/decode/{epc}`)
  and `GET /api/tags/describe/{code}` for anything a scanner produces. The older
  `/api/tags/encode*` and `/api/tags/decode*` endpoints were removed in v2.0.

### Label designer

**Label designer** (web) edits a declarative design — label size, printer density, RFID encode flag
and elements (text, Code-128 barcode, QR, box, line) positioned in millimetres with placeholders — and
compiles it to the item type's ZPL template (`PUT /api/labels/designs/item-types/{id}`,
`POST /api/labels/designs/compile` for previews). Items of that type print with the design from the
web (item page) or the handheld.

### Print queue, reprint audit & label stock

`POST /api/labels/print` now enqueues `PrintJob`s (exact ZPL stored per job) and processes them
immediately; a worker retries failed jobs with back-off (5 attempts) and every successful print
records a `LabelPrinted` event on the item. Jobs for an item that already has a printed label are
classified as **Reprint** automatically; `reason` may also be `Replacement`/`Batch` with a `note`.
`GET /api/labels/jobs?itemId=&printerId=&status=`, `POST /api/labels/jobs/{id}/retry|cancel`,
`POST /api/labels/jobs/process`. Printer devices track `labelStock`/`labelStockMin` in Config
(`POST /api/labels/printers/{id}/stock {count, minimum}` after loading a roll); each printed copy
decrements the count and a Warning/Critical alert fires at the minimum / at zero. Report:
`print-jobs`. Web: **Print queue**.

### Printing from the handheld

Settings → **Label printer**: pick a network `Printer` device (the server renders and sends ZPL) or
*Bluetooth (mobile)* for a paired mobile printer (ZQ630R/ZQ520-class) through the `BtPrinter` native
module (`print(zpl)`); the app fetches the ZPL from `GET /api/labels/items/{id}`. Print buttons live on
the Lookup screen and after a successful Commission.

## Position history, heat maps & replay

Every position update that moves an item by more than 0.5 m, changes zone, or is more than 30 s
after the previous sample writes a `position_fixes` row (pruned after `Positions:RetentionDays`,
default 30). Endpoints:

| Endpoint | Purpose |
|---|---|
| `GET /api/positions/heatmap?locationId&from&to&cellM=1` | Dwell seconds per grid cell (`cells[]: ix, iy, x, y, samples, seconds, items`) |
| `GET /api/positions/history?itemId&from&to` | Ordered fixes for one item (path replay) |
| `GET /api/positions/history/items?locationId&from&to` | Items with recorded paths in a floor plan |

## Login

`POST /api/auth/login {email, password, tenantCode?}`. `tenantCode` is only needed when the same
e-mail exists in more than one tenant; without it such a login is refused as ambiguous.

## Single sign-on (OIDC)

```json
"Oidc": { "Enabled": true, "Authority": "https://login.microsoftonline.com/<tenant>/v2.0", "ClientId": "<spa-client-id>",
          "Audience": null, "Scopes": "openid profile email", "RoleClaim": "roles",
          "RoleMap": { "rfid-admins": "Admin", "rfid-operators": "Operator" }, "DefaultRole": "Viewer",
          "TenantCode": "demo", "AutoProvision": true }
```

Register the web app as a public (SPA) client with redirect URI `https://<web-host>/auth/callback`.
The API validates the provider's tokens (`Authority` discovery, audience = `Audience` or the client
id) as a second bearer scheme; on each request the external identity is mapped to a platform user
(matched by subject, then e-mail; provisioned when `AutoProvision` is on) and the `RoleClaim`/`groups`
values are translated with `RoleMap` (highest wins; `DefaultRole` when nothing matches). Password
login stays available; `GET /api/auth/config` tells clients whether SSO is on.

## Per-site access

`PUT /api/users/{id}/sites` `{ "restrictToSites": true, "sites": [{ "siteLocationId": "…", "role": "Operator" }] }`
scopes a user to location subtrees. Their JWT carries `restricted=true` and one `site` claim per
site; `ISiteAccess` filters item/location/device queries and `EnsureAsync(locationId, role)` guards
operations and stocktakes. Locations outside every site are refused; a user's global role still
applies to operations with no location. Device tokens are never restricted.

## Multi-node operation

Run several API containers behind a load balancer (`docker compose up --scale api=3`). Background
work is coordinated with leases in `worker_leases` (`GET /api/cluster` lists them; `POST
/api/cluster/leases/{name}/release` hands one over). Set `Redis:ConnectionString` (e.g.
`redis:6379`) so SignalR live updates reach clients connected to any node. Handheld apps can
pre-download master data and floor plans (Settings → *Download for offline*); GET responses are
served from the device cache whenever the server is unreachable.

## Reader health & firmware

Devices post `POST /api/devices/{id}/heartbeat` `{ firmwareVersion, cpuPercent, temperatureC, readsPerMinute, batteryPercent, metrics }`
(device tokens can use `POST /api/devices/heartbeat`). LLRP readers are sampled automatically with the
firmware version from `GET_READER_CAPABILITIES`. Health = age of the last heartbeat or read versus the
device SLA (`heartbeatSlaMinutes`, default `DeviceHealth:DefaultSlaMinutes` = 15; handhelds and
printers 24 h): Online ≤ SLA, Degraded ≤ 2×SLA, Offline beyond (critical alert, cleared on return).

Firmware: `POST /api/firmware/releases` `{ vendor, model, version, url, checksum, notes }`, then
`POST /api/firmware/releases/{id}/rollout` `{ deviceIds, scheduledAt? }`. Devices poll
`GET /api/devices/{id}/firmware/pending` (→ `{ rolloutId, version, url, checksum }`) and report with
`POST /api/firmware/rollouts/{id}/report` `{ status: Downloading|Installing|Done|Failed, error }`.

## GPS & geofences

`POST /api/ingest/gps` `{ deviceId?, fixes: [{ itemId? | epc? | identifier? | deviceId?, lat, lng, speedKph?, headingDeg?, accuracyM?, at? }] }`.
A telematics unit is linked to its vehicle/container with the device's `trackedItemId`. Fixes are
stored when the item moved ≥ 10 m or 5 min passed; every fix is tested against enabled fences:

- enter/exit → `GeofenceEntered` / `GeofenceExited` events (rules can trigger on them), alerts per the
  fence's trigger and severity, and a move to the fence's linked location on entry;
- `maxDwellMinutes` → one dwell alert when exceeded (checked every minute).

Fences: `GET/POST/PUT/DELETE /api/geo/fences` (`kind: Circle` with `centerLat/centerLng/radiusM` or
`Polygon` with `points`). Map data: `GET /api/geo/map?hours`, tracks: `GET /api/geo/track?itemId&from&to`.
Tiles default to OpenStreetMap; set `VITE_MAP_TILES` / `VITE_MAP_ATTRIBUTION` for your own tile server.

## Notification channels & escalation

| Kind | Config keys |
|---|---|
| Email | `host`, `port`, `useTls`, `username`, `password`, `from`, `to` (comma list) |
| Sms | `accountSid`, `authToken`, `from`, `to` (comma list), `apiBase` (Twilio-compatible REST) |
| Teams / Slack | `url` (incoming webhook; Teams receives an Adaptive Card) |
| Webhook | `url` (JSON `{ subject, body, severity, alertId, itemId, link, at }`) |

Rules list `notifyChannelIds` and an `escalationPolicyId`; channels with `catchAll` receive every
alert at or above `minSeverity`. An escalation policy is an ordered list of steps
`{ afterMinutes, channelIds, message }`; the first step with `afterMinutes: 0` fires immediately,
later steps fire while the alert is still open and unacknowledged (`repeatLastStep` keeps
re-notifying). A policy marked `isDefault` applies to alerts raised without a rule (device health,
geofence, firmware). `GET /api/notifications/log` is the delivery audit; `POST
/api/notifications/channels/{id}/test` sends a test message. `Notifications:BaseUrl` is the link
included in messages.

## Warehouse export

| Setting | Meaning |
|---|---|
| `Warehouse:Enabled` | run the incremental export every `Warehouse:IntervalMinutes` |
| `Warehouse:Path` | landing zone root (default `./warehouse`; mount an object-storage gateway or NFS here) |
| `Warehouse:Format` | `parquet` (default) or `csv` |
| `Warehouse:InitialDays` | how far back the first incremental run reaches |

Files land at `{Path}/{tenant}/{dataset}/dt=YYYY-MM-DD/{dataset}-{from}-{to}-{run}.parquet`. Datasets:
`items` (daily snapshot), `events`, `reads`, `alerts`, `operations`, `position_fixes`, `gps_fixes`,
`presence_sessions`, `stocktakes`, `device_heartbeats`. `POST /api/warehouse/export` writes one window
on demand, `POST /api/warehouse/run` triggers the incremental pass, `GET /api/warehouse/download`
streams a window to the caller. Analytics endpoints: `/api/analytics/trend|utilization|dwell|accuracy|alert-response|uptime`.

## GS1 encoding at scale

Serial pools (`/api/encoding/pools`) fix scheme, company prefix, reference and filter and hand out
serials atomically: `POST /api/encoding/pools/{id}/allocate` `{ count }` → `{ epcs, firstSerial, lastSerial }`
(used by the handheld's Commission screen and by external label printers). The wizard calls
`POST /api/encoding/preview` then `POST /api/encoding/commit` with
`{ scheme, companyPrefix, reference, filter, poolId? | firstSerial?, count, mode: tags|bind|items, itemTypeId?, itemIds?, identifierPrefix?, printerDeviceId? }`.
Schemes: SGTIN-96 (reference = item reference), GRAI-96 (asset type), GIAI-96 (serial = asset
reference), SSCC-96 (reference = extension digit). `GET /api/encoding/decode/{epc}` identifies and
decodes any of them; `GET /api/encoding/schemes?companyPrefix=` returns digit rules for validation.

## Anomaly detection

`Anomaly:*` settings: `IntervalMinutes` (15), `BaselineDays` (14), `ZThreshold` (3), `AlertZ` (4),
`MinReads` (20). Detectors: read-rate spike/drop per device versus the same hour of day in the
baseline; off-hours activity per location (hours carrying < 2 % of baseline reads); unknown-tag
surge per device; item flapping (≥ 6 moves between the same two locations in 24 h); excessive
movement versus the item type's daily average. `GET /api/anomalies`, `POST /api/anomalies/run`,
`POST /api/anomalies/{id}/confirm|dismiss` (dismiss closes the linked alert), `GET /api/anomalies/profile?deviceId|locationId`.

## Audit log & retention

Every POST/PUT/DELETE under `/api` (except ingest, auth and heartbeats) is recorded in
`audit_entries` with `password`/`token`/`authToken`/`clientSecret`/`apiKey` masked and bodies
truncated at 4 KB. `GET /api/audit?q&userId&action&entityType&entityId&from&to&page` and
`GET /api/audit/summary`. Retention: `GET/PUT /api/retention`, `POST /api/retention/run`;
`Retention:IntervalMinutes` (360). Defaults: tag_reads 90 d, position_fixes 30 d, gps_fixes 90 d,
presence_sessions 180 d, device_heartbeats 30 d, notification_logs 90 d, audit_entries 365 d, closed
alerts 365 d, anomalies 90 d, print_jobs 90 d, warehouse_runs 180 d, item_events never.

## Handheld GPS

Settings → **GPS** enables `expo-location`; after a free scan or an online operation the app posts
`POST /api/ingest/gps` with one fix per EPC, so geofence rules apply to what the handheld saw. The
**Map & geofences** screen draws the cached fences around the device on a metre grid (no tiles
needed) and flags restricted (Critical) zones.

## Barcodes & hybrid flows

Any scanner input works wherever an EPC is accepted (`epc` fields in reads, operations, stocktakes):
hex EPCs are matched as tags; GS1-128 / DataMatrix element strings (`(01)…(21)…`, FNC1 payloads with
symbology identifiers) and GS1 Digital Links are parsed and matched to SGTIN/SSCC/GRAI/GIAI tags across
every company-prefix length; other strings match barcode tags (`technology: Barcode`, bound via
`POST /api/items/{id}/tags` with `symbology`) or the item identifier. `GET /api/items/by-code/{code}`
resolves a single code and `GET /api/tags/describe/{code}` explains what it is.

## Returnable-asset billing

Rate cards (`/api/billing/rate-cards`): `depositAmount`, `cycleFee` (per issue), `dailyFee` after
`freeDays`, `lateFeePerDay` past the operation's `dueBackAt`, `lossFee` when disposed while in
custody; scoped by party, party kind and/or item type (most specific wins). The hourly accrual
(`POST /api/billing/accrue` to run now) writes `LedgerEntry` rows once per custody event.
`POST /api/billing/invoices/generate { from, to, partyId?, dueDays }` creates draft invoices from
unbilled entries; `POST /api/billing/invoices/{id}/issue|pay|void`; `GET …/print` returns HTML.
`GET /api/billing/balances`, `GET /api/billing/ledger?partyId&unbilled`.

## Supplier / customer portals

Create a user with `portalPartyId` (Users page → *Portal account for party*). Their JWT carries a
`portal` claim; the API only serves `/api/portal/*` (`me`, `items`, `activity`, `invoices`, `ledger`,
`invoices/{id}/print`) and `/api/auth/*`, and the web app switches to the portal UI automatically.

## Predictive maintenance

`GET /api/maintenance/predictions?itemTypeId&minRisk` scores active items whose type has a cycle
limit or inspection interval, or that have inspection/maintenance history. Factors (weights): cycle
wear 30, inspection failures 25, inspection due 15, service-interval drift 15, usage intensity 10,
open alerts 10, age 5. `Maintenance:AlertRisk` (70) raises one warning alert per item;
`Maintenance:IntervalMinutes` (1440) snapshots forecasts for `GET /api/maintenance/predictions/{itemId}` trends.

## EPCIS 2.0

Business steps and dispositions are **data on the operation definition**: every built-in carries
its CBV vocabulary (`Receive` → `receiving`, `Dispatch` → `shipping`/`in_transit`, `Dispose` →
`destroying`/`destroyed`, …) and template/tenant definitions may set their own (`Sterilise` →
`sterilizing`/`sterile`). Operations stamp `bizStep`/`disposition` into their events' `data`; the
EPCIS projection uses them and falls back to the event type's default for reader-produced events.
Capture maps an inbound `bizStep` back to the definition that declares it (tenant definitions win
over built-ins), else `Transfer`.

`GET /api/epcis/v2` (discovery), `GET /api/epcis/v2/events` with `EQ_bizStep`, `EQ_disposition`,
`EQ_action`, `MATCH_epc`, `GE_eventTime`, `LT_eventTime`, `eventType`, `EQ_readPoint`, `perPage`,
`page` (responds `application/ld+json` with `GS1-EPCIS-Version: 2.0`, `X-Total-Count` and a `Link`
next page), `GET /api/epcis/v2/events/{eventId}`, `GET /api/epcis/v2/epcs/{epc}/events`,
`POST /api/epcis/v2/capture` (EPCISDocument; OBSERVE → reads at the read point, receiving/shipping/
destroying → Receive/Dispatch/Dispose operations at the bizLocation; 200 / 207 / 400) and
`GET /api/epcis/v2/capture/{id}`. Give locations an `sgln` attribute to emit/accept
`urn:epc:id:sgln:…` read points. Set `Epcis:BaseUrl` for the URIs used for locations, parties and items without GS1 keys.

## Template customisation

`GET /api/templates/export` returns the tenant's item types, lifecycles and rules as a template
definition; `POST /api/templates/import` applies one (skipping codes/names that already exist). Use it
to copy a configured vertical to another tenant, keep configuration in source control, or contribute a
new built-in template.
