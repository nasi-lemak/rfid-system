# Flow — Stocktake (cycle count)

A stocktake answers "what is actually in this location". It is the one workflow that deliberately spans
both surfaces: the handheld counts, the web decides.

Step tags: `[USER]` · `[SYS]` · `[HW]` · `[AUTO]`.

## 1. Lifecycle (facts)

```
 Open ──scans──► Open ──Reconcile──► Reconciled ──Apply result──► Applied
  │                                                     
  └──────────────── Cancel ───────────────────────────► Cancelled
```

| Step | Who | What changes |
|---|---|---|
| **Create** (`POST /api/stocktakes`, Operator on that location) | handheld *New stocktake* or web *+ New stocktake*, or a **schedule** (`AUTO`) | snapshot of expected items under the location (path prefix), optional item type filter; lines `Pending` |
| **Scan** (`POST /{id}/scans`) | handheld auto-flush every 1.5 s, barcode, or web *Submit scans* textarea | `Found` (expected), `Unexpected` (known item from elsewhere), `Unknown` (uncommissioned); item `LastSeen*` refreshed |
| **Reconcile** | handheld *Finish & reconcile* or web *Reconcile* | remaining `Pending` → `Missing`; status Reconciled; **items unchanged** |
| **Apply result** | **web only** | Missing → item status Missing (Counted event, rules fire); Found → Active; Unexpected → moved here (Moved event); Unknown ignored |
| **Cancel** | web only | status Cancelled; nothing applied |
| **Schedule** (`AUTO`) | StocktakeSchedule service | opens stocktakes on a cron; can auto-reconcile after a window |

## 2. Flow — as built

```
[USER][H]  Stocktake → New stocktake: location (list), optional name → Start
[SYS]      snapshot expected items (e.g. 312)
[USER][H]  ▶ Scan  → [HW] reader inventories continuously; useInventory dedupes
[SYS]      every 1.5 s the new EPCs are posted; counters update: Expected 312 · Found 287 · Missing 25 · Unexpected 3 · Unknown 1
[USER][H]  walk the location; 📷 Barcode for untagged/hybrid items
[USER][H]  Finish & reconcile (now confirmed: "Reconcile this count? Items not seen become Missing…")
[SYS]      Pending → Missing; status Reconciled
[USER][H]  message: "Reconciled: 287 found, 25 missing, 3 unexpected. Apply the result from the web app."
[USER][W]  Stocktakes → row → Stocktake detail: chips Found/Missing/Unexpected/Unknown; review
[USER][W]  Apply result (now confirmed)  or  Cancel (now confirmed)
[SYS]      items flagged Missing / moved; Counted + Moved events
[AUTO]     rules on Counted (result Missing) → alerts ("Tool missing at close", "High-value artefact missing…")
[AUTO]     Analytics → Inventory accuracy; Dashboard stocktakes widget
```

## 3. Wireframe — handheld stocktake (as built, annotated)

```
┌──────────────────────────────────────────┐
│ ‹ Stocktake                              │
│ Open stocktakes                          │  ← list of Open ones (resume by tapping)
│  • Gallery 2 – 16/09  287/312            │
│ ── or start a new one ──                 │
│ Location  [Gallery 2 ▾]  Name [        ] │
│ [ Start ]                                │
├──────────────────────────────────────────┤  after Start / resume:
│ Gallery 2 – 16/09            Open        │
│ ┌────────┐┌────────┐┌────────┐┌────────┐ │
│ │Expected││ Found  ││Missing ││Unexpect│ │  ← Stat tiles; Missing = "not yet seen" while Open
│ │  312   ││  287   ││   25   ││   3    │ │
│ └────────┘└────────┘└────────┘└────────┘ │
│ [ ▶ Scan / ■ Pause ]        [📷 Barcode] │
│ [ Leave open ]     [ Finish & reconcile ]│  ← confirmation added (P1)
│ Unknown: 1                                │
└──────────────────────────────────────────┘
```

## 4. Exceptions and gaps

| Situation | Behaviour | Label |
|---|---|---|
| **Network down during the count** | `stocktakeScans` fails; EPCs are put back into the pending set and retried on the next flush *while the screen is open*; leaving the screen loses unsent scans; creating a stocktake offline is impossible | **Missing product capability** (offline stocktake). P2 for pilots with reliable Wi-Fi, P1 for yards/basements. Backend already accepts idempotent scan batches; the handheld needs a local scan buffer per stocktake |
| Two handhelds count the same location | both post scans to the same stocktake (resume from the Open list) | works |
| Item found in another stocktake's location | `Unexpected` with its expected location; Apply moves it | works |
| Uncommissioned tag | `Unknown`; not applied; commission later from Tags | works |
| Reconcile too early (someone still counting) | Missing inflated; Cancel and redo, or Apply and re-Count later (Count clears Missing) | works; **Recommended improvement**: show "last scan 12 s ago" before allowing Finish |
| Supervisor forgets to Apply | stocktake stays Reconciled forever; items never flagged | **Recommended improvement**: Stocktakes list should highlight *Reconciled, not applied* rows and the Dashboard widget should count them |
| Apply on a very old reconciliation | flags items Missing that may have moved since | Recommended improvement: warn when Reconciled more than n days ago |
| Quantity items | tag Found; counted quantity not captured | Missing product capability (P3) |
| Site-restricted operator | create requires Operator on that location: "Requires Operator role on this site" | works |

## 5. Why Apply stays on the web

Applying changes item records for possibly hundreds of items and fires alerts; a supervisor should see
the Missing list before that happens. Keeping it web-only is a deliberate choice, but it must be
**visible**: the handheld message says it (good), and the web list should make unapplied reconciliations
impossible to miss (recommended above).
