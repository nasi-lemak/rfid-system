# Integration guide

## Inbound: getting reads into the platform

All readers authenticate with a **device token** (Admin → Readers & devices → *Issue token*), exchanged
for a JWT at `POST /api/auth/device {"token": "..."}`. Handhelds may also use a user login.

| Source | How |
|---|---|
| Handheld app | `POST /api/operations` (business transactions), `POST /api/ingest/handheld` (free-scan sightings), `POST /api/stocktakes/{id}/scans` |
| Any reader / edge agent | `POST /api/ingest/reads` `{deviceId?, sessionId?, reads:[{epc, tid?, antennaPort?, rssi?, readAt?, locationId?}]}` |
| Impinj R700 (IoT Interface) | Point the reader's HTTP POST / webhook at `POST /api/ingest/impinj?deviceId=<id>` — `tagInventoryEvent` payloads (single or array) are accepted as-is (`epcHex` or base64 `epc`, `peakRssiCdbm`, `antennaPort`, `tidHex`, `timestamp`) |
| Zebra FX7500 / FX9600 (IoT Connector) | HTTP POST endpoint → `POST /api/ingest/zebra?deviceId=<id>` (`data.idHex`, `antenna`, `peakRssi`, `TID`) |
| Anything else that can POST JSON | `POST /api/ingest/generic` — `{reads:[…]}`, `[…]`, a single read object, or an array of EPC strings; fields `epc/epcHex/idHex/tag/id`, `rssi/peakRssi`, `antennaPort/antenna/port`, `readAt/timestamp/time` |
| **MQTT** | Set `Mqtt:Enabled=true`, `Mqtt:Host`, `Mqtt:Topics` in `appsettings.json` (or env `Mqtt__Enabled` …). Give each device a `Config.mqttTopic` (exact topic or `+`/`#` wildcard pattern) and optionally `Config.vendor` (`impinj`/`zebra`). Payloads on matching topics go through the same adapters. |
| **LLRP** (native client) | Set `Config.llrpHost` (and optional `llrpPort`, default 5084) on a Fixed/Portal/Gate device. The API's `LlrpReaderService` connects to the reader, resets its configuration, adds/enables/starts a continuous Gen2 inventory ROSpec on all antennas and streams `RO_ACCESS_REPORT`s (EPC-96 / EPCData, antenna, peak RSSI, timestamps, seen count) into ingestion; keepalives are acknowledged and connections re-established automatically. Works with Impinj Speedway/R700 (LLRP mode), Zebra FX-series, Alien and other LLRP 1.0.1 readers. Set `llrpEnabled: false` to pause. **Configuration** (device Config): `llrpPower` (dBm, mapped to the reader's power table from `GET_READER_CAPABILITIES`), `llrpSession` (Gen2 S0–S3), `llrpTagPopulation`, `llrpAntennas` ("1,2"), `llrpGpiStart` (start inventory while a GPI is high — photo-eye/conveyor portals), `llrpReportEveryN`. `GET /api/devices/{id}/llrp/status` shows connection, capabilities (manufacturer, firmware, antennas, GPIO count, power table); `POST …/llrp/gpo {port,state}` drives stack lights/buzzers; `POST …/llrp/reconnect` applies new settings. |

Antenna → location mapping (with `In` / `Out` direction for portals) turns raw reads into zone
movements and rule evaluation. When several antennas or gateways see one tag in the same batch, the
**strongest RSSI** decides the zone ("nearest reader" resolution for BLE / active tags).

## Positioning (x/y trilateration)

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
- `POST /api/tags/encode {scheme: "SGTIN-96"|"GRAI-96"|"GIAI-96", companyPrefix, reference?, serial, filter?}`
  and `GET /api/tags/decode-any/{epc}`.

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

## Template customisation

`GET /api/templates/export` returns the tenant's item types, lifecycles and rules as a template
definition; `POST /api/templates/import` applies one (skipping codes/names that already exist). Use it
to copy a configured vertical to another tenant, keep configuration in source control, or contribute a
new built-in template.
