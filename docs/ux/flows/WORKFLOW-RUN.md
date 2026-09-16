# Flow — Guided workflow run, alert handling, integration delivery

Three flows that share the rules engine and the outbox: a handheld workflow run (the operator side), an
alert's life (the supervisor side) and an integration delivery (the machine side).

Step tags: `[USER]` · `[SYS]` · `[HW]` · `[AUTO]`.

## 1. Workflow run — CSSD reprocessing (as built)

```
[USER]  Home → Workflows → "🧪 CSSD reprocessing"                      list from GET /api/workflows (cached offline)
[SYS]   runId generated on the device; step index 0
[USER]  step 1 "Receive dirty trays": ▶ Scan → 8 trays → Submit
[SYS]   POST /api/operations { operation: Return, workflowRunId, workflowCode: cssd-reprocess, workflowStep: return,
                               clientId: runId:return, lines: 8 }
[SYS]   per line lifecycle check (TRAY InUse→Dirty on Return); 8 ok
[USER]  "✓ Receive dirty trays: 8 ok" → Next step →
[SYS]   step 2 has rescan → tag set cleared
[USER]  step 2 "Decontaminate": trays leave the washer → ▶ Scan → 8 → Submit
[SYS]   operation Decontaminate (template op, base ProcessStage): Dirty→Decontaminated; 7 ok, 1 rejected
        ("No transition from 'InUse' to 'Decontaminated'" – a tray that skipped step 1)
[USER]  sees the rejected line; onRejected=stop → that EPC is excluded from step 3 ("1 tag(s) stopped…")
[USER]  Next step → step 3 "Sterilise" (rescan): ▶ Scan → 7 → Destination "Sterile store" → Submit
[SYS]   Sterilise: Decontaminated→Sterilised, cycle +1, lastSterilisedAt=now, Moved → rules
[USER]  Finish → summary per step → Done
[USER][W]  Workflows → Runs: one row per runId with its operations (audit trail)
```

Offline variant: any step may return "Offline – step queued"; the run continues; the server applies the
steps in order when the queue drains (FIFO batches of 50). If the operator abandons the run mid-way the
completed steps stand; there is no resume (see [WIZARDS.md](../WIZARDS.md) §1).

## 2. Alert handling (as built)

```
[HW]    exit gate reads ARTEFACT "Bronze head" (state OnDisplay) on the Out antenna
[SYS]   Moved event { direction: Out } → RuleEngine: "Artefact left building without loan" matches
[SYS]   action CreateAlert (Critical) → Alert Open; outbox rows: live.alert, notification (if channels/policy), integration
[AUTO]  outbox dispatcher: SignalR "alert" → sidebar badge +1, Dashboard/Alerts invalidate; Notify → e-mail/Teams/SMS per
        channel; escalation policy steps after delays if still Open
[USER][W]  supervisor: Alerts → row "Bronze head passed exit while 'OnDisplay'" · Critical · Open → Acknowledge
[SYS]   Acknowledged (escalation stops at the policy's ack condition — verify per policy)
[USER]  physical response …
[USER][W]  Close  (or the item is Returned/Dispatched properly; alerts do **not** auto-close — Missing product capability, Optional)
[AUTO]  Analytics → Alert response time
```

Alert UX facts: no severity filter, no site filter, no link from the alert row to the item, no bulk
acknowledge; Viewers see Acknowledge/Close buttons the server refuses. All **Recommended improvements**,
P2. Handheld has no alert view.

## 3. Integration delivery (as built)

```
[USER][W]  Admin: Integrations → + New endpoint: URL, format (Generic / SAP AM / Dynamics 365 / Maximo), auth
           (None / Bearer / Basic / OAuth2 client credentials), HMAC secret, event-type and item-type filters
[SYS]   every ItemEvent commit writes an outbox row per matching endpoint (same transaction)
[AUTO]  outbox dispatcher: POST mapped payload; 2xx → delivered; failure → retry with back-off; lastError on the endpoint
[USER][W]  Integrations row: pending count, last delivery, lastError · "Deliver now" forces a flush
[USER][W]  ERP import (Admin): the reverse direction — CSV/JSON → dry run → import / reconcile
[USER][W]  EPCIS 2.0 (Admin): GS1 projection of the same events for partners that query rather than receive
```

Integration UX facts: failure text rendered in the green `.success` box (E-10); secrets partly in plain
inputs; no per-delivery log (only `lastError`) — **Missing UX**, P3 (the outbox rows exist; a
"Deliveries" tab per endpoint is a read-only view).

## 4. Where the three meet

```
                 ┌───────────────┐        ┌────────────┐        ┌───────────────────────┐
 operation ────► │  ItemEvent(s) │ ─────► │   Rules    │ ─────► │ Alert · SetState ·    │
 fixed read ───► │  (committed)  │        │ Event kind │        │ Webhook · Notify ·    │
 stocktake ────► │               │        └────────────┘        │ RunOperation          │
 schedule ─────► │               │ ◄──────────────────────────  └──────────┬────────────┘
                 └──────┬────────┘   RunOperation writes more events        │
                        │ same transaction                                  │
                        ▼                                                   ▼
                 ┌───────────────┐   background   ┌─────────────────────────────────────┐
                 │    Outbox     │ ─────────────► │ live hub · notifications · webhooks │
                 └───────────────┘                │ integrations · EPCIS                │
                                                  └─────────────────────────────────────┘
```

The operator never sees this machinery; the supervisor sees its *outputs* (alerts, notifications); the
admin sees its *health* (Notifications log, Integrations lastError, Cluster leases). Nothing on any screen
shows the outbox backlog itself — **Missing UX**, P3 (an "Outbox: n pending" stat on System status).
