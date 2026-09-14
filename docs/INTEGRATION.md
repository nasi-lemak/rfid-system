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
| LLRP-only readers | Use the vendor's LLRP-to-MQTT/HTTP bridge (Impinj IoT Interface, Zebra IoT Connector, Keonn, Nordic ID) or a small edge agent that posts to `/api/ingest/generic`; a native LLRP client is not included. |

Antenna → location mapping (with `In` / `Out` direction for portals) turns raw reads into zone
movements and rule evaluation. When several antennas or gateways see one tag in the same batch, the
**strongest RSSI** decides the zone ("nearest reader" resolution for BLE / active tags).

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

## Template customisation

`GET /api/templates/export` returns the tenant's item types, lifecycles and rules as a template
definition; `POST /api/templates/import` applies one (skipping codes/names that already exist). Use it
to copy a configured vertical to another tenant, keep configuration in source control, or contribute a
new built-in template.
