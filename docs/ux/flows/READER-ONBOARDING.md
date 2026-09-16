# Flow — Reader and gateway onboarding

How a fixed reader (or an edge gateway with its readers) goes from a box to "reads arrive at the right
zone", the states it passes through, which the current UI exposes, and the wizard that should replace
the JSON field.

Step tags: `[USER]` user action · `[SYS]` system action · `[HW]` hardware event · `[AUTO]` background automation.

## 1. Connection modes (facts)

| Mode | Who opens the connection | Server-side config (device `Config` keys) | Where reads enter |
|---|---|---|---|
| **Server-driven LLRP** | the API (`LlrpReaderService` supervisor) | `llrpHost`, `llrpPort` (5084), `llrpPower`, `llrpSession`, `llrpTagPopulation`, `llrpAntennas`, `llrpGpiStart`, `llrpEnabled` | internal |
| **Edge-driven LLRP** | the on-site edge agent | `edgeManaged: "true"`, `edgeGatewayId: <gateway device id>` + the same `llrp*` keys (pulled by the agent from `GET /api/edge/config`) | `POST /api/ingest/reads` with the gateway token |
| **Vendor push** (Impinj IoT Interface, Zebra IoT Connector, generic JSON) | the reader posts HTTP | none on the device beyond identity; the reader is configured (on its own web UI) to post to `/api/ingest/impinj?deviceId=…` (or to the edge agent's listener) | vendor shim endpoints |
| **MQTT** | the reader publishes; server subscriber or edge bridge subscribes | `mqttTopic`, `vendor` | `MqttIngestService` / edge `MqttBridge` |
| **Handheld** | the app | none | `POST /api/ingest/handheld`, `/api/operations`, `/api/stocktakes/{id}/scans` |

Health for all modes comes from heartbeats (`POST /api/devices/{id}/heartbeat`, or the gateway's
`POST /api/devices/heartbeat`) and `LastSeenAt` on reads; SLA 15 min (fixed) or 24 h (handheld/printer).

## 2. Flow — server-driven LLRP reader (as built)

```
[USER]  Readers & devices → + New device: name, kind Fixed, serial, model, site
[USER]  + Add antenna ×n: port, location (zone), direction (None/In/Out), power dBm, x/y (for RTLS)
[USER]  Config JSON: {"llrpHost":"192.168.10.21","llrpPower":"27","llrpAntennas":"1,2"}   ← the cliff
[USER]  Save
[SYS]   device row created; LlrpReaderService notices the new endpoint on its next supervision tick
[SYS]   connects to llrpHost:5084 → GET_READER_CAPABILITIES → ROSpec with power/session/antennas → ENABLE
[HW]    reader accepts; starts reporting tag reports
[SYS]   ReadIngestionService: EPC → item; antenna → location; direction → Moved/Seen; presence; rules; live hub
[USER]  Live reads shows the EPC, device, location, RSSI          ← the only "it works" confirmation
[AUTO]  heartbeat/LastSeen keep Health = Online; SLA breach → Degraded → Offline (+ alert if a rule/SLA raises one)
```

## 3. Flow — edge gateway with readers (as built)

```
[USER]  + New device kind Gateway "Hangar 3 agent" → Issue token → copy (shown once)
[USER]  for each reader: Edit → Config JSON add "edgeManaged":"true","edgeGatewayId":"<gateway guid>"
[USER]  on the box: appsettings / env Edge__Server__Url, Edge__Server__DeviceToken, Edge__Queue__Path; start agent
[AUTO]  agent POST /api/auth/device → 30-day JWT; GET /api/edge/config → reader list (revision hash)
[AUTO]  agent connects LLRP to each reader; buffers reads 2 s / 500; writes batch files; forwards in order
[SYS]   POST /api/ingest/reads with batchId agent:seq → idempotent ingest; duplicate → duplicate:true
[AUTO]  agent heartbeat every 60 s: queuePending, queuePoison, delivered, duplicates, lastError, readers[].connected
[SYS]   gateway Health Online; per-reader heartbeats too (firmware from capabilities)
[USER]  web: device card badge "Edge agent"; Health tab shows Online/Offline — metrics not rendered (Missing UX)
```

## 4. Onboarding states and what the UI exposes

| State | How it is known | Exposed on web today | Should be |
|---|---|---|---|
| **Registered, never connected** | `Health = Unknown`, no `LastSeenAt` | badge `Unknown` on the card | "Waiting for first connection" with the endpoint it will try |
| **Connecting / unreachable host** | LLRP supervisor retrying; `connected: false` | only in *Status / GPIO* modal (`connected: false`), no reason | card state "Cannot reach 192.168.10.21:5084" with last error |
| **Connected, no antennas mapped** | reads recorded with no location | nothing | warning on the card "antenna 1 has no location" (E-8) |
| **Connected, reads arriving** | `tagsReceived` grows; Live reads | Live reads; LLRP modal counter | "Last read 3 s ago · 412 tags today" on the card |
| **Online** | heartbeat/read within SLA | badge `Online` | same |
| **Degraded** | age ≤ 2×SLA | badge `Degraded` | same + "last heartbeat 22 min ago" |
| **Offline** | age > 2×SLA | badge `Offline`; alert if configured | same + reason if known (agent `lastError`) |
| **Paused** (`llrpEnabled: false`) | endpoint null | nothing distinct (looks Unknown) | badge `Paused` |
| **Edge-managed** | `edgeManaged` | badge *Edge agent* | plus gateway name and link |
| **Gateway queue backing up** | heartbeat `queuePending` | not rendered | gateway card "1 240 batches queued · WAN down since 10:12" |
| **Gateway poison batches** | heartbeat `queuePoison` | not rendered | gateway card "3 rejected batches" (inspect on the box) |
| **Reader on both edge and server** | `edgeManaged` unset while agent drives it | nothing; double reads | agent heartbeat lists reader ids → server can flag "driven by agent but not marked edge-managed" (small backend check) |
| **Firmware updating** | `Updating` sticky | badge | same |
| **Config revision applied** | heartbeat `configRevision` vs server revision | not rendered | "config up to date / pending" on gateway card |

## 5. Failure states and recovery

| Failure | Detect | Recover | Auto? |
|---|---|---|---|
| Wrong host/port | never connects | fix Config | supervisor keeps retrying |
| Reader rebooted | connection drops | — | LLRP client reconnects; edge agent reconnects (30 s supervision) |
| WAN down (edge) | gateway Degraded/Offline; queue grows | — | yes; backlog drains in order; `MaxBatches` 20 000 then oldest → poison |
| Bad batch (4xx) | poison file | inspect/delete on the box | delivery continues |
| Token revoked (Rotate token on the gateway) | 401 → agent re-logs in with the *old* token and fails | update the agent's token | no |
| Antenna cable unplugged | reads stop on that port | — | none (no per-antenna heartbeat; **Missing product capability**: antenna-level health, Optional) |
| Reader clock wrong | `readAt` far off; late-read logic ignores state regression | set NTP; LLRP `HasUtcClock` in capabilities | partially |

## 6. Wireframe — reader onboarding wizard (Recommended improvement, not built)

```
┌──────────────────────────────────────────────────────────────────────────────┐
│ Add a reader                                              step 2 of 4        │
│ ● Identity   ● Connection   ○ Antennas & zones   ○ Verify                    │
├──────────────────────────────────────────────────────────────────────────────┤
│ How does this reader deliver reads?                                          │
│ (•) Platform connects to it (LLRP)      Host [192.168.10.21] Port [5084]     │
│ ( ) An on-site edge agent drives it     Gateway [Hangar 3 agent ▾]           │
│ ( ) The reader pushes (Impinj / Zebra / generic JSON) → shows the URL to     │
│     configure on the reader                                                  │
│ ( ) MQTT topic  [rfid/site1/dock1]  vendor [impinj ▾]                        │
│                                                                              │
│ Power [27] dBm   Session [2 ▾]   Antennas [1] [2] [ ] [ ]                     │
│                                                       [ ‹ Back ] [ Next › ]  │
└──────────────────────────────────────────────────────────────────────────────┘

Step 3 – Antennas & zones                    Step 4 – Verify
┌───────────────────────────────────────┐    ┌───────────────────────────────────────────┐
│ Port  Zone                Direction   │    │ ✓ Connected to 192.168.10.21 (Impinj R700)│
│ 1     [Dock 1        ▾]   [In  ▾]     │    │ ✓ 2 antennas mapped                        │
│ 2     [Dock 1 outside▾]   [Out ▾]     │    │ ⧗ Waiting for the first read … hold a tag │
│ + add antenna                          │    │   near antenna 1                           │
│ ⓘ Out moves items to the zone's parent│    │   E200 3412 … · antenna 1 · −48 dBm ✓     │
└───────────────────────────────────────┘    │                     [ Finish ]             │
                                             └───────────────────────────────────────────┘
```

All fields map to existing `DeviceWrite` + `Config` keys and antenna rows; verification uses the existing
LLRP status endpoint and the live hub. **Backend work: none.**
