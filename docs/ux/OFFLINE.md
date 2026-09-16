# Offline UX

Offline is a normal operating state for a handheld in a warehouse or a hospital basement and for a
fixed-reader site whose WAN drops. This document states what the platform does today and how the
operator should experience it.

## 1. What is offline-safe today (facts)

| Surface | Action | Offline behaviour today |
|---|---|---|
| Handheld | Operations (Transfer, Issue, Sterilise…), Commission, Workflow steps | Queued on the device with a `clientId`, replayed in order, results matched by id; server de-duplicates by `clientId` |
| Handheld | Stocktake scans, "Post as sighting", GPS fixes, label printing, lookups, history | **Not queued** — inline error; the operator must retry when back online |
| Handheld | Master data (locations, parties, item types, floor plans, fences, operation definitions, workflows) | Cached network-first; served from cache when the network fails; explicit "Download for offline" in Settings |
| Edge gateway | Reader reads (LLRP, pushes, MQTT bridge) | Written to disk, forwarded in order with monotonic `batchId`; server acknowledges replays and never regresses state on late batches |
| Edge gateway | Reader configuration | Last pulled configuration kept on disk; local config is the baseline |
| Web | everything | Requires the server; no offline mode (by design) |

## 2. The operator's mental model (what we promise)

```
ONLINE                                  OFFLINE
operation ──► committed immediately     operation ──► "Offline – queued (3 pending)"
           ◄── result per line                      │
                                                    ▼ connection restored
                                        automatic sync (Home) ──► "Synced 3 queued operation(s)"
                                                    │
                                                    ├─ accepted: item state updated on the server
                                                    └─ rejected: kept in a rejected list with the server's reason
```

Rules the UI must honour:

1. The operator never decides whether to retry a transport failure; the app does.
2. A business rejection (invalid transition, unknown destination) is never retried; it is shown.
3. A queued operation is shown as *pending*, not as done.
4. The operator may keep working while items are pending.

## 3. What the operator can see today

| Question | Where | As built |
|---|---|---|
| How many operations are pending? | Home banner, Settings → Offline queue | yes ("N operation(s) waiting to sync") |
| Did the last sync succeed? | Home message after focus | partially ("Synced N…" only when something was sent) |
| When was the last successful sync? | — | **Missing UX** |
| Which operations failed and why? | Settings → Offline queue → rejected list; Home count | **Fixed** (G-H3); previously stored but not shown |
| Am I online right now? | — | **Missing UX** (no connectivity indicator; failure is discovered on submit) |
| Is a pending step part of a workflow run? | Workflow completion summary marks "(queued)" | partially |
| May I safely continue? | implied | yes, but not stated |

## 4. Recommended offline status block (Recommended improvement)

One compact block on Home and at the top of Settings → Offline queue:

```
┌──────────────────────────────────────────────┐
│ ● Online · last sync 2 min ago               │   ● grey "Offline since 10:42" when down
│ 3 pending · 1 rejected            [Sync now] │   [Sync now] forces past back-off
│ Rejected: Transfer of 2 tags — "TL-7 is     │   tap → rejected list with reasons and "Retry as new"
│ Disposed" · 10:31                            │
└──────────────────────────────────────────────┘
```

Backend work: none. The app already records `attempts`, `lastError`, `nextAttemptAt` and the
rejected list; `sync(force)` exists.

## 5. Failure semantics the UI should convey (facts)

| Situation | What happens today | What the operator should see |
|---|---|---|
| Server unreachable at submit | queued, attempt 1, retry in 5 s… up to 10 min back-off | "Offline – queued" (exists) |
| Server returns 5xx | queued (treated as transport failure) | same |
| Server returns 4xx business error | not queued, error shown | the server message (exists) |
| Batch response missing a result | item retried later | nothing visible (fine) |
| 20 attempts exhausted | moved to rejected: "Gave up after 20 attempts" | rejected list in Settings (**fixed**, G-H3) |
| Token expired (401) while syncing | The batch call fails as a whole, so the queue treats it like a transport error: every operation is retried with back-off and, after 20 attempts (roughly three hours), moved to the hidden *rejected* list with "Gave up after 20 attempts: 401". A live submit with an expired token shows a bare "401 Unauthorized" and is **not** queued. | **Fixed** (G-H1): the operator is signed out with an explanation and the pending count; the queue is untouched and 401 batches are not counted as attempts; a live submit on 401 is queued |
| Queue replayed twice | server returns the original result (idempotent) | no visible difference (correct) |

## 6. Fixed readers and the edge

The edge agent's states are described in `flows/READER-ONBOARDING.md`. From the operator's point of
view nothing changes during a WAN outage: reads keep being captured; the web shows the reader as
*Online* while heartbeats arrive (the agent forwards them once the WAN returns) and *Degraded/Offline*
past its SLA. The gateway device card shows queue depth and poison count through its heartbeat metrics
(**Missing UX**: the web does not render those metrics; they are visible only in the raw heartbeat).

## 7. Web

The web app has no offline mode. The live-connection dot in the sidebar turns red when SignalR is
down; REST calls fail with the error box. That is acceptable for an administration client.
