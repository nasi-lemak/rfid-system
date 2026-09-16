# Vertical journey — Tool crib and tool control

Template `tool-tracking`. Item types TOOL (InCrib → CheckedOut → InCrib; UnderRepair; Retired; inspection
every 180 days), TOOL-KIT (container). Custom operation **Calibrate** (Inspect + attributes
`calibrated=true`, `calibratedAt=now`). Workflow **Tool return & check** (Return → Inspect
[continue on reject] → Calibrate [optional, rescan]). Rules *Tool overdue* (Warning), *FOD: tool left
hangar while checked out* (Critical), *Tool missing at close* (Critical).

Markers: **[H]** handheld · **[F]** fixed reader · **[A]** automatic · **[W]** web.

## 1. Deployment shape

```
      Tool crib                     Hangar floor                 Hangar door
 ┌──────────────────┐   Issue    ┌──────────────────┐         ┌──────────────┐
 │ InCrib           │──────────► │ CheckedOut       │ ──────► │ [F] Out      │ → FOD alert if CheckedOut
 │ [H] Count nightly│ ◄───────── │ custodian = tech │         └──────────────┘
 └──────────────────┘  Return &  └──────────────────┘
                       check (workflow)
```

Technicians are Parties (kind Employee). The crib is a Location; the hangar door antenna has
`Direction = Out`.

## 2. Happy path — one shift

```
[USER][H]  Issue: scan tools → Party "J. Tan" → Submit                     InCrib→CheckedOut, custodian set, due-back (none from handheld)
[HW][F]    (optional) crib door antenna reads tools leaving                Moved (Out)
    …work…
[USER][H]  Workflows → Tool return & check
           1 "Return to crib": scan returned tools → dest "Crib" → Submit  CheckedOut→InCrib, custody cleared
           2 "Inspect": target state (InCrib or UnderRepair) → Submit      Inspected; onRejected=continue keeps all tags
           3 "Calibrate" (optional, rescan): scan only the tools due → Submit   attributes calibrated=true, calibratedAt
[USER][H]  Close of shift: Stocktake of "Crib" → scan → Finish & reconcile
[USER][W]  Supervisor: Apply result → Missing tools flagged
[AUTO][A]  Rule "Tool missing at close" (Counted/result=Missing) → Critical alert
```

## 3. Exception paths

| Situation | What happens | Who sees what | Label |
|---|---|---|---|
| **Tool leaves the hangar while CheckedOut** (FOD) | [F] Out read → Moved(direction Out) → rule → **Critical** "Drill 12 passed exit gate while checked out" | web Alerts + notifications; handheld nothing | works |
| **Tool overdue** | `item.overdue` needs a due-back; handheld Issue sets none → rule never fires from handheld issues | web only when issued from the web with a due date | **Missing UX** (handheld due-back), P2 — same finding as ARTWORK |
| **Tool returned by someone else** | Return clears custody; who returned is not recorded | — | works; Missing product capability (minor) |
| **Tool fails inspection** | step 2 target UnderRepair → *→UnderRepair; `onRejected=continue` so a rejected line (e.g. wrong type in the set) does not block the rest | operator sees per-line results | works |
| **Tool never scanned at return** | stays CheckedOut; nightly stocktake marks it Missing after Apply; alert | supervisor | works |
| **Kit issued as a whole** | Pack tools into TOOL-KIT once; Issue the kit tag → container move cascades; custody is set **on the kit only** (children get Moved via container, not CustodyChanged) | Items → In custody shows the kit, not its tools | **Missing product capability**: custody cascade to children; P2 for kits |
| **Calibration due** | `RequiresInspection` + 180 days → *Inspection due* chip on Items, Maintenance risk page; no handheld view | web | Missing UX on handheld (P3) |
| **Wrong tool scanned into Issue** | no per-row remove; Clear and rescan | operator | Missing UX (H-row-remove), P2 |
| **Handheld offline at return** | workflow steps queue; the nightly count is not offline-capable | operator | works / stocktake gap (P2) |

## 4. Gaps specific to this vertical

| Gap | Label | Priority |
|---|---|---|
| Due-back on handheld Issue | Missing UX | P2 |
| Custody cascade to packed children | Missing product capability | P2 (kits) |
| Overdue/inspection-due list on the handheld | Missing UX | P3 |
| Workflow step 2 asks `targetState` as a free-text field on the handheld (the item type's states are not offered as chips); a chip pair *Pass (InCrib) / Fail (UnderRepair)* would be clearer | Recommended improvement (handheld) | P3 |
