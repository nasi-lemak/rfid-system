# Reader edge agent (`Rfid.Edge`)

The edge agent runs on site — on a small box, an industrial PC or the reader's own container host —
and makes fixed-reader ingestion **offline-first**: it drives LLRP readers and/or receives vendor
pushes locally, stores every batch on disk, and forwards batches to the platform in order with
monotonically increasing batch ids. When the WAN is down nothing is lost; when it comes back the
backlog drains and the server applies each batch exactly once.

```
 LLRP readers ──┐                     ┌──────────────┐   POST /api/ingest/reads   ┌────────────┐
 Impinj/Zebra ──┤→ ReadBuffer → EdgeQueue (disk) → │  Forwarder   │ ─────────────────────────► │  platform  │
 pushes         ┘   (2 s / 500 reads)   NNNNNNNNNN.json  └──────────────┘   batchId = agent:seq       └────────────┘
                                                                  ▲ 5 s·2ⁱ back-off, poison/ for 4xx
```

## What it shares with the server

The agent references only `Rfid.Protocols` — the LLRP codec/client, the vendor payload adapters
(Impinj IoT Interface, Zebra IoT Connector, generic JSON) and the ingest contract
(`ReadBatchRequest`, `ReadRequest`, `IngestResult`). The server uses the same library, so a batch
produced on the edge is byte-for-byte what the server's own LLRP supervisor would have produced.
No EF, no ASP.NET on the edge; the image is the .NET runtime plus a few hundred kilobytes.

## Guarantees

| Property | How |
|---|---|
| **Durable** | Each batch is a file written atomically (temp + rename); the sequence resumes from disk after a restart. |
| **Ordered** | One forwarder, oldest first; a transport failure stops the drain and backs off (5 s → 5 min) without reordering. |
| **Exactly-once effect** | `batchId = {agentId}:{sequence}`; the server stores the key with the reads in one commit and acknowledges replays with `duplicate: true`. |
| **Late-safe** | `readAt` is authoritative. A batch that arrives after newer reads is recorded as raw history but never moves item state backwards (`late` count in the result). |
| **Never blocked by one bad batch** | A 4xx (other than 401/408/429) moves the batch to `poison/` with the reason; delivery continues. |
| **Bounded** | Above `Queue.MaxBatches` the oldest pending batches are moved to `poison/` (newest data wins when the outage is very long). |
| **Observable** | Heartbeats to `/api/devices/heartbeat` (the gateway) and `/api/devices/{id}/heartbeat` (each reader) carry queue depth, poison count, delivered/duplicate counters, reader connection state. Health SLAs and alerts then cover the agent. |

## Setting it up

1. **Create a Gateway device** (Devices → New, kind `Gateway`) and issue its token. The agent
   authenticates with that token (`POST /api/auth/device`) and re-logs in on 401.
2. **Mark the readers it drives** as *Driven by an on-site edge agent* (device config
   `edgeManaged: true`). The server then never opens its own LLRP connection to them, and their
   antennas/locations keep working exactly as before because the agent attributes reads to the
   reader's `deviceId`.
3. **Configure the agent** (`appsettings.json` or environment variables):

```json
{
  "Edge": {
    "AgentId": "hangar-3",
    "Server": { "Url": "https://rfid.example.com", "DeviceToken": "<gateway token>" },
    "Queue": { "Path": "/queue", "MaxBatches": 20000, "MaxBackoffSeconds": 300 },
    "Flush": { "Seconds": 2, "MaxReads": 500 },
    "Listen": "http://0.0.0.0:8090/",
    "HeartbeatSeconds": 60,
    "Readers": [
      { "DeviceId": "5f4c…", "Host": "192.168.10.21", "Port": 5084, "PowerDbm": 27, "Session": 2, "Antennas": [1, 2], "GpiStartPort": null },
      { "DeviceId": "9a10…", "Host": "192.168.10.22" }
    ]
  }
}
```

Environment form: `Edge__Server__Url`, `Edge__Server__DeviceToken`, `Edge__Readers__0__DeviceId`,
`Edge__Readers__0__Host`, ….

4. **Run it**: `dotnet run --project src/backend/Rfid.Edge`, or the container
   (`docker compose --profile edge up` uses `src/backend/Dockerfile.edge`; mount `/queue`).

## Reader pushes

With `Listen` set, readers that push HTTP (Impinj IoT Interface, Zebra IoT Connector, or any JSON
with an `epc` field) post to the agent instead of the platform:

- `POST http://agent:8090/impinj?deviceId=<reader device id>`
- `POST http://agent:8090/zebra?deviceId=…`
- `POST http://agent:8090/generic?deviceId=…`

The agent answers `202 {"accepted": n}` immediately; the reads join the same buffer and queue.

## Operating notes

- The queue directory is the only state. Back it up or place it on persistent storage; deleting it
  loses undelivered batches (never the sequence: a fresh directory restarts at 1, which is safe
  because the batch id also carries the agent id — change `AgentId` if you redeploy a wiped agent
  against a server that already saw its ids).
- `poison/` holds rejected batches with a `.reason` file each; inspect and delete by hand.
- A reader that is both configured on the agent and not marked `edgeManaged` on the server will be
  read twice (once by each). The server-side badge *Edge agent* on the device card shows the flag.
- MQTT-publishing readers keep using the server's MQTT subscriber; an on-site broker bridge is the
  natural extension if MQTT must also survive WAN loss.
