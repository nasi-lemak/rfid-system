# Flow — Ordinary operation (Transfer) and a fixed-reader event

The Transfer is the archetype of every handheld operation (Receive, Issue, Return, Dispatch, Pack, …
follow the same shape with different inputs). The second half shows what happens with nobody acting when
a fixed reader sees the same item.

Step tags: `[USER]` · `[SYS]` · `[HW]` · `[AUTO]`.

## 1. Transfer from the handheld (as built)

```
[USER]  Home → Operations → chip "Transfer"                 (or Inventory → "Operate on these" with the set prefilled)
[USER]  ▶ Scan                                              [HW] reader inventories; dedupe; list fills: EPC · name · state
[USER]  ■ Stop (or hardware trigger release on Zebra/Chainway)
[USER]  Destination › picker (locations, cached for offline) → "Rack A-03"
[USER]  (Party optional – Transfer's SetCustodian is optional)   Reference [optional]
[USER]  Submit Transfer                                     button enabled only when tags ≥ 1 and destination chosen
[SYS]   stamp clientId, occurredAt → POST /api/operations
[SYS]   site RBAC: Operator on the destination (and source when given)
[SYS]   per line: resolve EPC → item; disposed? type allowed? lifecycle allows Transfer from this state?
        → Move effect (container children cascade) → Moved event → rules → live hub → outbox
[SYS]   result { ok, unknown, rejected, lines[] }
[USER]  all ok → "✓ Transfer: 14 item(s)", set cleared
        some rejected/unknown → set kept, lines show result + message; operator fixes and resubmits
                                (server replays the same clientId idempotently → identical result; a *new*
                                submit after Clear gets a new clientId)
[SYS]   offline → "Offline – queued (n pending)"; Home shows the queue; sync on Home focus / Sync now
```

### Line-result semantics the operator must learn

| Result | Meaning | Next action |
|---|---|---|
| ok · `NewState` | applied | none |
| Unknown "Tag not commissioned" | tag has no item | Commission, then redo for that tag |
| Rejected "Duplicate in operation" | same item twice in the set (two tags on one item) | none; applied once |
| Rejected "Transfer not allowed while X is 'InTransit'" | lifecycle forbids | use the right operation |
| Rejected "X is disposed" | retired item | remove from set |
| Rejected "Location is outside your sites" | RBAC | choose another destination / ask admin |

## 2. Wireframe — after a partial reject (as built)

```
┌──────────────────────────────────────────┐
│ ‹ Operations                             │
│ [Receive][Transfer●][Issue][Return]…     │
│ Destination            Rack A-03      ›  │
│ Party                  choose…        ›  │
│ [▶ Scan]  [Clear]                        │
│ 14 tag(s)                                │
│ ✓ E200…3412  Carton 0412   → PutAway     │
│ ✓ E200…3413  Carton 0413   → PutAway     │
│ ! E200…3499  —             Unknown: Tag  │
│                            not commissioned│
│ ✗ E200…3500  Pallet 12     Rejected: …   │
│ [ Submit Transfer ]                      │  ← resubmit sends all 14 again; server replays ok lines
└──────────────────────────────────────────┘
```

**Missing UX**: no per-row remove, no "resubmit only failed", no "Commission this tag" from the Unknown
row. These are the three handheld improvements with the best effort/value ratio after the P1 list.

## 3. Fixed-reader event for the same item (as built)

```
[HW]    antenna 2 (Dock 1, direction Out) reads E200…3412 at −52 dBm; reader reports every N tags / 2 s
[SYS]   (server-driven) LLRP client receives the report  ·  (edge) agent buffers → batch file → POST /api/ingest/reads
[SYS]   idempotency: batchId seen before? → return stored result (duplicate)
[SYS]   in-batch dedupe: latest read per (EPC, antenna); strongest RSSI per EPC wins the zone
[SYS]   resolve EPC → Carton 0412; readAt older than item.LastSeenAt? → count as late, ignore state change
[SYS]   direction Out → item moves to Dock 1's parent (Warehouse); Moved event { direction: Out, zone, rssi }
        direction In  → item moves into the zone;  None → Moved(Seen) if zone changed, else Seen (30 s debounce)
[SYS]   item.Status Missing → Active; LastSeen*, device.LastSeenAt refreshed
[SYS]   presence: not for Out; for In/None open a session in the zone, close sessions elsewhere
[SYS]   RTLS sighting collected if the antenna has x/y
[SYS]   live hub: LiveRead → web Live reads page; Presence page (5 s poll) updates
[AUTO]  rules on Moved: e.g. "Dispatched without staging" (direction Out ∧ toLocation.kind ≠ Dock ∧ state ∉ Staged,Dispatched) → alert
[AUTO]  outbox: live.alert, notification channels, integration endpoints (ERP), EPCIS projection
[AUTO]  presence sweeper: sessions with no read for `presenceTimeoutSec` (default 300 s) close → item moves to the
        zone's parent with a Moved{direction: Out, dwellSeconds} event → rules again
[USER]  supervisor sees: Alerts (badge), Item detail history (Moved · Dock 1 → Warehouse · device · antenna), Presence occupancy
```

### What the operator on the floor sees

Nothing. The handheld has no presence view and no alert view (**Missing UX**, acceptable for a pilot; a
"my zone" occupancy or an alert list on the handheld is Optional).

## 4. Web-driven Transfer

Operations → *+ Run operation* → same form (`OperationForm`): operation select (built-in and
vertical/custom groups), destination, party, container, target state, EPC textarea (or preselected item
ids from Item detail). Same result table. Used for corrections and for operations the handheld cannot
express (due-back on Dispatch/Issue).
